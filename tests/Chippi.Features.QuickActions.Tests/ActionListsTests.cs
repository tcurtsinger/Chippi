// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.QuickActions.Views;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.QuickActions.Tests;

public sealed class ActionListsTests
{
    [Fact]
    public void All_thirty_three_apps_are_reachable_without_hiding_their_protection()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var apps = Enumerable.Range(1, 33).Select(i => new OpenApp($@"C:\TestApps\app{i}.exe", $"App {i}", i == 1)).ToArray();
            f.Start(QuickState.Empty with { Apps = apps }, new() { Shown = ["quit"], Kept = [apps[0].Path, apps[32].Path] });
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Quit);
            Assert.Equal(4, page.ListPageCount);
            Assert.Contains(Find<TextBlock>(page), t => t.Text == "Front \u00b7 Kept");
            var seen = new HashSet<string>();
            for (int i = 0; i < 4; i++)
            {
                foreach (var button in Find<Button>(page))
                {
                    string name = AutomationProperties.GetName(button);
                    if (name.StartsWith("Quit App ", StringComparison.Ordinal)) seen.Add(name);
                }
                if (i < 3) Click(Named(page, "Next apps"));
            }
            Assert.Equal(33, seen.Count);
            Assert.Contains("Quit App 33", seen);
            Assert.Contains(Find<TextBlock>(page), t => t.Text == "Kept");
            Assert.False(Named(page, "Next apps").IsEnabled);
            Assert.True(page.Back());
            page.Press(QuickActionId.Quit);
            Assert.Equal(0, page.ListPage);
        });
    }

    [Fact]
    public void A_drive_failure_stays_with_that_drive_and_successful_retry_clears_it()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            f.Start(QuickState.Empty with { Drives = [new("a", "Backup (E:)", 1024), new("b", "Archive (F:)", 2048)] }, new() { Shown = ["eject", "power"] });
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Eject);
            f.Pc.Fail = true;
            Click(Named(page, "Eject Backup (E:)"));
            Assert.Contains(Find<TextBlock>(page), t => t.Text == "Eject failed");
            Assert.NotNull(Named(page, "Retry ejecting Backup (E:)"));
            Assert.NotNull(Named(page, "Eject Archive (F:)"));
            Assert.True(page.Back());
            page.Press(QuickActionId.Power);
            Assert.DoesNotContain(Find<TextBlock>(page), t => t.Text == "Eject failed");
            Assert.Equal("", page.ListFailure);
            Assert.True(page.Back());
            page.Press(QuickActionId.Eject);
            Assert.Equal("Eject failed", page.ListFailure);
            f.Pc.Fail = false;
            Click(Named(page, "Retry ejecting Backup (E:)"));
            Assert.Equal("", page.ListFailure);
            Assert.DoesNotContain(Find<TextBlock>(page), t => t.Text == "Eject failed");
            Assert.Equal(["eject Backup (E:)"], f.Pc.Calls);
        });
    }

    [Fact]
    public void Bulk_quit_uses_the_reviewed_snapshot_even_if_another_app_opens()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            f.Start(QuickState.Empty with { Apps = [FakePc.Chrome, FakePc.Spotify, FakePc.Discord] }, new() { Shown = ["quit"], Kept = [FakePc.Spotify.Path] });
            var page = new ActionsPage(f.Module);
            page.OpenConfirmation = _ => { };
            {
                page.Press(QuickActionId.Quit);
                Click(Find<Button>(page).Single(b => Equals(b.Content, "Quit all")));
                Assert.Equal("Quit 2 apps?", page.ConfirmationQuestion);
                Assert.Equal([FakePc.Chrome, FakePc.Discord], page.ConfirmedApps);
                var confirm = page.Confirmation!;
                Assert.Contains(Find<TextBlock>(confirm.Child), t => t.Text == "Keep open: Spotify");
                var newcomer = new OpenApp(@"C:\TestApps\new.exe", "New app", false);
                f.Pc.State = f.Pc.State with { Apps = [.. f.Pc.State.Apps, newcomer] };
                f.Module.RefreshAsync().GetAwaiter().GetResult();
                Assert.Equal("Quit 2 apps?", page.ConfirmationQuestion);
                Click(Find<Button>(confirm.Child).Single(b => Equals(b.Content, "Quit")));
                Assert.Equal(["quit Google Chrome", "quit Discord"], f.Pc.Calls);
                Assert.Equal([FakePc.Spotify, newcomer], f.Pc.State.Apps);
            }
        });
    }

    [Fact]
    public void Bulk_failure_marks_only_the_app_that_refused_and_retry_keeps_other_apps_alone()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            f.Start(QuickState.Empty with { Apps = [FakePc.Chrome, FakePc.Discord, FakePc.Spotify] }, new() { Shown = ["quit"] });
            f.Pc.Refuse.Add("Discord");
            var page = new ActionsPage(f.Module);
            page.OpenConfirmation = _ => { };
            {
                page.Press(QuickActionId.Quit);
                Click(Find<Button>(page).Single(b => Equals(b.Content, "Quit all")));
                Click(Find<Button>(page.Confirmation!.Child).Single(b => Equals(b.Content, "Quit")));
                Assert.Equal(["quit Google Chrome", "quit Spotify"], f.Pc.Calls);
                Assert.Single(Find<TextBlock>(page), t => t.Text == "Quit failed");
                var retry = Named(page, "Retry quitting Discord");
                f.Pc.Refuse.Clear();
                Click(retry);
                Assert.Equal(["quit Google Chrome", "quit Spotify", "quit Discord"], f.Pc.Calls);
                Assert.Equal("", page.ListFailure);
            }
        });
    }

    [Fact]
    public void Drives_and_plans_page_without_losing_the_last_item()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var drives = Enumerable.Range(1, 23).Select(i => new RemovableDrive(i.ToString(), $"Drive {i}", 1024)).ToArray();
            var plans = Enumerable.Range(1, 12).Select(i => new PowerPlan(Guid.NewGuid(), $"Plan {i}", i == 1)).ToArray();
            f.Start(QuickState.Empty with { Drives = drives, Plans = plans }, new() { Shown = ["eject", "plan"] });
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Eject);
            Click(Named(page, "Next drives"));
            Click(Named(page, "Next drives"));
            Assert.NotNull(Named(page, "Eject Drive 23"));
            Assert.True(page.Back());
            page.Press(QuickActionId.Plan);
            Click(Named(page, "Next plans"));
            Click(Named(page, "Plan 12"));
            Assert.Contains($"plan {plans[11].Id}", f.Pc.Calls);
        });
    }

    [Fact]
    public void A_failed_row_keeps_keyboard_focus_on_its_retry_even_when_app_names_match()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var first = new OpenApp(@"C:\One\app.exe", "Workbench", false);
            var second = new OpenApp(@"C:\Two\app.exe", "Workbench", false);
            f.Start(QuickState.Empty with { Apps = [first, second] }, new() { Shown = ["quit"] });
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Quit);
            string identity = "quit:" + second.Path.ToUpperInvariant();
            UIElement? focus = Find<Button>(page).Single(b => Equals(b.Tag, identity));
            page.CurrentListFocus = () => focus;
            page.SetListFocus = element => focus = element;
            f.Pc.Fail = true;
            Click(Assert.IsType<Button>(focus));
            var retry = Assert.IsType<Button>(focus);
            Assert.Equal(identity, retry.Tag);
            Assert.Equal("Retry quitting Workbench", AutomationProperties.GetName(retry));
            Assert.True(retry.IsEnabled);
            Assert.Empty(f.Pc.Calls);
        });
    }

    [Fact]
    public void A_pending_row_does_not_reclaim_focus_after_the_user_moves_elsewhere()
    {
        WpfTestThread.Run(() =>
        {
            HeldSystem? system = null;
            using var f = new Fixture(pc => system = new HeldSystem(pc));
            f.Start(QuickState.Empty with { Apps = [FakePc.Chrome, FakePc.Discord] }, new() { Shown = ["quit"] });
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Quit);
            UIElement? focus = Named(page, "Quit Discord");
            page.CurrentListFocus = () => focus;
            page.SetListFocus = element => focus = element;
            system!.QuitHold = new TaskCompletionSource();
            Click(Assert.IsType<Button>(focus));
            Assert.IsType<Grid>(focus); // no other app can be quit with Enter while this row is pending
            focus = Named(page, "Quit Google Chrome");
            system.QuitHold.SetException(new InvalidOperationException("Access denied"));
            Drain();
            Assert.Equal("Quit Google Chrome", AutomationProperties.GetName(Assert.IsType<Button>(focus)));
            Assert.NotNull(Named(page, "Retry quitting Discord"));
        });
    }

    [Fact]
    public void Closing_a_focused_app_does_not_focus_another_apps_quit_button()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            f.Start(QuickState.Empty with { Apps = [FakePc.Chrome, FakePc.Spotify] }, new() { Shown = ["quit"], Kept = [FakePc.Spotify.Path] });
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Quit);
            UIElement? focus = Named(page, "Quit Google Chrome");
            page.CurrentListFocus = () => focus;
            page.SetListFocus = element => focus = element;
            Click(Assert.IsType<Button>(focus));
            Assert.IsType<Grid>(focus);
            Assert.Equal(["quit Google Chrome"], f.Pc.Calls);
            Assert.Equal([FakePc.Spotify], f.Pc.State.Apps);
        });
    }

    [Fact]
    public void A_queued_plan_button_cannot_overlap_a_pending_plan_change()
    {
        WpfTestThread.Run(() =>
        {
            HeldSystem? system = null;
            using var f = new Fixture(pc => system = new HeldSystem(pc));
            var balanced = new PowerPlan(Guid.NewGuid(), "Balanced", true);
            var fast = new PowerPlan(Guid.NewGuid(), "Performance", false);
            var saver = new PowerPlan(Guid.NewGuid(), "Power saver", false);
            f.Start(QuickState.Empty with { Plans = [balanced, fast, saver] }, new() { Shown = ["plan"] });
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Plan);
            var queued = Named(page, "Power saver");
            system!.PlanHold = new TaskCompletionSource();
            Click(Named(page, "Performance"));
            Click(queued); // the old visual can still have an invocation queued after its replacement is disabled
            Assert.Equal([fast.Id], system.Plans);
            system.PlanHold.SetResult();
            Drain();
            Assert.Equal([fast.Id], system.Plans);
            Assert.Equal(fast.Id, f.Module.State.Plans.Single(p => p.Active).Id);
        });
    }

    [Fact]
    public void A_queued_individual_quit_cannot_overlap_the_reviewed_bulk_quit()
    {
        WpfTestThread.Run(() =>
        {
            HeldSystem? system = null;
            using var f = new Fixture(pc => system = new HeldSystem(pc));
            f.Start(QuickState.Empty with { Apps = [FakePc.Chrome, FakePc.Discord] }, new() { Shown = ["quit"] });
            var page = new ActionsPage(f.Module) { OpenConfirmation = _ => { } };
            page.Press(QuickActionId.Quit);
            var queued = Named(page, "Quit Discord");
            Click(Named(page, "Quit all"));
            system!.QuitHold = new TaskCompletionSource();
            Click(Named(page.Confirmation!.Child, "Quit"));
            Click(queued);
            Assert.Equal([FakePc.Chrome], system.Quits);
            system.QuitHold.SetResult();
            Drain();
            Assert.Equal([FakePc.Chrome, FakePc.Discord], system.Quits);
            Assert.Empty(f.Pc.State.Apps);
        });
    }

    [Fact]
    public void A_shrinking_list_clamps_to_its_remaining_page_and_keeps_full_names()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            string longName = new('W', 100);
            var apps = Enumerable.Range(1, 11).Select(i => new OpenApp($@"C:\Apps\app{i}.exe", i == 1 ? longName : $"App {i}", false)).ToArray();
            f.Start(QuickState.Empty with { Apps = apps }, new() { Shown = ["quit"] });
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Quit);
            Click(Named(page, "Next apps"));
            Assert.Equal(1, page.ListPage);
            f.Pc.State = f.Pc.State with { Apps = [apps[0]] };
            f.Module.RefreshAsync().GetAwaiter().GetResult();
            page.Render();
            Assert.Equal(0, page.ListPage);
            Assert.Equal(1, page.ListPageCount);
            Assert.NotNull(Named(page, "Quit " + longName));
            Assert.Contains(Find<TextBlock>(page), text => text.Text == longName);
        });
    }

    [Fact]
    public void Power_holds_Lock_Turn_off_displays_and_Screen_saver_beside_the_power_choices()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            f.Start(QuickState.Empty, new() { Shown = ["power"] });
            var page = new ActionsPage(f.Module);
            page.OpenConfirmation = _ => { };
            page.Press(QuickActionId.Power);
            Assert.Equal(["Lock", "Sleep", "Turn off displays", "Restart", "Screen saver", "Shut down", "Sign out"],
                Find<Button>(page).Select(AutomationProperties.GetName).Where(n => n.Length > 0 && n != "Edit actions" && n != "Done").ToArray());
            Click(Named(page, "Lock"));
            Click(Named(page, "Turn off displays"));
            Click(Named(page, "Screen saver"));
            Assert.Equal(["run Lock", "run Displays", "run ScreenSaver"], f.Pc.Calls);
            Assert.Null(page.ConfirmationQuestion);
            Click(Named(page, "Sleep"));
            Assert.Equal("Sleep?", page.ConfirmationQuestion);
        });
    }

    [Fact]
    public void A_frozen_app_shows_Not_responding_and_its_X_and_the_footer_End_ask_before_ending_it()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var outlook = new OpenApp(@"C:\Office\Outlook.exe", "Outlook", false, NotResponding: true);
            f.Start(QuickState.Empty with { Apps = [FakePc.Chrome, outlook], Frozen = 1 }, new() { Shown = ["quit"] });
            var page = new ActionsPage(f.Module);
            page.OpenConfirmation = _ => { };
            page.Press(QuickActionId.Quit);
            Assert.Contains(Find<TextBlock>(page), t => t.Text == "Not responding");
            Assert.Contains(Find<Button>(page), b => Equals(b.Tag, "quit-all"));
            var end = Find<Button>(page).Single(b => Equals(b.Tag, "end-frozen"));
            Assert.Equal("End Outlook", end.Content);

            Click(Named(page, "End Outlook (not responding)"));
            Assert.Equal("End Outlook?", page.ConfirmationQuestion);
            Assert.Empty(f.Pc.Calls);
            Click(Find<Button>(page.Confirmation!.Child).Single(b => Equals(b.Content, "End")));
            Assert.Equal(["end Outlook"], f.Pc.Calls);

            // The footer ends every frozen app, after asking.
            var second = new OpenApp(@"C:\Office\Word.exe", "Word", false, NotResponding: true);
            f.Pc.Calls.Clear();
            f.Pc.State = f.Pc.State with { Apps = [FakePc.Chrome, outlook, second], Frozen = 2 };
            f.Module.PollAsync().GetAwaiter().GetResult();
            Drain();
            page.Render();
            Click(Find<Button>(page).Single(b => Equals(b.Tag, "end-frozen")));
            Assert.Equal("End 2 apps?", page.ConfirmationQuestion);
            Click(Find<Button>(page.Confirmation!.Child).Single(b => Equals(b.Content, "End")));
            Assert.Equal(["end Outlook", "end Word"], f.Pc.Calls);
        });
    }

    [Fact]
    public void The_footer_ends_only_the_apps_its_question_named_even_if_another_hangs_meanwhile()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            var outlook = new OpenApp(@"C:\Office\Outlook.exe", "Outlook", false, NotResponding: true);
            f.Start(QuickState.Empty with { Apps = [FakePc.Chrome, outlook], Frozen = 1 }, new() { Shown = ["quit"] });
            var page = new ActionsPage(f.Module);
            page.OpenConfirmation = _ => { };
            page.Press(QuickActionId.Quit);
            Click(Find<Button>(page).Single(b => Equals(b.Tag, "end-frozen")));
            Assert.Equal("End Outlook?", page.ConfirmationQuestion);
            var popup = page.Confirmation!;
            // Word hangs while the question is open.
            f.Pc.State = f.Pc.State with { Apps = [FakePc.Chrome, outlook, new OpenApp(@"C:\Office\Word.exe", "Word", false, NotResponding: true)], Frozen = 2 };
            Click(Find<Button>(popup.Child).Single(b => Equals(b.Content, "End")));
            Assert.Equal(["end Outlook"], f.Pc.Calls);
        });
    }

    [Fact]
    public void A_responding_apps_X_still_quits_it_without_the_End_footer()
    {
        WpfTestThread.Run(() =>
        {
            using var f = new Fixture();
            f.Start(QuickState.Empty with { Apps = [FakePc.Chrome] }, new() { Shown = ["quit"] });
            var page = new ActionsPage(f.Module);
            page.Press(QuickActionId.Quit);
            Assert.DoesNotContain(Find<Button>(page), b => Equals(b.Tag, "end-frozen"));
            Click(Named(page, "Quit Google Chrome"));
            Assert.Equal(["quit Google Chrome"], f.Pc.Calls);
        });
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    private static Button Named(DependencyObject root, string name) => Find<Button>(root).Single(b => AutomationProperties.GetName(b) == name);
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private sealed class Fixture : IDisposable
    {
        public FakePc Pc { get; } = new();
        public QuickActionsModule Module { get; }
        public Fixture(Func<FakePc, IQuickSystem>? system = null) => Module = new QuickActionsModule(() => system?.Invoke(Pc) ?? Pc, () => new Hotkey());
        public void Start(QuickState state, QuickActionsSettings settings)
        {
            Pc.State = state;
            Module.StartAsync(new ModuleContext(QuickActionsModule.Id, new Store(settings), NullLogger.Instance,
                new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), new EmptyUi()), default).GetAwaiter().GetResult();
        }
        public void Dispose() => Module.StopAsync(default).GetAwaiter().GetResult();
    }

    private sealed class Store(QuickActionsSettings value) : IModuleSettings
    {
        private QuickActionsSettings _value = value;
        public T Get<T>() where T : class, new() => (T)(object)_value;
        public void Save<T>(T settings) where T : class => _value = (QuickActionsSettings)(object)settings;
    }

    private sealed class EmptyUi : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(QuickActionsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> page) { }
    }

    private sealed class Hotkey : IHotkey
    {
        public bool Register(KeyShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }

    private sealed class HeldSystem(FakePc pc) : IQuickSystem
    {
        public TaskCompletionSource? QuitHold, PlanHold;
        public List<OpenApp> Quits { get; } = [];
        public List<Guid> Plans { get; } = [];
        public Task<QuickState> ReadAsync(uint frontProcessId, CancellationToken ct = default) => pc.ReadAsync(frontProcessId, ct);
        public Task SetAsync(QuickActionId id, bool on) => pc.SetAsync(id, on);
        public async Task SetPowerPlanAsync(Guid plan)
        {
            Plans.Add(plan);
            if (PlanHold is { } hold) await hold.Task;
            await pc.SetPowerPlanAsync(plan);
        }
        public Task EjectAsync(RemovableDrive drive) => pc.EjectAsync(drive);
        public async Task QuitAsync(OpenApp app)
        {
            Quits.Add(app);
            if (QuitHold is { } hold) await hold.Task;
            await pc.QuitAsync(app);
        }
        public Task EndAsync(OpenApp app) => pc.EndAsync(app);
        public Task RunAsync(QuickActionId id) => pc.RunAsync(id);
        public Task PowerAsync(PowerChoice choice) => pc.PowerAsync(choice);
    }
}
