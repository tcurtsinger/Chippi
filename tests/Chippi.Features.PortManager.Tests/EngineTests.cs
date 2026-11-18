// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;

namespace Chippi.Features.PortManager.Tests;

public sealed class EngineTests
{
    private sealed class Clock
    {
        public long Now;
        public readonly List<TaskCompletionSource> Gates = [];
        public long Read() => Now;
        /// <summary>Each wait is a gate the test opens, so the loop advances one tick at a time.</summary>
        public Task Delay(TimeSpan span, CancellationToken ct)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => gate.TrySetCanceled(ct));
            lock (Gates) Gates.Add(gate);
            return gate.Task;
        }
        public async Task TickAsync(long ms)
        {
            Now += ms;
            for (int i = 0; i < 400 && Gates.Count == 0; i++) await Task.Delay(5);
            TaskCompletionSource[] open;
            lock (Gates) { open = [.. Gates]; Gates.Clear(); }
            foreach (var g in open) g.TrySetResult();
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(5);
        Assert.True(condition());
    }

    private static (FakeSystem System, PortManagerEngine Engine, Clock Clock) Make()
    {
        var system = new FakeSystem().Process(4120, "node.exe");
        system.Listen(3000, 4120);
        var clock = new Clock();
        return (system, new PortManagerEngine(system, null, clock.Read, clock.Delay), clock);
    }

    [Fact]
    public async Task The_first_read_marks_nothing_and_later_rows_are_new_then_ordinary_again()
    {
        var (system, engine, clock) = Make();
        await engine.RefreshAsync();
        Assert.Equal([PortMark.None], engine.Rows.Select(r => r.Mark));
        system.Process(7788, "vite.exe");
        system.Listen(5173, 7788);
        clock.Now = 2000;
        await engine.RefreshAsync();
        Assert.Equal([(3000, PortMark.None), (5173, PortMark.New)], engine.Rows.Select(r => (r.Row.Endpoint.LocalPort, r.Mark)));
        clock.Now = 5000;   // inside the few seconds
        await engine.RefreshAsync();
        Assert.Equal(PortMark.New, engine.Rows[1].Mark);
        clock.Now = 2000 + (long)PortManagerEngine.NewFor.TotalMilliseconds;
        await engine.RefreshAsync();
        Assert.Equal(PortMark.None, engine.Rows[1].Mark);
    }

    [Fact]
    public async Task An_ended_row_stays_marked_for_a_few_seconds_then_goes()
    {
        var (system, engine, clock) = Make();
        await engine.RefreshAsync();
        system.Kill(4120);
        clock.Now = 1000;
        await engine.RefreshAsync();
        var ended = Assert.Single(engine.Rows);
        Assert.Equal((3000, PortMark.Ended), (ended.Row.Endpoint.LocalPort, ended.Mark));
        clock.Now = 1000 + (long)PortManagerEngine.EndedFor.TotalMilliseconds;
        await engine.RefreshAsync();
        Assert.Empty(engine.Rows);
    }

    [Fact]
    public async Task A_row_that_comes_back_is_new_not_ended()
    {
        var (system, engine, clock) = Make();
        await engine.RefreshAsync();
        system.Kill(4120);
        clock.Now = 100;
        await engine.RefreshAsync();
        system.Process(5012, "node.exe");
        system.Listen(3000, 5012);
        clock.Now = 200;
        await engine.RefreshAsync();
        Assert.Equal([PortMark.New, PortMark.Ended], engine.Rows.Select(r => r.Mark).OrderBy(m => m));
    }

    [Fact]
    public async Task Changed_is_raised_only_when_the_list_changed()
    {
        var (system, engine, clock) = Make();
        int raised = 0;
        engine.Changed += () => raised++;
        await engine.RefreshAsync();
        Assert.Equal(1, raised);
        await engine.RefreshAsync();
        await engine.RefreshAsync();
        Assert.Equal(1, raised);
        system.Listen(3001, 4120);
        await engine.RefreshAsync();
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task A_failed_read_keeps_the_last_rows_and_says_so()
    {
        var (system, engine, _) = Make();
        await engine.RefreshAsync();
        system.ReadFails = new System.ComponentModel.Win32Exception(5);
        await engine.RefreshAsync();
        Assert.Equal(("Read failed", 1), (engine.Error, engine.Rows.Count));
        system.ReadFails = null;
        await engine.RefreshAsync();
        Assert.Null(engine.Error);
    }

    [Fact]
    public async Task Nothing_reads_until_the_page_is_visible_and_hiding_stops_it()
    {
        var (system, engine, clock) = Make();
        Assert.Equal(0, system.Reads);
        engine.SetVisible(true);
        await Until(() => system.Reads == 1);
        await clock.TickAsync(2000);
        await Until(() => system.Reads == 2);
        engine.SetVisible(false);
        await clock.TickAsync(2000);
        await Task.Delay(50);
        Assert.Equal(2, system.Reads);
        Assert.False(engine.IsVisible);
        engine.Dispose();
    }

    [Fact]
    public async Task Pause_stops_the_refresh_but_a_manual_refresh_still_reads()
    {
        var (system, engine, clock) = Make();
        engine.SetVisible(true);
        await Until(() => system.Reads == 1);
        engine.Paused = true;
        await clock.TickAsync(2000);
        await clock.TickAsync(2000);
        await Task.Delay(50);
        Assert.Equal(1, system.Reads);
        await engine.RefreshAsync();
        Assert.Equal(2, system.Reads);
        engine.Paused = false;
        await clock.TickAsync(2000);
        await Until(() => system.Reads == 3);
        engine.Dispose();
    }

    [Fact]
    public async Task Lookup_reads_the_port_directly_and_is_empty_when_nothing_is_on_it_or_the_read_fails()
    {
        var (system, engine, clock) = Make();
        Assert.Equal(4120, Assert.Single(engine.Lookup(3000)).Holder.Pid);
        Assert.Empty(engine.Lookup(3001));
        system.ReadFails = new InvalidOperationException();
        clock.Now += 300;   // past the 250 ms a focused read is kept
        Assert.Empty(engine.Lookup(3000));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Free_confirms_through_the_ender_then_reads_again()
    {
        var (system, engine, clock) = Make();
        await engine.RefreshAsync();
        var row = engine.Rows.Single().Row;
        var result = await engine.FreeAsync(row);
        Assert.Equal(PortEndState.Freed, result.State);
        clock.Now = 10;
        Assert.Equal(PortMark.Ended, engine.Rows.Single().Mark);
        Assert.Equal(["terminate 4120@1000"], system.Calls);
    }

    [Fact]
    public async Task A_listener_this_test_starts_is_found_and_ended_and_nothing_else_is()
    {
        // The one real ending: a child this test starts, listening on loopback. It is found by its port, ended by its PID and start
        // time, and checked gone. No other process is looked at for ending, and no service is touched.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-Command",
             "$l=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$l.Start();[Console]::Out.WriteLine($l.LocalEndpoint.Port);Start-Sleep -Seconds 120"])
        { RedirectStandardOutput = true, CreateNoWindow = true, UseShellExecute = false };
        using var child = Process.Start(start)!;
        try
        {
            var line = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            int port = int.Parse(line!);
            using var engine = new PortManagerEngine(new WindowsPortSystem());
            var found = engine.Lookup(port);
            var row = Assert.Single(found);
            Assert.Equal((child.Id, HolderKind.Own, PortVerb.Free), (row.Holder.Pid, row.Holder.Kind, row.Verb));
            Assert.Equal(child.StartTime.ToFileTime(), row.Holder.StartTime);

            var result = await engine.FreeAsync(row, force: true);   // force: skip the close request, whatever windows the host has

            Assert.Equal(PortEndState.Freed, result.State);
            Assert.True(child.WaitForExit(10000));
            Assert.Empty(engine.Lookup(port));
        }
        finally
        {
            if (!child.HasExited) child.Kill();
        }
    }
}
