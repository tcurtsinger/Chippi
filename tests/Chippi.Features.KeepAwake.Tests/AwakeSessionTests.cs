// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Features.KeepAwake;

namespace Chippi.Features.KeepAwake.Tests;

public sealed class AwakeSessionTests
{
    [Theory]
    [InlineData(-1, 60, 1080)]
    [InlineData(1, 0, 1080)]
    [InlineData(1, 1441, 1080)]
    [InlineData(2, 60, 1440)]
    public void InvalidSettingsRejected(int mode, int duration, int until) =>
        Assert.Throws<ArgumentException>(() => new KeepAwakeSettings { Mode = (AwakeMode)mode, DurationMinutes = duration, UntilMinute = until }.Validate());

    [Fact]
    public void IndefiniteHasNoDeadline() => Assert.Null(new KeepAwakeSettings().Deadline(DateTimeOffset.Now));

    [Fact]
    public void PastTimeMeansTomorrow()
    {
        var now = new DateTimeOffset(DateTime.Today.AddHours(18));
        var end = new KeepAwakeSettings { Mode = AwakeMode.UntilTime, UntilMinute = 17 * 60 }.Deadline(now);
        Assert.Equal(now.LocalDateTime.Date.AddDays(1).AddHours(17), end!.Value.LocalDateTime);
    }

    [Fact]
    public async Task StartStopAndDisposeUseSameDedicatedThread()
    {
        var native = new FakeNative();
        var session = new AwakeSession(native);
        await session.StartAsync(new() { KeepDisplayOn = true });
        await session.StopAsync();
        await session.DisposeAsync();
        Assert.True(native.Calls.First().Active);
        Assert.True(native.Calls.First().Display);
        Assert.All(native.Calls.Skip(1), c => Assert.False(c.Active));
        Assert.Single(native.Calls.Select(c => c.Thread).Distinct());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.StartAsync(new()));
    }

    [Fact]
    public async Task DurationUsesMonotonicClockAndCompletesOnlyOnce()
    {
        var clock = new Clock();
        var native = new FakeNative();
        await using var session = new AwakeSession(native, clock);
        int successes = 0;
        session.Changed += (_, _, timed, _) => { if (timed) successes++; };
        await session.StartAsync(new() { Mode = AwakeMode.Duration, DurationMinutes = 1 });
        clock.Wall = clock.Wall.AddDays(1);
        await session.CheckAsync();
        Assert.Equal(0, successes);
        clock.Ticks = TimeSpan.FromMinutes(1).Ticks;
        await session.CheckAsync();
        await session.CheckAsync();
        Assert.Equal(1, successes);
        Assert.False(native.Calls.Last().Active);
    }

    [Fact]
    public async Task ManualStopDoesNotCelebrate()
    {
        await using var session = new AwakeSession(new FakeNative());
        bool celebrated = false;
        session.Changed += (_, _, timed, _) => celebrated |= timed;
        await session.StartAsync(new() { Mode = AwakeMode.Duration });
        await session.StopAsync();
        Assert.False(celebrated);
    }

    [Fact]
    public async Task FailureTerminatesWorkerAndAttemptsRelease()
    {
        var native = new FakeNative { FailStart = true };
        var session = new AwakeSession(native);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(new()));
        await session.DisposeAsync();
        Assert.False(native.Calls.Last().Active);
        Assert.Single(native.Calls.Select(c => c.Thread).Distinct());
    }

    [Fact]
    public async Task InvalidStartDoesNotKillWorker()
    {
        await using var session = new AwakeSession(new FakeNative());
        await Assert.ThrowsAsync<ArgumentException>(() => session.StartAsync(new() { DurationMinutes = 0 }));
        await session.StartAsync(new());
        await session.StopAsync();
    }

    [Fact]
    public async Task UntilTimeExpiresWithWallClock()
    {
        var clock = new Clock { Wall = new DateTimeOffset(DateTime.Today.AddHours(12)) };
        await using var session = new AwakeSession(new FakeNative(), clock);
        int successes = 0;
        session.Changed += (_, _, timed, _) => { if (timed) successes++; };
        await session.StartAsync(new() { Mode = AwakeMode.UntilTime, UntilMinute = 13 * 60 });
        clock.Wall = clock.Wall.AddMinutes(59);
        await session.CheckAsync();
        Assert.Equal(0, successes);
        clock.Wall = clock.Wall.AddMinutes(1);
        await session.CheckAsync();
        Assert.Equal(1, successes);
    }

    [Fact]
    public async Task DisposingActiveSessionReleasesIt()
    {
        var native = new FakeNative();
        var session = new AwakeSession(native);
        await session.StartAsync(new());
        await session.DisposeAsync();
        Assert.False(native.Calls.Last().Active);
        Assert.Single(native.Calls.Select(c => c.Thread).Distinct());
    }

    [Fact]
    public async Task ConcurrentCommandsAllCompleteBeforeShutdown()
    {
        var native = new FakeNative();
        var session = new AwakeSession(native);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => i % 2 == 0 ? session.StartAsync(new()) : session.StopAsync()));
        await session.DisposeAsync();
        Assert.False(native.Calls.Last().Active);
        Assert.Single(native.Calls.Select(c => c.Thread).Distinct());
    }

    private sealed class FakeNative : IExecutionState
    {
        public ConcurrentQueue<(bool Active, bool Display, int Thread)> Calls { get; } = new();
        public bool FailStart { get; init; }
        public void Set(bool active, bool display)
        {
            Calls.Enqueue((active, display, Environment.CurrentManagedThreadId));
            if (active && FailStart) throw new InvalidOperationException("Simulated failure");
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Wall = DateTimeOffset.UtcNow;
        public long Ticks;
        public override DateTimeOffset GetUtcNow() => Wall.ToUniversalTime();
        public override long GetTimestamp() => Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
