// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
/// Settings › Apps › Uninstaller, offscreen, on a fixture PC and fakes: nothing here shows a window, runs an uninstaller or touches the registry,
/// the Recycle Bin or the clipboard. With CHIPPI_ISLAND_SHOTS naming a folder it saves each board of docs/phase-7/design/uninstaller.html (dark, or
/// light with CHIPPI_SHOTS_THEME=light); CHIPPI_UNINSTALLER_TIMINGS names a report of the list's opening and its search keystrokes over 300 apps.
/// </summary>
public sealed class ScreenTests
{
    // ---- the mockup's PC: name, publisher, version, size in MB, installed, kind ----

    private static readonly (string Name, string Publisher, string Version, double Mb, string Installed, string Kind)[] Mock =
    [
        ("7-Zip", "Igor Pavlov", "24.09", 5.6, "20260512", "hklm"), ("Adobe Acrobat Reader", "Adobe", "24.005.20414", 438, "20260208", "hklm"),
        ("Audacity", "Audacity Team", "3.7.1", 94, "20260821", "hklm"), ("Blender", "Blender Foundation", "4.3.0", 1740, "20261002", "hklm"),
        ("Calculator", "Microsoft Corporation", "11.2410.2.0", 2.1, "20251201", "store"), ("Discord", "Discord Inc.", "1.0.9171", 348, "20261114", "hkcu"),
        ("Docker Desktop", "Docker Inc.", "4.35.1", 1980, "20261019", "hklm"), ("Dropbox", "Dropbox, Inc.", "212.4.4128", 612, "20260630", "hkcu"),
        ("Epic Games Launcher", "Epic Games, Inc.", "1.3.80.0", 178, "20260314", "hklm"), ("Figma", "Figma, Inc.", "124.5.5", 402, "20260909", "hkcu"),
        ("Firefox", "Mozilla", "132.0.1", 231, "20261112", "hklm"), ("Git", "The Git Development Community", "2.47.0", 312, "20261011", "hklm"),
        ("GitHub Desktop", "GitHub, Inc.", "3.4.9", 301, "20260704", "hkcu"), ("Google Chrome", "Google LLC", "131.0.6778", 612, "20261109", "hklm"),
        ("HandBrake", "The HandBrake Team", "1.9.0", 87, "20260417", "hklm"), ("Inkscape", "Inkscape Project", "1.4", 398, "20260602", "hklm"),
        ("Krita", "Krita Foundation", "5.2.9", 715, "20260805", "hklm"), ("Microsoft Teams", "Microsoft Corporation", "24295.605", 204, "20260123", "store"),
        ("Node.js", "Node.js Foundation", "22.11.0", 84, "20261024", "hklm"), ("Notepad", "Microsoft Corporation", "11.2410.15.0", 8.4, "20251201", "store"),
        ("Notepad++", "Notepad++ Team", "8.7.1", 12.4, "20260903", "hklm"), ("Notion", "Notion Labs, Inc.", "4.2.0", 356, "20260721", "hkcu"),
        ("OBS Studio", "OBS Project", "30.2.3", 328, "20260930", "hklm"), ("OneNote", "Microsoft Corporation", "16001.14326", 92, "20260123", "store"),
        ("Paint", "Microsoft Corporation", "11.2410.17.0", 31, "20251201", "store"), ("PowerToys", "Microsoft Corporation", "0.85.1", 804, "20261111", "hkcu"),
        ("Python 3.13.0", "Python Software Foundation", "3.13.150", 112, "20261008", "hklm"), ("Slack", "Slack Technologies", "4.40.131", 291, "20260812", "hkcu"),
        ("Spotify", "Spotify AB", "1.2.50", 263, "20260527", "hkcu"), ("Steam", "Valve Corporation", "2.10.91", 3240, "20260202", "hklm"),
        ("Sticky Notes", "Microsoft Corporation", "6.0.7.0", 18, "20251201", "store"), ("Thunderbird", "Mozilla", "128.4.3", 210, "20260917", "hklm"),
        ("VLC media player", "VideoLAN", "3.0.21", 163, "20260309", "hklm"), ("Visual Studio Code", "Microsoft Corporation", "1.95.3", 398, "20261029", "hkcu"),
        ("WinRAR", "win.rar GmbH", "7.01", 9.3, "20260402", "hklm"), ("Windows Terminal", "Microsoft Corporation", "1.21.2911", 62, "20260114", "store"),
        ("Zoom Workplace", "Zoom Video Communications", "6.2.5", 304, "20260615", "hklm"), ("Chippi", "Chippi", "0.1.11", 96, "20261105", "self"),
        ("Microsoft .NET Runtime 8.0.10", "Microsoft Corporation", "8.0.10", 74, "20261015", "comp"),
        ("Microsoft Edge WebView2 Runtime", "Microsoft Corporation", "131.0.2903", 420, "20261108", "comp"),
        ("Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Corporation", "14.40.33810", 20, "20260202", "comp"),
    ];

    /// <summary>The mockup's apps in a fixture PC (the island page's shots): machine apps, the user's own and Store packages.</summary>
    internal static void SeedMock(Fixture f)
    {
        foreach (var (name, publisher, version, mb, installed, kind) in Mock)
        {
            if (kind == "store")
            {
                f.Packages.Packages.Add(new RawPackage("Full_" + name, "Family_" + name, name, name, publisher, version, @"C:\WindowsApps\" + name, DateTime.ParseExact(installed, "yyyyMMdd", CultureInfo.InvariantCulture), false, false, false));
                continue;
            }
            var source = kind == "hkcu" ? AppSource.User : AppSource.Machine64;
            string folder = source == AppSource.User ? f.Local("Programs", name) : f.ProgramFiles(name);
            string? command = name == "Zoom Workplace" ? null : "\"" + Path.Combine(folder, "unins000.exe") + "\"";
            f.Registry.Add(source, name, name, publisher, version, folder, command, null, [("EstimatedSize", (int)(mb * 1024)), ("InstallDate", installed)]);
        }
    }

    // ---- fakes ----

    private sealed class Ui : IModuleUi
    {
        public Func<string?, object>? Page;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution("uninstaller", spec);
        public void SetSettingsPage(Func<string?, object> createView) => Page = createView;
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) { }
        public void AddCommandSearch(Func<string, IReadOnlyList<ChippiCommand>> search) { }
    }

    private sealed class Settings : IModuleSettings
    {
        public object? Stored;
        public T Get<T>() where T : class, new() => Stored as T ?? new();
        public void Save<T>(T value) where T : class => Stored = value;
    }

    private sealed class Island : IIslandPublisher
    {
        public void Publish(IslandActivity activity) { }
        public void Clear(string key) { }
        public void ReportSuccess(string? message = null) { }
    }

    /// <summary>Runs the user's uninstallers: each finishes at once except the Nth, which waits for <see cref="Release"/>.</summary>
    private sealed class GateUser(Fixture f) : IUserProcess
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public int HoldAt { get; set; }
        public void Release() => _gate.TrySetResult();
        public async Task<int?> RunAsync(string application, string arguments, TimeSpan limit, CancellationToken ct)
        {
            if (Path.GetFileName(application).Equals("reg.exe", StringComparison.OrdinalIgnoreCase)) return await f.User.RunAsync(application, arguments, limit, ct);
            if (++_calls == HoldAt) await _gate.Task;
            f.Registry.Remove(AppSource.User, Path.GetFileName(Path.GetDirectoryName(application))!);
            return 0;
        }
    }

    private sealed class ThrowingRegistry : IAppRegistry
    {
        public IReadOnlyList<RawApp> Read(AppSource source) => throw new IOException("read");
        public RawApp? ReadOne(AppSource source, string key) => null;
    }

    private sealed class Rig : IDisposable
    {
        public Fixture F { get; } = new();
        public Settings Settings { get; } = new();
        public Ui Ui { get; } = new();
        public List<string> Edited { get; } = [];
        public ModuleContext Context { get; }
        public UninstallerEngine Engine { get; private set; } = null!;
        public GateUser Gate { get; }

        public Rig()
        {
            UninstallerUi.LoadIcons = false;
            Context = new ModuleContext(UninstallerModule.Id, Settings, NullLogger.Instance, new Island(), Ui) { Admin = F.Admin, DataDirectory = Path.Combine(F.Base, "data") };
            Gate = new GateUser(F);
        }

        /// <summary>The mockup's apps. <paramref name="asUser"/> registers those machine apps under the user's own key (so a queue of them runs one by one).</summary>
        public Rig Seed(IEnumerable<string>? asUser = null, bool apps = true)
        {
            var user = new HashSet<string>(asUser ?? []);
            if (apps)
            {
                foreach (var (name, publisher, version, mb, installed, kind) in Mock)
                {
                    long kb = (long)(mb * 1024);
                    if (kind == "store")
                    {
                        F.Packages.Packages.Add(new RawPackage("Full_" + name, "Family_" + name, name, name, publisher, version, @"C:\WindowsApps\" + name, DateTime.ParseExact(installed, "yyyyMMdd", CultureInfo.InvariantCulture), false, false, false));
                        continue;
                    }
                    var source = kind == "hkcu" || user.Contains(name) ? AppSource.User : AppSource.Machine64;
                    string folder = source == AppSource.User ? F.Local("Programs", name) : F.ProgramFiles(name);
                    string exe = Path.Combine(folder, "unins000.exe");
                    string? command = name == "Zoom Workplace" ? null : "\"" + exe + "\"";
                    F.Registry.Add(source, name, name, publisher, version, folder, command, null, [("EstimatedSize", (int)kb), ("InstallDate", installed)]);
                }
            }
            return this;
        }

        public UninstallerEngine Make(IAdminHelper? admin = null, IAppRegistry? registry = null)
        {
            var sys = F.Sys with { UserProcess = Gate };
            if (registry is not null) sys = sys with { Registry = registry };
            Engine = new UninstallerEngine(sys, admin ?? F.Admin, F.Records(), F.Roots, F.BackupFolder, null, F.Logger, new FixedTime(F), (_, _) => Task.CompletedTask, TimeSpan.Zero, _ => false);
            F.Admin.OnUninstall = item => F.Registry.Remove(item.View == AdminRegistryView.Registry32 ? AppSource.Machine32 : AppSource.Machine64, item.KeyName);
            return Engine;
        }

        public UninstallerSettingsView View(string? section = null)
        {
            var view = new UninstallerSettingsView(Engine, Context, section, folder => Edited.Add(folder));
            view.Now = () => new DateTime(2026, 11, 17, 12, 0, 0);
            view.Apps.Now = () => new DateTime(2026, 11, 17, 12, 0, 0);
            return view;
        }

        public void Dispose() => F.Dispose();
    }

    // ---- helpers ----

    private static Rig Loaded(IEnumerable<string>? asUser = null)
    {
        var rig = new Rig().Seed(asUser);
        rig.Make();
        WpfTestThread.Wait(rig.Engine.RefreshAsync());
        return rig;
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static IEnumerable<T> Walk<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Walk<T>(child)) yield return nested;
        }
    }

    private static IReadOnlyList<string> Texts(DependencyObject root) => [.. Walk<TextBlock>(root).Select(t => t.Text)];

    private static Button Named(DependencyObject root, string name) => Walk<Button>(root).First(b => AutomationProperties.GetName(b) == name);

    private static AppRowView RowOf(DependencyObject root, string name) => Walk<AppRowView>(root).First(r => AutomationProperties.GetName(r) == name);

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
        throw new TimeoutException("The screen never reached the state the test waited for.");
    });

    private static void Settle() => Application.Current.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, () => { });

    private const double Width = 768, Height = 606;

    /// <summary>Shows the Apps tab at its mockup size and realizes its rows.</summary>
    private static UninstallerSettingsView Open(Rig rig, string? section = null, double width = Width)
    {
        var view = rig.View(section);
        view.Height = Height;
        Arrange(view, width, Height);
        view.Apps.Rebuild();
        Arrange(view, width, Height);
        return view;
    }

    // ---- the Apps list ----

    [Fact]
    public void The_list_draws_the_apps_as_48_DIP_rows_with_publisher_size_date_and_the_shield_of_a_machine_app_and_virtualizes_them() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        var page = view.Apps;
        Assert.Equal("38 apps", page.CountText);
        var rows = Walk<AppRowView>(page.List).ToList();
        Assert.InRange(rows.Count, 8, 25);   // only the rows on screen are built
        Assert.All(rows, r => Assert.Equal(48, r.Height));
        var zip = RowOf(page.List, "7-Zip");
        Assert.Equal(["7Z", "7-Zip", "24.09", "Igor Pavlov", "5.6 MB", "May 12, 2026", "Uninstall"], Texts(zip));   // its letters stand in for its icon
        Assert.Single(Walk<UacShield>(zip));
        Assert.Equal(["Uninstall"], Walk<Button>(zip).Select(b => AutomationProperties.GetName(b)).Where(n => n == "Uninstall"));
        var calculator = RowOf(page.List, "Calculator");
        Assert.Contains("Store", Texts(calculator));
        Assert.Empty(Walk<UacShield>(calculator));   // a Store app never asks for admin
        Assert.Empty(Walk<UacShield>(RowOf(page.List, "Discord")));   // nor does a per-user app
        page.SetSearch("Firefox");
        Arrange(view, Width, Height);
        Assert.Contains("New", Texts(RowOf(page.List, "Firefox")));   // installed in the last 7 days
    });

    [Fact]
    public void Search_filters_on_every_keystroke_and_the_one_filter_shows_the_Windows_components_which_have_no_verb() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        var page = view.Apps;
        page.SetSearch("note");
        Arrange(view, Width, Height);
        Assert.Equal("4 of 38 apps", page.CountText);
        Assert.Equal(["Notepad", "Notepad++", "OneNote", "Sticky Notes"], page.Shown.Select(a => a.Name));
        page.SetSearch("zzq");
        Arrange(view, Width, Height);
        Assert.Contains("No apps match", Texts(page));
        page.SetSearch("");
        page.SetFilter(AppsPage.Components);
        Arrange(view, Width, Height);
        Assert.Equal("3 apps", page.CountText);
        Assert.All(Walk<AppRowView>(page.List), r => Assert.DoesNotContain(Walk<Button>(r), b => AutomationProperties.GetName(b) == "Uninstall"));
        Assert.Empty(Walk<UacShield>(page.List));
    });

    [Fact]
    public void The_header_sorts_by_name_publisher_size_and_installed_and_a_second_press_reverses() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        var page = view.Apps;
        Assert.Equal("7-Zip", page.Shown[0].Name);
        Click(Named(page, "Sort by Size"));   // size starts largest first
        Assert.Equal("Steam", page.Shown[0].Name);
        Click(Named(page, "Sort by Size"));
        Assert.Equal("7-Zip", page.Shown[0].Name);   // the second press: smallest first, the apps with no size last
        Click(Named(page, "Sort by Installed"));
        Assert.Equal("Discord", page.Shown[0].Name);
        Click(Named(page, "Sort by Name"));
        Assert.Equal("7-Zip", page.Shown[0].Name);
    });

    [Fact]
    public void Ticking_apps_shows_the_bar_with_their_count_and_size_the_Windows_components_and_Chippi_cannot_be_ticked() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        var page = view.Apps;
        Assert.Equal(Visibility.Collapsed, page.Bar.Visibility);
        foreach (var name in new[] { "Adobe Acrobat Reader", "Audacity", "Blender" }) page.Tick(page.Shown.First(a => a.Name == name).Id, true);
        Arrange(view, Width, Height);
        Assert.Equal(Visibility.Visible, page.Bar.Visibility);
        Assert.Equal(["3 selected", "2.2 GB", "Clear", "Uninstall"], Texts(page.Bar));
        Assert.Single(Walk<UacShield>(page.Bar));   // machine apps: the verb asks for admin
        page.SetFilter(AppsPage.Components);
        Arrange(view, Width, Height);
        Assert.All(Walk<CheckBox>(page.List), c => Assert.False(c.IsEnabled));
        page.SetFilter(AppsPage.AllApps);
        Arrange(view, Width, Height);
        Assert.False(Walk<CheckBox>(RowOf(page.List, "Chippi")).Single().IsEnabled);
        Click(Named(page.Bar, "Clear"));
        Assert.Equal(Visibility.Collapsed, page.Bar.Visibility);
    });

    [Fact]
    public void Uninstall_asks_first_with_Cancel_focused_Cancel_runs_nothing_and_Uninstall_runs_the_queue_and_keeps_the_rows_with_their_result() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        var page = view.Apps;
        foreach (var name in new[] { "Adobe Acrobat Reader", "Audacity", "Blender" }) page.Tick(page.Shown.First(a => a.Name == name).Id, true);
        Click(Named(page.Bar, "Uninstall"));
        Assert.True(page.AsksQuestion);
        Assert.Contains("Uninstall 3 apps?", Texts(page.PopoverElement!));
        Assert.Equal(["Adobe Acrobat Reader", "Audacity", "Blender"], Texts(page.PopoverElement!).Where(t => t is "Adobe Acrobat Reader" or "Audacity" or "Blender"));
        WpfTestThread.Wait(Until(() => page.FocusTarget is { } t && AutomationProperties.GetName(t) == "Cancel"));
        Assert.Single(Walk<UacShield>(Named(page.PopoverElement!, "Uninstall")));
        Click(Named(page.PopoverElement!, "Cancel"));
        Assert.False(page.AsksQuestion);
        Assert.Empty(rig.F.Admin.Calls);   // nothing ran
        Assert.Equal(3, page.Ticked.Count);

        Click(Named(page.Bar, "Uninstall"));
        var run = page.Confirm(page.Shown.Where(a => page.Ticked.Contains(a.Id)).ToList());
        WpfTestThread.Wait(run);
        Assert.Equal(["uninstall 3"], rig.F.Admin.Calls);   // the helper's session stays open for the scan that follows
        Arrange(view, Width, Height);
        Assert.Equal("38 apps", page.CountText);   // the rows stay, with their result, until Refresh
        var adobe = RowOf(page.List, "Adobe Acrobat Reader");
        Assert.Contains("Uninstalled", Texts(adobe));
        Assert.Contains("Scan", Walk<Button>(adobe).Select(b => AutomationProperties.GetName(b)));
        Assert.Empty(Walk<UacShield>(adobe));
        Assert.Equal(Visibility.Collapsed, page.Bar.Visibility);
        Assert.Empty(page.Ticked);
    });

    [Fact]
    public void A_single_row_asks_in_place_with_its_name_and_Cancel_has_the_keyboard() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        var page = view.Apps;
        Click(Named(RowOf(page.List, "7-Zip"), "Uninstall"));
        Assert.Equal(["Uninstall 7-Zip?"], Texts(page.PopoverElement!).Where(t => t.EndsWith('?')));
        Assert.Equal(["Cancel", "Uninstall"], Walk<Button>(page.PopoverElement!).Select(b => AutomationProperties.GetName(b)));
        WpfTestThread.Wait(Until(() => page.FocusTarget is { } t && AutomationProperties.GetName(t) == "Cancel"));
        Click(Named(page.PopoverElement!, "Cancel"));
        Assert.Empty(rig.F.Admin.Calls);
        Assert.Equal(["Uninstall"], Walk<Button>(RowOf(page.List, "Discord")).Select(b => AutomationProperties.GetName(b)).Where(n => n == "Uninstall"));
        Click(Named(RowOf(page.List, "Discord"), "Uninstall"));
        Assert.Empty(Walk<UacShield>(Named(page.PopoverElement!, "Uninstall")));   // a per-user app: no shield
    });

    [Fact]
    public void A_redraw_keeps_the_keyboard_on_the_row_button_it_was_on() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        var page = view.Apps;
        var app = page.Shown.First(a => a.Name == "Audacity");
        page.FocusedKeyOverride = "verb:" + app.Id;
        var before = Named(RowOf(page.List, "Audacity"), "Uninstall");
        page.Redraw();
        Assert.Equal("verb:" + app.Id, page.FocusTarget!.Tag);
        Assert.NotSame(before, page.FocusTarget);   // the row rebuilt its buttons and the new one takes the keyboard back
    });

    [Fact]
    public void An_app_with_no_uninstaller_says_so_in_Caution_and_has_no_working_verb() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        view.Apps.SetSearch("Zoom");
        Arrange(view, Width, Height);
        var zoom = RowOf(view.Apps.List, "Zoom Workplace");
        Assert.Contains("No uninstaller", Texts(zoom));
        Assert.False(Named(zoom, "Uninstall").IsEnabled);
    });

    [Fact]
    public void Every_button_on_the_page_is_a_bare_verb_or_a_short_label() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        string[] allowed = ["Uninstall", "Open folder", "Refresh", "Select all", "Clear", "Cancel", "Scan", "Retry", "Done", "Remove", "Back"];
        foreach (var b in Walk<Button>(view))
        {
            string name = AutomationProperties.GetName(b);
            Assert.True(allowed.Contains(name) || name.StartsWith("Sort by ", StringComparison.Ordinal), name);
        }
    });

    // ---- Uninstalled and Shortcuts ----

    [Fact]
    public void Uninstalled_lists_the_apps_whose_record_Chippi_holds_with_their_day_and_Scan_opens_the_review() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded(["Audacity", "Slack"]);
        var view = Open(rig);
        view.Now = () => rig.F.Now.LocalDateTime;
        var page = view.Apps;
        var apps = page.Shown.Where(a => a.Name is "Audacity" or "Slack").ToList();
        WpfTestThread.Wait(page.Confirm(apps));
        view.SelectTab(1);
        Arrange(view, Width, Height);
        var texts = Texts(view.RecentTab);
        Assert.Contains("Audacity", texts);
        Assert.Contains("Slack", texts);
        Assert.Contains("Today", texts);
        Assert.Equal(["Scan", "Scan"], Walk<Button>(view.RecentTab).Select(b => AutomationProperties.GetName(b)));
        Click(Walk<Button>(view.RecentTab).First());
        Assert.NotNull(view.Review);
        Assert.Contains("Audacity leftovers", Texts(view.Review!));   // the review takes the page's place, with its own Back
        Assert.Contains("Back", Walk<Button>(view.Review!).Select(b => AutomationProperties.GetName(b)));
        Click(Named(view.Review!, "Back"));
        Assert.Null(view.Review);
        Assert.Equal(1, view.SelectedTab);
    });

    [Fact]
    public void Uninstalled_with_nothing_in_it_says_so_and_Shortcuts_lists_both_verbs_read_only_with_Edit() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        view.SelectTab(1);
        Arrange(view, Width, Height);
        Assert.Contains("Nothing uninstalled lately", Texts(view.RecentTab));
        view.SelectTab(2);
        Arrange(view, Width, Height);
        var texts = Texts(view);
        Assert.Contains("Uninstall app", texts);
        Assert.Contains("Scan for leftovers", texts);
        Assert.Equal(2, texts.Count(t => t == "Not set"));
        Assert.Equal(2, Walk<Button>(view).Count(b => AutomationProperties.GetName(b).StartsWith("Edit ", StringComparison.Ordinal)));
    });

    [Fact]
    public void A_section_opens_the_search_with_its_text_or_the_Uninstalled_tab_and_the_module_hands_Settings_this_page() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = rig.View(UninstallerModule.SectionFor("note"));
        Assert.Equal("note", view.Apps.SearchBox.Text);
        Assert.Equal(0, view.SelectedTab);
        Assert.Equal(1, rig.View(UninstallerModule.RecentSection).SelectedTab);
        var module = new UninstallerModule(() => rig.F.Sys, () => rig.F.Roots, new FixedTime(rig.F));
        Assert.True(module.StartAsync(rig.Context.WithUi(rig.Ui), default).IsCompletedSuccessfully);
        Assert.IsType<UninstallerSettingsView>(rig.Ui.Page!("search"));
        WpfTestThread.Wait(module.StopAsync(default));
    });

    // ---- the review ----

    private static (LeftoverTests Scans, AppRecord Record, UninstallerEngine Engine) Mozilla(bool helperRecord = true)
    {
        var scans = new LeftoverTests();
        UninstallerUi.LoadIcons = false;
        return (scans, scans.Mozilla(helperRecord), scans.Fx.Engine());
    }

    private static LeftoverPage Review(UninstallerEngine engine, AppRecord record, List<string>? opened = null, Action<bool>? back = null)
    {
        var page = new LeftoverPage(engine, record, back ?? (_ => { }), f => opened?.Add(f)) { Width = Width, Height = Height };
        Arrange(page, Width, Height);
        return page;
    }

    [Fact]
    public void A_tick_redraws_the_review_in_the_same_scroll_viewer_at_the_same_offset_and_the_keyboard_stays_on_the_checkbox() => WpfTestThread.Run(() =>
    {
        var (scans, record, engine) = Mozilla();
        using var _ = scans;
        using var __ = engine;
        var page = Review(engine, record);
        WpfTestThread.Wait(page.ScanAsync());
        Arrange(page, Width, 150);
        var scroll = Walk<ScrollViewer>(page).First();
        Assert.True(scroll.ScrollableHeight > 60);
        scroll.ScrollToVerticalOffset(60);
        Arrange(page, Width, 150);
        Assert.Equal(60, scroll.VerticalOffset);

        var box = Walk<CheckBox>(page).First(c => c.Tag is string t && t.StartsWith("item:", StringComparison.Ordinal) && c.IsChecked != true);
        box.IsChecked = true;
        box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Arrange(page, Width, 150);
        Assert.Same(scroll, Walk<ScrollViewer>(page).First());
        Assert.Equal(60, scroll.VerticalOffset);   // the list does not jump back to the top

        // A redraw rebuilds every row: the one the keyboard was on is found again, though it is a CheckBox and not a Button.
        string key = (string)box.Tag;
        page.FocusedKeyOverride = key;
        page.Redraw();
        Arrange(page, Width, 150);
        var target = Assert.IsType<CheckBox>(page.FocusTarget);
        Assert.Equal(key, target.Tag);
        Assert.NotSame(box, target);
    });

    [Fact]
    public void The_apps_list_finds_a_row_tick_again_after_a_redraw() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded();
        var view = Open(rig);
        var page = view.Apps;
        var app = page.Shown.First(a => a.Name == "Audacity");
        page.FocusedKeyOverride = "check:" + app.Id;
        page.Redraw();
        Arrange(page, Width, 150);
        Assert.IsType<CheckBox>(page.FocusTarget);
    });

    [Fact]
    public void Retrying_declined_items_keeps_the_first_runs_removals_in_the_results() => WpfTestThread.Run(() =>
    {
        var (scans, record, engine) = Mozilla();
        using var _ = scans;
        using var __ = engine;
        bool decline = true;
        scans.Fx.Admin.Leftover = item => new AdminLeftoverResult(item, decline ? AdminLeftoverState.NeedsAdmin : AdminLeftoverState.Removed);
        var page = Review(engine, record);
        WpfTestThread.Wait(page.ScanAsync());
        Arrange(page, Width, Height);
        int ticked = page.Ticked.Count;
        WpfTestThread.Wait(page.RemoveAsync());
        Arrange(page, Width, Height);
        Assert.Equal(LeftoverStep.Results, page.Step);
        int first = page.Run!.Removed;
        Assert.True(first > 0 && page.Run.NotRemoved > 0);

        decline = false;
        Click(Named(page, "Remove"));   // the retry beside "Needs admin"
        WpfTestThread.Wait(Until(() => page.Step == LeftoverStep.Results && page.Run!.NotRemoved == 0));
        Arrange(page, Width, Height);
        Assert.Equal(ticked, page.Run!.Results.Count);
        Assert.Contains(ticked + " removed", Texts(page));
    });

    [Fact]
    public void An_uninstall_finished_elsewhere_adds_its_row_to_the_open_Uninstalled_list() => WpfTestThread.Run(() =>
    {
        using var rig = Loaded(["Audacity"]);
        var view = Open(rig);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));   // loaded: the view now follows the engine
        view.SelectTab(1);
        Arrange(view, Width, Height);
        Assert.Contains("Nothing uninstalled lately", Texts(view.RecentTab));
        var app = rig.Engine.Apps.First(a => a.Name == "Audacity");
        WpfTestThread.Wait(rig.Engine.UninstallAsync([app]));   // the island's or Search's run finishing
        Arrange(view, Width, Height);
        Assert.Contains("Audacity", Texts(view.RecentTab));
        Assert.DoesNotContain("Nothing uninstalled lately", Texts(view.RecentTab));
    });

    [Fact]
    public void The_scan_lists_Safe_Check_first_and_Shared_with_the_defaults_ticked_and_a_shared_item_dimmed_with_a_lock_and_no_checkbox() => WpfTestThread.Run(() =>
    {
        var (scans, record, engine) = Mozilla();
        using var _ = scans;
        using var __ = engine;
        var page = Review(engine, record);
        Assert.Equal(LeftoverStep.Scanning, page.Step);
        Assert.Contains("Scanning", Texts(page));
        WpfTestThread.Wait(page.ScanAsync());
        Arrange(page, Width, Height);
        Assert.Equal(LeftoverStep.Review, page.Step);
        var texts = Texts(page);
        Assert.Contains("Safe", texts);
        Assert.Contains("Check first", texts);
        Assert.Contains("Shared", texts);
        Assert.Contains("Mozilla Firefox (x64 en-US) leftovers", texts);
        // The defaults: Safe ticked, Check first and Shared never.
        var scan = page.Scan!;
        Assert.Equal([.. scan.Items.Where(i => i.TickedByDefault).Select(i => i.Id)], [.. page.Ticked]);
        Assert.DoesNotContain(scan.CheckFirst, i => page.Ticked.Contains(i.Id));
        // Shared: the other app's name under the path, no checkbox of its own.
        var shared = scan.Shared.First();
        Assert.Contains(texts, t => t.Contains("Thunderbird", StringComparison.Ordinal));
        Assert.DoesNotContain(Walk<CheckBox>(page), c => AutomationProperties.GetName(c) == "Select " + shared.Path);
        var row = Walk<Border>(page).First(b => AutomationProperties.GetName(b) == shared.Path);
        Assert.True(row.Opacity < 1);
        Assert.Contains(scan.Items.Count(i => i.TickedByDefault) + " ticked", Texts(page.Bar));
    });

    [Fact]
    public void Remove_asks_with_Cancel_focused_Cancel_removes_nothing_and_Remove_removes_only_the_ticked_then_shows_the_results_grouped() => WpfTestThread.Run(() =>
    {
        var (scans, record, engine) = Mozilla();
        using var _ = scans;
        using var __ = engine;
        var page = Review(engine, record);
        WpfTestThread.Wait(page.ScanAsync());
        Arrange(page, Width, Height);
        string shared = scans.Fx.Data("Mozilla");
        string firefox = scans.Fx.ProgramFiles("Mozilla Firefox");
        int ticked = page.Ticked.Count;
        Click(Named(page.Bar, "Remove"));
        Assert.True(page.AsksQuestion);
        Assert.Contains(UninstallerText.RemoveQuestion(ticked), Texts(page.PopoverElement!));
        WpfTestThread.Wait(Until(() => page.FocusTarget is { } t && AutomationProperties.GetName(t) == "Cancel"));
        Click(Named(page.PopoverElement!, "Cancel"));
        Assert.False(page.AsksQuestion);
        Assert.True(Directory.Exists(firefox));
        Assert.Empty(scans.Fx.Admin.LeftoverCalls);

        page.Ask();
        Click(Named(page.PopoverElement!, "Remove"));
        WpfTestThread.Wait(Until(() => page.Step == LeftoverStep.Results));
        Arrange(page, Width, Height);
        var texts = Texts(page);
        Assert.Contains(ticked + " removed", texts);
        Assert.Contains("Removed", texts);
        Assert.True(Directory.Exists(shared));   // a shared folder is never removed
        Assert.Equal(["Done"], Walk<Button>(page.Bar).Select(b => AutomationProperties.GetName(b)));
    });

    [Fact]
    public void Nothing_ticked_leaves_Remove_off_and_without_the_helpers_record_the_review_says_so_and_offers_only_Check_first() => WpfTestThread.Run(() =>
    {
        var (scans, record, engine) = Mozilla(helperRecord: false);
        using var _ = scans;
        using var __ = engine;
        var page = Review(engine, record);
        WpfTestThread.Wait(page.ScanAsync());
        Arrange(page, Width, Height);
        var all = Walk<CheckBox>(page).First(c => AutomationProperties.GetName(c) == "Select all");
        all.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));   // the Safe items are ticked and the rest not: the first press ticks all
        Arrange(page, Width, Height);
        Walk<CheckBox>(page).First(c => AutomationProperties.GetName(c) == "Select all").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));   // the second unticks all
        Arrange(page, Width, Height);
        Assert.Empty(page.Ticked);
        Assert.False(Named(page.Bar, "Remove").IsEnabled);
        Assert.Contains("0 ticked", Texts(page.Bar));
    });

    [Fact]
    public void Back_and_Cancel_leave_the_review_and_a_scan_of_an_app_still_installed_goes_straight_back() => WpfTestThread.Run(() =>
    {
        var (scans, record, engine) = Mozilla();
        using var _ = scans;
        using var __ = engine;
        var back = new List<bool>();
        var page = Review(engine, record, back: back.Add);
        WpfTestThread.Wait(page.ScanAsync());
        Click(Named(page.Bar, "Cancel"));
        Assert.Equal([false], back);
        // Its entry is back in the registry: the engine returns no scan and the page leaves at once.
        scans.Fx.Registry.Add(AppSource.Machine64, record.Key, record.Name, "Mozilla", "132.0.1", scans.Fx.ProgramFiles("Mozilla Firefox"), "\"x.exe\"");
        var again = Review(engine, record, back: back.Add);
        WpfTestThread.Wait(again.ScanAsync());
        Assert.Equal([false, false], back);
    });

    [Fact]
    public void A_Scan_from_the_island_opens_the_review_of_that_app_and_a_record_that_is_gone_opens_the_Uninstalled_list() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig().Seed(apps: false);
        var record = new AppRecord(Guid.NewGuid(), "Firefox", "Mozilla", "132.0", AppSource.User, "Firefox", null, null, [], null, rig.F.Now.UtcDateTime);
        rig.F.Records().Put(record);
        rig.Make();
        var view = rig.View(UninstallerModule.ScanSectionFor(record.Id));
        Assert.NotNull(view.Review);
        var gone = rig.View(UninstallerModule.ScanSectionFor(Guid.NewGuid()));
        Assert.Null(gone.Review);
        Assert.Equal(1, gone.SelectedTab);
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
            var timings = new List<string>();
            void Shot(string board, FrameworkElement page, double width = 848, double height = 680, Action? before = null)
                => Frame(Path.Combine(output, $"uninstaller-{board}-{theme}.png"), page, width, height, before);

            string[] first = ["Adobe Acrobat Reader", "Audacity", "Blender"];
            // ---- Apps ----
            using (var rig = Loaded())
            {
                var view = rig.View();
                Shot("settings-list", view);
                var narrow = rig.View();
                Shot("settings-list-narrow", narrow, 812, 680);
                var search = rig.View(UninstallerModule.SectionFor("note"));
                Shot("settings-search", search);
                var confirm = rig.View();
                Shot("settings-confirm", confirm, before: () =>
                {
                    confirm.Apps.ScrollTo("Discord");
                    Settle(); Arrange(confirm, Width, Height);
                    Click(Named(RowOf(confirm.Apps.List, "Git"), "Uninstall"));
                });
                var selected = rig.View();
                Shot("settings-selected", selected, before: () =>
                {
                    selected.Apps.ScrollTo("Adobe Acrobat Reader");
                    foreach (var n in first) selected.Apps.Tick(selected.Apps.Shown.First(a => a.Name == n).Id, true);
                });
                var queueConfirm = rig.View();
                Shot("settings-confirm-queue", queueConfirm, before: () =>
                {
                    queueConfirm.Apps.ScrollTo("Adobe Acrobat Reader");
                    foreach (var n in first) queueConfirm.Apps.Tick(queueConfirm.Apps.Shown.First(a => a.Name == n).Id, true);
                    queueConfirm.Apps.AskSelected();
                });
                var rows = rig.View();
                Shot("sheet-list-states-nomatch", rows, 848, 300, () => rows.Apps.SetSearch("zzq"));
                var components = rig.View();
                Shot("sheet-list-states-components", components, 848, 300, () => components.Apps.SetFilter(AppsPage.Components));
            }
            using (var rig = Loaded(first))
            {
                // A queue under way: the first app finished, the second runs, the third waits.
                var view = rig.View();
                rig.Gate.HoldAt = 2;
                view.Height = 606;
                Arrange(view, Width, Height);
                view.Apps.ScrollTo("Adobe Acrobat Reader");
                foreach (var n in first) view.Apps.Tick(view.Apps.Shown.First(a => a.Name == n).Id, true);
                var run = view.Apps.Confirm([.. first.Select(n => view.Apps.Shown.First(a => a.Name == n))]);
                WpfTestThread.Wait(Until(() => rig.Engine.StatusOf(view.Apps.Shown.First(a => a.Name == "Audacity").Id).State == AppRunState.Uninstalling));
                Shot("settings-queue", view);
                rig.Gate.Release();
                WpfTestThread.Wait(run);
                Shot("settings-queue-done", view);
            }
            using (var rig = new Rig().Seed())
            {
                // The row states: each reached by running the engine, not by drawing it.
                var f = rig.F;
                f.Processes.RunningFolders.Add(f.ProgramFiles("Firefox"));
                rig.Make();
                f.Admin.OnUninstall = item => { if (item.KeyName is not ("WinRAR" or "VLC media player" or "Python 3.13.0")) f.Registry.Remove(AppSource.Machine64, item.KeyName); };
                f.Admin.Uninstall = item => item.KeyName switch
                {
                    "VLC media player" => new AdminUninstallResult(item, AdminUninstallState.NeedsAdmin),
                    "WinRAR" => new AdminUninstallResult(item, AdminUninstallState.Failed, 1),
                    "Python 3.13.0" => new AdminUninstallResult(item, AdminUninstallState.RestartNeeded, 3010, false, Guid.NewGuid()),
                    _ => new AdminUninstallResult(item, AdminUninstallState.Uninstalled, 0, true, Guid.NewGuid()),
                };
                WpfTestThread.Wait(rig.Engine.RefreshAsync());
                var view = rig.View();
                rig.Gate.HoldAt = 1;
                var apps = new[] { "Firefox", "OBS Studio", "Python 3.13.0", "VLC media player", "WinRAR", "Discord" }.Select(n => rig.Engine.Apps.First(a => a.Name == n)).ToList();
                var run = view.Apps.Confirm(apps);
                WpfTestThread.Wait(Until(() => rig.Engine.StatusOf(apps[5].Id).State == AppRunState.Uninstalling));
                var card = new StackPanel();
                foreach (var n in new[] { "Firefox", "Discord", "VLC media player", "WinRAR", "Zoom Workplace", "Python 3.13.0", "OBS Studio", "Calculator", "Chippi", "Microsoft .NET Runtime 8.0.10" })
                {
                    var app = rig.Engine.Apps.FirstOrDefault(a => a.Name == n) ?? view.Apps.Shown.First(a => a.Name == n);
                    card.Children.Add(new AppRowView { DataContext = new AppRowModel(app, view.Apps) });
                }
                var host = new Border { Child = card, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
                host.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                host.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                Shot("sheet-row-states", host, 848, 10 * 48 + 2 + 32, null);
                rig.Gate.Release();
                WpfTestThread.Wait(run);
            }
            using (var rig = new Rig().Seed(apps: false))
            {
                rig.Make(registry: new ThrowingRegistry());
                var view = rig.View();
                WpfTestThread.Wait(rig.Engine.RefreshAsync());
                Shot("sheet-list-states-error", view, 848, 300);
                var skeleton = new Rig().Seed(apps: false);
                skeleton.Make();
                Shot("sheet-list-states-loading", skeleton.View(), 848, 400);
                skeleton.Dispose();
            }

            // ---- Uninstalled and Shortcuts ----
            using (var rig = Loaded())
            {
                var store = rig.F.Records();
                AppRecord Rec(string name, string publisher, DateTime when, bool helper = true, bool cleaned = false) => new(Guid.NewGuid(), name, publisher, "1.0", AppSource.Machine64, name, null, null, [], null, when, helper ? Guid.NewGuid() : null, cleaned);
                store.Put(Rec("Firefox", "Mozilla", new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc)));
                store.Put(Rec("Slack", "Slack Technologies", new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc)));
                store.Put(Rec("Notepad++", "Notepad++ Team", new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc), cleaned: true));
                store.Put(Rec("Blender", "Blender Foundation", new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), helper: false));
                var view = rig.View();
                view.SelectTab(1);
                Shot("settings-uninstalled", view, 848, 300);
                view.SelectTab(2);
                Shot("settings-shortcuts", view, 848, 300);
            }
            using (var rig = new Rig().Seed(apps: false))
            {
                rig.Make();
                var view = rig.View(UninstallerModule.RecentSection);
                Shot("sheet-list-states-uninstalled-empty", view, 848, 300);
            }

            // ---- the leftover review: Firefox, with Thunderbird still installed ----
            Review(output, theme);

            // ---- timings ----
            timings.AddRange(Timings());
            string? path = Environment.GetEnvironmentVariable("CHIPPI_UNINSTALLER_TIMINGS");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllLines(path, timings);
        });
    }

    [Fact]
    public void Time_the_list_opening_and_a_search_keystroke_over_300_apps()
    {
        string? path = Environment.GetEnvironmentVariable("CHIPPI_UNINSTALLER_TIMINGS");
        if (string.IsNullOrWhiteSpace(path) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS"))) return;
        WpfTestThread.Run(() => File.WriteAllLines(path, Timings()));
    }

    private static IEnumerable<string> Timings()
    {
        using var rig = new Rig();
        for (int i = 0; i < 300; i++)
        {
            string folder = rig.F.ProgramFiles("App" + i);
            rig.F.Registry.Add(i % 3 == 0 ? AppSource.User : AppSource.Machine64, "app" + i, (i % 7 == 0 ? "Note " : "Application ") + i, "Publisher " + i % 20, "1." + i, folder, "\"" + folder + "\\unins000.exe\"",
                null, [("EstimatedSize", 1000 + i * 37), ("InstallDate", "2026" + (1 + i % 11).ToString("00") + "10")]);
        }
        rig.Make();
        WpfTestThread.Wait(rig.Engine.RefreshAsync());
        var lines = new List<string>();
        double OpenOnce()
        {
            var watch = Stopwatch.StartNew();
            var view = rig.View();
            view.Height = Height;
            Arrange(view, Width, Height);
            view.Apps.Rebuild();
            Arrange(view, Width, Height);
            watch.Stop();
            view.Dispose();
            return watch.Elapsed.TotalMilliseconds;
        }
        OpenOnce();
        var opens = Enumerable.Range(0, 5).Select(_ => OpenOnce()).ToList();
        lines.Add($"{WpfTestThread.ShotsTheme} Settings Apps list, 300 apps, construct + first layout: median {opens.Order().ElementAt(2):F1} ms, max {opens.Max():F1} ms");
        var open = rig.View();
        open.Height = Height;
        Arrange(open, Width, Height);
        var keys = new List<double>();
        foreach (var text in new[] { "n", "no", "not", "note", "note 7", "note 70", "note 700", "note 70", "note 7", "note", "no", "n", "" })
        {
            var watch = Stopwatch.StartNew();
            open.Apps.SetSearch(text);
            Arrange(open, Width, Height);
            watch.Stop();
            keys.Add(watch.Elapsed.TotalMilliseconds);
        }
        lines.Add($"{WpfTestThread.ShotsTheme} Search keystroke (filter, sort, rebind, layout), 300 apps: median {keys.Order().ElementAt(keys.Count / 2):F1} ms, max {keys.Max():F1} ms");
        return lines;
    }

    private static void Review(string output, string theme)
    {
        void Shot(string board, FrameworkElement page, Action? before = null) => Frame(Path.Combine(output, $"uninstaller-{board}-{theme}.png"), page, 848, 680, before);
        string pf = @"C:\Program Files", pd = @"C:\ProgramData", me = @"C:\Users\Travis";
        var record = new AppRecord(Guid.NewGuid(), "Firefox", "Mozilla", "132.0.1", AppSource.Machine64, "Mozilla Firefox", pf + @"\Mozilla Firefox", null, [], null, new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc), Guid.NewGuid());
        LeftoverItem I(LeftoverKind kind, string path, long? bytes, LeftoverClass cls = LeftoverClass.Safe, bool admin = false, string? with = null, bool noBin = false, bool inUse = false)
        {
            RegHive? hive = kind == LeftoverKind.Key ? (path.StartsWith("HKCU", StringComparison.Ordinal) ? RegHive.User : RegHive.Machine64) : null;
            string? keyPath = kind == LeftoverKind.Key ? path[(path.IndexOf('\\') + 1)..] : null;
            return new LeftoverItem(LeftoverItem.IdFor(kind, path), kind, path, hive, keyPath, cls, "", with, bytes, inUse, noBin, admin, false);
        }
        var safe = new List<LeftoverItem>
        {
            I(LeftoverKind.Folder, pf + @"\Mozilla Firefox", 1_300_000, admin: true),
            I(LeftoverKind.Folder, @"C:\Program Files (x86)\Mozilla Maintenance Service", 312_000, admin: true),
            I(LeftoverKind.File, pd + @"\Microsoft\Windows\Start Menu\Programs\Firefox.lnk", 1_000, admin: true),
            I(LeftoverKind.File, @"C:\Users\Public\Desktop\Firefox.lnk", 1_000, admin: true),
            I(LeftoverKind.Folder, pd + @"\Mozilla-1de4eec8-1241-4177-a864-acaac3b1b5ae", 38_000_000, admin: true),
            I(LeftoverKind.Folder, me + @"\AppData\Local\Mozilla\updates\308046B0AF4A39CB", 26_000_000),
            I(LeftoverKind.File, me + @"\Desktop\Firefox.lnk", 1_000),
            I(LeftoverKind.Key, @"HKLM\SOFTWARE\Mozilla\Firefox\132.0.1 (x64 en-US)", null, admin: true),
            I(LeftoverKind.Key, @"HKLM\SOFTWARE\Clients\StartMenuInternet\Firefox-308046B0AF4A39CB", null, admin: true),
            I(LeftoverKind.File, pd + @"\Mozilla\updates\308046B0AF4A39CB\update-backup.bin", 6_400_000_000, admin: true, noBin: true),
        };
        var check = new List<LeftoverItem>
        {
            I(LeftoverKind.Folder, me + @"\AppData\Roaming\Mozilla\Firefox", 482_000_000, LeftoverClass.CheckFirst),
            I(LeftoverKind.Folder, me + @"\AppData\Local\Mozilla\Firefox", 1_100_000_000, LeftoverClass.CheckFirst),
            I(LeftoverKind.Key, @"HKCU\Software\Mozilla\Firefox", null, LeftoverClass.CheckFirst),
        };
        var shared = new List<LeftoverItem>
        {
            I(LeftoverKind.Folder, pd + @"\Mozilla", 9_000_000, LeftoverClass.Shared, with: "Thunderbird"),
            I(LeftoverKind.Key, @"HKLM\SOFTWARE\Mozilla", null, LeftoverClass.Shared, with: "Thunderbird"),
        };
        var scan = new LeftoverScan(record, [.. safe, .. check, .. shared], true);

        LeftoverPage Make(LeftoverScan? preset, bool adopt = true)
        {
            using var rig = new Rig();
            rig.Make();
            var page = new LeftoverPage(rig.Engine, record, _ => { }, _ => { }, adopt ? preset : null);
            return page;
        }
        // The engine is only used by a removal; the preview states below never reach it, so one throwaway rig per page is enough.
        Shot("settings-scan", Make(null, adopt: false));
        Shot("settings-review", Make(scan));
        var folded = Make(scan);
        Shot("settings-review-shared", folded, () => folded.Fold("Safe"));
        var noRecord = new LeftoverScan(record with { HelperRecordId = null }, [.. check], false);
        Shot("settings-review-norecord", Make(noRecord));
        Shot("settings-review-empty", Make(new LeftoverScan(record, [], true)));
        var confirm = Make(scan);
        Shot("settings-review-confirm", confirm, confirm.Ask);
        var removing = Make(scan);
        Shot("settings-removing", removing, () => removing.PreviewRemoving(safe, [.. safe.Take(3).Select(i => new LeftoverResult(i, LeftoverState.Removed))]));
        LeftoverRun Results(Func<LeftoverItem, LeftoverResult> result, string? backup) => new([.. safe.Select(result)], backup);
        var results = Make(scan);
        Shot("settings-results", results, () => results.PreviewResults(Results(i => i.Path.Contains("Maintenance") ? new(i, LeftoverState.Kept, "Kept: changed since the scan")
            : i.Path.Contains("Mozilla-1de4") ? new(i, LeftoverState.InUse) : i.NoRecycleBin ? new(i, LeftoverState.Failed) : new(i, LeftoverState.Removed),
            @"C:\Users\Travis\AppData\Roaming\Chippi\Uninstaller\backups")));
        var kept = Make(scan);
        Shot("settings-results-norecord", kept, () => kept.PreviewResults(new LeftoverRun([
            .. new[] { safe[0], safe[4], safe[7] }.Select(i => new LeftoverResult(i, LeftoverState.Kept, "Kept: no uninstall record")),
            .. check.Select(i => new LeftoverResult(i, LeftoverState.Removed))], null)));
        var admin = Make(scan);
        Shot("settings-results-admin", admin, () => admin.PreviewResults(Results(i => i.NeedsAdmin ? new(i, LeftoverState.NeedsAdmin) : new(i, LeftoverState.Removed), null)));
    }

    /// <summary>The page at the Settings window's content size: its title above, the 40-DIP margins, and a window background.</summary>
    private static void Frame(string path, FrameworkElement page, double width, double height, Action? before)
    {
        var root = new Grid { Width = width, Height = height };
        root.SetResourceReference(Panel.BackgroundProperty, "WindowBackgroundBrush");
        var host = new StackPanel { Margin = new Thickness(40, 4, 40, 28) };
        var title = new TextBlock { Text = "Uninstaller", Margin = new Thickness(0, 0, 0, 14) };
        title.SetResourceReference(FrameworkElement.StyleProperty, "DisplayTextStyle");
        host.Children.Add(title);
        double inner = width - 80;
        page.Width = inner;
        if (page is UninstallerSettingsView || page is LeftoverPage) page.Height = height - 32 - 42;
        if (page is UninstallerSettingsView redraw) redraw.Apps.Rebuild();
        host.Children.Add(page);
        root.Children.Add(host);
        Arrange(root, width, height);
        if (page is UninstallerSettingsView { Apps: var apps } && apps.List.ItemsSource is null) apps.Rebuild();
        Arrange(root, width, height);
        if (before is not null) { before(); Settle(); Arrange(root, width, height); Settle(); Arrange(root, width, height); }
        var bitmap = new RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        host.Children.Clear();
    }
}

internal static class ContextExtensions
{
    /// <summary>The same context with another UI hook: the module under test registers its Settings page there.</summary>
    internal static ModuleContext WithUi(this ModuleContext context, IModuleUi ui) =>
        new(context.ModuleId, context.Settings, context.Logger, new NullIsland(), ui) { Admin = context.Admin, DataDirectory = context.DataDirectory };

    private sealed class NullIsland : IIslandPublisher
    {
        public void Publish(IslandActivity activity) { }
        public void Clear(string key) { }
        public void ReportSuccess(string? message = null) { }
    }
}
