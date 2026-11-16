// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO.Pipes;
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated.Tests;

public sealed class LifetimeTests
{
    [Fact]
    public async Task Idle_exits_on_an_injected_clock_and_disconnect_runs_nothing()
    {
        await using var pair = await Pair.Open();
        var clock = new Clock();
        var updater = new Updater();
        var run = new HelperSession(updater, clock).RunAsync(pair.Helper);
        Assert.Equal("Ready", (await Messages.ReadReplyAsync(pair.Client))!.State);
        clock.Fire(); await run.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(0, updater.Count);

        await using var other = await Pair.Open();
        var disconnected = new HelperSession(updater).RunAsync(other.Helper);
        Assert.Equal("Ready", (await Messages.ReadReplyAsync(other.Client))!.State);
        other.Client.Dispose(); await disconnected.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(0, updater.Count);
    }

    [Fact]
    public async Task A_busy_job_ignores_idle_expiry_and_a_duplicate_is_not_replayed()
    {
        await using var pair = await Pair.Open(); var clock = new Clock();
        var complete = new TaskCompletionSource<AdminReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updater = new Updater { Next = complete.Task };
        var run = new HelperSession(updater, clock).RunAsync(pair.Helper);
        await Messages.ReadReplyAsync(pair.Client);
        var request = new AdminRequest(Guid.NewGuid(), "UpdatePackage", "a.b");
        await Messages.WriteAsync(pair.Client, request);
        Assert.Equal("Progress", (await Messages.ReadReplyAsync(pair.Client))!.State);
        clock.Fire(); Assert.False(run.IsCompleted);
        complete.SetResult(new(request.Id, "Done", request.PackageId, 0));
        Assert.Equal("Done", (await Messages.ReadReplyAsync(pair.Client))!.State);
        await Messages.WriteAsync(pair.Client, request);
        await Assert.ThrowsAsync<InvalidDataException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, updater.Count);
    }

    private sealed class Updater : IPackageUpdater
    {
        public int Count;
        public Task<AdminReply>? Next;
        public Task<AdminReply> UpdateAsync(AdminRequest request) { Count++; return Next ?? Task.FromResult(new AdminReply(request.Id, "Done", request.PackageId, 0)); }
    }
    private sealed class Clock : TimeProvider
    {
        private Timer? _timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _timer = new Timer(callback, state);
        public void Fire() => _timer?.Fire();
        private sealed class Timer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Pair(NamedPipeServerStream client, NamedPipeClientStream helper) : IAsyncDisposable
    {
        public Stream Client => client; public Stream Helper => helper;
        public static async Task<Pair> Open()
        {
            string name = "chippi-lifetime-" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var peer = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            var accept = server.WaitForConnectionAsync(); await peer.ConnectAsync(5000); await accept;
            return new(server, peer);
        }
        public ValueTask DisposeAsync() { client.Dispose(); helper.Dispose(); return ValueTask.CompletedTask; }
    }
}
