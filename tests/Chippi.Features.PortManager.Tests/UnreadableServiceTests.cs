// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;

namespace Chippi.Features.PortManager.Tests;

/// <summary>
/// A service running as LocalSystem or NetworkService is a process the owner's standard token can't open (the Sandbox run, 2026-11-18): its
/// start time comes from the process list, and the service list alone attributes it, so Stop is still offered.
/// </summary>
public sealed class UnreadableServiceTests
{
    private static PortRow Only(FakeSystem system, int port) => new PortScanner(system).Scan(e => e.LocalPort == port).Single();

    [Fact]
    public void An_unreadable_process_hosting_one_service_gets_Stop_with_the_listed_start_time()
    {
        var s = new FakeSystem();
        s.Processes[3380] = new ProcessInfo(3380, 133_000_000_000_000_000, "postgres.exe", null, Unreadable: true);
        s.Services.Add(new("postgresql-x64-17", "PostgreSQL", 3380));
        s.Listen(5432, 3380, "0.0.0.0");
        var row = Only(s, 5432);
        Assert.Equal((HolderKind.Service, PortVerb.Stop, "postgresql-x64-17", 133_000_000_000_000_000L),
            (row.Holder.Kind, row.Verb, row.ServiceName, row.Holder.StartTime));
    }

    [Fact]
    public void An_unreadable_service_process_without_a_start_time_has_no_verb()
    {
        var s = new FakeSystem();
        s.Processes[3380] = new ProcessInfo(3380, 0, "postgres.exe", null, Unreadable: true);
        s.Services.Add(new("postgresql-x64-17", "PostgreSQL", 3380));
        s.Listen(5432, 3380, "0.0.0.0");
        Assert.Equal((HolderKind.Windows, PortVerb.None), (Only(s, 5432).Holder.Kind, Only(s, 5432).Verb));
    }

    [Fact]
    public void An_unreadable_process_that_is_no_service_still_has_no_verb()
    {
        var s = new FakeSystem();
        s.Processes[3380] = new ProcessInfo(3380, 133_000_000_000_000_000, "agent.exe", null, Unreadable: true);
        s.Listen(7000, 3380, "0.0.0.0");
        Assert.Equal((HolderKind.Windows, PortVerb.None), (Only(s, 7000).Holder.Kind, Only(s, 7000).Verb));
    }

    [Fact]
    public void An_unreadable_core_service_stays_Windows()
    {
        var s = new FakeSystem();
        s.Processes[900] = new ProcessInfo(900, 133_000_000_000_000_000, "svchost.exe", null, Unreadable: true);
        s.Services.Add(new("Dnscache", "DNS Client", 900));
        s.Listen(53, 900, "0.0.0.0", PortProtocol.Udp);
        Assert.Equal((HolderKind.Windows, PortVerb.None), (Only(s, 53).Holder.Kind, Only(s, 53).Verb));
    }

    [Fact]
    public void The_process_list_gives_the_same_start_time_and_name_as_opening_the_process()
    {
        using var self = Process.GetCurrentProcess();
        var names = new ProcessNames();
        Assert.Equal(self.StartTime.ToFileTime(), names.StartOf(Environment.ProcessId));
        Assert.Equal(self.ProcessName, names.Get(Environment.ProcessId), ignoreCase: true);
        Assert.Equal("System", names.Get(4));
        Assert.Equal(0, names.StartOf(-5));
    }
}
