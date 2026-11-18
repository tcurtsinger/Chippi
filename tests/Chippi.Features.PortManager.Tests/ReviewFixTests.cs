// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Net;
using Chippi.Core.Admin;

namespace Chippi.Features.PortManager.Tests;

/// <summary>Concurrent scans, Search's cheap lookups, work off the UI thread, protected processes and the helper session's end.</summary>
public sealed class ReviewFixTests
{
    private sealed class Clock { public long Now; public long Read() => Now; }

    // ---- 1: a scan's process names are its own ----

    [Fact]
    public void Concurrent_scans_each_get_their_own_name_map_and_all_read_correctly()
    {
        var system = new NamesSystem();
        for (int i = 0; i < 20; i++) system.Listen(3000 + i, 100 + i);
        const int scans = 24;
        var results = new IReadOnlyList<PortRow>[scans];
        Parallel.For(0, scans, new ParallelOptions { MaxDegreeOfParallelism = 8 }, n => results[n] = new PortScanner(system).Scan());
        Assert.Equal(scans, system.Maps.Distinct().Count());
        Assert.All(results, rows =>
        {
            Assert.Equal(20, rows.Count);
            Assert.All(rows, r => Assert.Equal($"p{r.Holder.Pid}.exe", r.Holder.Name));
        });
    }

    [Fact]
    public void One_scan_shares_one_lazily_read_name_map_across_its_processes()
    {
        int loads = 0;
        var names = new ProcessNames(() => { loads++; return new Dictionary<int, string> { [5] = "a", [6] = "b" }; });
        Assert.Equal(0, loads);
        Assert.Equal(("a", "b", null), (names.Get(5), names.Get(6), names.Get(7)));
        Assert.Equal(1, loads);
    }

    private sealed class NamesSystem : IPortSystem
    {
        private readonly List<PortEndpoint> _endpoints = [];
        public List<ProcessNames> Maps { get; } = [];
        public int CurrentPid => 1;
        public string? CurrentPath => null;
        public void Listen(int port, int pid) => _endpoints.Add(new(PortProtocol.Tcp, IPAddress.Loopback, port, IPAddress.Any, 0, TcpState.Listen, pid));
        public IReadOnlyList<PortEndpoint> ReadEndpoints() { Thread.Yield(); return [.. _endpoints]; }
        public string? OwnerModule(PortEndpoint endpoint) => null;
        public ProcessInfo? ReadProcess(int pid, ProcessNames? names = null)
        {
            lock (Maps) Maps.Add(names!);
            Thread.Yield();
            return new ProcessInfo(pid, 10, $"p{pid}.exe", @"C:\apps\p.exe", CanTerminate: true);
        }
        public IReadOnlyList<ServiceInfo> RunningServices() => [];
        public bool HasWindow(int pid) => false;
        public EndResult PostClose(int pid, long startTime) => default;
        public EndResult Terminate(int pid, long startTime) => default;
    }

    // ---- 2: Search never waits on a full read ----

    private static (FakeSystem System, PortManagerEngine Engine, Clock Clock) Engine()
    {
        var system = new FakeSystem().Process(4120, "node.exe");
        system.Listen(3000, 4120);
        var clock = new Clock();
        return (system, new PortManagerEngine(system, null, clock.Read, (_, _) => Task.Delay(Timeout.Infinite)), clock);
    }

    [Fact]
    public async Task Lookup_answers_from_a_fresh_full_read_without_reading_again()
    {
        var (system, engine, clock) = Engine();
        await engine.RefreshAsync();
        int reads = system.Reads;
        clock.Now = 1500;
        Assert.Equal(4120, Assert.Single(engine.Lookup(3000)).Holder.Pid);
        Assert.Empty(engine.Lookup(3001));
        Assert.Equal(reads, system.Reads);
        clock.Now = 2500;   // the read is 2.5 seconds old: one focused read
        Assert.Single(engine.Lookup(3000));
        Assert.Equal(reads + 1, system.Reads);
    }

    [Fact]
    public void A_cold_lookup_does_one_focused_read_and_keeps_it_for_250_ms_per_port()
    {
        var (system, engine, clock) = Engine();
        for (int i = 0; i < 5; i++) Assert.Single(engine.Lookup(3000));
        Assert.Equal(1, system.Reads);
        Assert.Empty(engine.Lookup(3001));
        Assert.Equal(2, system.Reads);
        clock.Now = 249;
        engine.Lookup(3000);
        Assert.Equal(2, system.Reads);
        clock.Now = 250;
        engine.Lookup(3000);
        Assert.Equal(3, system.Reads);
    }

    [Fact]
    public async Task A_verb_forgets_what_Search_was_answered_from_and_a_fresh_lookup_reads_off_the_caller_thread()
    {
        var (system, engine, clock) = Engine();
        await engine.RefreshAsync();
        var row = engine.Rows.Single().Row;
        Assert.Single(engine.Lookup(3000));
        Assert.Equal(PortEndState.Freed, (await engine.FreeAsync(row)).State);
        clock.Now = 10;   // inside both the snapshot's and the cache's life
        Assert.Empty(engine.Lookup(3000));
        system.Listen(3000, 4120);
        int caller = Environment.CurrentManagedThreadId, before = system.ReadThreads.Count;
        Assert.Single(await engine.LookupFreshAsync(3000));
        Assert.DoesNotContain(caller, system.ReadThreads.Skip(before));
        Assert.Single(engine.Lookup(3000));   // the fresh read is what the next keystroke is answered from
    }

    [Fact]
    public async Task The_enders_rechecks_and_verification_reads_leave_the_calling_thread()
    {
        var system = new FakeSystem().Process(4120, "node.exe");
        system.Listen(3000, 4120);
        var row = new PortScanner(system).Scan().Single();
        var ender = new PortEnder(system, null, (_, _) => Task.CompletedTask, () => 0);
        int caller = Environment.CurrentManagedThreadId, before = system.ReadThreads.Count;
        Assert.Equal(PortEndState.Freed, (await ender.FreeAsync(row)).State);
        Assert.True(system.ReadThreads.Count - before >= 2);   // the recheck and the verification
        Assert.DoesNotContain(caller, system.ReadThreads.Skip(before));
    }

    // ---- 4: protected processes ----

    [Fact]
    public void A_protected_process_the_limited_open_allowed_is_Windows_with_no_verb()
    {
        var system = new FakeSystem();
        system.Processes[900] = new ProcessInfo(900, 5, "av.exe", @"C:\av\av.exe", CanTerminate: false, Protected: true);
        system.Listen(8443, 900);
        var row = new PortScanner(system).Scan().Single();
        Assert.Equal((HolderKind.Windows, PortVerb.None), (row.Holder.Kind, row.Verb));
    }

    // ---- 7: scope ids are unsigned ----

    [Fact]
    public void A_high_ipv6_scope_id_is_read_as_unsigned()
    {
        var table = new byte[PortTables.TableHeader + PortTables.Tcp6Row];
        BitConverter.TryWriteBytes(table.AsSpan(0, 4), 1u);
        var row = table.AsSpan(PortTables.TableHeader, PortTables.Tcp6Row);
        IPAddress.Parse("fe80::1").TryWriteBytes(row[..16], out _);
        BitConverter.TryWriteBytes(row.Slice(16, 4), 0xFFFFFFF0u);
        row[20] = 0x1F; row[21] = 0x90;
        BitConverter.TryWriteBytes(row.Slice(48, 4), 2);
        BitConverter.TryWriteBytes(row.Slice(52, 4), 77);
        var parsed = Assert.Single(PortTables.Parse(PortProtocol.Tcp, true, table));
        Assert.Equal(0xFFFFFFF0L, parsed.LocalAddress.ScopeId);
    }

    // ---- 6: the flow ends the helper session ----

    [Fact]
    public async Task The_flow_ends_the_helper_session_when_the_result_is_shown_a_question_is_dismissed_or_cancelled_but_not_between_close_and_force_end()
    {
        var system = new FakeSystem().Process(3340, "postgres.exe", canTerminate: false);
        system.Listen(5432, 3340, "0.0.0.0");
        system.Windows.Add(3340);
        var admin = new FakeAdmin();
        admin.Results.Enqueue(new(AdminPortVerb.Close, AdminPortState.Freed));
        var clock = new Clock();
        var engine = new PortManagerEngine(system, admin, clock.Read, (_, _) => { clock.Now += 100; return Task.CompletedTask; });
        var flow = new PortFlow(engine, clock.Read);
        await engine.RefreshAsync();
        flow.Ask(engine.Rows.Single().Row);
        await flow.ConfirmAsync();   // the close request left the process running
        Assert.Equal(FlowStep.ForceAsking, flow.Step);
        Assert.Equal(0, admin.EndSessions);
        flow.Dismiss();
        Assert.Equal(1, admin.EndSessions);
        system.Windows.Clear();
        flow.Ask(engine.Rows.Single().Row);
        await flow.ConfirmAsync();   // ended at once (a failed verify is still a result)
        Assert.Equal(FlowStep.Result, flow.Step);
        Assert.Equal(2, admin.EndSessions);
    }
}
