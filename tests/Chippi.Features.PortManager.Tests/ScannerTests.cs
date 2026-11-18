// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.PortManager.Tests;

public sealed class ScannerTests
{
    private static PortRow Only(FakeSystem system, int port) => new PortScanner(system).Scan(e => e.LocalPort == port).Single();

    [Fact]
    public void Own_process_listening_gets_Free_without_admin()
    {
        var s = new FakeSystem().Process(4120, "node.exe", canTerminate: true);
        s.Listen(3000, 4120);
        var row = Only(s, 3000);
        Assert.Equal((HolderKind.Own, PortVerb.Free, false, "node.exe"), (row.Holder.Kind, row.Verb, row.NeedsAdmin, row.Holder.Name));
        Assert.Equal(1000, row.Holder.StartTime);
    }

    [Fact]
    public void An_elevated_or_other_users_process_gets_Free_with_the_shield()
    {
        var s = new FakeSystem().Process(3340, "postgres.exe", canTerminate: false);
        s.Listen(5432, 3340, "0.0.0.0");
        var row = Only(s, 5432);
        Assert.Equal((HolderKind.Elevated, PortVerb.Free, true), (row.Holder.Kind, row.Verb, row.NeedsAdmin));
    }

    [Fact]
    public void A_service_whose_owner_module_names_it_gets_Stop_even_in_a_shared_host()
    {
        var s = new FakeSystem().Process(2880, "svchost.exe", canTerminate: false);
        s.Services.Add(new("Spooler", "Print Spooler", 2880));
        s.Services.Add(new("Fax", "Fax", 2880));
        var e = s.Listen(9100, 2880, "0.0.0.0");
        s.Owners[e.Key] = "Spooler";
        var row = Only(s, 9100);
        Assert.Equal((HolderKind.Service, PortVerb.Stop, true, "Spooler", "Print Spooler"), (row.Holder.Kind, row.Verb, row.NeedsAdmin, row.ServiceName, row.ServiceDisplay));
    }

    [Fact]
    public void Two_services_in_one_svchost_with_no_attribution_is_ambiguous_with_no_verb()
    {
        var s = new FakeSystem().Process(1124, "svchost.exe", canTerminate: false);
        s.Services.Add(new("RpcSs", "Remote Procedure Call", 1124));
        s.Services.Add(new("RpcEptMapper", "RPC Endpoint Mapper", 1124));
        s.Listen(135, 1124, "0.0.0.0");
        var row = Only(s, 135);
        Assert.Equal((HolderKind.Shared, PortVerb.None, null), (row.Holder.Kind, row.Verb, row.ServiceName));
    }

    [Fact]
    public void An_owner_module_that_is_no_service_of_the_process_does_not_guess_among_several()
    {
        var s = new FakeSystem().Process(1124, "svchost.exe", canTerminate: false);
        s.Services.Add(new("A", "A", 1124));
        s.Services.Add(new("B", "B", 1124));
        var e = s.Listen(8000, 1124);
        s.Owners[e.Key] = "something.dll";
        Assert.Equal((HolderKind.Shared, PortVerb.None), (Only(s, 8000).Holder.Kind, Only(s, 8000).Verb));
    }

    [Fact]
    public void A_process_hosting_exactly_one_service_owns_its_endpoints_when_the_stack_gives_no_name()
    {
        var s = new FakeSystem().Process(2644, "spoolsv.exe", canTerminate: false);
        s.Services.Add(new("Spooler", "Print Spooler", 2644));
        s.Listen(9100, 2644, "0.0.0.0");
        var row = Only(s, 9100);
        Assert.Equal((HolderKind.Service, PortVerb.Stop, "Spooler"), (row.Holder.Kind, row.Verb, row.ServiceName));
    }

    [Fact]
    public void A_core_windows_service_has_no_verb_and_never_the_host()
    {
        var s = new FakeSystem().Process(900, "svchost.exe", canTerminate: false);
        s.Services.Add(new("Dnscache", "DNS Client", 900));
        s.Listen(53, 900, "0.0.0.0", PortProtocol.Udp);
        var row = Only(s, 53);
        Assert.Equal((HolderKind.Windows, PortVerb.None, "Dnscache"), (row.Holder.Kind, row.Verb, row.ServiceName));
    }

    [Fact]
    public void A_svchost_with_no_known_service_is_Windows_not_an_own_process()
    {
        var s = new FakeSystem().Process(900, "svchost.exe", canTerminate: true);
        s.Listen(5040, 900, "0.0.0.0");
        Assert.Equal((HolderKind.Windows, PortVerb.None), (Only(s, 5040).Holder.Kind, Only(s, 5040).Verb));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void System_and_a_closing_connection_have_no_verb_whatever_the_row(int pid)
    {
        var s = new FakeSystem();
        s.Listen(445, pid, "0.0.0.0");
        var row = Only(s, 445);
        Assert.Equal((HolderKind.Windows, PortVerb.None), (row.Holder.Kind, row.Verb));
    }

    [Theory]
    [InlineData("csrss.exe")]
    [InlineData("wininit.exe")]
    [InlineData("LSASS.EXE")]
    [InlineData("services.exe")]
    [InlineData("smss.exe")]
    [InlineData("MsMpEng.exe")]
    [InlineData("dwm.exe")]
    [InlineData("winlogon.exe")]
    public void The_never_ended_list_has_no_verb_even_when_the_user_could_end_it(string name)
    {
        var s = new FakeSystem().Process(876, name, canTerminate: true);
        s.Listen(49664, 876, "0.0.0.0");
        Assert.Equal((HolderKind.Windows, PortVerb.None), (Only(s, 49664).Holder.Kind, Only(s, 49664).Verb));
    }

    [Fact]
    public void A_process_windows_will_not_open_is_Windows_and_a_gone_one_has_no_verb()
    {
        var s = new FakeSystem().Process(100, "System", unreadable: true);
        s.Listen(1, 100);
        s.Listen(2, 101);
        Assert.Equal(HolderKind.Windows, Only(s, 1).Holder.Kind);
        Assert.Equal((HolderKind.Gone, PortVerb.None), (Only(s, 2).Holder.Kind, Only(s, 2).Verb));
    }

    [Fact]
    public void Chippi_itself_by_pid_or_path_has_no_verb()
    {
        var s = new FakeSystem().Process(6204, "Chippi.exe", path: @"C:\Chippi\Chippi.exe").Process(7000, "Chippi.exe", path: @"c:\chippi\CHIPPI.EXE");
        s.Listen(47100, 6204);
        s.Listen(47101, 7000);
        Assert.Equal((HolderKind.Chippi, PortVerb.None), (Only(s, 47100).Holder.Kind, Only(s, 47100).Verb));
        Assert.Equal((HolderKind.Chippi, PortVerb.None), (Only(s, 47101).Holder.Kind, Only(s, 47101).Verb));
    }

    [Fact]
    public void Connections_and_time_wait_never_carry_a_verb()
    {
        var s = new FakeSystem().Process(14220, "chrome.exe");
        s.Connect(49811, 14220, TcpState.Established);
        s.Connect(50122, 0, TcpState.TimeWait);
        s.Listen(5353, 14220, "0.0.0.0", PortProtocol.Udp);
        Assert.Equal(PortVerb.None, Only(s, 49811).Verb);
        Assert.Equal(PortVerb.None, Only(s, 50122).Verb);
        Assert.Equal(PortVerb.Free, Only(s, 5353).Verb);   // a bound UDP endpoint has one
    }

    [Fact]
    public void A_process_on_several_ports_is_one_holder_with_a_port_count()
    {
        var s = new FakeSystem().Process(14220, "chrome.exe");
        s.Listen(3000, 14220);
        s.Listen(3000, 14220, "::1");           // IPv4 and IPv6: the same port
        s.Listen(5353, 14220, "0.0.0.0", PortProtocol.Udp);
        s.Listen(9222, 14220);
        s.Connect(49811, 14220, TcpState.Established);   // a connection is not a port it holds
        var rows = new PortScanner(s).Scan();
        Assert.All(rows, r => Assert.Equal(3, r.Holder.PortCount));
        Assert.Equal(5, rows.Count);
        Assert.Equal([3000, 3000, 5353, 9222, 49811], rows.Select(r => r.Endpoint.LocalPort));
    }

    [Fact]
    public void Owner_lookup_runs_only_for_held_rows_of_service_hosts()
    {
        var s = new FakeSystem().Process(2880, "svchost.exe", canTerminate: false).Process(4120, "node.exe");
        s.Services.Add(new("Spooler", "Print Spooler", 2880));
        s.Services.Add(new("Fax", "Fax", 2880));
        s.Listen(3000, 4120);
        s.Connect(52000, 2880, TcpState.Established);
        new PortScanner(s).Scan();
        Assert.Empty(s.Calls);
    }

    [Fact]
    public void A_failing_process_or_service_read_does_not_fail_the_scan()
    {
        var s = new FakeSystem().Process(4120, "node.exe");
        s.Listen(3000, 4120);
        var scanner = new PortScanner(new Throwing(s));
        var row = Assert.Single(scanner.Scan());
        Assert.Equal((HolderKind.Windows, PortVerb.None), (row.Holder.Kind, row.Verb));   // an unreadable process is treated as protected
    }

    private sealed class Throwing(FakeSystem inner) : IPortSystem
    {
        public int CurrentPid => inner.CurrentPid;
        public string? CurrentPath => inner.CurrentPath;
        public IReadOnlyList<PortEndpoint> ReadEndpoints() => inner.ReadEndpoints();
        public string? OwnerModule(PortEndpoint endpoint) => inner.OwnerModule(endpoint);
        public ProcessInfo? ReadProcess(int pid, ProcessNames? names = null) => throw new System.ComponentModel.Win32Exception(5);
        public IReadOnlyList<ServiceInfo> RunningServices() => throw new System.ComponentModel.Win32Exception(5);
        public bool HasWindow(int pid) => inner.HasWindow(pid);
        public EndResult PostClose(int pid, long startTime) => inner.PostClose(pid, startTime);
        public EndResult Terminate(int pid, long startTime) => inner.Terminate(pid, startTime);
    }
}
