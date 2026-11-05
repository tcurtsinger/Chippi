// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.FocusFollowsMouse;

public enum TrackingSetting { Enabled, Raise, Delay }

// The adapter is injected: constructing or testing the engine never accesses Windows settings.
public interface ITrackingSettings
{
    uint Read(TrackingSetting setting);
    void Write(TrackingSetting setting, uint value);
}

public sealed record TrackingOwnership(uint Original, uint Applied);

public interface ITrackingJournal
{
    Dictionary<TrackingSetting, TrackingOwnership> Load();
    // Must be durable before returning. A failure must leave the previous journal intact.
    void Save(IReadOnlyDictionary<TrackingSetting, TrackingOwnership> ownership);
}

// Called serially by the module's dispatcher. The journal belongs to the Windows user,
// not a dev profile: native tracking is a user-wide setting.
public sealed class FocusTrackingEngine(ITrackingSettings settings, ITrackingJournal journal)
{
    private readonly Dictionary<TrackingSetting, TrackingOwnership> _ownership = journal.Load();
    private Dictionary<TrackingSetting, uint>? _original;
    public bool Enabled { get; private set; }
    public bool Paused { get; private set; }

    // Call before accepting enable requests, including when the feature is installed but off.
    public void Recover() => Restore();

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            Restore();
            Enabled = false;
            _original = null;
            return;
        }
        if (Enabled) return;
        Recover();
        _original = Enum.GetValues<TrackingSetting>().ToDictionary(key => key, settings.Read);
        if (!Paused) Apply();
        Enabled = true;
    }

    // Seam for the shared fullscreen/lock policy. Pausing retains the original snapshot.
    public void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        if (Enabled)
        {
            if (paused) Restore();
            else Apply();
        }
        Paused = paused;
    }

    private void Apply()
    {
        try
        {
            // Enable last so the delay and raise policy are set before tracking starts.
            Write(TrackingSetting.Delay, 250);
            Write(TrackingSetting.Raise, 1);
            Write(TrackingSetting.Enabled, 1);
        }
        catch (Exception applyError)
        {
            try { Restore(); }
            catch (Exception restoreError) { throw new AggregateException(applyError, restoreError); }
            throw;
        }
    }

    private void Write(TrackingSetting key, uint value)
    {
        var ownership = new TrackingOwnership(_original![key], value);
        _ownership[key] = ownership;
        // Journal the intent first: covers a process exit immediately after the native write.
        journal.Save(_ownership);
        settings.Write(key, value);
        if (settings.Read(key) != value) throw new InvalidOperationException("Focus change failed");
    }

    private void Restore()
    {
        List<Exception> errors = [];
        // Restore tracking first, then its parameters. Attempt every owned field even on failure.
        foreach (var key in new[] { TrackingSetting.Enabled, TrackingSetting.Raise, TrackingSetting.Delay })
        {
            if (!_ownership.TryGetValue(key, out var owned)) continue;
            try
            {
                if (settings.Read(key) == owned.Applied)
                {
                    settings.Write(key, owned.Original);
                    if (settings.Read(key) != owned.Original)
                        throw new InvalidOperationException("Focus change failed");
                }
                // A newer external value is left alone. Persist removal only after restoration.
                var remaining = new Dictionary<TrackingSetting, TrackingOwnership>(_ownership);
                remaining.Remove(key);
                journal.Save(remaining);
                _ownership.Remove(key);
            }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Focus change failed", errors);
    }
}
