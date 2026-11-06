// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Modules;

namespace Chippi.Features.WindowLayouts.Tests;

public sealed class ArrangementEngineTests
{
    private static readonly SavedWindowPlacement Placement = new(10, 20, 500, 400, 0, 0, 1920, 1040, false, false);
    private static ArrangementRow Row(string app = "editor") => new() { AppIdentity = app, Target = "C:\\apps\\" + app + ".exe",
        App = app, ClassName = "Editor", TitleHint = "Document", DisplayId = "saved", Placement = Placement };
    private static ArrangementLiveWindow Live(int handle, string app = "editor") => new(
        new(new ActionWindow(handle, (uint)handle, 1, 1, "Editor", "C:\\apps\\" + app + ".exe", app, "Document", false, 1),
            app, new(10, 20, 500, 400)), "C:\\apps\\" + app + ".exe", "saved", "Display", 96, Placement);
    private static SavedArrangement Arrangement(params ArrangementRow[] rows) => new() { Name = "Work", Rows = [.. rows] };

    [Fact]
    public async Task Reuses_all_existing_windows_before_launching_missing_apps()
    {
        var desktop = new Desktop();
        desktop.Live.Add(Live(1));
        desktop.OnLaunch = row => desktop.Live.Add(Live(2, row.AppIdentity));
        var runner = new ArrangementRunner(desktop);
        await runner.LaunchAsync(Arrangement(Row("missing"), Row()), "saved");
        Assert.Equal(new[] { "place:1", "launch:missing", "place:2" }, desktop.Calls);
        Assert.All(runner.Results, r => Assert.Equal(ArrangementRowState.Ready, r.State));
    }

    [Fact]
    public async Task Needs_admin_does_not_stop_the_remaining_rows()
    {
        var desktop = new Desktop();
        desktop.OnLaunch = row => { if (row.AppIdentity == "admin") throw new ActionFailure("Needs admin");
            desktop.Live.Add(Live(2, row.AppIdentity)); };
        var runner = new ArrangementRunner(desktop);
        await runner.LaunchAsync(Arrangement(Row("admin"), Row()), "saved");
        Assert.Equal(ArrangementRowState.Skipped, runner.Results[0].State);
        Assert.Equal("Needs admin", runner.Results[0].Reason);
        Assert.Equal(ArrangementRowState.Ready, runner.Results[1].State);
    }

    [Fact]
    public async Task Deadline_is_sixty_seconds_without_wall_clock_waiting()
    {
        var clock = new Clock();
        var desktop = new Desktop();
        var runner = new ArrangementRunner(desktop, clock, (_, _) => { clock.Now += 10000; return Task.CompletedTask; });
        await runner.LaunchAsync(Arrangement(Row()), "saved");
        Assert.Equal(60000, clock.Now);
        Assert.Equal(ArrangementRowState.Failed, runner.Results[0].State);
        Assert.Equal(new[] { "launch:editor" }, desktop.Calls);
    }

    [Fact]
    public async Task Cancel_stops_pending_rows_and_never_closes_apps()
    {
        var desktop = new Desktop();
        ArrangementRunner? runner = null;
        runner = new ArrangementRunner(desktop, delay: (_, _) => { runner!.Cancel(); return Task.CompletedTask; });
        await runner.LaunchAsync(Arrangement(Row(), Row("second")), "saved");
        Assert.All(runner.Results, r => Assert.Equal(ArrangementRowState.Cancelled, r.State));
        Assert.Equal(new[] { "launch:editor" }, desktop.Calls);
    }

    [Fact]
    public async Task Missing_display_falls_back_to_invocation_display_and_reports_it()
    {
        var desktop = new Desktop();
        desktop.Live.Add(Live(1));
        desktop.Connected = [new("pointer", "Pointer display", 2, true, new(0, 0, 1000, 800), new(0, 0, 1000, 760))];
        var runner = new ArrangementRunner(desktop);
        await runner.LaunchAsync(Arrangement(Row()), "pointer");
        Assert.Equal("Pointer display", runner.Results[0].SubstituteDisplay);
        Assert.Equal((nint)2, desktop.LastMonitor);
    }

    [Fact]
    public async Task Retry_does_not_move_successful_rows_again()
    {
        var desktop = new Desktop();
        desktop.Live.AddRange([Live(1), Live(2, "second")]);
        desktop.FailWindow = 2;
        var runner = new ArrangementRunner(desktop);
        await runner.LaunchAsync(Arrangement(Row(), Row("second")), "saved");
        Assert.Equal(ArrangementRowState.Failed, runner.Results[1].State);
        desktop.FailWindow = 0;
        await runner.RetryAsync();
        Assert.Equal(new[] { "place:1", "place:2", "place:2" }, desktop.Calls);
        Assert.All(runner.Results, r => Assert.Equal(ArrangementRowState.Ready, r.State));
    }

    [Fact]
    public async Task Single_instance_redirect_to_a_reserved_window_is_skipped()
    {
        var desktop = new Desktop();
        desktop.Live.Add(Live(1));
        var clock = new Clock();
        var runner = new ArrangementRunner(desktop, clock, (_, _) => { clock.Now += 10000; return Task.CompletedTask; });
        await runner.LaunchAsync(Arrangement(Row(), Row()), "saved");
        Assert.Equal(ArrangementRowState.Skipped, runner.Results[1].State);
        Assert.Equal("Window already used", runner.Results[1].Reason);
        Assert.Equal(new[] { "place:1", "launch:editor" }, desktop.Calls);
    }

    [Fact]
    public async Task A_second_window_can_arrive_after_the_app_reuses_its_process()
    {
        var desktop = new Desktop();
        desktop.Live.Add(Live(1));
        var clock = new Clock();
        var runner = new ArrangementRunner(desktop, clock, (_, _) =>
        {
            clock.Now += 100;
            if (clock.Now == 500) desktop.Live.Add(Live(2));
            return Task.CompletedTask;
        });
        await runner.LaunchAsync(Arrangement(Row(), Row()), "saved");
        Assert.All(runner.Results, r => Assert.Equal(ArrangementRowState.Ready, r.State));
        Assert.Equal(new[] { "place:1", "launch:editor", "place:2" }, desktop.Calls);
    }

    [Fact]
    public async Task Other_desktop_is_skipped_without_launching_a_duplicate()
    {
        var desktop = new Desktop();
        var live = Live(1);
        desktop.Live.Add(live with { Candidate = live.Candidate with { CurrentDesktop = false } });
        var runner = new ArrangementRunner(desktop);
        await runner.LaunchAsync(Arrangement(Row()), "saved");
        Assert.Empty(desktop.Calls);
        Assert.Equal("Another desktop", runner.Results[0].Reason);
    }

    [Fact]
    public void Persistence_keeps_layouts_and_publishes_only_after_a_successful_save()
    {
        var settings = new MemorySettings();
        var store = new SnapLayoutStore(settings);
        store.SaveArrangement(Arrangement(Row()) with { Name = " Work " });
        Assert.Equal("Work", new SnapLayoutStore(settings).Settings.Arrangements.Single().Name);
        Assert.NotEmpty(settings.Value.Layouts);
        Assert.Throws<ArgumentException>(() => store.SaveArrangement(Arrangement(Row()) with { Name = "work" }));
        settings.FailSave = true;
        Assert.Throws<IOException>(() => store.DeleteArrangement(store.Settings.Arrangements.Single().Id));
        Assert.Single(store.Settings.Arrangements);
    }

    [Fact]
    public void Recapture_is_an_independent_draft_and_preserves_only_unique_launch_edits()
    {
        var original = Arrangement(Row() with { Arguments = "--new" });
        var draft = original.Recapture([Row()]);
        Assert.Equal("--new", draft.Rows[0].Arguments);
        draft.Rows.Clear();
        Assert.Single(original.Rows);
        Assert.All(original.Recapture([Row(), Row()]).Rows, r => Assert.Empty(r.Arguments));
    }

    [Fact]
    public void Capture_exclusions_and_unresolved_targets_are_visible_in_the_draft()
    {
        var live = Live(1);
        var capture = ArrangementCaptureBuilder.Capture([live with { Fullscreen = true },
            live with { Candidate = live.Candidate with { Window = live.Candidate.Window with { Elevated = true } } },
            live with { Target = "" }]);
        Assert.Equal(new[] { "Fullscreen", "Needs admin" }, capture.Skipped);
        Assert.False(capture.Rows.Single().IsValid);
    }

    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => 1000;
    }
    private sealed class MemorySettings : IModuleSettings
    {
        public WindowLayoutsSettings Value = new();
        public bool FailSave;
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class
        { if (FailSave) throw new IOException("Disk full"); Value = (WindowLayoutsSettings)(object)value; }
    }
    private sealed class Desktop : IArrangementDesktop
    {
        public List<ArrangementLiveWindow> Live = [];
        public List<string> Calls = [];
        public Action<ArrangementRow>? OnLaunch;
        public int FailWindow;
        public nint LastMonitor;
        public IReadOnlyList<SnapDisplay> Connected = [new("saved", "Display", 1, true,
            new(0, 0, 1920, 1080), new(0, 0, 1920, 1040))];
        public IReadOnlyList<ArrangementLiveWindow> Windows() => Live.ToArray();
        public IReadOnlyList<SnapDisplay> Displays() => Connected;
        public string? StopReason(IReadOnlySet<ArrangementProcess> launched) => null;
        public ArrangementProcess? Launch(ArrangementRow row)
        { Calls.Add("launch:" + row.AppIdentity); OnLaunch?.Invoke(row); return null; }
        public void Place(ActionWindow target, SavedWindowPlacement placement, nint monitor)
        { Calls.Add("place:" + target.Handle); LastMonitor = monitor;
            if (target.Handle == FailWindow) throw new ActionFailure("Refused"); }
    }
}
