// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Admin;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.PortManager.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.PortManager.Tests;

/// <summary>
/// The screens, offscreen, on fixture data and fakes: nothing here shows a window, reads a real port or ends a real process. With
/// CHIPPI_ISLAND_SHOTS naming a folder they save each board of docs/phase-7/design/port-manager.html (dark, or light with
/// CHIPPI_SHOTS_THEME=light) next to timings.
/// </summary>
public sealed class ScreenTests
{
    // ---- the PC on paper: the mockup's invented processes ----

    private sealed class Clock
    {
        public long Now;
        public long Read() => Now;
        /// <summary>Time passes as the engine waits, so a close request's three seconds take no real time.</summary>
        public Task Advance(TimeSpan span, CancellationToken ct) { Now += (long)span.TotalMilliseconds; return Task.CompletedTask; }
        /// <summary>The wait never ends: a close request or a prompt that is still open.</summary>
        public Task Hold(TimeSpan span, CancellationToken ct) => new TaskCompletionSource().Task;
    }

    private sealed class GateAdmin(FakeSystem system) : IAdminHelper
    {
        public TaskCompletionSource<AdminPortResult> Gate { get; } = new();
        public int KillOnDone { get; set; }
        public Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items, IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminPortResult> CloseProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public async Task<AdminPortResult> EndProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) { var r = await Gate.Task; if (KillOnDone != 0) system.Kill(KillOnDone); return r; }
        public async Task<AdminPortResult> StopServiceAsync(string serviceName, AdminEndpoint endpoint, CancellationToken ct = default) { var r = await Gate.Task; if (KillOnDone != 0) system.Kill(KillOnDone); return r; }
    }

    private sealed class Mockup
    {
        public FakeSystem System { get; } = new();
        public Clock Clock { get; } = new();
        public IAdminHelper? Admin { get; set; }
        public PortManagerEngine Engine { get; private set; } = null!;
        public PortFlow Flow { get; private set; } = null!;
        public ModuleContext Context { get; private set; } = null!;
        public List<string?> Opened { get; } = [];

        public static Mockup Make(Func<Mockup, IAdminHelper?>? admin = null, bool hold = false, bool established = false, bool empty = false)
        {
            var m = new Mockup();
            var s = m.System;
            if (!empty)
            {
                s.Process(4120, "node.exe", path: @"C:\Program Files\nodejs\node.exe").Process(7788, "node.exe", path: @"C:\Program Files\nodejs\node.exe")
                    .Process(14220, "chrome.exe").Process(3340, "postgres.exe", canTerminate: false).Process(5520, "redis-server.exe")
                    .Process(9012, "java.exe").Process(2880, "spoolsv.exe", canTerminate: false).Process(10312, "msedge.exe")
                    .Process(4408, "mongod.exe", canTerminate: false).Process(6204, "Chippi.exe", path: @"C:\Chippi\Chippi.exe")
                    .Process(876, "lsass.exe", canTerminate: false).Process(2644, "MsMpEng.exe", canTerminate: false)
                    .Process(1124, "svchost.exe", canTerminate: false).Process(4, "System", canTerminate: false);
                s.Services.Add(new("Spooler", "Print Spooler", 2880));
                s.Services.Add(new("RpcSs", "Remote Procedure Call", 1124));
                s.Services.Add(new("RpcEptMapper", "RPC Endpoint Mapper", 1124));
                s.Listen(135, 1124, "0.0.0.0");
                s.Listen(445, 4, "0.0.0.0");
                s.Listen(3000, 4120);
                s.Listen(3000, 4120, "::1");
                s.Listen(5353, 14220, "0.0.0.0", PortProtocol.Udp);
                s.Listen(5354, 14220, "0.0.0.0", PortProtocol.Udp);
                s.Listen(5355, 14220, "0.0.0.0", PortProtocol.Udp);
                s.Listen(5432, 3340, "0.0.0.0");
                s.Listen(6379, 5520);
                s.Listen(9100, 2880, "0.0.0.0");
                s.Listen(9222, 10312);
                s.Listen(27017, 4408);
                s.Listen(47100, 6204);
                s.Listen(49664, 876, "0.0.0.0");
                s.Listen(49670, 2644);
                if (established)
                {
                    s.Connect(52210, 4120, TcpState.Established, "127.0.0.1", 3000);
                    s.Connect(49811, 14220, TcpState.Established, "142.250.80.14");
                    s.Connect(50122, 0, TcpState.TimeWait, "20.190.151.9");
                }
            }
            m.Admin = admin?.Invoke(m) ?? new FakeAdmin();
            m.Engine = new PortManagerEngine(s, m.Admin, m.Clock.Read, hold ? m.Clock.Hold : m.Clock.Advance);
            m.Flow = new PortFlow(m.Engine, m.Clock.Read);
            m.Context = new ModuleContext(PortManagerModule.Id, new NoSettings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher(PortManagerModule.Id), new NoUi())
            { OpenSettings = m.Opened.Add };
            WpfTestThread.Wait(m.Engine.RefreshAsync());
            return m;
        }

        public PortRow Row(int port) => Engine.Rows.First(r => r.Row.Endpoint.LocalPort == port && r.Row.Endpoint.IsHeld).Row;
        public PortManagerView Island() => new(Engine, Context, Flow);
        public PortsSettingsView Settings(string? section = null) => new(Engine, Context, section, Flow);
    }

    private sealed class NoSettings : IModuleSettings
    { public T Get<T>() where T : class, new() => new(); public void Save<T>(T value) where T : class { } }

    private sealed class NoUi : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(PortManagerModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> createView) { }
    }

    // ---- behavior ----

    [Fact]
    public void The_picker_lists_one_row_per_process_five_a_page_and_never_Windows_or_Chippi() => WpfTestThread.Run(() =>
    {
        PortUi.LoadIcons = false;
        var m = Mockup.Make();
        using var view = m.Island();
        Arrange(view, new(496, 300));
        // node 3000, node 5173 is not in this fixture; chrome's three UDP ports are one row, "5353 +2".
        Assert.Equal([3000, 5353, 5432, 6379, 9100, 9222, 27017], view.Entries.Select(e => e.Row.Endpoint.LocalPort).ToArray());
        Assert.Equal(3, view.Entries.Single(e => e.Row.Endpoint.LocalPort == 5353).Ports);
        Assert.Contains(Texts(view), t => t == "Port 5353 +2 · PID 14220");
        Assert.Contains(Texts(view), t => t == "1–5 of 7");
        Assert.Equal(5, Buttons(view).Count(b => AutomationProperties.GetName(b) is "Free node.exe" or "Free chrome.exe" or "Free postgres.exe" or "Free redis-server.exe" or "Stop Print Spooler"));
        Click(view, "Next ports");
        Arrange(view, new(496, 300));
        Assert.Contains(Texts(view), t => t == "6–7 of 7");
        Assert.DoesNotContain(Entries(view), t => t.Contains("Chippi", StringComparison.Ordinal) || t.Contains("lsass", StringComparison.Ordinal));
    });

    [Fact]
    public void A_picker_row_asks_the_question_with_Cancel_focused_and_nothing_ends_until_End() => WpfTestThread.Run(() =>
    {
        PortUi.LoadIcons = false;
        var m = Mockup.Make();
        using var view = m.Island();
        Arrange(view, new(496, 300));
        Click(view, "Free node.exe");
        Arrange(view, new(496, 300));
        Assert.Equal(FlowStep.Asking, m.Flow.Step);
        Assert.Contains(Texts(view), t => t == "End node.exe?");
        Assert.Contains(Texts(view), t => t == "Port 3000 · PID 4120");
        Assert.Equal("Cancel", AutomationProperties.GetName((Button)view.InitialFocus!));
        Assert.DoesNotContain(m.System.Calls, c => c.StartsWith("terminate", StringComparison.Ordinal) || c.StartsWith("close", StringComparison.Ordinal));
        Click(view, "Cancel");
        Assert.Equal(FlowStep.Idle, m.Flow.Step);
        Assert.True(view.Picking);
        Assert.DoesNotContain(m.System.Calls, c => c.StartsWith("terminate", StringComparison.Ordinal));
    });

    [Fact]
    public void End_runs_the_verb_and_the_result_is_a_row_with_the_Ports_row_after_it() => WpfTestThread.Run(() =>
    {
        PortUi.LoadIcons = false;
        var m = Mockup.Make();
        using var view = m.Island();
        view.Ask(m.Row(3000));
        Arrange(view, new(496, 300));
        WpfTestThread.Wait(m.Flow.ConfirmAsync());
        view.Redraw();
        Arrange(view, new(496, 300));
        Assert.Contains(m.System.Calls, c => c.StartsWith("terminate 4120", StringComparison.Ordinal));
        Assert.Contains(Texts(view), t => t == "Port 3000 freed");
        Assert.Contains(Texts(view), t => t == "Ports");
        Assert.Contains(Texts(view), t => t == "Settings › System");
    });

    [Fact]
    public void A_failure_says_End_failed_with_Retry_and_Retry_asks_again() => WpfTestThread.Run(() =>
    {
        PortUi.LoadIcons = false;
        var m = Mockup.Make();
        m.System.TerminateResult = new(EndOutcome.Failed, 5);
        using var view = m.Island();
        view.Ask(m.Row(3000));
        WpfTestThread.Wait(m.Flow.ConfirmAsync());
        view.Redraw();
        Arrange(view, new(496, 300));
        Assert.Contains(Texts(view), t => t == "End failed");
        Assert.Contains(Texts(view), t => t == "Retry");
        WpfTestThread.Wait(m.Flow.RetryAsync());
        Assert.Equal(FlowStep.Asking, m.Flow.Step);
        Assert.Equal(PortEndState.Failed, m.Flow.Result is null ? PortEndState.Failed : m.Flow.Result.State);
    });

    [Fact]
    public void A_close_request_that_is_ignored_asks_Force_end_and_nothing_forces_without_it() => WpfTestThread.Run(() =>
    {
        PortUi.LoadIcons = false;
        var m = Mockup.Make();
        m.System.Windows.Add(4120);
        using var view = m.Island();
        view.Ask(m.Row(3000));
        WpfTestThread.Wait(m.Flow.ConfirmAsync());
        Assert.Equal(FlowStep.ForceAsking, m.Flow.Step);
        Assert.Equal("Force end node.exe?", m.Flow.Title);
        Assert.DoesNotContain(m.System.Calls, c => c.StartsWith("terminate", StringComparison.Ordinal));
        WpfTestThread.Wait(m.Flow.ConfirmAsync());
        Assert.Contains(m.System.Calls, c => c.StartsWith("terminate 4120", StringComparison.Ordinal));
    });

    [Fact]
    public void Settings_lists_listening_rows_with_verbs_only_on_holders_and_asks_in_the_row() => WpfTestThread.Run(() =>
    {
        PortUi.LoadIcons = false;
        var m = Mockup.Make(established: true);
        using var view = m.Settings();
        Arrange(view, new(780, 900));
        var names = Buttons(view).Select(b => AutomationProperties.GetName(b)).ToList();
        Assert.Contains("Free node.exe", names);
        Assert.Contains("Stop Print Spooler", names);
        // Chippi, Windows and the shared svchost have no verb.
        Assert.DoesNotContain(names, n => n is "Free Chippi.exe" or "Free lsass.exe" or "Free System" or "Free svchost.exe");
        Assert.Contains(Texts(view), t => t == "· Shared service");
        Assert.Contains(Texts(view), t => t == "· Service");
        view.ShowAll(true);
        Arrange(view, new(780, 900));
        Assert.Contains(Texts(view), t => t == "Closing connection");
        Assert.Contains(Texts(view), t => t == "Established");
        // Connections and Time wait have no verb: one verb a held row only.
        static bool IsVerb(string? n) => n is not null && (n.StartsWith("Free ", StringComparison.Ordinal) || n.StartsWith("Stop ", StringComparison.Ordinal));
        Assert.Equal(names.Count(IsVerb), Buttons(view).Select(b => AutomationProperties.GetName(b)).Count(IsVerb));
        // A row's verb asks in the row.
        view.ShowAll(false);
        Click(view, "Free node.exe");
        Assert.Equal(FlowStep.Asking, m.Flow.Step);
        view.Redraw();
        Arrange(view, new(780, 900));
        Assert.Contains(Texts(view), t => t == "End node.exe?");
        Assert.Contains(Texts(view), t => t == "PID 4120");
        Assert.DoesNotContain(m.System.Calls, c => c.StartsWith("terminate", StringComparison.Ordinal));
    });

    [Fact]
    public void Settings_opened_at_a_port_marks_it_and_search_finds_both_address_families() => WpfTestThread.Run(() =>
    {
        PortUi.LoadIcons = false;
        var m = Mockup.Make();
        using var view = m.Settings(PortManagerModule.SectionFor(3000));
        Arrange(view, new(780, 900));
        view.SearchFor("3000");
        Arrange(view, new(780, 900));
        Assert.Contains(Texts(view), t => t == "[::1]:3000");
        Assert.Contains(Texts(view), t => t == "127.0.0.1:3000");
        Assert.Contains(Texts(view), t => t == "2 listening");
        view.SearchFor("3001");
        Arrange(view, new(780, 900));
        Assert.Contains(Texts(view), t => t == "Port 3001 not in use");
    });

    [Fact]
    public void The_filter_matches_port_prefix_pid_name_and_service()
    {
        var m = Mockup.Make();
        var node = m.Row(3000);
        Assert.True(PortFilter.Matches(node, "300"));
        Assert.True(PortFilter.Matches(node, "4120"));
        Assert.True(PortFilter.Matches(node, "NODE"));
        Assert.False(PortFilter.Matches(node, "3001"));
        Assert.True(PortFilter.Matches(m.Row(9100), "print"));
        Assert.True(PortFilter.Matches(node, ""));
    }

    [Fact]
    public void The_flow_never_runs_until_confirmed_and_cancel_leaves_everything_alone()
    {
        var m = Mockup.Make();
        var row = m.Row(3000);
        m.Flow.Ask(row);
        Assert.Equal(("End node.exe?", "Port 3000 · PID 4120"), (m.Flow.Title, m.Flow.Detail));
        m.Flow.Cancel();
        Assert.Equal(FlowStep.Idle, m.Flow.Step);
        Assert.DoesNotContain(m.System.Calls, c => c.StartsWith("terminate", StringComparison.Ordinal) || c.StartsWith("close", StringComparison.Ordinal));
        m.Flow.Ask(m.Row(5353));
        Assert.Equal(("End chrome.exe?", "3 ports · PID 14220"), (m.Flow.Title, m.Flow.Detail));
        m.Flow.Ask(m.Row(9100));
        Assert.Equal(("Stop Print Spooler?", "Port 9100"), (m.Flow.Title, m.Flow.Detail));
    }

    [Fact]
    public void An_admin_verb_waits_for_admin_then_reports_and_a_declined_prompt_says_Needs_admin()
    {
        var m = Mockup.Make();
        var gate = new GateAdmin(m.System);
        var engine = new PortManagerEngine(m.System, gate, m.Clock.Read, m.Clock.Advance);
        var flow = new PortFlow(engine, m.Clock.Read);
        WpfTestThread.Wait(engine.RefreshAsync());
        var pg = engine.Rows.First(r => r.Row.Endpoint.LocalPort == 5432).Row;
        Assert.True(pg.NeedsAdmin);
        flow.Ask(pg);
        var run = flow.ConfirmAsync();
        Assert.Equal((FlowStep.Working, true), (flow.Step, flow.WaitsForAdmin));
        gate.Gate.SetResult(new(AdminPortVerb.End, AdminPortState.NeedsAdmin));
        WpfTestThread.Wait(run);
        Assert.Equal(("Needs admin", OutcomeKind.Failed, "Retry"), (flow.Outcome!.Title, flow.Outcome.Kind, flow.Outcome.Verb));
    }

    // ---- the boards ----

    [Fact]
    public void Render_approved_states_offscreen_with_fixture_data_only()
    {
        string? output = Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        string theme = WpfTestThread.ShotsTheme.ToLowerInvariant();
        WpfTestThread.Run(() =>
        {
            PortUi.LoadIcons = false;
            if (Application.Current.TryFindResource("IslandButtonStyle") is null)
                Application.Current.Resources.MergedDictionaries.Add(new() { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
            var timings = new List<string>();
            void Island(string state, FrameworkElement view, FrameworkElement header)
            {
                var shell = new Border { Width = 520, Height = 360, Padding = new(12), CornerRadius = new(8) };
                shell.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
                var content = new Grid();
                content.RowDefinitions.Add(new() { Height = new(28) });
                content.RowDefinitions.Add(new() { Height = new(8) });
                content.RowDefinitions.Add(new());
                var top = new DockPanel();
                var back = PortUi.IslandIcon(LucideIcons.ChevronLeft, "Back", () => { });
                DockPanel.SetDock(back, Dock.Left);
                top.Children.Add(back);
                top.Children.Add(header);
                content.Children.Add(top);
                Grid.SetRow(view, 2);
                content.Children.Add(view);
                shell.Child = content;
                Snapshot(shell, new(520, 360), Path.Combine(output, $"port-{state}-{theme}.png"));
                content.Children.Remove(view);
                top.Children.Remove(header);
            }
            void Page(string state, PortsSettingsView view, int height = 1000)
            {
                var shell = new Border { Padding = new(40, 4, 40, 28), Child = view };
                shell.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
                Snapshot(shell, new(860, height), Path.Combine(output, $"port-settings-{state}-{theme}.png"));
                shell.Child = null;
            }

            // The island: the picker, the question and its steps.
            {
                var m = Mockup.Make();
                var watch = Stopwatch.StartNew();
                using var view = m.Island();
                Arrange(view, new(496, 300));
                timings.Add($"{theme} picker construction and layout: {watch.Elapsed.TotalMilliseconds:F2} ms");
                Island("picker-1", view, view.Header);
                watch.Restart();
                Click(view, "Next ports");
                Arrange(view, new(496, 300));
                timings.Add($"{theme} picker page switch and layout: {watch.Elapsed.TotalMilliseconds:F2} ms");
                Island("picker-2", view, view.Header);
            }
            {
                var m = Mockup.Make(empty: true);
                using var view = m.Island();
                Island("picker-empty", view, view.Header);
            }
            foreach (var (state, port) in new[] { ("q-end", 3000), ("q-multi", 5353), ("q-stop", 9100), ("q-admin", 5432) })
            {
                var m = Mockup.Make();
                using var view = m.Island();
                view.Ask(m.Row(port));
                Island(state, view, view.Header);
            }
            {
                // A close request is out: Closing, 62 percent of the way through its three seconds.
                var m = Mockup.Make(hold: true);
                m.System.Windows.Add(4120);
                using var view = m.Island();
                view.Ask(m.Row(3000));
                _ = m.Flow.ConfirmAsync();
                m.Clock.Now = 1860;
                view.Redraw();
                Island("q-closing", view, view.Header);
            }
            {
                var m = Mockup.Make();
                m.System.Windows.Add(4120);
                using var view = m.Island();
                view.Ask(m.Row(3000));
                WpfTestThread.Wait(m.Flow.ConfirmAsync());
                Island("q-force", view, view.Header);
            }
            {
                var m = Mockup.Make(x => new GateAdmin(x.System));
                using var view = m.Island();
                view.Ask(m.Row(5432));
                _ = m.Flow.ConfirmAsync();
                Island("q-waiting", view, view.Header);
            }
            {
                // Force end of an admin process.
                var m = Mockup.Make(x => new FakeAdmin());
                var admin = (FakeAdmin)m.Admin!;
                admin.Results.Enqueue(new(AdminPortVerb.Close, AdminPortState.Freed));
                m.System.Windows.Add(3340);
                using var view = m.Island();
                view.Ask(m.Row(5432));
                WpfTestThread.Wait(m.Flow.ConfirmAsync());
                Island("q-force-admin", view, view.Header);
            }

            // Results, as the Search row they replace.
            void Result(string state, Action<Mockup, PortManagerView> arrange, Func<Mockup, PortRow> row, Action<Mockup>? before = null)
            {
                var m = Mockup.Make(x => new FakeAdmin());
                before?.Invoke(m);
                using var view = m.Island();
                view.Ask(row(m));
                arrange(m, view);
                view.Redraw();
                Arrange(view, new(496, 300));
                Island(state, view, view.Header);
            }
            void Confirm(Mockup m, PortManagerView view) => WpfTestThread.Wait(m.Flow.ConfirmAsync());
            Result("r-freed", Confirm, m => m.Row(3000));
            Result("r-stopped", Confirm, m => m.Row(9100), m => ((FakeAdmin)m.Admin!).OnCall = _ => m.System.Kill(2880));
            Result("r-respawn", Confirm, m => m.Row(3000), m => m.System.AfterKill = pid =>
            {
                if (pid != 4120) return;
                m.System.Process(5012, "node.exe", start: 2000, path: @"C:\Program Files\nodejs\node.exe");
                m.System.Listen(3000, 5012);
            });
            Result("r-end-failed", Confirm, m => m.Row(3000), m => m.System.TerminateResult = new(EndOutcome.Failed, 5));
            Result("r-stop-failed", Confirm, m => m.Row(9100), m => ((FakeAdmin)m.Admin!).Results.Enqueue(new(AdminPortVerb.Stop, AdminPortState.Failed, "Windows error 5")));
            Result("r-needs-admin", Confirm, m => m.Row(5432), m => ((FakeAdmin)m.Admin!).Results.Enqueue(new(AdminPortVerb.End, AdminPortState.NeedsAdmin)));
            Result("r-closed", (m, v) => { m.System.Kill(4120); Confirm(m, v); }, m => m.Row(3000));
            Result("r-changed", (m, v) => { m.System.Processes[4120] = new ProcessInfo(4120, 2000, "node.exe", @"C:\Program Files\nodejs\node.exe", false, true); Confirm(m, v); }, m => m.Row(3000));
            Result("r-kept-shared", Confirm, m => m.Engine.Rows.First(r => r.Row.Endpoint.LocalPort == 135).Row);
            Result("r-kept-changed", Confirm, m => m.Row(5432), m => ((FakeAdmin)m.Admin!).Results.Enqueue(new(AdminPortVerb.End, AdminPortState.Kept, "Protected process")));

            // Settings.
            Mockup Marked(bool established = false)
            {
                // The first read marks nothing; a second, with vite in and java out, marks the new row and the ended one.
                var m = Mockup.Make(established: established);
                m.System.Process(9012, "java.exe");
                m.System.Listen(8080, 9012);
                WpfTestThread.Wait(m.Engine.RefreshAsync());
                m.Clock.Now += 10;
                m.System.Kill(9012);
                m.System.Listen(5173, 7788);
                WpfTestThread.Wait(m.Engine.RefreshAsync());
                return m;
            }
            {
                var m = Marked();
                var watch = Stopwatch.StartNew();
                using var view = m.Settings();
                Arrange(view, new(780, 960));
                timings.Add($"{theme} Ports page construction and layout: {watch.Elapsed.TotalMilliseconds:F2} ms");
                Page("listening", view);
            }
            {
                var m = Marked(established: true);
                using var view = m.Settings();
                view.ShowAll(true);
                Page("all", view, 1100);
            }
            { var m = Marked(); using var view = m.Settings(PortManagerModule.SectionFor(5432)); Page("land", view); }
            { var m = Marked(); using var view = m.Settings(); view.SearchFor("3000"); Page("search", view, 460); }
            { var m = Marked(); using var view = m.Settings(); view.SearchFor("3001"); Page("none", view, 460); }
            { var m = Marked(); using var view = m.Settings(); view.Pause(true); Page("paused", view); }
            foreach (var (state, port) in new[] { ("ask-end", 3000), ("ask-admin", 5432), ("ask-stop", 9100) })
            {
                var m = Marked();
                using var view = m.Settings();
                m.Flow.Ask(m.Row(port));
                view.Redraw();
                Page(state, view);
            }
            {
                var rebuilt = Mockup.Make(hold: true);
                rebuilt.System.Windows.Add(4120);
                using var held = rebuilt.Settings();
                rebuilt.Flow.Ask(rebuilt.Row(3000));
                _ = rebuilt.Flow.ConfirmAsync();
                rebuilt.Clock.Now = 1860;
                held.Redraw();
                Page("closing", held);
                rebuilt.Flow.Cancel();
            }
            {
                var m = Mockup.Make(x => new GateAdmin(x.System));
                using var view = m.Settings();
                m.Flow.Ask(m.Row(5432));
                _ = m.Flow.ConfirmAsync();
                view.Redraw();
                Page("waiting", view);
            }
            void Strip(string state, Mockup m, int port, Action<Mockup>? before = null, bool confirm = true)
            {
                before?.Invoke(m);
                using var view = m.Settings();
                m.Flow.Ask(m.Row(port));
                if (confirm) WpfTestThread.Wait(m.Flow.ConfirmAsync());
                view.Redraw();
                Page(state, view);
            }
            Strip("freed", Mockup.Make(x => new FakeAdmin()), 3000);
            Strip("failed", Mockup.Make(x => new FakeAdmin()), 3000, m => m.System.TerminateResult = new(EndOutcome.Failed, 5));
            {
                var m = Mockup.Make(x => new FakeAdmin());
                using var view = m.Settings();
                m.Flow.Ask(m.Row(3000));
                m.System.Kill(4120);
                WpfTestThread.Wait(m.Flow.ConfirmAsync());
                view.Redraw();
                Page("closed", view);
            }
            {
                var m = Mockup.Make(x => new FakeAdmin());
                ((FakeAdmin)m.Admin!).Results.Enqueue(new(AdminPortVerb.End, AdminPortState.Kept, "Protected process"));
                using var view = m.Settings();
                m.Flow.Ask(m.Row(5432));
                WpfTestThread.Wait(m.Flow.ConfirmAsync());
                view.Redraw();
                Page("kept", view);
            }
            string? path = Environment.GetEnvironmentVariable("CHIPPI_PORT_MANAGER_TIMINGS");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllLines(path, timings);
        });
    }

    // ---- helpers ----

    private static void Arrange(FrameworkElement element, Size size) { element.Measure(size); element.Arrange(new Rect(size)); element.UpdateLayout(); }

    private static void Click(DependencyObject root, string name)
        => Buttons(root).First(b => AutomationProperties.GetName(b) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<Button> Buttons(DependencyObject root) => Descendants<Button>(root);

    private static IReadOnlyList<string> Texts(DependencyObject root) => [.. Descendants<TextBlock>(root).Select(t => t.Text)];

    private static IEnumerable<string> Entries(DependencyObject root) => Texts(root);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Snapshot(FrameworkElement element, Size size, string path)
    {
        Arrange(element, size);
        var bitmap = new RenderTargetBitmap((int)size.Width * 2, (int)size.Height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
