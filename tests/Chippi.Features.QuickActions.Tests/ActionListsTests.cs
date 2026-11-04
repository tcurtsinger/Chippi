// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            var window = new Window { Left = -20000, Top = 0, Width = 520, Height = 360, ShowActivated = false, ShowInTaskbar = false, Content = page };
            try
            {
                window.Show();
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
            finally { window.Close(); }
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
            var window = new Window { Left = -20000, Top = 0, Width = 520, Height = 360, ShowActivated = false, ShowInTaskbar = false, Content = page };
            try
            {
                window.Show();
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
            finally { window.Close(); }
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
        public Fixture() => Module = new QuickActionsModule(() => Pc, () => new Hotkey());
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

    private sealed class Hotkey : IMicHotkey
    {
        public bool Register(MicShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
