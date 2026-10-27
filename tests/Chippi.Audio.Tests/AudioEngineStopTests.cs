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
}
