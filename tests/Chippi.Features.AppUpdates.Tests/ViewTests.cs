// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.AppUpdates.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.AppUpdates.Tests;

public sealed class ViewTests
{
    [Fact]
    public void Twelve_updates_fit_five_at_a_time_and_keep_every_row_reachable() => WithFixture(12, fixture =>
    {
        using var view = fixture.View();
        Arrange(view, new(496, 300)); Arrange(view.Header, new(464, 28));
        Assert.Equal(5, view.VisibleKeys.Count);
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "1–5 of 12");
        Click(view, "Next apps"); Arrange(view, new(496, 300));
        Assert.Equal(1, view.Page); Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "6–10 of 12");
        Click(view, "Next apps"); Arrange(view, new(496, 300));
        Assert.Equal(2, view.Page); Assert.Equal(2, view.VisibleKeys.Count);
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "11–12 of 12");
        Assert.All(Descendants<Button>(view).Where(b => b.Visibility == Visibility.Visible), button =>
            Assert.True(button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize)).Bottom <= 300.1));
        Assert.True(view.DesiredSize.Height <= 300.1);
    });

    [Fact]
    public void Confirmation_keeps_exact_versions_while_a_new_check_arrives() => WithFixture(5, fixture =>
    {
        UpdateBatch? received = null;
        using var view = fixture.View((batch, _) => { received = batch; return Task.CompletedTask; });
        var batch = fixture.Engine.CaptureBatch(); view.ShowConfirmation(batch);
        fixture.Source.Packages = [.. fixture.Source.Packages, Package(99)];
        WpfTestThread.Wait(fixture.Engine.CheckAsync());
        Assert.Equal(5, view.Confirmation!.Packages.Count);
        Assert.Equal(batch.Packages, view.Confirmation.Packages);
        Arrange(view, new(496, 300));
        Click(view, "Update");
        Assert.NotNull(received); Assert.Equal(batch.Packages, received.Packages);
        Assert.DoesNotContain(Descendants<TextBlock>(view), t => t.Text.Contains("Windows may ask", StringComparison.Ordinal));
    });

    [Fact]
    public void Long_confirmation_pages_keep_all_twelve_reviewed_apps_and_cancel_is_safe() => WithFixture(12, fixture =>
    {
        int executed = 0;
        using var view = fixture.View((_, _) => { executed++; return Task.CompletedTask; });
        view.ShowConfirmation(fixture.Engine.CaptureBatch()); Arrange(view, new(496, 300));
        Assert.Equal(12, view.Confirmation!.Packages.Count);
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "Update 12 apps?");
        Click(view, "Next apps in batch"); Arrange(view, new(496, 300));
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "6–10 of 12");
        Click(view, "Cancel"); Assert.Null(view.Confirmation); Assert.Equal(0, executed);
    });

    [Fact]
    public void Unknown_installed_versions_stay_explicit_in_the_confirmation() => WithFixture(1, fixture =>
    {
        fixture.Source.Packages = [fixture.Source.Packages[0] with { InstalledVersion = "Unknown" }];
        WpfTestThread.Wait(fixture.Engine.CheckAsync()); fixture.Engine.SetShowUnknownVersions(true);
        using var view = fixture.View(); view.ShowConfirmation(fixture.Engine.CaptureBatch()); Arrange(view, new(496, 300));
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "Unknown → 1.1.0");
    });

    [Fact]
    public void Submission_cannot_open_a_second_confirmation_during_soft_close_preflight() => WithFixture(5, fixture =>
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int submissions = 0;
        using var view = fixture.View((_, _) => { submissions++; return release.Task; });
        try
        {
            var batch = fixture.Engine.CaptureBatch(); view.ShowConfirmation(batch); Arrange(view, new(496, 300));
            Click(view, "Update"); view.ShowConfirmation(batch);
            Assert.Null(view.Confirmation); Assert.Equal(1, submissions);
            Assert.All(Descendants<Button>(view).Where(b => b.Tag is string key && key.StartsWith("update:", StringComparison.Ordinal)), b => Assert.False(b.IsEnabled));
        }
        finally { release.TrySetResult(); }
    });

    [Fact]
    public void Progress_keeps_row_controls_and_unload_stops_observing_the_engine() => WithFixture(5, fixture =>
    {
        fixture.Source.HoldIndex = 2;
        using var view = fixture.View(); Arrange(view, new(496, 300));
        Button original = Descendants<Button>(view).Single(b => Equals(b.Tag, "update:" + fixture.Source.Packages[2].Key));
        var run = fixture.Engine.RunAsync(fixture.Engine.CaptureBatch());
        WpfTestThread.Wait(fixture.Source.Entered.Task); Arrange(view, new(496, 300));
        Assert.Same(original, Descendants<Button>(view).Single(b => Equals(b.Tag, original.Tag)));
        fixture.Source.Report(.8); Arrange(view, new(496, 300));
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text.Contains("80", StringComparison.Ordinal));
        string before = TextOf(view);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        fixture.Source.Report(.9);
        Assert.Equal(before, TextOf(view));
        fixture.Source.Release.TrySetResult(); WpfTestThread.Wait(run);
        Assert.DoesNotContain(Descendants<TextBlock>(view), t => t.Text.Contains("Updates continue", StringComparison.Ordinal));
    });

    [Fact]
    public void One_confirmed_update_keeps_twenty_rows_reachable_and_reports_only_its_batch() => WithFixture(20, fixture =>
    {
        fixture.Source.HoldIndex = 0;
        using var view = fixture.View();
        PackageUpdate selected = fixture.Source.Packages[0];
        var run = fixture.Engine.RunAsync(fixture.Engine.CaptureBatch([selected]));
        WpfTestThread.Wait(fixture.Source.Entered.Task); Arrange(view, new(496, 300));
        Assert.Contains(Descendants<TextBlock>(view), t => SemanticText(t).Contains("0 of 1 updated", StringComparison.Ordinal) && SemanticText(t).Contains("Updating " + selected.Name, StringComparison.Ordinal));
        fixture.Source.Release.TrySetResult(); WpfTestThread.Wait(run); Arrange(view, new(496, 300));
        Assert.Contains(Descendants<TextBlock>(view), t => SemanticText(t) == "1 of 1 updated");
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            foreach (string key in view.VisibleKeys) reachable.Add(key);
            var next = Descendants<Button>(view).Single(b => AutomationProperties.GetName(b) == "Next apps");
            if (!next.IsEnabled) break;
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Arrange(view, new(496, 300));
        }
        Assert.Equal(20, reachable.Count);
        Click(view.Header, "Update all");
        Assert.Equal(19, view.Confirmation!.Packages.Count);
        Assert.DoesNotContain(view.Confirmation.Packages, p => p.Key == selected.Key);
    });

    [Fact]
    public void Partial_source_warnings_keep_the_available_count_and_checked_context() => WithFixture(20, fixture =>
    {
        fixture.Source.Warning = "Check failed · Microsoft Store";
        using var view = fixture.View(); Arrange(view, new(496, 300));
        var summary = Descendants<TextBlock>(view).Single(t => SemanticText(t).Contains("20 updates", StringComparison.Ordinal));
        string text = SemanticText(summary);
        Assert.Contains("Checked", text); Assert.Contains(fixture.Source.Warning, text);
        Assert.Equal(text, Assert.IsType<string>(summary.ToolTip));
        Assert.Equal(text, AutomationProperties.GetName(summary));
        Assert.Equal(5, view.VisibleKeys.Count);
    });

    [Fact]
    public void Settings_toggle_uses_checked_events_and_shortcuts_open_the_central_editor() => WithFixture(5, fixture =>
    {
        using var view = new AppUpdatesSettingsView(fixture.Engine, fixture.Context);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Arrange(view, new(700, 620));
        var toggle = Assert.Single(Descendants<ToggleButton>(view));
        toggle.IsChecked = true; Assert.True(fixture.Engine.Preferences.ShowUnknownVersions);
        toggle.IsChecked = false; Assert.False(fixture.Engine.Preferences.ShowUnknownVersions);
        Click(view, "Edit Check app updates shortcut"); Assert.Equal("Check app updates", fixture.EditedShortcut);
        fixture.Engine.Skip(fixture.Source.Packages[0], false); Arrange(view, new(700, 620));
        Click(view, "Restore updates for " + fixture.Source.Packages[0].Name);
        Assert.Empty(fixture.Engine.Preferences.Skipped);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    });

    [Fact]
    public void Failed_checks_keep_a_recovery_button_and_show_cached_rows_when_available() => WithFixture(0, fixture =>
    {
        using var view = fixture.View(); fixture.Source.FailCheck = true;
        WpfTestThread.Wait(fixture.Engine.CheckAsync()); Arrange(view, new(496, 300));
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "Check failed");
        fixture.Source.FailCheck = false; fixture.Source.Packages = [Package(0)];
        var check = Descendants<Button>(view).First(b => AutomationProperties.GetName(b) == "Check again");
        check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Arrange(view, new(496, 300)); Assert.Single(view.VisibleKeys);
    });

    [Fact]
    public void Render_approved_states_offscreen_with_fixture_data_only()
    {
        string? output = Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        WpfTestThread.Run(() =>
        {
            Directory.CreateDirectory(output);
            var dictionaries = Application.Current.Resources.MergedDictionaries; var old = dictionaries.ToArray();
            var timings = new List<string>();
            try
            {
                foreach (string theme in new[] { "Dark", "Light" })
                {
                    dictionaries.Clear();
                    foreach (string file in new[] { $"Themes/{theme}.xaml", "Themes/Palette.xaml", "Themes/SharedStyles.xaml", "Controls/Controls.xaml" })
                        dictionaries.Add(new() { Source = new Uri("pack://application:,,,/Chippi.UI;component/" + file) });
                    dictionaries.Add(new() { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
                    WithFixtureOnThread(0, fixture =>
                    {
                        using var view = fixture.View(); Save(view, "empty");
                        fixture.Source.FailCheck = true; WpfTestThread.Wait(fixture.Engine.CheckAsync()); Save(view, "check-failed");
                    });
                    WithFixtureOnThread(12, fixture =>
                    {
                        var watch = Stopwatch.StartNew(); using var view = fixture.View(); Arrange(view, new(496, 300));
                        timings.Add($"{theme} cached page construction and layout: {watch.Elapsed.TotalMilliseconds:F2} ms");
                        Save(view, "list-12"); watch.Restart(); Click(view, "Next apps"); Arrange(view, new(496, 300));
                        timings.Add($"{theme} page switch and layout: {watch.Elapsed.TotalMilliseconds:F2} ms"); Save(view, "list-next");
                        view.ShowConfirmation(fixture.Engine.CaptureBatch()); Save(view, "confirm-12");
                    });
                    WithFixtureOnThread(5, fixture =>
                    {
                        using var view = fixture.View(); Save(view, "list-5");
                        view.ShowConfirmation(fixture.Engine.CaptureBatch()); Save(view, "confirm-5"); Assert.True(view.Back());
                        fixture.Source.HoldIndex = 2; fixture.Source.FailIndex = 2;
                        var run = fixture.Engine.RunAsync(fixture.Engine.CaptureBatch()); WpfTestThread.Wait(fixture.Source.Entered.Task); Save(view, "running");
                        fixture.Source.Release.TrySetResult(); WpfTestThread.Wait(run); Save(view, "results");
                        fixture.Engine.Skip(fixture.Source.Packages[0], false); fixture.Engine.Skip(fixture.Source.Packages[1], true);
                        using var settings = new AppUpdatesSettingsView(fixture.Engine, fixture.Context); settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                        var settingsShell = new Border { Padding = new(40, 4, 40, 28), Child = settings };
                        settingsShell.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
                        Snapshot(settingsShell, new(860, 800), Path.Combine(output, $"app-updates-settings-{theme.ToLowerInvariant()}.png"));
                        settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    });
                    WithFixtureOnThread(20, fixture =>
                    {
                        fixture.Source.Warning = "Check failed · Microsoft Store";
                        using var view = fixture.View(); Save(view, "partial-store-20");
                        fixture.Source.Warning = null; WpfTestThread.Wait(fixture.Engine.CheckAsync());
                        fixture.Source.HoldIndex = 0;
                        var run = fixture.Engine.RunAsync(fixture.Engine.CaptureBatch([fixture.Source.Packages[0]]));
                        WpfTestThread.Wait(fixture.Source.Entered.Task); Save(view, "single-running-20");
                        fixture.Source.Release.TrySetResult(); WpfTestThread.Wait(run); Save(view, "single-result-20");
                    });
                    void Save(AppUpdatesView view, string state)
                    {
                        var shell = new Border { Width = 520, Height = 360, Padding = new(12), CornerRadius = new(8) };
                        shell.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
                        var content = new Grid(); content.RowDefinitions.Add(new() { Height = new(28) }); content.RowDefinitions.Add(new() { Height = new(8) }); content.RowDefinitions.Add(new());
                        var header = new DockPanel(); var back = AppUpdatesUi.Icon(LucideIcons.ChevronLeft, "Back", () => view.Back()); DockPanel.SetDock(back, Dock.Left); header.Children.Add(back); header.Children.Add(view.Header);
                        content.Children.Add(header); Grid.SetRow(view, 2); content.Children.Add(view); shell.Child = content;
                        Snapshot(shell, new(520, 360), Path.Combine(output, $"app-updates-{state}-{theme.ToLowerInvariant()}.png"));
                        content.Children.Remove(view); header.Children.Remove(view.Header);
                    }
                }
                string? path = Environment.GetEnvironmentVariable("CHIPPI_APP_UPDATES_TIMINGS");
                if (!string.IsNullOrWhiteSpace(path)) File.WriteAllLines(path, timings);
            }
            finally { dictionaries.Clear(); foreach (var item in old) dictionaries.Add(item); }
        });
    }

    private static PackageUpdate Package(int index) => new("Fixture.App" + index,
        index == 10 ? "A deliberately long application name that still keeps its update action in view" : new[] { "Visual Studio Code", "Firefox", "Discord", "Git", "7-Zip" }[index % 5] + (index < 5 ? "" : " " + index),
        "1.0.0", "1.1.0", "winget", UpdateScope.User);

    private static void WithFixture(int count, Action<Fixture> action) => WpfTestThread.Run(() => WithFixtureOnThread(count, action));
    private static void WithFixtureOnThread(int count, Action<Fixture> action)
    {
        if (Application.Current.TryFindResource("IslandButtonStyle") is null)
            Application.Current.Resources.MergedDictionaries.Add(new() { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
        var fixture = new Fixture(count);
        try { action(fixture); }
        finally { fixture.Source.Release.TrySetResult(); }
    }
    private sealed class Fixture
    {
        internal Fixture(int count)
        {
            Source.Packages = Enumerable.Range(0, count).Select(Package).ToArray();
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            Engine = new(Source, null, new UiStore(), action => { if (dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action); });
            Context = new ModuleContext("app-updates", new UiSettings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher("app-updates"), new UiContributions())
            { EditShortcut = name => EditedShortcut = name };
            WpfTestThread.Wait(Engine.CheckAsync());
        }
        internal UiSource Source { get; } = new();
        internal AppUpdatesEngine Engine { get; }
        internal ModuleContext Context { get; }
        internal string? EditedShortcut { get; private set; }
        internal AppUpdatesView View(Func<UpdateBatch, bool, Task>? execute = null)
        {
            var view = new AppUpdatesView(Engine, Context, execute ?? ((batch, _) => Engine.RunAsync(batch)), loadIcons: false);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); return view;
        }
    }
    private sealed class UiSource : IPackageSource, IPackageSourceStatus
    {
        internal IReadOnlyList<PackageUpdate> Packages { get; set; } = [];
        internal int HoldIndex { get; set; } = -1;
        internal int FailIndex { get; set; } = -1;
        internal bool FailCheck { get; set; }
        public string? Warning { get; set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IProgress<PackageInstallProgress>? _progress;
        public Task<IReadOnlyList<PackageUpdate>> CheckAsync(bool includeUnknown, CancellationToken ct = default)
            => FailCheck ? Task.FromException<IReadOnlyList<PackageUpdate>>(new InvalidOperationException("Fixture source unavailable")) : Task.FromResult(Packages);
        public async Task<PackageInstallResult> UpdateAsync(PackageUpdate package, IProgress<PackageInstallProgress>? progress = null, CancellationToken ct = default)
        {
            int index = Array.FindIndex(Packages.ToArray(), p => p.Key == package.Key);
            if (index == HoldIndex)
            {
                _progress = progress; progress?.Report(new(.62, "Installing")); Entered.TrySetResult(); await Release.Task;
            }
            return index == FailIndex ? new(UpdateState.Failed, "Download interrupted") : new(UpdateState.Updated);
        }
        internal void Report(double value) => _progress?.Report(new(value, "Installing"));
    }
    private sealed class UiStore : IAppUpdatesStore
    {
        private AppUpdatesPreferences _value = new();
        public AppUpdatesPreferences Load() => _value;
        public void Save(AppUpdatesPreferences preferences) => _value = preferences;
    }
    private sealed class UiSettings : IModuleSettings
    {
        public T Get<T>() where T : class, new() => new();
        public void Save<T>(T value) where T : class { }
    }
    private sealed class UiContributions : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution("app-updates", spec);
        public void SetSettingsPage(Func<string?, object> createView) { }
    }
    private static void Click(DependencyObject root, string name)
        => Descendants<Button>(root).Single(b => AutomationProperties.GetName(b) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Arrange(FrameworkElement element, Size size) { element.Measure(size); element.Arrange(new Rect(size)); element.UpdateLayout(); }
    private static string SemanticText(TextBlock block) => new TextRange(block.ContentStart, block.ContentEnd).Text.Trim();
    private static string TextOf(DependencyObject root) => string.Join("|", Descendants<TextBlock>(root).Select(SemanticText));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Snapshot(FrameworkElement element, Size size, string path)
    {
        Arrange(element, size);
        var bitmap = new RenderTargetBitmap((int)size.Width * 2, (int)size.Height * 2, 192, 192, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
