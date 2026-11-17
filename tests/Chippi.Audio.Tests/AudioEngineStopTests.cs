// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Audio.Tests;

public sealed class AudioEngineStopTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Stopping_an_ended_engine_returns_its_original_completion()
    {
        var system = new FakeDeviceSystem();
        var engine = new AudioService.Engine(new AudioService(() => system, null));
        engine.Start();
        await engine.Invoke(_ => true).WaitAsync(Timeout);
        var first = engine.StopAsync();
        await first.WaitAsync(Timeout);

        // Loop's finally has disposed the queue. A late stop must never inspect it.
        Assert.Same(first, engine.StopAsync());
        Assert.True(system.IsDisposed);
    }

    [Fact]
    public async Task Two_stops_while_work_is_pending_share_one_completion()
    {
        var system = new FakeDeviceSystem();
        var engine = new AudioService.Engine(new AudioService(() => system, null));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        engine.Start();
        var work = engine.Invoke(_ =>
        {
            entered.Set();
            if (!release.Wait(Timeout)) throw new TimeoutException("Blocked work was not released");
            return true;
        });
        Task? stopped = null;
        try
        {
            Assert.True(entered.Wait(Timeout));
            stopped = engine.StopAsync();
            Assert.Same(stopped, engine.StopAsync());
            Assert.False(stopped.IsCompleted);
        }
        finally
        {
            release.Set();
            await work.WaitAsync(Timeout);
            await (stopped ?? engine.StopAsync()).WaitAsync(Timeout);
        }
        Assert.True(system.IsDisposed);
    }

    [Fact]
    public async Task A_command_after_stop_is_rejected_through_its_task()
    {
        var engine = new AudioService.Engine(new AudioService(() => new FakeDeviceSystem(), null));
        engine.Start();
        await engine.StopAsync().WaitAsync(Timeout);

        Task<bool>? command = null;
        Assert.Null(Record.Exception(() => { command = engine.Invoke(_ => true); }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => command!);
        Assert.Equal("The audio service is stopping.", error.Message);
    }

    [Fact]
    public async Task A_lease_taken_while_the_old_engine_stops_starts_its_engine_after_the_old_one_is_gone()
    {
        var first = new FakeDeviceSystem();
        var second = new FakeDeviceSystem();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        bool? firstGoneWhenSecondCreated = null;
        var service = new AudioService(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                // The first engine's thread is busy, so its stop is still queued behind this.
                entered.Set();
                if (!release.Wait(Timeout)) throw new TimeoutException("Blocked start was not released");
                return first;
            }
            firstGoneWhenSecondCreated = first.IsDisposed;
            return second;
        }, null);

        var lease1 = service.Acquire();
        Assert.True(entered.Wait(Timeout));
        var releasing = lease1.DisposeAsync().AsTask();
        var lease2 = service.Acquire();
        Assert.False(releasing.IsCompleted);

        release.Set();
        await releasing.WaitAsync(Timeout);
        await service.ReadyAsync().WaitAsync(Timeout);

        Assert.Equal(true, firstGoneWhenSecondCreated);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
        await lease2.DisposeAsync();
        Assert.True(second.IsDisposed);
    }
}
