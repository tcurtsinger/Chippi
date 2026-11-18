// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Net;
using System.Net.Sockets;
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated.Tests;

/// <summary>PortActions on a fake system: nothing here touches a real process or service.</summary>
public sealed class PortActionTests
{
    private const long Start = 133_000_000_000_000_000;
    private const int Self = 9000, Peer = 9001, Node = 4120, Host = 1500;

    [Fact]
    public void Close_posts_to_a_matching_unprotected_process()
    {
        var fake = new Fake();
        Assert.Equal("Done", Run(fake, Request(Messages.CloseProcess, 3000, Node)).State);
        Assert.Equal(["Close " + Node], fake.Actions);
    }

    [Fact]
    public void End_terminates_with_the_start_time_the_owner_confirmed()
    {
        var fake = new Fake();
        Assert.Equal("Done", Run(fake, Request(Messages.EndProcess, 3000, Node)).State);
        Assert.Equal([$"End {Node}@{Start}"], fake.Actions);
    }

    [Theory] [InlineData(0)] [InlineData(4)] [InlineData(Self)] [InlineData(Peer)]
    public void Pid_zero_four_the_helper_and_chippi_are_kept_without_reading_anything(int pid)
    {
        var fake = new Fake();
        var reply = Run(fake, Request(Messages.EndProcess, 3000, pid));
        Assert.Equal(("Kept", PortActions.ProtectedReason), (reply.State, reply.Reason));
        Assert.Equal(0, fake.Reads); Assert.Empty(fake.Actions);
    }

    [Theory]
    [InlineData("csrss")] [InlineData("smss")] [InlineData("wininit")] [InlineData("winlogon")] [InlineData("lsass")] [InlineData("services")]
    [InlineData("svchost")] [InlineData("MsMpEng")] [InlineData("NisSrv")] [InlineData("LSASS")]
    public void Protected_names_are_kept_for_close_and_end(string name)
    {
        foreach (string verb in new[] { Messages.CloseProcess, Messages.EndProcess })
        {
            var fake = new Fake { Process = new ProcessIdentity(Start, name, @"C:\Windows\System32\" + name + ".exe") };
            var reply = Run(fake, Request(verb, 3000, Node));
            Assert.Equal(("Kept", PortActions.ProtectedReason), (reply.State, reply.Reason));
            Assert.Empty(fake.Actions);
        }
    }

    [Fact]
    public void A_process_that_cannot_be_read_is_protected()
    {
        var fake = new Fake { Process = new ProcessIdentity(0, null, null, Unreadable: true) };
        Assert.Equal("Kept", Run(fake, Request(Messages.EndProcess, 3000, Node)).State);
        Assert.Empty(fake.Actions);
    }

    [Fact]
    public void Chippi_by_its_exe_is_kept_even_under_another_pid()
    {
        var fake = new Fake { Process = new ProcessIdentity(Start, "Chippi", @"C:\App\Chippi.exe") };
        var reply = Run(fake, Request(Messages.EndProcess, 3000, Node), chippiPath: @"c:\app\chippi.EXE");
        Assert.Equal("Kept", reply.State); Assert.Empty(fake.Actions);
    }

    [Fact]
    public void A_reused_pid_with_another_start_time_is_changed()
    {
        var fake = new Fake { Process = new ProcessIdentity(Start + 10, "node", @"C:\node.exe") };
        var reply = Run(fake, Request(Messages.EndProcess, 3000, Node));
        Assert.Equal(("Changed", PortActions.ChangedReason), (reply.State, reply.Reason));
        Assert.Empty(fake.Actions);
    }

    [Fact]
    public void A_port_handed_to_another_process_is_changed()
    {
        var fake = new Fake { Rows = [new(PortProtocol.Tcp, IPAddress.Loopback, 3000, 777)] };
        var reply = Run(fake, Request(Messages.EndProcess, 3000, Node));
        Assert.Equal("Changed", reply.State); Assert.Empty(fake.Actions);
    }

    [Fact]
    public void A_connection_sharing_the_local_port_with_another_pid_does_not_hide_the_match()
    {
        var fake = new Fake { Rows = [new(PortProtocol.Tcp, IPAddress.Loopback, 3000, 777), new(PortProtocol.Tcp, IPAddress.Loopback, 3000, Node)] };
        Assert.Equal("Done", Run(fake, Request(Messages.EndProcess, 3000, Node)).State);
    }

    [Fact]
    public void An_endpoint_that_is_gone_or_a_process_that_exited_is_already_closed()
    {
        var gone = new Fake { Rows = [] };
        Assert.Equal("Gone", Run(gone, Request(Messages.EndProcess, 3000, Node)).State);
        var exited = new Fake { Process = null };
        Assert.Equal("Gone", Run(exited, Request(Messages.CloseProcess, 3000, Node)).State);
        Assert.Empty(gone.Actions); Assert.Empty(exited.Actions);
    }

    [Fact]
    public void The_endpoint_must_match_address_family_protocol_and_port()
    {
        var other = new Fake { Rows = [new(PortProtocol.Tcp, IPAddress.Parse("10.0.0.5"), 3000, Node)] };
        Assert.Equal("Gone", Run(other, Request(Messages.EndProcess, 3000, Node)).State);
        var udp = new Fake { Rows = [new(PortProtocol.Udp, IPAddress.Loopback, 3000, Node)] };
        Assert.Equal("Gone", Run(udp, Request(Messages.EndProcess, 3000, Node)).State);
        var v6 = new Fake { Rows = [new(PortProtocol.Tcp, IPAddress.IPv6Loopback, 3000, Node)] };
        Assert.Equal("Done", Run(v6, Request(Messages.EndProcess, 3000, Node, address: "::1")).State);
        Assert.Equal("Gone", Run(v6, Request(Messages.EndProcess, 3000, Node, address: "127.0.0.1")).State);
        var scoped = new Fake { Rows = [new(PortProtocol.Tcp, IPAddress.Parse("fe80::1%5"), 3000, Node)] };
        Assert.Equal("Done", Run(scoped, Request(Messages.EndProcess, 3000, Node, address: "fe80::1%5")).State);
        Assert.Equal("Gone", Run(scoped, Request(Messages.EndProcess, 3000, Node, address: "fe80::1%6")).State);
    }

    [Fact]
    public void A_udp_endpoint_is_read_from_the_udp_table()
    {
        var fake = new Fake { Rows = [new(PortProtocol.Udp, IPAddress.Any, 5353, Node)] };
        var request = Request(Messages.EndProcess, 5353, Node, address: "0.0.0.0") with { Protocol = "Udp" };
        Assert.Equal("Done", Run(fake, request).State);
    }

    [Fact]
    public void Windows_refusals_become_failed_with_no_state_change()
    {
        var fake = new Fake { Native = new NativeResult(NativeOutcome.Failed, 5) };
        Assert.Equal("Failed", Run(fake, Request(Messages.EndProcess, 3000, Node)).State);
        var none = new Fake { Native = new NativeResult(NativeOutcome.Failed, PortActions.NoWindowError) };
        var reply = Run(none, Request(Messages.CloseProcess, 3000, Node));
        Assert.Equal(("Failed", PortActions.NoWindowReason), (reply.State, reply.Reason));
        var raced = new Fake { Native = new NativeResult(NativeOutcome.Changed) };
        Assert.Equal("Changed", Run(raced, Request(Messages.EndProcess, 3000, Node)).State);
    }

    // StopService

    private static Fake Svchost(params ServiceEntry[] services) => new()
    {
        Pid = Host,
        Process = new ProcessIdentity(Start, "svchost", @"C:\Windows\System32\svchost.exe"),
        Services = services,
    };
    private static ServiceEntry Service(string name, int pid = Host, uint state = 4, uint controls = 1, uint type = 0x20) => new(name, type, state, controls, pid);

    [Fact]
    public void Stop_passes_when_the_owner_module_names_the_service_even_in_a_shared_host()
    {
        var fake = Svchost(Service("Spooler"), Service("Other")); fake.Owner = "Spooler";
        Assert.Equal("Done", Run(fake, Stop("Spooler", 3000, Host)).State);
        Assert.Equal(["Stop Spooler"], fake.Actions);
    }

    [Fact]
    public void Stop_of_a_service_that_is_not_the_endpoints_owner_is_refused_even_in_the_same_pid()
    {
        var fake = Svchost(Service("A"), Service("B")); fake.Owner = "A";
        var reply = Run(fake, Stop("B", 3000, Host));
        Assert.Equal(("Kept", PortActions.SharedReason), (reply.State, reply.Reason));
        Assert.Empty(fake.Actions);
    }

    [Fact]
    public void Stop_with_no_owner_name_needs_exactly_one_running_service_in_the_pid()
    {
        var two = Svchost(Service("A"), Service("B"));
        Assert.Equal("Kept", Run(two, Stop("A", 3000, Host)).State);
        Assert.Equal("Kept", Run(two, Stop("B", 3000, Host)).State);
        Assert.Empty(two.Actions);
        var one = Svchost(Service("A"), Service("B", pid: 77));
        Assert.Equal("Done", Run(one, Stop("A", 3000, Host)).State);
        var stoppedNeighbour = Svchost(Service("A"), Service("B", state: 1));
        Assert.Equal("Done", Run(stoppedNeighbour, Stop("A", 3000, Host)).State);
    }

    [Theory] [InlineData("RpcSs")] [InlineData("BFE")] [InlineData("WinDefend")] [InlineData("schedule")] [InlineData("gpsvc")]
    public void Core_windows_services_are_never_stopped(string name)
    {
        var fake = Svchost(Service(name)); fake.Owner = name;
        var reply = Run(fake, Stop(name, 3000, Host));
        Assert.Equal(("Kept", PortActions.CoreServiceReason), (reply.State, reply.Reason));
        Assert.Empty(fake.Actions);
    }

    [Fact]
    public void Stop_refuses_drivers_services_that_do_not_accept_stop_and_services_in_other_hosts()
    {
        var driver = Svchost(Service("Drv", type: 0x1)); driver.Owner = "Drv";
        Assert.Equal("Kept", Run(driver, Stop("Drv", 3000, Host)).State);
        var noStop = Svchost(Service("A", controls: 0)); noStop.Owner = "A";
        Assert.Equal("Kept", Run(noStop, Stop("A", 3000, Host)).State);
        var moved = Svchost(Service("A", pid: 88)); moved.Owner = "A";
        Assert.Equal("Changed", Run(moved, Stop("A", 3000, Host)).State);
        var missing = Svchost(); missing.Owner = "A";
        Assert.Equal("Kept", Run(missing, Stop("A", 3000, Host)).State);
        Assert.Empty(driver.Actions.Concat(noStop.Actions).Concat(moved.Actions).Concat(missing.Actions));
    }

    [Fact]
    public void Stop_still_applies_the_protected_names_to_other_processes_and_never_ends_the_host()
    {
        var lsass = new Fake { Process = new ProcessIdentity(Start, "lsass", @"C:\Windows\System32\lsass.exe"), Services = [Service("A", pid: Node)], Owner = "A" };
        Assert.Equal("Kept", Run(lsass, Stop("A", 3000, Node)).State);
        var host = Svchost(Service("A")); host.Owner = "A";
        Run(host, Stop("A", 3000, Host));
        Assert.DoesNotContain(host.Actions, a => a.StartsWith("End") || a.StartsWith("Close"));
    }

    [Fact]
    public void Stop_maps_windows_errors()
    {
        var dependents = Svchost(Service("A")); dependents.Owner = "A"; dependents.Native = new(NativeOutcome.Failed, PortActions.DependentServicesRunning);
        var reply = Run(dependents, Stop("A", 3000, Host));
        Assert.Equal(("Failed", "Dependent services running"), (reply.State, reply.Reason));
        var stopped = Svchost(Service("A")); stopped.Owner = "A"; stopped.Native = new(NativeOutcome.Failed, PortActions.ServiceNotActive);
        Assert.Equal("Gone", Run(stopped, Stop("A", 3000, Host)).State);
    }

    [Fact]
    public void A_process_at_a_protection_level_is_kept_for_close_and_end_though_limited_access_opened_it()
    {
        foreach (string verb in new[] { Messages.CloseProcess, Messages.EndProcess })
        {
            var fake = new Fake { Process = new ProcessIdentity(Start, "av", @"C:\av\av.exe", Protected: true) };
            var reply = Run(fake, Request(verb, 3000, Node));
            Assert.Equal(("Kept", PortActions.ProtectedReason), (reply.State, reply.Reason));
            Assert.Empty(fake.Actions);
        }
    }

    [Fact]
    public void A_process_hosting_any_service_is_never_closed_or_ended_directly()
    {
        foreach (string verb in new[] { Messages.CloseProcess, Messages.EndProcess })
        {
            var fake = new Fake { Services = [Service("Other", pid: 77), Service("Mine", pid: Node)] };
            var reply = Run(fake, Request(verb, 3000, Node));
            Assert.Equal(("Kept", PortActions.ServiceHostReason), (reply.State, reply.Reason));
            Assert.Empty(fake.Actions);
        }
        // A service in another process does not keep this one.
        var other = new Fake { Services = [Service("Other", pid: 77)] };
        Assert.Equal("Done", Run(other, Request(Messages.EndProcess, 3000, Node)).State);
    }

    [Fact]
    public async Task Any_unexpected_exception_becomes_a_failed_reply_and_nothing_of_it_is_sent()
    {
        var fake = new Fake { ThrowOnRead = true, Exception = new ArgumentOutOfRangeException("scopeid", "secret detail") };
        var reply = await new PortActions(fake, [Self]).RunAsync(Request(Messages.EndProcess, 3000, Node), default);
        Assert.Equal(("Failed", PortActions.FailedReason), (reply.State, reply.Reason));
        Assert.Empty(fake.Actions);
        var services = new Fake { Services = null!, Exception = new NullReferenceException() };
        var second = await new PortActions(services, [Self]).RunAsync(Request(Messages.CloseProcess, 3000, Node), default);
        Assert.Equal("Failed", second.State);
    }

    [Fact]
    public async Task A_native_failure_while_reading_becomes_a_failed_reply()
    {
        var fake = new Fake { ThrowOnRead = true };
        var reply = await new PortActions(fake, [Self]).RunAsync(Request(Messages.EndProcess, 3000, Node), default);
        Assert.Equal("Failed", reply.State);
    }

    // The real table reader, read-only, on a listener this test opens. It never calls an action.

    [Fact]
    public void The_real_reader_finds_a_loopback_listener_this_test_opened()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var system = new NativePortSystem();
        var row = system.ReadEndpoints(PortProtocol.Tcp, AddressFamily.InterNetwork).Single(e => e.Port == port && e.Address.Equals(IPAddress.Loopback));
        Assert.Equal(Environment.ProcessId, row.Pid);
        _ = system.OwnerModule(row);
        var me = system.Identify(Environment.ProcessId)!;
        Assert.Equal(System.Diagnostics.Process.GetCurrentProcess().StartTime.ToFileTime(), me.StartTime);
        Assert.NotNull(me.Name);
        Assert.False(me.Protected);   // the test host is an ordinary process
        Assert.Null(system.Identify(int.MaxValue / 2));
        _ = system.ReadEndpoints(PortProtocol.Udp, AddressFamily.InterNetwork);
        _ = system.ReadEndpoints(PortProtocol.Udp, AddressFamily.InterNetworkV6);
        _ = system.ReadEndpoints(PortProtocol.Tcp, AddressFamily.InterNetworkV6);
        Assert.Contains(system.RunningServices(), s => s.Pid > 0);
    }

    [Fact]
    public void The_real_reader_reads_a_udp_socket_this_test_opened()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        var row = new NativePortSystem().ReadEndpoints(PortProtocol.Udp, AddressFamily.InterNetwork).Single(e => e.Port == port);
        Assert.Equal(Environment.ProcessId, row.Pid);
    }

    [Fact]
    public void The_real_reader_reads_ipv6_tcp_and_udp_rows_this_test_opened()
    {
        using var listener = new TcpListener(IPAddress.IPv6Loopback, 0); listener.Start();
        int tcp = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        int udp = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        var system = new NativePortSystem();
        var t = system.ReadEndpoints(PortProtocol.Tcp, AddressFamily.InterNetworkV6).Single(e => e.Port == tcp && e.Address.Equals(IPAddress.IPv6Loopback));
        var u = system.ReadEndpoints(PortProtocol.Udp, AddressFamily.InterNetworkV6).Single(e => e.Port == udp && e.Address.Equals(IPAddress.IPv6Loopback));
        Assert.Equal((Environment.ProcessId, Environment.ProcessId), (t.Pid, u.Pid));
        _ = system.OwnerModule(t); _ = system.OwnerModule(u);
    }

    // helpers

    private static AdminRequest Request(string verb, int port, int pid, string address = "127.0.0.1", string? service = null) =>
        new(Guid.NewGuid(), verb, Protocol: "Tcp", LocalAddress: address, LocalPort: port, Pid: pid, StartTime: Start, ServiceName: service);
    private static AdminRequest Stop(string name, int port, int pid) => Request(Messages.StopService, port, pid, service: name);
    private static AdminReply Run(Fake fake, AdminRequest request, string? chippiPath = null) => new PortActions(fake, [Self, Peer], chippiPath).Run(request);

    private sealed class Fake : IPortSystem
    {
        public IReadOnlyList<PortEntry>? Rows;
        public int Pid = Node;
        public ProcessIdentity? Process = new(Start, "node", @"C:\node\node.exe");
        public IReadOnlyList<ServiceEntry> Services = [];
        public string? Owner;
        public NativeResult Native = new(NativeOutcome.Done);
        public bool ThrowOnRead;
        public Exception? Exception;
        public int Reads;
        public List<string> Actions { get; } = [];
        public IReadOnlyList<PortEntry> ReadEndpoints(PortProtocol protocol, AddressFamily family)
        {
            Reads++;
            if (ThrowOnRead) throw Exception ?? new System.ComponentModel.Win32Exception(5);
            var rows = Rows ?? [new(PortProtocol.Tcp, IPAddress.Loopback, 3000, Pid)];
            return rows.Where(r => r.Protocol == protocol && r.Address.AddressFamily == family).ToArray();
        }
        public string? OwnerModule(PortEntry entry) => Owner;
        public ProcessIdentity? Identify(int pid) => Process;
        public NativeResult PostClose(int pid, long startTime) { Actions.Add("Close " + pid); return Native; }
        public NativeResult Terminate(int pid, long startTime) { Actions.Add($"End {pid}@{startTime}"); return Native; }
        public IReadOnlyList<ServiceEntry> RunningServices() => Exception is not null && Services is null ? throw Exception : Services;
        public NativeResult StopService(string name) { Actions.Add("Stop " + name); return Native; }
    }
}
