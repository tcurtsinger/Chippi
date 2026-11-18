// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.IO.Pipes;
using System.Text;
using Chippi.Core.Admin;
using Chippi.Elevated.Protocol;
using Chippi.UI.Admin;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Elevated.Tests;

public sealed class PortSessionTests
{
    private static readonly AdminEndpoint Endpoint = new(AdminProtocol.Tcp, "127.0.0.1", 3000, 4120, 133_000_000_000_000_000);

    private static AdminRequest Valid(string verb = "EndProcess", string? service = null) =>
        new(Guid.NewGuid(), verb, Protocol: "Tcp", LocalAddress: "::1", LocalPort: 3000, Pid: 4120, StartTime: 1, ServiceName: service);

    [Fact]
    public void Valid_port_requests_are_allowed_and_others_are_not()
    {
        Assert.True(Messages.RequestAllowed(Valid("CloseProcess")));
        Assert.True(Messages.RequestAllowed(Valid("EndProcess")));
        Assert.True(Messages.RequestAllowed(Valid("StopService", "Spooler")));
        Assert.True(Messages.RequestAllowed(Valid("EndProcess") with { Protocol = "Udp", LocalAddress = "0.0.0.0" }));
        Assert.False(Messages.RequestAllowed(Valid("StopService")));                                    // no name
        Assert.False(Messages.RequestAllowed(Valid("EndProcess", "Spooler")));                          // name on a process verb
        Assert.False(Messages.RequestAllowed(Valid("StopService", @"a\b")));
        Assert.False(Messages.RequestAllowed(Valid("StopService", "a/b")));
        Assert.False(Messages.RequestAllowed(Valid("StopService", new string('a', 257))));
        Assert.False(Messages.RequestAllowed(Valid() with { Protocol = "Icmp" }));
        Assert.False(Messages.RequestAllowed(Valid() with { LocalAddress = "localhost" }));
        Assert.False(Messages.RequestAllowed(Valid() with { LocalAddress = null }));
        Assert.False(Messages.RequestAllowed(Valid() with { LocalPort = 0 }));
        Assert.False(Messages.RequestAllowed(Valid() with { LocalPort = 65536 }));
        Assert.False(Messages.RequestAllowed(Valid() with { Pid = -1 }));
        Assert.False(Messages.RequestAllowed(Valid() with { Pid = null }));
        Assert.False(Messages.RequestAllowed(Valid() with { StartTime = 0 }));
        Assert.False(Messages.RequestAllowed(Valid() with { PackageId = "a.b" }));
        Assert.False(Messages.RequestAllowed(Valid() with { IncludeUnknown = true }));
        Assert.False(Messages.RequestAllowed(Valid() with { Id = Guid.Empty }));
    }

    [Fact]
    public void Endpoint_fields_do_not_ride_on_other_verbs()
    {
        Assert.False(Messages.RequestAllowed(new AdminRequest(Guid.NewGuid(), "Done", Pid: 4)));
        Assert.False(Messages.RequestAllowed(new AdminRequest(Guid.NewGuid(), "UpdatePackage", "a.b", "1.0", ServiceName: "x")));
    }

    [Theory]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"EndProcess\",\"Protocol\":\"Tcp\",\"LocalAddress\":\"127.0.0.1\",\"LocalPort\":1,\"Pid\":2,\"StartTime\":3,\"Command\":\"x\"}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"EndProcess\",\"Protocol\":\"Tcp\",\"LocalAddress\":\"127.0.0.1\",\"LocalPort\":1,\"Pid\":2,\"Pid\":3,\"StartTime\":3}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"EndProcess\",\"Protocol\":\"Tcp\",\"LocalAddress\":\"127.0.0.1\",\"LocalPort\":1,\"Pid\":2}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"KillProcess\",\"Protocol\":\"Tcp\",\"LocalAddress\":\"127.0.0.1\",\"LocalPort\":1,\"Pid\":2,\"StartTime\":3}")]
    public async Task Invalid_port_frames_end_the_session(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var frame = new MemoryStream();
        frame.Write(BitConverter.GetBytes(bytes.Length)); frame.Write(bytes); frame.Position = 0;
        var error = await Record.ExceptionAsync(() => Messages.ReadRequestAsync(frame));
        Assert.True(error is InvalidDataException or System.Text.Json.JsonException, error?.ToString());
    }

    [Fact]
    public async Task A_port_request_round_trips()
    {
        var stream = new MemoryStream();
        var request = Valid("StopService", "Spooler");
        await Messages.WriteAsync(stream, request); stream.Position = 0;
        Assert.Equal(request, await Messages.ReadRequestAsync(stream));
    }

    [Theory]
    [InlineData("Done", AdminPortState.Freed)] [InlineData("Gone", AdminPortState.AlreadyClosed)] [InlineData("Changed", AdminPortState.Changed)]
    [InlineData("Kept", AdminPortState.Kept)] [InlineData("Failed", AdminPortState.Failed)]
    public async Task Each_reply_state_maps_to_a_result(string state, AdminPortState expected)
    {
        await using var factory = new Factory((_, r) => new AdminReply(r.Id, state, Reason: "why"));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var result = await helper.EndProcessAsync(Endpoint);
        Assert.Equal(new AdminPortResult(AdminPortVerb.End, expected, "why"), result);
        Assert.Equal(1, factory.Opens);
        await helper.EndPortSessionAsync();
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task The_confirmed_endpoint_and_service_reach_the_helper_exactly()
    {
        await using var factory = new Factory((_, r) => new AdminReply(r.Id, "Done"));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        await helper.StopServiceAsync("Spooler", new(AdminProtocol.Udp, "::1", 9100, 7, 42));
        var seen = Assert.Single(factory.Seen);
        Assert.Equal(new AdminRequest(seen.Id, "StopService", Protocol: "Udp", LocalAddress: "::1", LocalPort: 9100, Pid: 7, StartTime: 42, ServiceName: "Spooler"), seen);
        await helper.EndPortSessionAsync();
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Two_port_actions_in_a_row_share_one_session_and_one_launch()
    {
        await using var factory = new Factory((_, r) => new AdminReply(r.Id, "Done"));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        Assert.Equal(AdminPortState.Freed, (await helper.CloseProcessAsync(Endpoint)).State);
        Assert.Equal(AdminPortState.Freed, (await helper.EndProcessAsync(Endpoint)).State);   // the "No window" fallback and Force end
        Assert.Equal(new[] { "CloseProcess", "EndProcess" }, factory.Seen.Select(r => r.Verb));
        Assert.Equal(1, factory.Opens);
        Assert.False(factory.Finished.IsCompleted);   // the helper is still waiting for its next job
        await helper.EndPortSessionAsync();
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));   // Done ended the helper's session
        await helper.EndPortSessionAsync();           // nothing open: harmless
        await helper.EndProcessAsync(Endpoint);       // a later flow is a new session
        Assert.Equal(2, factory.Opens);
    }

    [Fact]
    public async Task The_session_ends_with_Done_when_it_has_been_idle_and_not_before()
    {
        var clock = new Clock();
        await using var factory = new Factory((_, r) => new AdminReply(r.Id, "Done"));
        using var helper = new AdminHelper(factory, NullLogger.Instance, portIdle: TimeSpan.FromSeconds(50), time: clock);
        await helper.CloseProcessAsync(Endpoint);
        clock.Advance(TimeSpan.FromSeconds(49));
        await helper.EndProcessAsync(Endpoint);   // inside the idle time: reused, and the idle clock starts over
        Assert.Equal(1, factory.Opens);
        clock.Advance(TimeSpan.FromSeconds(49));
        await helper.EndProcessAsync(Endpoint);
        Assert.Equal(1, factory.Opens);
        clock.Advance(TimeSpan.FromSeconds(50));
        clock.FireTimers();
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));   // the idle timer sent Done
        await helper.EndProcessAsync(Endpoint);
        Assert.Equal(2, factory.Opens);
    }

    [Fact]
    public async Task A_session_left_idle_past_the_limit_is_not_reused_even_if_its_timer_never_ran()
    {
        var clock = new Clock();
        await using var factory = new Factory((_, r) => new AdminReply(r.Id, "Done"));
        using var helper = new AdminHelper(factory, NullLogger.Instance, portIdle: TimeSpan.FromSeconds(50), time: clock);
        await helper.CloseProcessAsync(Endpoint);
        clock.Advance(TimeSpan.FromSeconds(51));
        await helper.EndProcessAsync(Endpoint);
        Assert.Equal(2, factory.Opens);
    }

    [Fact]
    public async Task A_helper_that_went_away_while_idle_costs_one_fresh_session_and_never_a_failure()
    {
        await using var factory = new Factory((_, r) => new AdminReply(r.Id, "Done"));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        await helper.CloseProcessAsync(Endpoint);
        factory.KillHelper();
        var result = await helper.EndProcessAsync(Endpoint).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.State == AdminPortState.Freed && factory.Opens == 2 || result is { State: AdminPortState.Failed, Reason: "Admin helper failed" }, result.ToString());
        // Either way the helper is usable again afterwards.
        Assert.Equal(AdminPortState.Freed, (await helper.EndProcessAsync(Endpoint).WaitAsync(TimeSpan.FromSeconds(10))).State);
    }

    [Fact]
    public async Task A_cancelled_wait_stops_waiting_and_drops_the_session_and_a_dead_pipe_is_failed()
    {
        using var hang = new ManualResetEventSlim();
        int calls = 0;
        await using var factory = new Factory((_, r) => { if (Interlocked.Increment(ref calls) == 1) hang.Wait(TimeSpan.FromSeconds(20)); return new AdminReply(r.Id, "Done"); });
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var result = await helper.EndProcessAsync(Endpoint, cancel.Token).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(AdminPortState.Cancelled, result.State);
        hang.Set();
        Assert.Equal(AdminPortState.Freed, (await helper.EndProcessAsync(Endpoint).WaitAsync(TimeSpan.FromSeconds(10))).State);
        Assert.Equal(2, factory.Opens);
    }

    [Fact]
    public async Task An_invalid_endpoint_is_refused_before_any_session_starts()
    {
        await using var factory = new Factory((_, r) => new AdminReply(r.Id, "Done"));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => helper.EndProcessAsync(Endpoint with { LocalAddress = "not an address" }));
        await Assert.ThrowsAsync<ArgumentException>(() => helper.StopServiceAsync("", Endpoint));
        Assert.Equal(0, factory.Opens);
    }

    [Fact]
    public async Task Decline_is_needs_admin_a_missing_helper_is_failed_and_neither_retries()
    {
        var declined = new RefusingFactory(new Win32Exception(1223));
        using var helper = new AdminHelper(declined, NullLogger.Instance);
        Assert.Equal(AdminPortState.NeedsAdmin, (await helper.EndProcessAsync(Endpoint)).State);
        Assert.Equal(1, declined.Opens);
        var absent = new RefusingFactory(new FileNotFoundException());
        using var missing = new AdminHelper(absent, NullLogger.Instance);
        var result = await missing.StopServiceAsync("Spooler", Endpoint);
        Assert.Equal((AdminPortState.Failed, "Stop failed"), (result.State, result.Text));
        Assert.Equal(1, absent.Opens);
    }

    [Fact]
    public async Task A_cancelled_call_never_opens_a_session()
    {
        await using var factory = new Factory((_, r) => new AdminReply(r.Id, "Done"));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var call = helper.EndProcessAsync(Endpoint, cancel.Token);
        var error = await Record.ExceptionAsync(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(error is OperationCanceledException || (error is null && (await call).State == AdminPortState.Cancelled));
        Assert.Equal(0, factory.Opens);
    }

    [Fact]
    public async Task A_wrong_id_or_state_in_the_reply_fails_the_action()
    {
        await using var wrongId = new Factory((_, _) => new AdminReply(Guid.NewGuid(), "Done"));
        using var helper = new AdminHelper(wrongId, NullLogger.Instance);
        Assert.Equal(AdminPortState.Failed, (await helper.EndProcessAsync(Endpoint)).State);
        await using var wrongState = new Factory((_, r) => new AdminReply(r.Id, "Refused"));
        using var other = new AdminHelper(wrongState, NullLogger.Instance);
        Assert.Equal(AdminPortState.Failed, (await other.EndProcessAsync(Endpoint)).State);
    }

    [Fact]
    public async Task A_silent_helper_times_out_and_releases_the_gate()
    {
        using var hang = new ManualResetEventSlim();
        await using var factory = new Factory((_, r) => { hang.Wait(TimeSpan.FromSeconds(20)); return new AdminReply(r.Id, "Done"); });
        using var helper = new AdminHelper(factory, NullLogger.Instance, portReplyCeiling: TimeSpan.FromMilliseconds(300));
        Assert.Equal(AdminPortState.Failed, (await helper.EndProcessAsync(Endpoint).WaitAsync(TimeSpan.FromSeconds(10))).State);
        hang.Set();
    }

    [Fact]
    public void Result_text_uses_the_approved_words()
    {
        Assert.Equal("Freed", new AdminPortResult(AdminPortVerb.End, AdminPortState.Freed).Text);
        Assert.Equal("Already closed", new AdminPortResult(AdminPortVerb.Close, AdminPortState.AlreadyClosed).Text);
        Assert.Equal("Changed", new AdminPortResult(AdminPortVerb.End, AdminPortState.Changed).Text);
        Assert.Equal("Kept", new AdminPortResult(AdminPortVerb.Stop, AdminPortState.Kept).Text);
        Assert.Equal("End failed", new AdminPortResult(AdminPortVerb.Close, AdminPortState.Failed).Text);
        Assert.Equal("End failed", new AdminPortResult(AdminPortVerb.End, AdminPortState.Failed).Text);
        Assert.Equal("Stop failed", new AdminPortResult(AdminPortVerb.Stop, AdminPortState.Failed).Text);
        Assert.Equal("Needs admin", new AdminPortResult(AdminPortVerb.End, AdminPortState.NeedsAdmin).Text);
    }

    [Fact]
    public async Task A_session_without_port_actions_fails_the_request_and_still_ends_cleanly()
    {
        var (client, helperEnd) = await Pair();
        using (client) using (helperEnd)
        {
            var run = new HelperSession(new NoUpdates()).RunAsync(helperEnd);
            Assert.Equal("Ready", (await Messages.ReadReplyAsync(client))!.State);
            var request = Valid();
            await Messages.WriteAsync(client, request);
            var reply = (await Messages.ReadReplyAsync(client))!;
            Assert.Equal((request.Id, "Failed"), (reply.Id, reply.State));
            await Messages.WriteAsync(client, new AdminRequest(Guid.NewGuid(), "Done"));
            Assert.Equal("Done", (await Messages.ReadReplyAsync(client))!.State);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task<(NamedPipeClientStream Client, NamedPipeServerStream Helper)> Pair()
    {
        string name = "chippi-test-" + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync(); await client.ConnectAsync(5000); await accept;
        return (client, server);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ms;
        private readonly List<Timer> _timers = [];
        public override long GetTimestamp() => Interlocked.Read(ref _ms);
        public override long TimestampFrequency => 1000;
        public void Advance(TimeSpan span) => Interlocked.Add(ref _ms, (long)span.TotalMilliseconds);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state);
            lock (_timers) _timers.Add(timer);
            return timer;
        }
        public void FireTimers() { Timer[] live; lock (_timers) live = [.. _timers]; foreach (var timer in live) timer.Fire(); }
        private sealed class Timer(TimerCallback callback, object? state) : ITimer
        {
            private volatile bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class NoUpdates : IPackageUpdater
    { public Task<AdminReply> UpdateAsync(AdminRequest request, CancellationToken ceiling) => throw new InvalidOperationException(); }
    private sealed class FakePorts(Func<int, AdminRequest, AdminReply> reply, Action<AdminRequest> seen) : IPortActions
    {
        private int _n;
        public Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct) { seen(request); return Task.FromResult(reply(++_n, request)); }
    }
    private sealed class RefusingFactory(Exception error) : IAdminSessionFactory
    {
        public int Opens;
        public Task<IAdminConnection> OpenAsync(CancellationToken ct) { Opens++; return Task.FromException<IAdminConnection>(error); }
    }
    private sealed class Factory(Func<int, AdminRequest, AdminReply> reply) : IAdminSessionFactory, IAsyncDisposable
    {
        public int Opens;
        public List<AdminRequest> Seen { get; } = [];
        public Task Finished { get; private set; } = Task.CompletedTask;
        private readonly List<IDisposable> _ends = [];
        private IDisposable? _lastHelperEnd;
        /// <summary>The helper's end of the last pipe closes, as if the helper had exited.</summary>
        public void KillHelper() => _lastHelperEnd?.Dispose();
        public async Task<IAdminConnection> OpenAsync(CancellationToken ct)
        {
            Opens++;
            var (client, server) = await Pair();
            _ends.Add(client); _ends.Add(server); _lastHelperEnd = client;
            Finished = new HelperSession(new NoUpdates(), ports: new FakePorts(reply, Seen.Add)).RunAsync(client);
            return new Connection(server);
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var end in _ends) end.Dispose();
            try { await Finished; } catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException) { }
        }
        private sealed class Connection(Stream stream) : IAdminConnection
        { public Stream Stream => stream; public ValueTask DisposeAsync() { stream.Dispose(); return ValueTask.CompletedTask; } }
    }
}
