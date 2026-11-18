// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
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
using Chippi.Features.Cleaner.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Cleaner.Tests;

/// <summary>
/// The screens, offscreen, on fixture folders and a fake helper: nothing here shows a window, reads a real folder or runs the helper. With
/// CHIPPI_ISLAND_SHOTS naming a folder they save each board of docs/phase-7/design/cleaner.html (dark, or light with CHIPPI_SHOTS_THEME=light).
/// </summary>
public sealed class ScreenTests
{
    // ---- the PC on paper ----

    /// <summary>Holds the process read the engine makes in each category, so a measure stays open as long as a board needs.</summary>
    private sealed class Hold
    {
        private int _calls;
        private readonly ManualResetEventSlim _gate = new();
        public int BlockAt { get; set; }
        public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> Read()
        {
            if (Interlocked.Increment(ref _calls) == BlockAt) _gate.Wait(TimeSpan.FromSeconds(30));
            return Running;
        }
        public void Release() => _gate.Set();
    }

    private sealed class ShotAdmin : IAdminHelper
    {
        public List<string> Calls { get; } = [];
        public TaskCompletionSource? MeasureGate { get; set; }
        public TaskCompletionSource? CleanGate { get; set; }
        public AdminCategory? HoldAt { get; set; }
        public Exception? CleanThrows { get; set; }
        public Func<AdminCategory, long> Size { get; set; } = c => c switch
        {
            AdminCategory.WindowsTemp => 1_400_000_000, AdminCategory.WindowsUpdateDownloads => 1_700_000_000,
            AdminCategory.DeliveryOptimization => 600_000_000, _ => 190_000_000,
        };
        public Func<AdminCategory, AdminCategoryState> MeasureState { get; set; } = _ => AdminCategoryState.Measured;
        public Func<AdminCategory, AdminCategoryState> CleanState { get; set; } = _ => AdminCategoryState.Cleaned;

        public Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items, IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminPortResult> CloseProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminPortResult> EndProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdminPortResult> StopServiceAsync(string serviceName, AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();

        public async Task<IReadOnlyList<AdminCategoryResult>> MeasureCategoriesAsync(IReadOnlyList<AdminCategory> names, CancellationToken ct = default)
        {
            Calls.Add("measure");
            if (MeasureGate is { } gate) await gate.Task;
            return [.. names.Select(n => new AdminCategoryResult(n, MeasureState(n), MeasureState(n) == AdminCategoryState.Measured ? Size(n) : 0, 3))];
        }

        public async Task<IReadOnlyList<AdminCategoryResult>> CleanCategoriesAsync(IReadOnlyList<AdminCategory> names, IProgress<AdminCategoryProgress>? progress = null, CancellationToken ct = default)
        {
            Calls.Add("clean");
            if (CleanThrows is { } ex) throw ex;
            var results = new List<AdminCategoryResult>();
            for (int i = 0; i < names.Count; i++)
            {
                progress?.Report(new(names[i], i, names.Count));
                if (HoldAt == names[i] && CleanGate is { } gate) await gate.Task;
                var state = CleanState(names[i]);
                var r = new AdminCategoryResult(names[i], state, state == AdminCategoryState.Cleaned ? Size(names[i]) * 95 / 100 : 0, 3);
                results.Add(r);
                progress?.Report(new(names[i], i, names.Count, r));
            }
            return results;
        }

        public Task EndSessionAsync() { Calls.Add("end"); return Task.CompletedTask; }
    }

    private sealed class Settings : IModuleSettings
    {
        public object? Stored;
        public bool Fail;
        public T Get<T>() where T : class, new() => Stored as T ?? new();
        public void Save<T>(T value) where T : class { if (Fail) throw new IOException("disk"); Stored = value; }
    }

    private sealed class NoUi : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution("cleaner", spec);
        public void SetSettingsPage(Func<string?, object> createView) { }
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands.Add(commands);
        public List<Func<IReadOnlyList<ChippiCommand>>> Commands { get; } = [];
    }

    private sealed class Bar : IIslandPublisher
    {
        public List<IslandActivity> Shown { get; } = [];
        public void Publish(IslandActivity activity) => Shown.Add(activity);
        public void Clear(string key) { }
        public void ReportSuccess(string? message = null) { }
    }

    private sealed class Rig : IDisposable
    {
        public Fixture F { get; } = new();
        public Hold Hold { get; } = new();
        public ShotAdmin Admin { get; } = new();
        public Settings Settings { get; } = new();
        public Bar Bar { get; } = new();
        public NoUi Ui { get; } = new();
        public List<string?> Opened { get; } = [];
        public CleanerModule Module { get; private set; } = null!;
        public ModuleContext Context { get; private set; } = null!;
        public CleanerEngine Engine => Module.Engine!;
        public CleanerView View => Module.View();

        /// <summary>The mockup's invented sizes: 820 MB of temp, 210 MB of thumbnails, 1.2 GB of Chrome's cache, a 1.6 GB Windows Update folder, 190 MB of dumps.</summary>
        public Rig Seed(bool thumbnails = true)
        {
            Big(F.Temp("a.tmp"), 820_000_000);
            if (thumbnails) Big(F.Local("Microsoft", "Windows", "Explorer", "thumbcache_256.db"), 210_000_000);
            Big(F.Local("Google", "Chrome", "User Data", "Default", "Cache", "data_1"), 1_200_000_000);
            Big(F.Windows("SoftwareDistribution", "Download", "pkg.cab"), 1_600_000_000);
            Big(F.Local("CrashDumps", "app.dmp"), 190_000_000);
            return this;
        }

        public Rig Start(bool island = true, Action<ModuleContext>? tweak = null)
        {
            Module = new CleanerModule(F.Roots, Hold.Read);
            Context = new ModuleContext(CleanerModule.Id, Settings, NullLogger.Instance, Bar, Ui)
            {
                OpenSettings = Opened.Add, Admin = Admin,
                TryOpenIslandPage = _ => island,
            };
            Assert.True(Module.StartAsync(Context, default).IsCompletedSuccessfully);
            return this;
        }

        public void Dispose()
        {
            Hold.Release();
            try { Module.StopAsync(default).GetAwaiter().GetResult(); } catch (Exception) { }
            F.Dispose();
        }

        public void Big(string path, long bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(path)) stream.SetLength(bytes);
            var when = DateTime.UtcNow - TimeSpan.FromDays(2);
            File.SetCreationTimeUtc(path, when);
            File.SetLastWriteTimeUtc(path, when);
        }
    }

    private static Rig Make(bool island = true) => new Rig().Start(island);

    // ---- behavior ----

    private static IReadOnlyList<string> Row(CleanerView view, string name) =>
        [.. Descendants<Button>(view).Where(b => AutomationProperties.GetName(b) == name).SelectMany(b => Descendants<TextBlock>(b).Select(t => t.Text))];

    [Fact]
    public void The_list_draws_seven_rows_that_fit_the_panel_with_tags_shields_and_sizes() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.F.File(rig.F.Temp("a.tmp"), 820_000);
        rig.F.File(rig.F.Local("Microsoft", "Windows", "Explorer", "thumbcache_256.db"), 210_000);
        rig.Start();
        WpfTestThread.Wait(rig.Engine.MeasureAsync());
        var view = rig.View;
        view.Redraw();
        // Seven 36-DIP rows and the meta line: inside the 494 x 302 page, so nothing ever scrolls.
        view.Measure(new Size(494, double.PositiveInfinity));
        Assert.True(view.DesiredSize.Height <= 302, $"{view.DesiredSize.Height}");
        Assert.Equal(7, Descendants<Button>(view).Count(b => AutomationProperties.GetName(b) is { Length: > 8 } n && CleanerCatalog.All.Any(c => c.Name == n)));
        Assert.Contains("Check first", Row(view, "App caches"));
        Assert.Equal(4, Descendants<UacShield>(view).Count());
        Assert.Contains("820 KB", Row(view, "User temp files"));
        Assert.Contains("—", Row(view, "Windows temp"));
        // The header carries Scan and Clean; Clean is enabled with the default ticks.
        Assert.Equal(["Scan", "Clean"], Descendants<Button>(view.Header).Select(b => AutomationProperties.GetName(b)));
        Assert.All(Descendants<Button>(view.Header), b => Assert.True(b.IsEnabled));
    });

    [Fact]
    public void A_row_toggles_its_remembered_tick_and_Clean_is_off_with_nothing_ticked_and_shows_the_shield_with_an_admin_category() => WpfTestThread.Run(() =>
    {
        using var rig = Make();
        var view = rig.View;
        view.Redraw();
        void Press(string name) => Descendants<Button>(view).First(b => AutomationProperties.GetName(b) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Press("User temp files");
        Press("Thumbnail and icon caches");
        Assert.False(rig.Engine.IsTicked("temp"));
        Assert.Contains(((CleanerSettings)rig.Settings.Stored!).Ticks, p => p is { Key: "temp", Value: false });
        Assert.False(Descendants<Button>(view.Header).Single(b => AutomationProperties.GetName(b) == "Clean").IsEnabled);
        Press("Windows Update cache");
        var clean = Descendants<Button>(view.Header).Single(b => AutomationProperties.GetName(b) == "Clean");
        Assert.True(clean.IsEnabled);
        Assert.Single(Descendants<UacShield>(clean));
    });

    [Fact]
    public void Clean_asks_the_question_with_Cancel_first_Cancel_deletes_nothing_and_Clean_runs_and_shows_the_result() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        string a = rig.F.File(rig.F.Temp("a.tmp"), 2_000_000);
        string b = rig.F.File(rig.F.Local("Microsoft", "Windows", "Explorer", "thumbcache_256.db"), 1_000_000);
        rig.Start();
        var view = rig.View;
        var asked = rig.Module.RequestCleanAsync();
        WpfTestThread.Wait(Until(() => view.Question is not null));
        Assert.Equal("Clean 3 MB?", view.Question!.Question);
        Assert.Contains("Clean 3 MB?", Texts(view));
        var cancel = Descendants<Button>(view).First(x => AutomationProperties.GetName(x) == "Cancel");
        Assert.Same(cancel, view.InitialFocus);   // Cancel has the keyboard
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WpfTestThread.Wait(asked);
        Assert.True(rig.F.Exists(a) && rig.F.Exists(b));
        Assert.Null(view.Question);

        asked = rig.Module.RequestCleanAsync();
        WpfTestThread.Wait(Until(() => view.Question is not null));
        Descendants<Button>(view).First(x => AutomationProperties.GetName(x) == "Clean 3 MB").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WpfTestThread.Wait(asked);
        Assert.False(rig.F.Exists(a) || rig.F.Exists(b));
        Assert.Equal("3 MB freed", rig.Engine.LastRun!.Freed);
        Assert.Contains("3 MB freed", Texts(view));
        Assert.Equal(["Scan"], Descendants<Button>(view.Header).Select(x => AutomationProperties.GetName(x)));
        // The bar: "3 MB freed" over Cleaner, as feedback that clears itself.
        var shown = rig.Bar.Shown.Last();
        Assert.Equal(("3 MB freed", "Cleaner", true, false, "Trash2"), (shown.Title, shown.Source, shown.IsFeedback, shown.NeedsAttention, shown.Icon));
    });

    [Fact]
    public void A_declined_prompt_marks_the_rows_Needs_admin_with_a_small_Clean_and_the_bar_says_how_many() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.F.File(rig.F.Temp("a.tmp"), 2_000_000);
        rig.Start();
        rig.Admin.MeasureState = _ => AdminCategoryState.NeedsAdmin;
        rig.Engine.SetTicked("wtemp", true);
        rig.Engine.SetTicked("wu", true);
        var asked = rig.Module.RequestCleanAsync();
        WpfTestThread.Wait(Until(() => rig.View.Question is not null));
        Descendants<Button>(rig.View).First(x => AutomationProperties.GetName(x) == "Clean 2 MB").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WpfTestThread.Wait(asked);
        Assert.Equal(2, Texts(rig.View).Count(t => t == "Needs admin"));
        Assert.Equal(2, Descendants<Button>(rig.View).Count(x => AutomationProperties.GetName(x) == "Clean" && x.Height == 24));
        var shown = rig.Bar.Shown.Last();
        Assert.Equal(("2 MB freed", "2 need admin", true, "Shield"), (shown.Title, shown.Source, shown.NeedsAttention, shown.Icon));
    });

    [Fact]
    public void A_failed_category_offers_Retry_which_asks_again_for_that_row_only() => WpfTestThread.Run(() =>
    {
        using var rig = Make();
        rig.Engine.SetTicked("temp", false);
        rig.Engine.SetTicked("thumb", false);
        rig.Engine.SetTicked("wu", true);
        rig.Admin.CleanState = _ => AdminCategoryState.Failed;
        var asked = rig.Module.RequestCleanAsync();
        WpfTestThread.Wait(Until(() => rig.View.Question is not null));
        Descendants<Button>(rig.View).First(x => AutomationProperties.GetName(x).StartsWith("Clean ", StringComparison.Ordinal)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WpfTestThread.Wait(asked);
        Assert.Contains("Failed", Texts(rig.View));
        Assert.Contains("· 1 failed", Texts(rig.View));
        Assert.Equal(("Clean failed", "Windows Update cache", true), (rig.Bar.Shown.Last().Title, rig.Bar.Shown.Last().Source, rig.Bar.Shown.Last().NeedsAttention));
        rig.Admin.CleanState = _ => AdminCategoryState.Cleaned;
        Descendants<Button>(rig.View).Single(x => AutomationProperties.GetName(x) == "Retry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WpfTestThread.Wait(Until(() => rig.View.Question is not null));
        Assert.Equal(["Windows Update cache"], rig.View.Question!.Items.Select(i => i.Category.Name));
    });

    [Fact]
    public void Nothing_to_clean_shows_the_empty_page_with_only_Scan() => WpfTestThread.Run(() =>
    {
        using var rig = Make();
        WpfTestThread.Wait(rig.Module.RequestCleanAsync());
        Assert.True(rig.View.Empty);
        Assert.Contains("Nothing to clean", Texts(rig.View));
        Assert.Equal(["Scan"], Descendants<Button>(rig.View.Header).Select(x => AutomationProperties.GetName(x)));
        Assert.All(rig.Admin.Calls, c => Assert.Equal("end", c));   // nothing measured or cleaned; any open session ends
    });

    [Fact]
    public void With_the_island_off_a_verb_opens_Settings_and_neither_prompts_nor_deletes() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        string a = rig.F.File(rig.F.Temp("a.tmp"), 2_000_000);
        rig.Start(island: false);
        rig.Engine.SetTicked("wu", true);
        WpfTestThread.Wait(rig.Module.RequestCleanAsync());
        Assert.Empty(rig.Admin.Calls);
        Assert.True(rig.F.Exists(a));
        Assert.Equal([null], rig.Opened);
    });

    [Fact]
    public void Stop_cleaning_and_quit_asks_on_the_island_and_answers_the_button() => WpfTestThread.Run(() =>
    {
        using var rig = Make();
        var question = rig.Module.AskQuitAsync();
        Assert.True(rig.View.Quitting);
        Assert.Contains("Stop cleaning and quit?", Texts(rig.View));
        Descendants<Button>(rig.View).First(x => AutomationProperties.GetName(x) == "Quit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WpfTestThread.Wait(question);
        Assert.True(question.Result);
        question = rig.Module.AskQuitAsync();
        Assert.True(rig.View.Back());
        WpfTestThread.Wait(question);
        Assert.False(question.Result);
        using var off = Make(island: false);
        Assert.Null(off.Module.AskQuitAsync().GetAwaiter().GetResult());
    });

    [Fact]
    public void Settings_lists_the_categories_as_the_lists_remembered_ticks() => WpfTestThread.Run(() =>
    {
        using var rig = Make();
        using var page = new CleanerSettingsView(rig.Engine, rig.Context);
        Assert.Equal(7, page.Switches.Count);
        Assert.True(page.Switches["temp"].IsChecked);
        Assert.False(page.Switches["wu"].IsChecked);
        Assert.Equal(4, Descendants<UacShield>(page).Count());
        Assert.Single(Descendants<Badge>(page), b => b.Text == "Check first");
        page.Switches["wu"].IsChecked = true;
        Assert.True(rig.Engine.IsTicked("wu"));
        Assert.Equal("On", page.Switches["wu"].Content);
        rig.Settings.Fail = true;
        page.Switches["wu"].IsChecked = false;
        Assert.True(rig.Engine.IsTicked("wu"));       // the save failed: the tick stays
        Assert.True(page.Switches["wu"].IsChecked);   // and the switch goes back
    });

    [Fact]
    public void The_Apps_menu_row_shows_the_last_size_and_Search_the_word_Cleaner() => WpfTestThread.Run(() =>
    {
        using var rig = new Rig();
        rig.F.File(rig.F.Temp("a.tmp"), 820_000_000 / 1000);
        rig.Start();
        WpfTestThread.Wait(rig.Engine.MeasureAsync());
        var clean = rig.Ui.Commands.Single()().Single(c => c.MenuOnly);
        Assert.Equal(("Cleaner", "820 KB", "Trash2"), (clean.Detail, clean.MenuNote, clean.Icon));
        var page = rig.Ui.Commands.Single()().Single(c => c.Kind == ChippiCommandKind.Page);
        Assert.Equal(("Cleaner", "Settings"), (page.Name, page.Detail));
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
            var timings = new List<string>();
            void Island(string state, CleanerView view)
            {
                var shell = new Border { Width = 520, Height = 360, Padding = new(12), CornerRadius = new(8) };
                shell.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
                var content = new Grid();
                content.RowDefinitions.Add(new() { Height = new(28) });
                content.RowDefinitions.Add(new() { Height = new(8) });
                content.RowDefinitions.Add(new());
                var top = new DockPanel();
                var back = new Button { Content = new LucideIcon { Data = LucideIcons.ChevronLeft, Size = 14 } };
                back.SetResourceReference(FrameworkElement.StyleProperty, "IslandIconButtonStyle");
                DockPanel.SetDock(back, Dock.Left);
                top.Children.Add(back);
                top.Children.Add(view.Header);
                content.Children.Add(top);
                Grid.SetRow(view, 2);
                content.Children.Add(view);
                shell.Child = content;
                view.Redraw();
                Snapshot(shell, new(520, 360), Path.Combine(output, $"cleaner-{state}-{theme}.png"));
                content.Children.Remove(view);
                top.Children.Remove(view.Header);
            }
            void Page(string state, CleanerSettingsView view, double width)
            {
                var shell = new Border { Padding = new(40, 4, 40, 28), Child = view };
                shell.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
                Snapshot(shell, new(width, 760), Path.Combine(output, $"cleaner-settings-{state}-{theme}.png"));
                shell.Child = null;
            }

            // The list, measured; opening with the last sizes dimmed; the first measure with skeletons.
            using (var rig = new Rig().Seed().Start())
            {
                var watch = Stopwatch.StartNew();
                WpfTestThread.Wait(rig.Engine.MeasureAsync());
                var view = rig.View;
                view.Redraw();
                timings.Add($"{theme} list construction and layout: {watch.Elapsed.TotalMilliseconds:F2} ms");
                Island("list", view);
                rig.Engine.SetTicked("app", true);
                rig.Hold.Running.Add("chrome");
                WpfTestThread.Wait(rig.Engine.MeasureAsync());
                Island("list-app-open", view);
                rig.Engine.SetTicked("app", false);
                rig.Hold.Running.Clear();
                WpfTestThread.Wait(rig.Engine.MeasureAsync());   // Chrome closed: the next measure reads App caches again
                rig.Engine.SetTicked("temp", false);
                rig.Engine.SetTicked("thumb", false);
                Island("list-none", view);
                rig.Engine.SetTicked("temp", true);
                rig.Engine.SetTicked("thumb", true);
                rig.Engine.SetTicked("wtemp", true);
                rig.Engine.SetTicked("wu", true);
                Island("list-admin", view);
            }
            using (var rig = new Rig().Seed().Start())
            {
                // The sizes of last time, dimmed while a fresh measure reads the first folder.
                var seeded = new CleanerSettings
                {
                    Sizes = new()
                    {
                        ["temp"] = new(820_000_000, false), ["thumb"] = new(210_000_000, false), ["app"] = new(1_200_000_000, false),
                        ["wu"] = new(1_600_000_000, true), ["dump"] = new(190_000_000, true),
                    },
                };
                rig.Settings.Stored = seeded;
                rig.Module.StopAsync(default).GetAwaiter().GetResult();
                rig.Hold.BlockAt = 1;
                rig.Start();
                var measuring = rig.Engine.MeasureAsync();
                Island("list-opening", rig.View);
                rig.Hold.Release();
                WpfTestThread.Wait(measuring);
            }
            using (var rig = new Rig().Seed().Start())
            {
                rig.Hold.BlockAt = 3;   // temp and thumbnails are in; the app caches are being read
                var measuring = rig.Engine.MeasureAsync();
                WpfTestThread.Wait(Until(() => rig.Engine.Rows[1].State == CleanerRowState.Measured));
                Island("list-measuring", rig.View);
                rig.Hold.Release();
                WpfTestThread.Wait(measuring);
            }
            using (var rig = new Rig().Seed(thumbnails: false).Start())
            {
                WpfTestThread.Wait(rig.Engine.MeasureAsync());
                Island("list-unavailable", rig.View);   // no Explorer folder: Thumbnail and icon caches is Unavailable
            }
            using (var rig = new Rig().Start())
            {
                WpfTestThread.Wait(rig.Module.RequestCleanAsync());
                Island("empty", rig.View);
            }

            // Asking, waiting, running.
            using (var rig = new Rig().Seed().Start())
            {
                rig.Hold.BlockAt = 3;
                var asking = rig.Module.RequestCleanAsync();
                WpfTestThread.Wait(Until(() => rig.Engine.IsPreparing && rig.Engine.Rows[1].State == CleanerRowState.Measured));
                Island("measuring-shortcut", rig.View);
                rig.Hold.Release();
                WpfTestThread.Wait(Until(() => rig.View.Question is not null));
                Island("confirm", rig.View);
                rig.View.Cancel();
                WpfTestThread.Wait(asking);
            }
            using (var rig = new Rig().Seed().Start())
            {
                rig.Admin.MeasureGate = new();
                rig.Engine.SetTicked("wtemp", true);
                rig.Engine.SetTicked("wu", true);
                var asking = rig.Module.RequestCleanAsync();
                WpfTestThread.Wait(Until(() => rig.Engine.WaitsForAdmin));
                Island("waiting-admin", rig.View);
                rig.Admin.MeasureGate.SetResult();
                WpfTestThread.Wait(Until(() => rig.View.Question is not null));
                Island("confirm-admin", rig.View);
                rig.View.Cancel();
                WpfTestThread.Wait(asking);
            }
            using (var rig = new Rig().Seed().Start())
            {
                rig.Admin.CleanGate = new();
                rig.Admin.HoldAt = AdminCategory.WindowsUpdateDownloads;
                foreach (var id in new[] { "wtemp", "wu", "dump" }) rig.Engine.SetTicked(id, true);
                var asking = rig.Module.RequestCleanAsync();
                WpfTestThread.Wait(Until(() => rig.View.Question is not null));
                rig.View.Confirm();
                WpfTestThread.Wait(Until(() => rig.Engine.RunningId == "wu"));
                Island("running", rig.View);
                var quit = rig.Module.AskQuitAsync();
                Island("quit", rig.View);
                rig.View.Back();
                rig.Admin.CleanGate.SetResult();
                WpfTestThread.Wait(asking);
                WpfTestThread.Wait(quit);
            }

            // Results.
            using (var rig = new Rig().Seed().Start())
            {
                foreach (var id in new[] { "wu", "dump" }) rig.Engine.SetTicked(id, true);
                Result(rig, "result");
            }
            using (var rig = new Rig().Seed().Start())
            {
                rig.Engine.SetTicked("app", true);
                rig.Hold.Running.Add("chrome");
                Result(rig, "result-app-open");
            }
            using (var rig = new Rig().Seed().Start())
            {
                rig.Engine.SetTicked("wu", true);
                rig.Engine.SetTicked("wtemp", true);
                rig.Admin.CleanState = c => c == AdminCategory.WindowsUpdateDownloads ? AdminCategoryState.Failed : AdminCategoryState.Cleaned;
                Result(rig, "result-failed");
            }
            using (var rig = new Rig().Seed().Start())
            {
                rig.Engine.SetTicked("wu", true);
                rig.Engine.SetTicked("wtemp", true);
                rig.Admin.MeasureState = _ => AdminCategoryState.NeedsAdmin;
                Result(rig, "result-declined");
            }
            using (var rig = new Rig().Seed().Start())
            {
                rig.Engine.SetTicked("wu", true);
                rig.Engine.SetTicked("dump", true);
                rig.Admin.CleanThrows = new IOException("helper");
                Result(rig, "result-helper");
            }

            void Result(Rig rig, string state)
            {
                var asking = rig.Module.RequestCleanAsync();
                WpfTestThread.Wait(Until(() => rig.View.Question is not null));
                rig.View.Confirm();
                WpfTestThread.Wait(asking);
                WpfTestThread.Wait(Until(() => rig.View.Result is not null));
                Island(state, rig.View);
            }

            // Settings, at its full and its 860-wide window.
            using (var rig = new Rig().Start())
            {
                var watch = Stopwatch.StartNew();
                using var view = new CleanerSettingsView(rig.Engine, rig.Context);
                Page("full", view, 848);
                timings.Add($"{theme} Settings page construction and layout: {watch.Elapsed.TotalMilliseconds:F2} ms");
                Page("narrow", view, 588);
            }
            string? path = Environment.GetEnvironmentVariable("CHIPPI_CLEANER_SCREEN_TIMINGS");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllLines(path, timings);
        });
    }

    // ---- helpers ----

    /// <summary>Waits, pumping the UI thread, until the condition holds.</summary>
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

    private static void Arrange(FrameworkElement element, Size size) { element.Measure(size); element.Arrange(new Rect(size)); element.UpdateLayout(); }

    private static IReadOnlyList<string> Texts(DependencyObject root) => [.. Descendants<TextBlock>(root).Select(t => t.Text)];

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        // A control's template (a button's content) is only in the visual tree once it has been laid out.
        if (root is FrameworkElement { Parent: null } element && VisualTreeHelper.GetParent(element) is null && !(element.IsMeasureValid && element.IsArrangeValid))
            Arrange(element, element is CleanerView ? new Size(494, 302) : element is CleanerSettingsView ? new Size(780, 1000) : new Size(494, 28));
        return Walk<T>(root);
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
