// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.FocusFollowsMouse;

using System.IO;

namespace Chippi.Features.FocusFollowsMouse.Tests;

public sealed class FocusTrackingTests
{
    [Theory]
    [InlineData(0u, 0u, 500u)]
    [InlineData(1u, 1u, 100u)]
    public void EnablePauseResumeDisableRestoresOriginal(uint enabled, uint raise, uint delay)
    {
        var settings = new Settings(enabled, raise, delay);
        var journal = new Journal();
        var engine = new FocusTrackingEngine(settings, journal);
        Assert.False(engine.Enabled);
        Assert.Equal(0, settings.Writes);
        engine.SetEnabled(true);
        Assert.Equal(new uint[] { 1, 1, 250 }, settings.Values);
        engine.SetEnabled(true);
        for (var i = 0; i < 3; i++)
        {
            engine.SetPaused(true);
            Assert.Equal(new[] { 0u, raise, delay }, settings.Values);
            engine.SetPaused(false);
            Assert.Equal(new uint[] { 1, 1, 250 }, settings.Values);
        }
        engine.SetEnabled(false);
        Assert.Equal(new[] { enabled, raise, delay }, settings.Values);
        Assert.Empty(journal.Values);
    }

    [Fact]
    public void CrashRecoveryPreservesAnExternalChange()
    {
        var settings = new Settings(0, 0, 500);
        var journal = new Journal();
        new FocusTrackingEngine(settings, journal).SetEnabled(true);
        settings.Values[2] = 900;
        var restarted = new FocusTrackingEngine(settings, journal);
        restarted.Recover();
        Assert.Equal(new uint[] { 0, 0, 900 }, settings.Values);
        Assert.False(restarted.Enabled);
        Assert.Empty(journal.Values);
    }

    [Fact]
    public void PartialApplyRollsBackAndCanRetry()
    {
        var settings = new Settings(0, 0, 500) { FailOnce = TrackingSetting.Raise };
        var journal = new Journal();
        var engine = new FocusTrackingEngine(settings, journal);
        Assert.Throws<IOException>(() => engine.SetEnabled(true));
        Assert.False(engine.Enabled);
        Assert.Equal(new uint[] { 0, 0, 500 }, settings.Values);
        Assert.Empty(journal.Values);
        engine.SetEnabled(true);
        Assert.True(engine.Enabled);
    }

    [Fact]
    public void FailedJournalDoesNotChangeWindowsAndRecoveryRetries()
    {
        var settings = new Settings(0, 0, 500);
        var journal = new Journal { Fail = true };
        var engine = new FocusTrackingEngine(settings, journal);
        Assert.ThrowsAny<Exception>(() => engine.SetEnabled(true));
        Assert.Equal(0, settings.Writes);
        journal.Fail = false;
        engine.Recover();
        Assert.Empty(journal.Values);
    }

    [Fact]
    public void EnablingWhilePausedDefersChanges()
    {
        var settings = new Settings(0, 0, 500);
        var engine = new FocusTrackingEngine(settings, new Journal());
        engine.SetPaused(true);
        engine.SetEnabled(true);
        Assert.Equal(0, settings.Writes);
        engine.SetPaused(false);
        Assert.Equal(new uint[] { 1, 1, 250 }, settings.Values);
        engine.SetEnabled(false);
        Assert.Equal(new uint[] { 0, 0, 500 }, settings.Values);
    }

    [Fact]
    public void FailedRestoreRetainsOwnershipAndStillRestoresOtherFields()
    {
        var settings = new Settings(0, 0, 500);
        var journal = new Journal();
        var engine = new FocusTrackingEngine(settings, journal);
        engine.SetEnabled(true);
        settings.FailOnce = TrackingSetting.Enabled;
        Assert.Throws<AggregateException>(() => engine.SetEnabled(false));
        Assert.Equal(new uint[] { 1, 0, 500 }, settings.Values);
        Assert.Equal(TrackingSetting.Enabled, Assert.Single(journal.Values).Key);
        engine.SetEnabled(false);
        Assert.Equal(new uint[] { 0, 0, 500 }, settings.Values);
        Assert.Empty(journal.Values);
        Assert.False(engine.Enabled);
    }

    [Fact]
    public void ANewEngineRecoversFromTheDurableJournal()
    {
        var folder = Path.Combine(Path.GetTempPath(), "chippi-focus-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "tracking.json");
        try
        {
            var settings = new Settings(0, 0, 500);
            new FocusTrackingEngine(settings, new TrackingJournal(path)).SetEnabled(true);
            Assert.Equal(3, new TrackingJournal(path).Load().Count);
            var restarted = new FocusTrackingEngine(settings, new TrackingJournal(path));
            restarted.Recover();
            Assert.Equal(new uint[] { 0, 0, 500 }, settings.Values);
            Assert.Empty(new TrackingJournal(path).Load());
            Assert.False(restarted.Enabled);
            // Damaged ownership is treated as empty; no original preferences are guessed.
            File.WriteAllText(path, "{");
            Assert.Empty(new TrackingJournal(path).Load());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }
    [Fact]
    public void FailedPauseAfterRestoreStillAllowsResumeToReapplyTracking()
    {
        var settings = new Settings(0, 0, 500);
        var engine = new FocusTrackingEngine(settings, new Journal());
        engine.SetEnabled(true);
        settings.FailReadAfterRestore = true;
        Assert.Throws<IOException>(() => engine.SetPaused(true));
        Assert.True(engine.Paused);
        Assert.Equal(new uint[] { 0, 0, 500 }, settings.Values);
        settings.FailReadAfterRestore = false;
        engine.SetPaused(false);
        Assert.Equal(new uint[] { 1, 1, 250 }, settings.Values);
    }

    [Fact]
    public void FailedResumeCanRetryTheSamePolicy()
    {
        var settings = new Settings(0, 0, 500);
        var engine = new FocusTrackingEngine(settings, new Journal());
        engine.SetEnabled(true);
        engine.SetPaused(true);
        settings.FailOnce = TrackingSetting.Raise;
        Assert.Throws<IOException>(() => engine.SetPaused(false));
        Assert.False(engine.Paused);
        Assert.Equal(new uint[] { 0, 0, 500 }, settings.Values);
        engine.SetPaused(false);
        Assert.Equal(new uint[] { 1, 1, 250 }, settings.Values);
    }

    private sealed class Settings(uint enabled, uint raise, uint delay) : ITrackingSettings
    {
        public uint[] Values { get; } = [enabled, raise, delay];
        public int Writes { get; private set; }
        public TrackingSetting? FailOnce { get; set; }
        public bool FailReadAfterRestore;
        private bool _readFailure;
        public uint Read(TrackingSetting setting)
        {
            if (_readFailure && setting == TrackingSetting.Enabled) { _readFailure = false; throw new IOException("Injected post-restore read failure"); }
            return Values[(int)setting];
        }
        public void Write(TrackingSetting setting, uint value)
        {
            if (FailOnce == setting)
            {
                FailOnce = null;
                throw new IOException("Injected write failure");
            }
            Writes++;
            Values[(int)setting] = value;
            if (FailReadAfterRestore && setting == TrackingSetting.Delay && value == 500) _readFailure = true;
        }
    }

    private sealed class Journal : ITrackingJournal
    {
        public Dictionary<TrackingSetting, TrackingOwnership> Values { get; private set; } = [];
        public bool Fail { get; set; }
        public Dictionary<TrackingSetting, TrackingOwnership> Load() => new(Values);
        public void Save(IReadOnlyDictionary<TrackingSetting, TrackingOwnership> ownership)
        {
            if (Fail) throw new IOException("Injected journal failure");
            Values = new(ownership);
        }
    }
}
