// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Core.Admin;

namespace Chippi.Features.PortManager.Tests;

public sealed class EnderTests
{
    /// <summary>A clock only the fake delay moves, so the three-second wait takes no time.</summary>
    private sealed class Clock
    {
        public long Now;
        public int Waits;
        public Action? OnWait;
        public long Read() => Now;
        public Task Delay(TimeSpan span, CancellationToken ct)
        {
            Waits++;
            Now += (long)span.TotalMilliseconds;
            OnWait?.Invoke();
            return Task.CompletedTask;
        }
    }

    private static (FakeSystem System, PortRow Row, Clock Clock, PortEnder Ender, FakeAdmin Admin) Own(bool window = false, bool canTerminate = true)
    {
        var system = new FakeSystem().Process(4120, "node.exe", start: 555, canTerminate: canTerminate);
        system.Listen(3000, 4120);
        if (window) system.Windows.Add(4120);
        var clock = new Clock();
        var admin = new FakeAdmin();
        var ender = new PortEnder(system, admin, clock.Delay, clock.Read);
        return (system, new PortScanner(system).Scan().Single(), clock, ender, admin);
    }

    [Fact]
    public async Task A_listener_with_no_window_ends_at_once_with_its_confirmed_start_time()
    {
        var (system, row, _, ender, admin) = Own();
        var result = await ender.FreeAsync(row);
        Assert.Equal(PortEndState.Freed, result.State);
        Assert.Equal(["terminate 4120@555"], system.Calls);
        Assert.Empty(admin.Calls);
    }

    [Fact]
    public async Task A_process_with_a_window_is_asked_to_close_first_and_is_not_forced_when_it_obeys()
    {
        var (system, row, clock, ender, _) = Own(window: true);
        system.CloseEndsProcess = true;
        var result = await ender.FreeAsync(row);
        Assert.Equal(PortEndState.Freed, result.State);
        Assert.Equal(["close 4120@555"], system.Calls);
        Assert.Equal(0, clock.Waits);
    }

    [Fact]
    public async Task A_process_that_ignores_the_close_request_gets_three_seconds_then_Force_end_is_offered()
    {
        var (system, row, clock, ender, _) = Own(window: true);
        var first = await ender.FreeAsync(row);
        Assert.Equal(PortEndState.StillRunning, first.State);
        Assert.Equal(["close 4120@555"], system.Calls);
        Assert.InRange(clock.Now, 3000, 3100);
        // Nothing ended by itself: the second step is its own confirmed call.
        var forced = await ender.FreeAsync(row, force: true);
        Assert.Equal(PortEndState.Freed, forced.State);
        Assert.Equal(["close 4120@555", "terminate 4120@555"], system.Calls);
    }

    [Fact]
    public async Task A_process_that_closes_during_the_wait_is_freed_without_force()
    {
        var (system, row, clock, ender, _) = Own(window: true);
        clock.OnWait = () => { if (clock.Now >= 1000) system.Kill(4120); };
        var result = await ender.FreeAsync(row);
        Assert.Equal(PortEndState.Freed, result.State);
        Assert.Equal(["close 4120@555"], system.Calls);
    }

    [Fact]
    public async Task A_window_that_vanished_between_the_check_and_the_request_is_ended_at_once()
    {
        var (system, row, _, ender, _) = Own(window: true);
        system.CloseResult = new(EndOutcome.Failed, 1400);
        var result = await ender.FreeAsync(row);
        Assert.Equal(PortEndState.Freed, result.State);
        Assert.Equal(["close 4120@555", "terminate 4120@555"], system.Calls);
    }

    [Fact]
    public async Task A_new_process_on_the_same_pid_changes_the_row_and_ends_nothing()
    {
        var (system, row, _, ender, _) = Own();
        system.Process(4120, "node.exe", start: 999);
        var result = await ender.FreeAsync(row);
        Assert.Equal((PortEndState.Changed, "Changed"), (result.State, result.Text));
        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task The_port_handed_to_another_process_changes_the_row_and_ends_nothing()
    {
        var (system, row, _, ender, _) = Own();
        system.Endpoints.Clear();
        system.Process(5012, "node.exe", start: 777);
        system.Listen(3000, 5012);
        Assert.Equal(PortEndState.Changed, (await ender.FreeAsync(row)).State);
        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task A_gone_endpoint_or_process_is_already_closed()
    {
        var (system, row, _, ender, _) = Own();
        system.Endpoints.Clear();
        Assert.Equal("Already closed", (await ender.FreeAsync(row)).Text);
        var (system2, row2, _, ender2, _) = Own();
        system2.Processes[4120] = null;
        Assert.Equal(PortEndState.AlreadyClosed, (await ender2.FreeAsync(row2)).State);
        Assert.Empty(system.Calls);
        Assert.Empty(system2.Calls);
    }

    [Fact]
    public async Task A_holder_that_would_now_be_protected_is_kept_and_nothing_ends()
    {
        var (system, row, _, ender, _) = Own();
        system.Process(4120, "lsass.exe", start: 555);     // the name is reused for a protected image
        var result = await ender.FreeAsync(row);
        Assert.Equal((PortEndState.Changed, "Changed"), (result.State, result.Text));
        Assert.Empty(system.Calls);
    }

    [Theory]
    [InlineData(HolderKind.Windows, "Protected process")]
    [InlineData(HolderKind.Chippi, "Protected process")]
    [InlineData(HolderKind.Shared, "Shared Windows service")]
    public async Task Rows_with_no_verb_are_kept_whatever_a_caller_passes(HolderKind kind, string reason)
    {
        var (system, row, _, ender, admin) = Own();
        var forged = row with { Holder = row.Holder with { Kind = kind } };
        foreach (var result in new[] { await ender.FreeAsync(forged), await ender.FreeAsync(forged, force: true), await ender.StopAsync(forged) })
            Assert.Equal((PortEndState.Kept, reason), (result.State, result.Reason));
        Assert.Empty(system.Calls);
        Assert.Empty(admin.Calls);
    }

    [Fact]
    public async Task A_verbless_row_is_kept_even_for_an_own_process()
    {
        var (system, row, _, ender, _) = Own();
        var forged = row with { Verb = PortVerb.None };
        Assert.Equal(PortEndState.Kept, (await ender.FreeAsync(forged)).State);
        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task A_windows_failure_is_End_failed_and_a_child_that_keeps_the_socket_is_too()
    {
        var (system, row, _, ender, _) = Own();
        system.TerminateResult = new(EndOutcome.Failed, 5);
        var failed = await ender.FreeAsync(row);
        Assert.Equal(("End failed", "Windows error 5"), (failed.Text, failed.Reason));

        var (system2, row2, _, ender2, _) = Own();
        system2.TerminateEndsProcess = false;                // reports Done but the socket is still held
        var held = await ender2.FreeAsync(row2);
        Assert.Equal((PortEndState.Failed, "Port still held"), (held.State, held.Reason));
    }

    [Fact]
    public async Task An_elevated_process_goes_through_the_helper_with_the_confirmed_endpoint_and_never_directly()
    {
        var (system, row, _, ender, admin) = Own(canTerminate: false);
        admin.OnCall = _ => system.Kill(4120);
        var result = await ender.FreeAsync(row);
        Assert.Equal(PortEndState.Freed, result.State);
        Assert.Equal(["end 4120@555 Tcp 127.0.0.1:3000"], admin.Calls);
        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task An_elevated_process_with_a_window_is_closed_through_the_helper_then_forced_with_a_second_confirm()
    {
        var (system, row, clock, ender, admin) = Own(window: true, canTerminate: false);
        var first = await ender.FreeAsync(row);
        Assert.Equal(PortEndState.StillRunning, first.State);
        Assert.Equal(["close 4120@555 Tcp 127.0.0.1:3000"], admin.Calls);
        Assert.InRange(clock.Now, 3000, 3100);
        admin.OnCall = _ => system.Kill(4120);
        var forced = await ender.FreeAsync(row, force: true);
        Assert.Equal(PortEndState.Freed, forced.State);
        Assert.Equal(["close 4120@555 Tcp 127.0.0.1:3000", "end 4120@555 Tcp 127.0.0.1:3000"], admin.Calls);
        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task The_helpers_refusals_and_declined_prompt_come_back_as_their_words()
    {
        var (system, row, _, ender, admin) = Own(canTerminate: false);
        admin.Results.Enqueue(new(AdminPortVerb.End, AdminPortState.NeedsAdmin));
        Assert.Equal("Needs admin", (await ender.FreeAsync(row)).Text);
        admin.Results.Enqueue(new(AdminPortVerb.End, AdminPortState.Kept, "Protected process"));
        var kept = await ender.FreeAsync(row);
        Assert.Equal(("Kept", "Protected process"), (kept.Text, kept.Reason));
        admin.Results.Enqueue(new(AdminPortVerb.End, AdminPortState.Failed, "Admin helper failed"));
        Assert.Equal("End failed", (await ender.FreeAsync(row)).Text);
        admin.Results.Enqueue(new(AdminPortVerb.End, AdminPortState.Changed, "Changed since the scan"));
        Assert.Equal("Changed", (await ender.FreeAsync(row)).Text);
        admin.Results.Enqueue(new(AdminPortVerb.End, AdminPortState.AlreadyClosed));
        Assert.Equal("Already closed", (await ender.FreeAsync(row)).Text);
        admin.Results.Enqueue(new(AdminPortVerb.End, AdminPortState.Cancelled));
        Assert.Equal("Cancelled", (await ender.FreeAsync(row)).Text);
        Assert.Empty(system.Calls);
    }

    [Fact]
    public async Task An_elevated_row_with_no_helper_needs_admin_and_a_cancelled_call_is_cancelled()
    {
        var (system, row, clock, _, _) = Own(canTerminate: false);
        var noHelper = new PortEnder(system, null, clock.Delay, clock.Read);
        Assert.Equal(PortEndState.NeedsAdmin, (await noHelper.FreeAsync(row)).State);
        var own = new FakeSystem().Process(4120, "node.exe", start: 555);
        own.Listen(3000, 4120);
        own.Windows.Add(4120);
        // The wait after a close request is cancelled (the page closed): nothing more ends.
        var cancelling = new PortEnder(own, null, (_, _) => throw new OperationCanceledException(), clock.Read);
        Assert.Equal(PortEndState.Cancelled, (await cancelling.FreeAsync(new PortScanner(own).Scan().Single())).State);
        Assert.Equal(["close 4120@555"], own.Calls);
    }

    private static (FakeSystem System, PortRow Row, PortEnder Ender, FakeAdmin Admin) Service()
    {
        var system = new FakeSystem().Process(2880, "svchost.exe", start: 321, canTerminate: false);
        system.Services.Add(new("Spooler", "Print Spooler", 2880));
        system.Services.Add(new("Fax", "Fax", 2880));
        var e = system.Listen(9100, 2880, "0.0.0.0");
        system.Owners[e.Key] = "Spooler";
        var admin = new FakeAdmin();
        var clock = new Clock();
        return (system, new PortScanner(system).Scan().Single(), new PortEnder(system, admin, clock.Delay, clock.Read), admin);
    }

    [Fact]
    public async Task Stop_asks_the_helper_to_stop_only_that_service_and_never_ends_the_host()
    {
        var (system, row, ender, admin) = Service();
        admin.OnCall = _ => system.Endpoints.Clear();
        var result = await ender.StopAsync(row);
        Assert.Equal(PortEndState.Freed, result.State);
        Assert.Equal(["stop Spooler 2880@321 0.0.0.0:9100"], admin.Calls);
        Assert.DoesNotContain(system.Calls, c => c.StartsWith("terminate") || c.StartsWith("close"));
        // Free on a service row is refused: it would end the host.
        Assert.Equal(PortEndState.Kept, (await ender.FreeAsync(row)).State);
    }

    [Fact]
    public async Task Stop_is_refused_when_the_attribution_changed_since_the_scan()
    {
        var (system, row, ender, admin) = Service();
        system.Owners[system.Endpoints[0].Key] = "Fax";
        var result = await ender.StopAsync(row);
        Assert.Equal((PortEndState.Changed, "Changed"), (result.State, result.Text));
        Assert.Empty(admin.Calls);
    }

    [Fact]
    public async Task Stop_failed_is_Stop_failed_and_a_service_that_stopped_but_left_the_port_is_too()
    {
        var (_, row, ender, admin) = Service();
        admin.Results.Enqueue(new(AdminPortVerb.Stop, AdminPortState.Failed, "Dependent services running"));
        var failed = await ender.StopAsync(row);
        Assert.Equal(("Stop failed", "Dependent services running"), (failed.Text, failed.Reason));
        var held = await ender.StopAsync(row);   // the helper says Freed, the port is still held by the same process
        Assert.Equal((PortEndState.Failed, "Port still held"), (held.State, held.Reason));
    }

    [Fact]
    public void The_endpoint_sent_to_the_helper_carries_protocol_address_port_pid_and_start_time()
    {
        var system = new FakeSystem().Process(14220, "chrome.exe", start: 42);
        system.Listen(5353, 14220, "::1", PortProtocol.Udp);
        var row = new PortScanner(system).Scan().Single();
        Assert.Equal(new AdminEndpoint(AdminProtocol.Udp, "::1", 5353, 14220, 42), PortEnder.Endpoint(row));
    }
}
