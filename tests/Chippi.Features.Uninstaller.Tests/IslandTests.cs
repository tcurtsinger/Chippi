// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Admin;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Uninstaller;
using Chippi.Features.Uninstaller.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Uninstaller.Tests;

/// <summary>
/// The Uninstaller's island page (docs/phase-7/design/uninstaller.html, sheets entry, confirm, pickers, run), offscreen, on a fixture PC and fakes:
/// nothing here shows a window, runs an uninstaller or touches the registry, the Recycle Bin or the clipboard. With CHIPPI_ISLAND_SHOTS naming a
/// folder it saves each state as uninstaller-island-*.png (dark, or light with CHIPPI_SHOTS_THEME=light).
/// </summary>
public sealed class IslandTests
{
    private sealed class Ui : IModuleUi
    {
        public List<IslandTileSpec> Tiles = [];
        public List<Func<IReadOnlyList<ChippiCommand>>> Commands = [];
        public List<Func<string, IReadOnlyList<ChippiCommand>>> Searches = [];
        public IIslandTile AddIslandTile(IslandTileSpec spec) { Tiles.Add(spec); return new IslandTileContribution("uninstaller", spec); }
        public void SetSettingsPage(Func<string?, object> createView) { }
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands.Add(commands);
        public void AddCommandSearch(Func<string, IReadOnlyList<ChippiCommand>> search) => Searches.Add(search);
    }

    private sealed class Settings : IModuleSettings
    {
        public object? Stored;
        public T Get<T>() where T : class, new() => Stored as T ?? new();
        public void Save<T>(T value) where T : class => Stored = value;
    }

    private sealed class Island : IIslandPublisher
    {
        public List<IslandActivity> Published = [];
        public void Publish(IslandActivity activity) => Published.Add(activity);
        public void Clear(string key) { }
        public void ReportSuccess(string? message = null) { }
    }

    /// <summary>The module on a fixture PC with the island "on": every page open is accepted and recorded.</summary>
    private sealed class Rig : IDisposable
    {
        public Fixture F { get; } = new();
        public Ui Ui { get; } = new();
        public Island Island { get; } = new();
        public List<string?> Opened { get; } = [];
        public List<string> Backs { get; } = [];
        public List<string> Pages { get; } = [];
        public int Closed { get; private set; }
        public bool IslandOn { get; set; } = true;
        public UninstallerModule Module { get; }
        public ModuleContext Context { get; }
        public UninstallerEngine Engine => Module.Engine!;
        public UninstallerView View => Module.View();

        public Rig(Action<Fixture>? seed = null)
        {
            UninstallerUi.LoadIcons = false;
            seed?.Invoke(F);
            Module = new UninstallerModule(() => F.Sys, () => F.Roots);
            Context = new ModuleContext(UninstallerModule.Id, new Settings(), NullLogger.Instance, Island, Ui)
            {
                OpenSettings = Opened.Add, Admin = F.Admin, DataDirectory = Path.Combine(F.Base, "data"),
                TryOpenIslandPage = key => { if (IslandOn) Pages.Add(key); return IslandOn; },
                TryGoBackIslandPage = key => { Backs.Add(key); return true; },
                CloseIsland = () => Closed++,
            };
            Assert.True(Module.StartAsync(Context, default).IsCompletedSuccessfully);
            WpfTestThread.Wait(Engine.RefreshAsync());
            F.Admin.OnUninstall = item => F.Registry.Remove(item.View == AdminRegistryView.Registry32 ? AppSource.Machine32 : AppSource.Machine64, item.KeyName);
        }

        public void Dispose() => F.Dispose();
    }

    private static string Exe(Fixture f, string folder) => f.WriteFile(Path.Combine(folder, "unins000.exe"));

    /// <summary>Notepad++ (a machine app with a wizard), Slack (the user's, quiet), Notepad (Store), Notion (the user's, wizard) and 20 fillers.</summary>
    private static void Apps(Fixture f)
    {
        string np = f.ProgramFiles("Notepad++");
        f.Registry.Add(AppSource.Machine64, "Notepad++", "Notepad++", "Notepad++ Team", "8.7.1", np, "\"" + Exe(f, np) + "\"", null, [("EstimatedSize", 12700), ("InstallDate", "20260903")]);
        string slack = f.Local("Programs", "Slack");
        f.Registry.Add(AppSource.User, "Slack", "Slack", "Slack Technologies", "4.40", slack, null, "\"" + Exe(f, slack) + "\" /quiet", [("EstimatedSize", 297984), ("InstallDate", "20260812")]);
        string notion = f.Local("Programs", "Notion");
        f.Registry.Add(AppSource.User, "Notion", "Notion", "Notion Labs, Inc.", "4.2", notion, "\"" + Exe(f, notion) + "\"", null, [("EstimatedSize", 364544), ("InstallDate", "20261114")]);
        f.Packages.Packages.Add(new RawPackage("Full_Notepad", "Family_Notepad", "Notepad", "Notepad", "Microsoft Corporation", "11.2410", @"C:\WindowsApps\Notepad", new DateTime(2025, 12, 1), false, false, false));
        for (int i = 1; i <= 20; i++)
        {
            string folder = f.Local("Programs", "Filler " + i.ToString("00"));
            f.Registry.Add(AppSource.User, "filler" + i, "Filler " + i.ToString("00"), "Maker", "1.0", folder, "\"" + Exe(f, folder) + "\"", null, [("EstimatedSize", 1024 * (10 + i))]);
        }
        // The user's uninstallers remove their own entry: the folder name says which.
        f.User.Run = (application, _) =>
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(application)!);
            f.Registry.Remove(AppSource.User, folder.StartsWith("Filler ", StringComparison.Ordinal) ? "filler" + int.Parse(folder[7..], CultureInfo.InvariantCulture) : folder);
            return 0;
        };
    }

    // ---- helpers ----

    private static void Arrange(FrameworkElement element, double width = 494, double height = 302)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static IEnumerable<T> Walk<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Walk<T>(child)) yield return nested;
        }
    }

    /// <summary>Lays the page out and reads what it says: the header, then the body.</summary>
    private static IReadOnlyList<string> Said(UninstallerView view)
    {
        Arrange(view);
        Arrange(view.Header, 494, 32);
        // A search field's placeholder is the control's, not the page's.
        return [.. Walk<TextBlock>(view.Header).Select(t => t.Text).Concat(Walk<TextBlock>(view).Select(t => t.Text)).Where(t => t is not ("Search" or "Search apps"))];
    }

    private static Button Named(UninstallerView view, string name)
    {
        Arrange(view);
        return Walk<Button>(view).First(b => AutomationProperties.GetName(b) == name);
    }

    /// <summary>A button by its key: the question's verb is named for the question ("Uninstall Notepad++"), so tests find it by key.</summary>
    private static Button Pressed(UninstallerView view, string key)
    {
        Arrange(view);
        return Walk<Button>(view).First(b => b.Tag as string == key);
    }

    private static T Result<T>(Task<T> task)
    {
        WpfTestThread.Wait(task);
        return task.Result;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static bool HasGlyph(DependencyObject root, System.Windows.Media.Geometry glyph) => Walk<LucideIcon>(root).Any(i => i.Data == glyph);

    private static Task Until(Func<bool> condition) => Task.Run(async () =>
    {
        var stop = Stopwatch.StartNew();
        while (stop.Elapsed < TimeSpan.FromSeconds(20))
        {
            bool ready = false;
            await Application.Current.Dispatcher.InvokeAsync(() => ready = condition());
            if (ready) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("The page never reached the state the test waited for.");
    });

    private static ChippiCommand RowFor(Rig rig, string text) => rig.Ui.Searches.Single()(text).First();

    private static AppEntry App(Rig rig, string name) => rig.Engine.Apps.First(a => a.Name == name);

    // ---- Search: Run asks first ----

    [Fact]
    public void A_Search_row_says_the_size_Store_app_or_Running_and_a_running_app_has_no_Run() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        var notepad = rig.Ui.Searches.Single()("uninstall notepad");
        Assert.Equal(["Uninstall Notepad", "Uninstall Notepad++"], notepad.Select(r => r.Name));
        Assert.Equal(("Store app", "Run", false), (notepad[0].Detail, notepad[0].Verb, notepad[0].NeedsAdmin));
        Assert.Equal(("12 MB", "Run", true), (notepad[1].Detail, notepad[1].Verb, notepad[1].NeedsAdmin));   // an HKLM app: the shield
        var slack = RowFor(rig, "uninstall slack");
        Assert.Equal(("291 MB", "Run", false), (slack.Detail, slack.Verb, slack.NeedsAdmin));   // the user's own: none
        using var again = new Rig(f => { Apps(f); f.Processes.RunningFolders.Add(f.ProgramFiles("Notepad++")); });
        var open = again.Ui.Searches.Single()("uninstall notepad++").Single();
        Assert.Equal(("Running", "", false), (open.Detail, open.Verb, open.NeedsAdmin));
    });

    [Fact]
    public void Run_asks_Uninstall_Notepad_first_with_Cancel_focused_and_Cancel_runs_nothing() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        var row = RowFor(rig, "uninstall notepad++");
        WpfTestThread.Wait(row.Run());
        Assert.Equal([UninstallerModule.Page], rig.Pages);
        var view = rig.View;
        Assert.Equal(UninstallerPageMode.Question, view.Mode);
        Assert.Equal(["Uninstall Notepad++?", "Cancel", "Uninstall"], Said(view));
        Assert.Equal("Cancel", AutomationProperties.GetName(view.FocusTarget!));   // the FocusTarget seam: Cancel has the keyboard
        Assert.Equal(["Cancel"], [view.InitialFocus is Button b ? AutomationProperties.GetName(b) : "?"]);
        Assert.Single(Walk<UacShield>(Pressed(view, "uninstall")));   // the helper is used: the shield is inside the verb
        Assert.Equal("Uninstall Notepad++", AutomationProperties.GetName(Pressed(view, "uninstall")));   // its accessible name is the question

        Click(Named(view, "Cancel"));
        Assert.Empty(rig.F.Admin.Calls);
        Assert.Empty(rig.F.User.Calls);
        Assert.Empty(rig.F.Packages.Removed);
        Assert.Equal(["command-bar"], rig.Backs);   // back to Search
        Assert.Contains(rig.Engine.Apps, a => a.Name == "Notepad++");
    });

    [Fact]
    public void The_question_of_a_user_app_has_no_shield_and_Esc_is_Cancel() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        WpfTestThread.Wait(RowFor(rig, "uninstall slack").Run());
        var view = rig.View;
        Assert.Equal(["Uninstall Slack?", "Cancel", "Uninstall"], Said(view));
        Assert.Empty(Walk<UacShield>(Pressed(view, "uninstall")));
        Assert.False(view.Back());   // not from a picker: the panel goes back itself
        Assert.Empty(rig.F.User.Calls);
    });

    [Fact]
    public void With_the_island_off_Run_and_the_shortcuts_open_Settings() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps) { IslandOn = false };
        WpfTestThread.Wait(RowFor(rig, "uninstall notepad++").Run());
        rig.Module.PickApp();
        WpfTestThread.Wait(rig.Module.ScanAsync());
        Assert.Equal(["search:Notepad++", "search", "uninstalled"], rig.Opened);
        Assert.Null(Result(rig.Module.AskQuitAsync()));
        Assert.Empty(rig.F.Admin.Calls);
    });

    // ---- the run and its states ----

    /// <summary>Asks the question for <paramref name="app"/> and presses Uninstall; returns when the page shows its result.</summary>
    private static void RunThrough(Rig rig, AppEntry app)
    {
        WpfTestThread.Wait(rig.Module.Request!([app]));
        var view = rig.View;
        Click(Pressed(view, "uninstall"));
        WpfTestThread.Wait(Until(() => view.Mode == UninstallerPageMode.Result));
    }

    [Fact]
    public void A_quiet_uninstaller_shows_the_spinner_and_a_moving_bar_then_Done_and_Scan() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        IReadOnlyList<string>? during = null;
        bool spinner = false, track = false, wizardGlyph = false;
        var user = rig.F.User.Run;
        rig.F.User.Run = (a, b) =>
        {
            during = Said(rig.View);
            spinner = HasGlyph(rig.View, LucideIcons.LoaderCircle);
            wizardGlyph = HasGlyph(rig.View, LucideIcons.AppWindow);
            track = Walk<Border>(rig.View).Any(x => x.Width == 180);
            return user(a, b);
        };
        RunThrough(rig, App(rig, "Slack"));
        Assert.Equal(["Uninstall", "Uninstalling Slack"], during);
        Assert.True(spinner && track && !wizardGlyph);
        var view = rig.View;
        Assert.Equal(["Uninstall", "Slack uninstalled", "Done", "Scan"], Said(view));
        Assert.True(HasGlyph(view, LucideIcons.CircleCheck));
        Assert.Equal("Scan", AutomationProperties.GetName(view.FocusTarget!));
        var record = Assert.Single(rig.Engine.Recent);
        Click(Named(view, "Scan"));
        Assert.Equal([UninstallerModule.ScanSectionFor(record.Id)], rig.Opened);   // Settings' review for that app
        Click(Named(view, "Done"));
        Assert.Equal(["command-bar"], rig.Backs);
    });

    [Fact]
    public void A_wizard_only_uninstaller_shows_the_window_glyph_without_a_bar() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        IReadOnlyList<string>? during = null;
        bool wizard = false, spinner = false;
        var user = rig.F.User.Run;
        rig.F.User.Run = (a, b) => { during = Said(rig.View); wizard = HasGlyph(rig.View, LucideIcons.AppWindow); spinner = HasGlyph(rig.View, LucideIcons.LoaderCircle); return user(a, b); };
        RunThrough(rig, App(rig, "Notion"));
        Assert.Equal(["Uninstall", "Uninstalling Notion"], during);
        Assert.True(wizard && !spinner);
    });

    [Fact]
    public void A_machine_app_waits_for_admin_with_the_shield_while_the_Windows_prompt_is_open() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        IReadOnlyList<string>? during = null;
        bool shield = false;
        var remove = rig.F.Admin.OnUninstall;
        rig.F.Admin.OnUninstall = item =>
        {
            Assert.True(rig.Engine.WaitsForAdmin);
            rig.View.Redraw();   // a shown page redraws on the engine's Changed
            during = Said(rig.View);
            shield = Walk<UacShield>(rig.View).Count() == 1;
            remove!(item);
        };
        RunThrough(rig, App(rig, "Notepad++"));
        Assert.Equal(["Uninstall", "Waiting for admin"], during);
        Assert.True(shield);
        Assert.False(rig.Engine.WaitsForAdmin);
        Assert.Equal(["Uninstall", "Notepad++ uninstalled", "Done", "Scan"], Said(rig.View));
    });

    [Fact]
    public void A_failed_uninstall_says_so_in_Caution_with_Done_and_Retry_and_Retry_runs_again() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        int exit = 1;
        var user = rig.F.User.Run;
        rig.F.User.Run = (a, b) => exit == 0 ? user(a, b) : exit;
        RunThrough(rig, App(rig, "Notion"));
        var view = rig.View;
        Assert.Equal(["Uninstall", "Uninstall failed", "Done", "Retry"], Said(view));
        Assert.True(HasGlyph(view, LucideIcons.CircleAlert));
        Assert.Equal("Retry", AutomationProperties.GetName(view.FocusTarget!));
        Assert.Equal(1, rig.F.User.Calls.Count(c => c.App.EndsWith("unins000.exe", StringComparison.Ordinal)));
        exit = 0;
        Click(Named(view, "Retry"));
        WpfTestThread.Wait(Until(() => view.Outcome is { Succeeded: true }));
        Assert.Equal(["Uninstall", "Notion uninstalled", "Done", "Scan"], Said(view));
    });

    [Fact]
    public void Declining_the_prompt_says_Needs_admin_and_Retry_carries_the_shield() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        rig.F.Admin.Uninstall = item => new AdminUninstallResult(item, AdminUninstallState.NeedsAdmin);
        RunThrough(rig, App(rig, "Notepad++"));
        var view = rig.View;
        Assert.Equal(["Uninstall", "Needs admin", "Done", "Retry"], Said(view));
        Assert.Single(Walk<UacShield>(Named(view, "Retry")));
        Assert.Contains(rig.Engine.Apps, a => a.Name == "Notepad++");
    });

    [Fact]
    public void An_app_that_is_open_says_it_is_running_and_Retry_checks_again() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        var app = App(rig, "Notion");
        rig.F.Processes.RunningFolders.Add(rig.F.Local("Programs", "Notion"));
        RunThrough(rig, app);
        var view = rig.View;
        Assert.Equal(["Uninstall", "Notion is running", "Done", "Retry"], Said(view));
        Assert.Empty(rig.F.User.Calls);
        rig.F.Processes.RunningFolders.Clear();
        Click(Named(view, "Retry"));
        WpfTestThread.Wait(Until(() => view.Outcome is { Succeeded: true }));
        Assert.Equal(["Uninstall", "Notion uninstalled", "Done", "Scan"], Said(view));
    });

    [Fact]
    public void A_restart_the_uninstaller_asked_for_says_Restart_to_finish_with_Done_only() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        var user = rig.F.User.Run;
        rig.F.User.Run = (a, b) => { user(a, b); return 3010; };
        RunThrough(rig, App(rig, "Notion"));
        var view = rig.View;
        Assert.Equal(["Uninstall", "Restart to finish", "Done"], Said(view));
        Assert.Equal("Done", AutomationProperties.GetName(view.FocusTarget!));
    });

    // ---- the bar ----

    [Fact]
    public void The_bar_matches_the_sheet_Uninstalling_over_the_app_a_window_glyph_for_a_wizard_and_2_of_3_in_a_queue() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        WpfTestThread.Wait(rig.Engine.UninstallAsync([App(rig, "Slack"), App(rig, "Notion"), App(rig, "Filler 01")]));
        var running = rig.Island.Published.Where(p => p.Key == "uninstaller/run").ToList();
        Assert.Contains(running, a => a is { Title: "Uninstalling", Source: "2 of 3", Icon: "AppWindow", OpensTile: UninstallerModule.Page });
        Assert.Contains(running, a => a is { Title: "Uninstalling", Icon: "LoaderCircle" } && a.Source.EndsWith("of 3", StringComparison.Ordinal));   // Slack is quiet
        Assert.All(running, a => Assert.InRange(a.Progress ?? 0, 0, 1));
        var done = rig.Island.Published.Last();
        Assert.Equal(("Uninstalled", "3 apps", "CircleCheck"), (done.Title, done.Source, done.Icon));
    });

    [Fact]
    public void The_bar_says_Waiting_for_admin_Needs_admin_and_Notepad_is_running_in_Caution() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        rig.F.Admin.Uninstall = item => new AdminUninstallResult(item, AdminUninstallState.NeedsAdmin);
        WpfTestThread.Wait(rig.Engine.UninstallAsync([App(rig, "Notepad++")]));
        Assert.Contains(rig.Island.Published, a => a is { Title: "Waiting for admin", Source: "Notepad++", Icon: "Shield", NeedsAttention: false });
        var declined = rig.Island.Published.Last();
        Assert.Equal(("Needs admin", "Notepad++", "Shield", true), (declined.Title, declined.Source, declined.Icon, declined.NeedsAttention));

        rig.F.Processes.RunningFolders.Add(rig.F.Local("Programs", "Notion"));
        WpfTestThread.Wait(rig.Engine.UninstallAsync([App(rig, "Notion")]));
        var running = rig.Island.Published.Last();
        Assert.Equal(("Notion is running", "", "AppWindow", true), (running.Title, running.Source, running.Icon, running.NeedsAttention));
    });

    // ---- the Uninstall app picker ----

    [Fact]
    public void The_picker_pages_seven_rows_at_a_time_and_the_island_never_scrolls() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        var view = rig.View;
        view.ShowPicker();
        Assert.Equal(UninstallerPageMode.Apps, view.Mode);
        Assert.Equal(24, view.Apps.Count);   // 4 apps + 20 fillers, Notepad++ included
        var said = Said(view);
        Assert.Contains("1–7 of 24", said);
        Assert.Equal(7, Walk<Border>(view).Count(b => b.Child is Grid { Height: 36 }));
        Assert.Empty(Walk<ScrollViewer>(view));
        Assert.Equal("Filler 01", view.Apps[0].Name);
        Assert.Contains("Maker · 11 MB", said);   // the second line: publisher · size
        Assert.Single(Walk<TextBlock>(view), t => t.Text == "Uninstall");   // the verb sits on the selected row only

        view.TurnPage(1);
        Assert.Contains("8–14 of 24", Said(view));
        Assert.Equal(7, view.Selected);
        Assert.True(Named(view, "Previous apps").IsEnabled);
        view.TurnPage(10);
        Assert.Contains("22–24 of 24", Said(view));
        Assert.False(Named(view, "Next apps").IsEnabled);
        Assert.Equal(3, Walk<Border>(view).Count(b => b.Child is Grid { Height: 36 }));
        Click(Named(view, "Previous apps"));
        Assert.Equal(2, view.Page);
    });

    [Fact]
    public void Typing_filters_on_every_keystroke_with_Store_and_New_badges_and_the_shield_on_the_selected_machine_row() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        var view = rig.View;
        view.Now = () => new DateTime(2026, 11, 17, 12, 0, 0);
        view.ShowPicker();
        view.Field.Text = "n";
        Assert.Equal(["Notepad", "Notepad++", "Notion"], view.Apps.Take(3).Select(a => a.Name));
        view.Field.Text = "no";
        view.Field.Text = "not";
        Assert.Equal(["Notepad", "Notepad++", "Notion"], view.Apps.Select(a => a.Name));
        var said = Said(view);
        Assert.Contains("3 apps", said);
        Assert.Contains("Store", said);   // Notepad is a package
        Assert.Contains("New", said);     // Notion was installed three days ago
        Assert.Empty(Walk<UacShield>(view));   // the selected row is a Store app
        view.PickerKey(Key.Down);
        Assert.Single(Walk<UacShield>(view));  // Notepad++: the helper
        Assert.Equal(1, view.Selected);
        view.Field.Text = "zzq";
        Assert.Contains("No apps match", Said(view));
        Assert.True(HasGlyph(view, LucideIcons.Search));
        view.Field.Text = "";
        Assert.Contains("1–7 of 24", Said(view));
        Assert.Equal(0, view.Selected);
    });

    [Fact]
    public void Enter_on_a_row_asks_the_same_question_and_Cancel_returns_to_the_picker_with_its_text() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        var view = rig.View;
        view.ShowPicker();
        view.Field.Text = "notepad+";
        Assert.True(view.PickerKey(Key.Enter));
        Assert.Equal(UninstallerPageMode.Question, view.Mode);
        Assert.Equal(["Uninstall Notepad++?", "Cancel", "Uninstall"], Said(view));
        Assert.Empty(rig.F.Admin.Calls);
        Click(Named(view, "Cancel"));
        Assert.Equal(UninstallerPageMode.Apps, view.Mode);
        Assert.Equal("notepad+", view.Field.Text);   // the filter stays
        Assert.Empty(rig.Backs);   // it came from the picker: it returns there, not to Search

        view.PickerKey(Key.Enter);
        Assert.True(view.Back());   // the panel's Back or Esc does the same
        Assert.Equal(UninstallerPageMode.Apps, view.Mode);
        view.Field.Text = "zzq";
        Assert.False(view.PickerKey(Key.Enter));   // nothing to ask
        Assert.Empty(rig.F.Admin.Calls);
    });

    // ---- the Scan for leftovers picker ----

    private static AppRecord Record(string name, DateTime when, bool helper = true, bool cleaned = false, bool restart = false) =>
        new(Guid.NewGuid(), name, "Maker", "1.0", AppSource.Machine64, name, null, null, [], null, when, helper ? Guid.NewGuid() : null, cleaned, restart);

    [Fact]
    public void The_Scan_picker_lists_the_recently_uninstalled_with_their_day_and_No_record_and_Scan_opens_the_review() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(f =>
        {
            var store = f.Records();
            store.Put(Record("Firefox", new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc)));
            store.Put(Record("Slack", new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc)));
            store.Put(Record("Blender", new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), helper: false));
            store.Put(Record("Notepad++", new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc), cleaned: true));
        });
        var view = rig.View;
        view.Now = () => new DateTime(2026, 11, 17, 12, 0, 0);
        WpfTestThread.Wait(rig.Module.ScanAsync());
        Assert.Equal([UninstallerModule.Page], rig.Pages);
        Assert.Equal(UninstallerPageMode.Scan, view.Mode);
        Assert.Equal(["Scan for leftovers", "Fi", "Firefox", "Yesterday", "Scan", "Sl", "Slack", "Nov 14", "Bl", "Blender", "Oct 20 · No record"], Said(view));   // a cleaned app is not listed; the letters stand in for each icon
        Assert.Empty(rig.Opened);
        view.PickerKey(Key.Down);
        view.PickerKey(Key.Down);
        Assert.True(view.PickerKey(Key.Enter));   // No record still scans: the Check first items
        var scanned = Assert.Single(rig.Opened);
        Assert.StartsWith("scan:", scanned);
        Assert.Equal(rig.Engine.Recent.First(r => r.Name == "Blender").Id.ToString("N"), scanned![5..]);
        view.ScanField.Text = "fire";
        Assert.Equal(["Firefox"], view.Records.Select(r => r.Name));
        view.ScanField.Text = "zzq";
        Assert.Contains("No apps match", Said(view));
    });

    [Fact]
    public void Nothing_uninstalled_says_so_and_a_record_that_asked_for_a_restart_has_no_Scan() => WpfTestThread.Run(() =>
    {
        using (var rig = new Rig())
        {
            rig.View.ShowScanPicker();
            Assert.Equal(["Scan for leftovers", "Nothing uninstalled"], Said(rig.View));
            Assert.True(HasGlyph(rig.View, UninstallerIcons.History));
        }
        using var restart = new Rig(f => f.Records().Put(Record("Notion", new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc), restart: true)));
        restart.View.Now = () => new DateTime(2026, 11, 17, 12, 0, 0);
        restart.View.ShowScanPicker();
        Assert.Equal(["Scan for leftovers", "No", "Notion", "Today · Restart to finish"], Said(restart.View));
        restart.View.PickerKey(Key.Enter);
        Assert.Empty(restart.Opened);
    });

    [Fact]
    public void The_shortcuts_open_the_island_pickers_and_the_page_is_a_tile_that_takes_the_keyboard() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        rig.Module.PickApp();
        Assert.Equal(UninstallerPageMode.Apps, rig.View.Mode);
        Assert.Same(rig.View.Field, rig.View.InitialFocus);   // typing filters at once
        WpfTestThread.Wait(rig.Module.Pick!());
        Assert.Equal(UninstallerPageMode.Scan, rig.View.Mode);
        Assert.Equal([UninstallerModule.Page, UninstallerModule.Page], rig.Pages);
        Assert.Empty(rig.Opened);
        var tile = Assert.Single(rig.Ui.Tiles);
        Assert.Equal((UninstallerModule.Page, false, 32d, true), (tile.Key, tile.OnHome, tile.HeaderHeight, tile.TakesKeyboard));
    });

    // ---- the quit question ----

    [Fact]
    public void Closing_Chippi_mid_queue_asks_Stop_uninstalling_and_quit_with_Cancel_focused() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig(Apps);
        var view = rig.View;
        var answer = rig.Module.AskQuitAsync();
        Assert.False(answer.IsCompleted);
        Assert.Equal(["Stop uninstalling and quit?", "Cancel", "Quit"], Said(view));
        Assert.Equal("Cancel", AutomationProperties.GetName(view.FocusTarget!));
        Click(Named(view, "Cancel"));
        Assert.False(Result(answer));
        Assert.False(view.Quitting);

        answer = rig.Module.AskQuitAsync();
        Click(Named(view, "Quit"));
        Assert.True(Result(answer));

        answer = rig.Module.AskQuitAsync();
        Assert.True(view.Back());   // Esc is Cancel
        Assert.False(Result(answer));
    });

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
            if (Application.Current.TryFindResource("IslandButtonStyle") is null)
                Application.Current.Resources.MergedDictionaries.Add(new() { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
            var now = new DateTime(2026, 11, 17, 12, 0, 0);

            void Shot(string state, UninstallerView view, bool back = true)
            {
                var shell = new Border { Width = 520, Height = 360, Padding = new(12), CornerRadius = new(8) };
                shell.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
                var content = new Grid();
                content.RowDefinitions.Add(new() { Height = new(32) });
                content.RowDefinitions.Add(new() { Height = new(8) });
                content.RowDefinitions.Add(new());
                var top = new DockPanel();
                if (back)
                {
                    var button = new Button { Content = new LucideIcon { Data = LucideIcons.ChevronLeft, Size = 14 } };
                    button.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
                    DockPanel.SetDock(button, Dock.Left);
                    top.Children.Add(button);
                }
                var header = view.Header;
                top.Children.Add(header);
                content.Children.Add(top);
                Grid.SetRow(view, 2);
                content.Children.Add(view);
                shell.Child = content;
                view.Redraw();
                Snapshot(shell, new(520, 360), Path.Combine(output, $"uninstaller-island-{state}-{theme}.png"));
                content.Children.Remove(view);
                top.Children.Remove(header);
            }

            // The pickers on the mockup's PC.
            using (var rig = new Rig(f => ScreenTests.SeedMock(f)))
            {
                var view = rig.View;
                view.Now = () => now;
                view.ShowPicker();
                Shot("picker", view);
                view.Field.Text = "note";
                Shot("picker-typing", view);
                view.Field.Text = "zzq";
                Shot("picker-nomatch", view);
                view.Field.Text = "";
                view.PickerKey(Key.Enter);
                Shot("confirm", view, back: false);
                view.Back();
                view.Field.Text = "slack";
                view.PickerKey(Key.Enter);
                Shot("confirm-user", view, back: false);
                var quit = rig.Module.AskQuitAsync();
                Shot("confirm-quit", view, back: false);
                view.Back();
                Result(quit);
            }
            using (var rig = new Rig(f =>
            {
                var store = f.Records();
                store.Put(Record("Firefox", new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc)));
                store.Put(Record("Slack", new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc)));
                store.Put(Record("Blender", new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), helper: false));
            }))
            {
                var view = rig.View;
                view.Now = () => now;
                view.ShowScanPicker();
                Shot("scan-picker", view);
                using var none = new Rig();
                none.View.ShowScanPicker();
                Shot("scan-picker-empty", none.View);
            }

            // The run states, each reached by running the engine on fakes.
            Rig Notepad(bool machine, bool quiet, Action<Rig>? tune = null)
            {
                var rig = new Rig(f =>
                {
                    string folder = machine ? f.ProgramFiles("Notepad++") : f.Local("Programs", "Notepad++");
                    f.Registry.Add(machine ? AppSource.Machine64 : AppSource.User, "Notepad++", "Notepad++", "Notepad++ Team", "8.7.1", folder, "\"" + Exe(f, folder) + "\"",
                        quiet ? "\"" + Exe(f, folder) + "\" /S" : null, [("EstimatedSize", 12700)]);
                    f.User.Run = (_, _) => { f.Registry.Remove(AppSource.User, "Notepad++"); return 0; };
                });
                tune?.Invoke(rig);
                return rig;
            }
            void Run(string state, Rig rig, Action<Rig, Action<string>>? hook = null, bool back = true)
            {
                var view = rig.View;
                var app = rig.Engine.Apps.First(a => a.Name == "Notepad++");
                WpfTestThread.Wait(rig.Module.Request!([app]));
                hook?.Invoke(rig, name => Shot(name, view));
                Click(Pressed(view, "uninstall"));
                WpfTestThread.Wait(Until(() => view.Mode == UninstallerPageMode.Result));
                if (state.Length > 0) Shot(state, view, back);
                rig.Dispose();
            }
            Run("", Notepad(true, false), (rig, shot) => { var remove = rig.F.Admin.OnUninstall; rig.F.Admin.OnUninstall = item => { shot("run-wait"); remove!(item); }; });
            Run("", Notepad(false, true), (rig, shot) => { var user = rig.F.User.Run; rig.F.User.Run = (a, b) => { shot("run-quiet"); return user(a, b); }; });
            Run("", Notepad(false, false), (rig, shot) => { var user = rig.F.User.Run; rig.F.User.Run = (a, b) => { shot("run-wizard"); return user(a, b); }; });
            Run("run-done", Notepad(false, true));
            Run("run-failed", Notepad(false, true, rig => rig.F.User.Run = (_, _) => 1));
            Run("run-needs-admin", Notepad(true, false, rig => rig.F.Admin.Uninstall = item => new AdminUninstallResult(item, AdminUninstallState.NeedsAdmin)));
            Run("run-running", Notepad(false, true, rig => rig.F.Processes.RunningFolders.Add(rig.F.Local("Programs", "Notepad++"))));
            Run("run-restart", Notepad(false, true, rig => { var user = rig.F.User.Run; rig.F.User.Run = (a, b) => { user(a, b); return 3010; }; }));
        });
    }

    private static void Snapshot(FrameworkElement element, Size size, string path)
    {
        Arrange(element, size.Width, size.Height);
        var bitmap = new RenderTargetBitmap((int)size.Width * 2, (int)size.Height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
