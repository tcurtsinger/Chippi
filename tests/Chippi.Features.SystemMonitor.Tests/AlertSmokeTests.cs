// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Core.Island;
using Chippi.Core.Notifications;
using Chippi.Features.SystemMonitor.Alerts;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Tests;

public sealed class AlertSmokeTests
{
    [Fact]
    public void A_sample_reaches_the_notifier_repeats_only_after_cooldown_and_stops_when_disabled()
    {
        var time = new Clock();
        var fake = new FakeSources { Memory = () => new MemoryReading(93, 100, 0, 100) };
        using var arrived = new SemaphoreSlim(0);
        using var sampler = new SensorSampler(fake.Sources, time);
        var notices = new Notices();
        var island = new Island();
        using var alerts = new MonitorAlerts(sampler, notices, island, new SystemMonitorSettings(), time);
        sampler.Updated += _ => arrived.Release();
        var settings = new SystemMonitorSettings { Memory = new() { On = true, Percent = 90 }, RepeatMinutes = 2 };
        alerts.Configure(settings);
        Assert.True(arrived.Wait(TimeSpan.FromSeconds(5)));
        AssertNotice(Assert.Single(notices.Items));
        Assert.True(island.Last!.NeedsAttention);
        Assert.Equal("system-monitor", island.Last.TileKey);
        Assert.Equal("Memory 93%", alerts.Current.First!.TileText);

        Tick(119);
        Assert.Single(notices.Items);
        Tick(1);
        Assert.Equal(2, notices.Items.Count);
        Assert.All(notices.Items, AssertNotice);
        alerts.Configure(settings with { Memory = new() { On = false, Percent = 90 } });
        Assert.Equal(SensorNeed.None, sampler.Need);
        Assert.Null(island.Last);
        Assert.Empty(alerts.Current.Conditions);

        void Tick(int seconds)
        {
            time.Advance(seconds);
            sampler.Interval = TimeSpan.FromSeconds(1); // Wake the loop after advancing the fake clock.
            Assert.True(arrived.Wait(TimeSpan.FromSeconds(5)));
        }
    }

    private static void AssertNotice(Notice notice)
    {
        Assert.Equal("memory", notice.Kind);
        Assert.Equal("Memory in use 93%", notice.Title);
        Assert.Equal("--system-monitor", notice.Activation);
        Assert.Empty(notice.Actions);
        Assert.False(notice.Celebrate);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        public void Advance(int seconds) => Interlocked.Add(ref _ticks, seconds * TimeSpan.TicksPerSecond);
    }

    private sealed class Notices : INotifier
    {
        public ConcurrentQueue<Notice> Items { get; } = new();
        public IReadOnlyList<NoticeKind> Kinds { get; private set; } = [];
        public void Declare(IReadOnlyList<NoticeKind> kinds) { Assert.Equal(5, kinds.Count); Kinds = kinds; }
        public NoticePlaces PlacesFor(string kindId) => NoticePlaces.Windows | NoticePlaces.Island;
        public void SetPlaces(string kindId, NoticePlaces places) { }
        public void Show(Notice notice) => Items.Enqueue(notice);
    }

    private sealed class Island : IIslandPublisher
    {
        public IslandActivity? Last;
        public void Publish(IslandActivity activity) => Last = activity;
        public void Clear(string key) => Last = null;
        public void ReportSuccess(string? message = null) => throw new InvalidOperationException("Alerts never celebrate.");
    }
}
