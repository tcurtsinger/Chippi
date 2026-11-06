// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Core.Actions;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.WindowLayouts.Views;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Features.WindowLayouts.Tests.ArrangementPageTests;
using static Chippi.Features.WindowLayouts.Tests.SnapPageTests;

namespace Chippi.Features.WindowLayouts.Tests;

/// <summary>Actions › Snap › Arrangements, a run's page and its lines on the bar (arrangements.html, sheet-arrangements-island).</summary>
public sealed class ArrangementIslandTests
{
    private sealed class Store : IModuleSettings
    {
        public WindowLayoutsSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (WindowLayoutsSettings)(object)value;
    }

    private sealed class Ui : IModuleUi
    {
        public Func<IReadOnlyList<ChippiCommand>> Commands = () => [];
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(WindowLayoutsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> page) { }
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
    }

    private sealed class Registrar : IHotkeyRegistrar
    {
        public bool Register(int id, KeyShortcut keys) => true;
        public void Unregister(int id) { }
        public event Action<int>? Pressed { add { } remove { } }
    }

    /// <summary>A desktop whose launches open their window at once, unless the test holds or fails them.</summary>
    private sealed class Desktop : IArrangementDesktop
    {
        public List<ArrangementLiveWindow> Live = [];
        public List<SnapDisplay> Screens = [Dell, Laptop];
        public string? Stop;
        public int Beeps;
        public Func<ArrangementRow, string?> Fails = _ => null;
        public Func<ArrangementRow, bool> Holds = _ => false;
        private int _handle = 900;
        public IReadOnlyList<ArrangementLiveWindow> Windows() => Live;
        public IReadOnlyList<SnapDisplay> Displays() => Screens;
        public string? StopReason(IReadOnlySet<ArrangementProcess> launched) => Stop;
        public ArrangementProcess? Launch(ArrangementRow row)
        {
            if (Fails(row) is { } failure) throw new ActionFailure(failure);
            if (!Holds(row)) Live.Add(ArrangementPageTests.Live(row, _handle++));
            return new(7, 1);
        }
        public void Place(ActionWindow target, SavedWindowPlacement placement, nint monitor) { }
        public void Beep() => Beeps++;
    }

    private sealed class Fixture
    {
        public readonly Store Store = new();
        public readonly Ui Ui = new();
        public readonly Desktop Desktop = new();
        public readonly IslandActivityHub Island = new();
        public bool CanShow = true;
        public readonly WindowLayoutsModule Module;

        public Fixture(WindowLayoutsSettings settings)
        {
            Module = new(() => Desktop.Screens);
            Store.Value = settings;
            Module.CreateArrangementDesktop = _ => Desktop;
            Module.StartAsync(new ModuleContext(WindowLayoutsModule.Id, Store, NullLogger.Instance, Island.CreatePublisher(WindowLayoutsModule.Id), Ui)
            { CanShowIslandFeedback = () => CanShow, Shortcuts = new ShortcutService(new Registrar()) }, default).GetAwaiter().GetResult();
        }

        public IslandActivity? Bar => Island.Activities.FirstOrDefault(a => a.Key == WindowLayoutsModule.RunKey);

        public void Stop() => Module.StopAsync(default).GetAwaiter().GetResult();
    }

    private static List<ArrangementRow> Coding() => ArrangementPageTests.Coding();

    private static WindowLayoutsSettings Board() => new()
    {
        Arrangements =
        [
            new() { Id = "coding", Name = "Coding", Rows = Coding() },
            new() { Id = "review", Name = "Review", Rows = [.. Coding().Take(5)] },
            new() { Id = "writing", Name = "Writing", Rows = [.. Coding().Where(r => r.DisplayId == Laptop.Id).Take(3)] },
        ],
    };

    private static void IslandTheme()
    {
        Theme();
        if (Application.Current.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("IslandStyles.xaml", StringComparison.Ordinal) == true))
            return;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
    }

    /// <summary>The page in the island's 496 × 304 body under its header: back, title and the header's right.</summary>
    private static void Page(SnapMenu menu, string name)
    {
        var title = new TextBlock { Text = menu.Title ?? "Snap", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var back = new TextBlock { Text = "‹", FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel { Height = 24, Margin = new Thickness(0, 0, 0, 8) };
        if (menu.Aside is FrameworkElement aside) { DockPanel.SetDock(aside, Dock.Right); header.Children.Add(aside); }
        header.Children.Add(back);
        header.Children.Add(title);
        var body = new Border { Width = 496, Height = 304, Child = menu };
        var stack = new StackPanel { Children = { header, body } };
        var host = new Border { Padding = new Thickness(12), Child = stack, Width = 520 };
        host.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        host.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimaryBrush");
        Shot(host, name, 520);
        Layout(host, 520);
        Assert.InRange(((FrameworkElement)menu.Content).ActualHeight, 0, 304);
        host.Child = null;
        header.Children.Clear();
        body.Child = null;
    }

    private static List<string> Lines(DependencyObject root) => Texts(root);

    [Fact]
    public void Snap_has_Arrangements_in_its_header_and_its_list_launches()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var f = new Fixture(Board());
            var target = new ActionWindow(42, 42, 1, 1, "Chrome_WidgetWin_1", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "Chrome", "Chippi", false);
            var menu = new SnapMenu(f.Module, target);
            Assert.Null(menu.Title);
            var aside = Assert.IsType<Button>(menu.Aside);
            Page(menu, "island-snap");

            aside.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SnapMenu.Page.List, menu.Showing);
            Assert.Equal("Arrangements", menu.Title);
            Assert.Null(menu.Aside);
            Layout(menu, 496);
            var lines = Lines(menu);
            Assert.Contains("12 windows", lines);
            Assert.Contains("3 windows · Built-in display", lines);
            Page(menu, "island-list");

            // Back returns to the layouts, and once more leaves the menu.
            Assert.True(menu.Back());
            Assert.Equal(SnapMenu.Page.Layouts, menu.Showing);
            Assert.False(menu.Back());

            // Search finds it by its name with Launch as the verb.
            var command = f.Ui.Commands().Single(c => c.Key == "window-layouts/arrangement/coding");
            Assert.Equal(("Coding", "Arrangement · 12 windows", ChippiCommandKind.Launch), (command.Name, command.Detail, command.Kind));
            f.Stop();

            var empty = new Fixture(new());
            menu = new SnapMenu(empty.Module, target);
            menu.Show(SnapMenu.Page.List);
            Layout(menu, 496);
            Assert.Contains("No arrangements", Lines(menu));
            Page(menu, "island-list-empty");
            empty.Stop();
        });
    }

    [Fact]
    public void A_run_shows_on_its_page_and_on_the_bar_until_dismissed()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var f = new Fixture(Board());
            var target = new ActionWindow(42, 42, 1, 1, "Notepad", Path.Combine(Environment.SystemDirectory, "notepad.exe"), "Notepad", "notes", false);

            // Launching: five windows are open, the rest are still opening.
            int handle = 500;
            f.Desktop.Live = [.. Coding().Take(5).Select(r => Live(r, handle++))];
            f.Desktop.Holds = _ => true;
            _ = f.Module.LaunchArrangementAsync("coding", Dell.Id);
            Pump(() => f.Module.Arrangements!.Results.Count(r => r.State == ArrangementRowState.Ready) == 5);
            Assert.Equal(("5 of 12 ready", "Coding", IslandActivityKind.Timer), (f.Bar!.Title, f.Bar.Source, f.Bar.Kind));
            Assert.Equal(5 / 12.0, f.Bar.Progress!.Value, 3);
            // A click on the bar opens Actions › Snap on the run.
            Assert.True(f.Module.TakeRunAsk());
            var menu = new SnapMenu(f.Module, target);
            Assert.Equal(SnapMenu.Page.Run, menu.Showing);
            Assert.Equal("Coding", menu.Title);
            Layout(menu, 496);
            Assert.Contains("5 of 12 ready", Lines(menu));
            Assert.Contains("Opening", Lines(menu));
            Assert.Contains("1\u201310 of 12", Lines(menu));
            Page(menu, "island-run-launching");

            // Cancelled: what's open stays; the rest say so, and Launch starts it again.
            f.Module.CancelArrangement();
            Pump(() => !f.Module.Arrangements!.IsRunning && f.Bar?.Title == "Cancelled");
            Assert.Equal("Coding · 5 ready", f.Bar!.Source);
            menu = new SnapMenu(f.Module, target);
            menu.Show(SnapMenu.Page.Run);
            Layout(menu, 496);
            Assert.Contains("5 ready · 7 cancelled", Lines(menu));
            Assert.Contains("Launch", Find<Button>(menu).Select(b => b.Content as string));
            Page(menu, "island-run-cancelled");

            // Stopped: a fullscreen app came to the front.
            f.Desktop.Live = [.. Coding().Take(5).Select(r => Live(r, handle++))];
            _ = f.Module.LaunchArrangementAsync("coding", Dell.Id);
            Pump(() => f.Module.Arrangements!.Results.Count(r => r.State == ArrangementRowState.Ready) == 5);
            f.Desktop.Stop = "Fullscreen app";
            Pump(() => !f.Module.Arrangements!.IsRunning && f.Bar?.Title == "Stopped");
            Assert.True(f.Bar!.NeedsAttention);
            menu.Show(SnapMenu.Page.Run);
            Layout(menu, 496);
            Assert.Contains("Stopped · Fullscreen app", Lines(menu));
            Page(menu, "island-run-stopped");

            // Partly done: problems first, Retry runs only those.
            f.Desktop.Stop = null;
            f.Desktop.Holds = _ => false;
            f.Desktop.Live = [.. Coding().Skip(3).Select(r => Live(r, handle++))];
            f.Desktop.Fails = r => r.App switch { "Notepad" => "Not found", "File Explorer" => "Needs admin", "Command Prompt" => "Timed out", _ => null };
            _ = f.Module.LaunchArrangementAsync("coding", Dell.Id);
            Pump(() => !f.Module.Arrangements!.IsRunning && f.Bar?.Title == "9 of 12 ready");
            Assert.Equal(("Coding · 3 need attention", true), (f.Bar!.Source, f.Bar.NeedsAttention));
            menu.Show(SnapMenu.Page.Run);
            Layout(menu, 496);
            var lines = Lines(menu);
            Assert.Contains("9 of 12 ready · 3 need attention", lines);
            Assert.Contains("Launching failed · Not found", lines);
            Assert.Contains("Skipped · Needs admin", lines);
            Page(menu, "island-run-partly");
            Find<Button>(menu).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Next windows").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(menu, 496);
            Assert.Contains("11\u201312 of 12", Lines(menu));
            Page(menu, "island-run-partly-2");

            // Retry the failed rows; then Dismiss takes it off the bar.
            f.Desktop.Fails = r => r.App == "File Explorer" ? "Needs admin" : null;
            _ = f.Module.RetryArrangementAsync();
            Pump(() => !f.Module.Arrangements!.IsRunning && f.Bar?.Title == "11 of 12 ready");
            f.Module.DismissArrangement();
            Assert.Null(f.Bar);
            f.Stop();
        });
    }

    [Fact]
    public void Guessed_windows_and_a_missing_display_stay_on_the_bar_and_a_clean_run_folds()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var target = new ActionWindow(42, 42, 1, 1, "Notepad", Path.Combine(Environment.SystemDirectory, "notepad.exe"), "Notepad", "notes", false);

            // Two Edge windows whose titles changed: the first is the nearest of two, a guess; the second is the one left.
            var f = new Fixture(Board());
            int handle = 700;
            f.Desktop.Live = [.. Coding().Select(r => Live(r.App == "Microsoft Edge" ? r with { TitleHint = "New tab" } : r, handle++))];
            _ = f.Module.LaunchArrangementAsync("coding", Dell.Id);
            Pump(() => !f.Module.Arrangements!.IsRunning && f.Bar is not null && !f.Module.ArrangementsPreparing);
            Assert.Equal(("12 of 12 ready", "Coding · 1 guessed", false), (f.Bar!.Title, f.Bar.Source, f.Bar.NeedsAttention));
            var menu = new SnapMenu(f.Module, target);
            menu.Show(SnapMenu.Page.Run);
            Layout(menu, 496);
            Assert.Contains("12 of 12 ready · 1 guessed", Lines(menu));
            Assert.Single(Lines(menu), l => l == "Ready · Guessed");
            Page(menu, "island-run-guessed");
            f.Stop();

            // The laptop's display isn't connected: its windows go to the one it was launched from.
            f = new Fixture(Board());
            f.Desktop.Screens = [Dell];
            f.Desktop.Live = [.. Coding().Select(r => Live(r, handle++))];
            _ = f.Module.LaunchArrangementAsync("coding", Dell.Id);
            Pump(() => !f.Module.Arrangements!.IsRunning && f.Bar is not null && !f.Module.ArrangementsPreparing);
            Assert.Equal("Coding · 6 on another display", f.Bar!.Source);
            menu = new SnapMenu(f.Module, target);
            menu.Show(SnapMenu.Page.Run);
            Layout(menu, 496);
            Assert.Contains("Ready · On DELL U2723QE", Lines(menu));
            Assert.Contains("Built-in display not connected", Lines(menu));
            Page(menu, "island-run-missing-display");
            f.Stop();

            // A clean run folds off the bar by itself.
            f = new Fixture(Board());
            f.Desktop.Live = [.. Coding().Select(r => Live(r, handle++))];
            _ = f.Module.LaunchArrangementAsync("writing", Dell.Id);
            Pump(() => !f.Module.Arrangements!.IsRunning && !f.Module.ArrangementsPreparing && f.Module.Arrangements.Results.Count == 3);
            Pump(() => f.Bar is null);
            f.Stop();
        });
    }

    /// <summary>
    /// CHIPPI_ARRANGEMENT_TIMINGS names a report: Settings › Snap opening filled with three arrangements, cold and warm; the
    /// review opening on a capture of fifteen windows; the island's list and run page; and the run page following a row.
    /// </summary>
    [Fact]
    public void Timings()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ARRANGEMENT_TIMINGS") is not { Length: > 0 } report) return;
        var lines = new List<string> { "what\tms" };
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var target = new ActionWindow(42, 42, 1, 1, "Notepad", Path.Combine(Environment.SystemDirectory, "notepad.exe"), "Notepad", "notes", false);
            static double Time(Action act)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                act();
                return watch.Elapsed.TotalMilliseconds;
            }
            for (int run = 0; run < 3; run++)
            {
                var f = new Fixture(Board());
                SnapPage? page = null;
                double open = Time(() => { page = new SnapPage(f.Module); Layout(page, 768); });
                lines.Add($"Settings › Snap opens filled with 3 arrangements ({(run == 0 ? "cold" : "warm")})\t{open:0.0}");
                var section = page!.Arrangements;
                int handle = 100;
                f.Desktop.Live = [.. Coding().Select(r => Live(r, handle++)), .. Coding().Take(3).Select(r => Live(r with { TitleHint = r.TitleHint + " 2" }, handle++))];
                double review = Time(() => { section.New(); Pump(() => section.Current is { Capturing: false }); Layout(page, 768); });
                lines.Add($"New arrangement: review filled with 15 windows (fake capture)\t{review:0.0}");
                double list = Time(() => { var menu = new SnapMenu(f.Module, target); menu.Show(SnapMenu.Page.List); Layout(menu, 496); });
                lines.Add($"Actions › Snap › Arrangements\t{list:0.0}");
                f.Desktop.Holds = _ => true;
                f.Desktop.Live = [.. Coding().Take(5).Select(r => Live(r, handle++))];
                _ = f.Module.LaunchArrangementAsync("coding", Dell.Id);
                Pump(() => f.Module.Arrangements!.Results.Count(r => r.State == ArrangementRowState.Ready) == 5);
                SnapMenu? shown = null;
                double runPage = Time(() => { shown = new SnapMenu(f.Module, target); shown.Show(SnapMenu.Page.Run); Layout(shown, 496); });
                lines.Add($"Run page opens with 12 rows\t{runPage:0.0}");
                f.Desktop.Live.Add(Live(Coding()[5], handle++));
                double follow = Time(() => Pump(() => Texts(shown!).Contains("6 of 12 ready")));
                lines.Add($"Run page shows the next ready window (includes the runner's 100 ms poll)\t{follow:0.0}");
                f.Module.CancelArrangement();
                f.Stop();
            }
        });
        File.WriteAllLines(report, lines);
    }

    [Fact]
    public void A_refused_launch_says_why_on_the_bar_or_beeps_once_when_it_cannot_show()
    {
        WpfTestThread.Run(() =>
        {
            var f = new Fixture(Board());
            f.Desktop.Stop = "Fullscreen app";
            Assert.Throws<ActionFailure>(() => { _ = f.Module.LaunchArrangementAsync("coding", Dell.Id); });
            Assert.Equal(("Not launched", "Coding · Fullscreen app", true, true), (f.Bar!.Title, f.Bar.Source, f.Bar.IsFeedback, f.Bar.NeedsAttention));
            Assert.Equal(0, f.Desktop.Beeps);

            // The widget hides for the fullscreen app: one beep, as Close protection gives.
            f.CanShow = false;
            Assert.Throws<ActionFailure>(() => { _ = f.Module.LaunchArrangementAsync("writing", Dell.Id); });
            Assert.Null(f.Bar);
            Assert.Equal(1, f.Desktop.Beeps);
            f.Stop();
        });
    }
}
