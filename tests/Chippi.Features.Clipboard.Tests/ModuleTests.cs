// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Clipboard.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Clipboard.Tests;

public sealed class ModuleTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeHub _hub = new();
    private readonly ClipboardFeatures _features = new();
    private readonly Ui _historyUi = new(), _cleanUi = new();
    private readonly Settings _settings = new();
    private readonly ClipboardHistoryModule _history;
    private readonly CleanUrlModule _clean;

    public ModuleTests()
    {
        _history = new ClipboardHistoryModule(_features, _folder.Path);
        _clean = new CleanUrlModule(_features);
    }

    public void Dispose()
    {
        _history.StopAsync(default).GetAwaiter().GetResult();
        _clean.StopAsync(default).GetAwaiter().GetResult();
        _folder.Dispose();
    }

    private ModuleContext Context(string id, Ui ui) =>
        new(id, _settings, NullLogger.Instance, new Island(), ui) { Clipboard = _hub, DataDirectory = _folder.Path };

    private Task StartHistoryAsync() => _history.StartAsync(Context(ClipboardHistoryModule.Id, _historyUi), default);

    private Task StartCleanAsync() => _clean.StartAsync(Context(CleanUrlModule.Id, _cleanUi), default);

    [Fact]
    public async Task The_tile_says_off_until_recording_then_counts_and_never_shows_the_copy()
    {
        await StartHistoryAsync();
        Assert.Equal("Off", _historyUi.Tile!.Status);
        Assert.Equal(0, _hub.Leases);
        _history.SetRecording(true);
        Assert.Equal("No items", _historyUi.Tile.Status);
        _hub.CopyText("hunter-not-a-secret");
        await _history.History!.IdleAsync();
        Assert.Equal("1 item", _historyUi.Tile.Status);
        _history.SetPaused(true);
        Assert.Equal("Paused", _historyUi.Tile.Status);
        Assert.Equal("Paused", _historyUi.Tile.PageNote);
        Assert.Equal(0, _hub.Leases);
    }

    [Fact]
    public async Task Recording_and_pause_are_kept_across_restarts()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        _history.SetPaused(true);
        await _history.StopAsync(default);
        await StartHistoryAsync();
        Assert.True(_history.Settings.Recording);
        Assert.True(_history.Settings.Paused);
    }

    [Fact]
    public async Task Uninstalling_erases_saved_history()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        _hub.CopyText("saved");
        await _history.History!.IdleAsync();
        Assert.True(_history.HasHistory);
        await _history.StopAsync(default);
        Assert.True(_history.HasHistory);
        await _history.UninstallAsync(default);
        Assert.False(_history.HasHistory);
        Assert.Empty(Directory.GetFiles(_folder.Path, "history.db*"));
    }

    [Fact]
    public async Task Uninstall_asks_only_when_history_is_saved()
    {
        await StartHistoryAsync();
        Assert.Null(((IFeatureModule)_history).UninstallQuestion());
        _history.SetRecording(true);
        _hub.CopyText("saved");
        await _history.History!.IdleAsync();
        Assert.Equal("Uninstall and erase history?", ((IFeatureModule)_history).UninstallQuestion());
    }

    [Fact]
    public async Task The_page_sees_esc_first_and_paste_goes_to_the_shells_target()
    {
        var target = new Core.Clipboard.PasteTarget(42, 7);
        bool closed = false;
        var context = new ModuleContext(ClipboardHistoryModule.Id, _settings, NullLogger.Instance, new Island(), _historyUi)
        {
            Clipboard = _hub, DataDirectory = _folder.Path, IslandPasteTarget = () => target, CloseIsland = () => closed = true,
        };
        await _history.StartAsync(context, default);
        _history.SetRecording(true);
        _hub.CopyText("to paste");
        await _history.History!.IdleAsync();
        Assert.Same(target, _features.PasteTarget!());
        WpfTestThread.Run(() =>
        {
            var view = (ClipboardView)_historyUi.Spec!.CreateSection();
            Load(view);
            Assert.False(_historyUi.Spec.HandleEscape!());
            Find<SearchBox>(view).Single().Text = "to";
            Assert.True(_historyUi.Spec.HandleEscape!());
        });
        _features.CloseIsland!();
        Assert.True(closed);
    }

    [Fact]
    public async Task Clean_url_alone_shows_manual_or_automatic_on_the_page_and_with_history_only_on_the_tile()
    {
        await StartCleanAsync();
        Assert.Equal("Manual", _cleanUi.Tile!.Status);
        Assert.Equal("Manual", _cleanUi.Tile.PageNote);
        _clean.Save(_clean.Settings with { Automatic = true });
        Assert.Equal("Automatic", _cleanUi.Tile.PageNote);
        Assert.Equal(1, _hub.Leases);
        await StartHistoryAsync();
        Assert.Equal("", _cleanUi.Tile.PageNote);
        var tile = Assert.Single(IslandTileContribution.Group([_historyUi.Tile!, _cleanUi.Tile]));
        Assert.Equal("Off", tile.Status);
    }

    [Fact]
    public async Task History_saves_the_link_automatic_cleaning_rewrote()
    {
        await StartCleanAsync();
        _clean.Save(_clean.Settings with { Automatic = true });
        await StartHistoryAsync();
        _history.SetRecording(true);
        _hub.CopyText("https://youtu.be/abc?si=share&t=30");
        await _history.History!.IdleAsync();
        Assert.Equal("https://youtu.be/abc?t=30", Assert.Single(_history.History.Entries).Text);
    }

    [Fact]
    public void Settings_keep_their_names_in_settings_json()
    {
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        var history = new ClipboardHistorySettings { Recording = true, Paused = true, AppsToSkip = [@"C:\Apps\Vault.exe"] };
        Assert.Equal(history.AppsToSkip, JsonSerializer.Deserialize<ClipboardHistorySettings>(JsonSerializer.Serialize(history, json), json)!.AppsToSkip);
        Assert.Contains("\"sitesToSkip\"", JsonSerializer.Serialize(new CleanUrlSettings(), json));
        Assert.Equal(["example.org"], new CleanUrlSettings { SitesToSkip = ["Example.org", "example.org", "not a site"] }.Normalized().SitesToSkip);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task Add_app_menu_uses_settings_theme_resources_without_island_styles(string theme)
    {
        await StartHistoryAsync();
        WpfTestThread.Run(() =>
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/Themes/{theme}.xaml") };
            dictionaries.Add(palette);
            try
            {
                var page = new ClipboardHistorySettingsPage(_history);
                var menu = page.CreateAddMenu(new[] { ("Test app", @"C:\Fixtures\Test.exe") });
                Assert.False(menu.IsOpen); // Never create a popup HWND on the user's desktop.
                var border = Assert.IsType<Border>(menu.Child);
                Assert.Equal(((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("FlyoutBrush")).Color,
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(border.Background).Color);
                Assert.Equal(((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("BorderBrush")).Color,
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(border.BorderBrush).Color);
                var rows = Find<Button>(border).ToArray();
                Assert.Equal(new[] { "Test app", "Browse\u2026" }, rows.Select(b => b.Content));
                var style = Application.Current.FindResource("FlyoutRowButtonStyle");
                Assert.All(rows, row => Assert.Same(style, row.Style));
                border.Measure(new Size(240, double.PositiveInfinity));
                border.Arrange(new Rect(0, 0, 240, border.DesiredSize.Height));
                var rendered = new System.Windows.Media.Imaging.RenderTargetBitmap(240, (int)Math.Ceiling(border.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rendered.Render(border);
                rows[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains(@"C:\Fixtures\Test.exe", _history.Settings.AppsToSkip);
            }
            finally { dictionaries.Remove(palette); }
        });
    }

    // ---- The page, on a WPF thread without a window ----

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item)
            yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child))
                yield return nested;
    }

    private static bool Shows(DependencyObject root, string text) =>
        Find<TextBlock>(root).Any(t => t.Text == text && IsShown(t));

    private static bool IsShown(DependencyObject element)
    {
        for (var at = element; at is not null; at = LogicalTreeHelper.GetParent(at))
            if (at is UIElement { Visibility: not Visibility.Visible })
                return false;
        return true;
    }

    private static void Load(FrameworkElement view) => view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

    [Fact]
    public async Task The_page_goes_from_first_use_to_the_list_and_esc_clears_a_search_first()
    {
        await StartHistoryAsync();
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var view = (ClipboardView)_historyUi.Spec!.CreateSection();
            Load(view);
            Assert.True(Shows(view, "Recording off"));
            Find<Button>(view).Single(b => b.Content as string == "Turn on").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Flush(view);
            Assert.False(Shows(view, "Recording off"));
            Assert.True(Shows(view, "No items"));

            var search = Find<SearchBox>(view).Single();
            search.Text = "nothing like it";
            Assert.True(view.HandleEscape());
            Assert.Equal("", search.Text);
            Assert.False(view.HandleEscape());

            // Clean URL's section stays out of the way while History runs.
            var section = (FrameworkElement)_cleanUi.Spec!.CreateSection();
            Load(section);
            Assert.Equal(Visibility.Collapsed, section.Visibility);
        });
        _hub.CopyText("Meeting moved to 3:30 PM");
        await _history.History!.IdleAsync();
        WpfTestThread.Run(() =>
        {
            var view = (ClipboardView)_historyUi.Spec!.CreateSection();
            Load(view);
            Assert.True(Shows(view, "Meeting moved to 3:30 PM"));
            Assert.True(Shows(view, "Now"));
        });
    }

    [Fact]
    public async Task The_header_holds_the_commands_and_paused_and_focus_follows_the_view()
    {
        await StartHistoryAsync();
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var spec = _historyUi.Spec!;
            var view = (ClipboardView)spec.CreateSection();
            var header = (FrameworkElement)spec.CreateHeader!();
            Load(view);
            Assert.Same(view.Header, header);
            Assert.Empty(Find<IconButton>(view));
            Assert.Equal("Turn on", Assert.IsType<Button>(spec.InitialFocus!()).Content);

            _history.SetRecording(true);
            _history.SetPaused(true);
            Flush(view);
            Assert.True(Shows(header, "Paused"));
            Assert.IsType<SearchBox>(spec.InitialFocus!());

            Find<IconButton>(header).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Clean URL").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Link", System.Windows.Automation.AutomationProperties.GetName((DependencyObject)spec.InitialFocus!()!));
            Assert.Null(_cleanUi.Spec!.InitialFocus!());
        });
    }

    [Fact]
    public void A_new_picture_on_a_reused_row_id_never_shows_the_erased_ones_thumbnail()
    {
        using var store = History.HistoryStore.Open(_folder.File("pictures.db"), new TestKeys());
        var blue = History.Pictures.Encode(new Core.Clipboard.ClipImage(1, 1, [255, 0, 0, 255]))!.Value;
        var red = History.Pictures.Encode(new Core.Clipboard.ClipImage(1, 1, [0, 0, 255, 255]))!.Value;
        var erased = store.Add(History.HistoryCapture.FromPicture(1, 1, blue.Thumbnail, blue.Png), DateTimeOffset.UtcNow).Entry!;
        store.EraseAll();
        var next = store.Add(History.HistoryCapture.FromPicture(1, 1, red.Thumbnail, red.Png), DateTimeOffset.UtcNow).Entry!;
        Assert.Equal(erased.Id, next.Id);
        WpfTestThread.Run(() =>
        {
            var view = new ClipboardView(_features);
            var first = view.Thumbnail(erased);
            var second = view.Thumbnail(next);
            Assert.NotSame(first, second);
            Assert.Same(second, view.Thumbnail(next));
        });
    }

    [Fact]
    public async Task Add_site_opens_again_after_cancel_and_survives_a_change_while_open()
    {
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var page = new CleanUrlSettingsPage(_clean);
            Button Named(string text) => Find<Button>(page).First(b => b.Content as string == text && IsShown(b));
            Named("Add site").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Named("Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Named("Add site").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            _clean.Save(_clean.Settings with { Automatic = true });
            Flush(page);
            var field = Find<TextBox>(page).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Site");
            field.Text = "Example.org";
            Named("Add").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.Equal(["example.org"], _clean.Settings.SitesToSkip);
    }

    [Fact]
    public async Task Without_history_the_page_is_clean_url_and_cleans_as_you_type()
    {
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var section = (FrameworkElement)_cleanUi.Spec!.CreateSection();
            Load(section);
            Assert.Equal(Visibility.Visible, section.Visibility);
            var field = Find<TextBox>(section).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Link");
            Assert.Same(field, _cleanUi.Spec.InitialFocus!());
            // One line the page's width: a long link scrolls, never wraps (Travis, 2026-10-28).
            Assert.Equal(TextWrapping.NoWrap, field.TextWrapping);
            Assert.Equal(1, field.MaxLines);
            Assert.Equal(HorizontalAlignment.Stretch, field.HorizontalAlignment);
            field.Text = "https://youtu.be/abc?si=share&t=30";
            Assert.True(Shows(section, "https://youtu.be/abc?t=30"));
            // Only the clean link: nothing lists what was removed.
            Assert.DoesNotContain(Find<TextBlock>(section), t => IsShown(t) && t.Text.StartsWith("Removed", StringComparison.Ordinal));
            field.Text = "https://example.org/?si=keep&ref=keep";
            Assert.True(Shows(section, "Nothing to remove"));
            field.Text = "ftp://files.example.org/a.zip";
            Assert.True(Shows(section, "Not a web link"));
        });
    }

    private static void Flush(System.Windows.Threading.DispatcherObject on) =>
        on.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, () => { });

    [Fact]
    public void Times_read_like_the_design()
    {
        var now = new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero).ToLocalTime();
        Assert.Equal("Now", Format.When(now.AddSeconds(-20), now));
        Assert.Equal("2 min", Format.When(now.AddMinutes(-2), now));
        Assert.Equal("1 hr", Format.When(now.AddMinutes(-90), now));
        Assert.Equal("Oct 21", Format.When(now.AddDays(-7), now));
        Assert.Equal("Yesterday", Format.When(now.AddDays(-1).AddMinutes(-1), now));
    }

    private sealed class Ui : IModuleUi
    {
        public IslandTileSpec? Spec;
        public IslandTileContribution? Tile;

        public IIslandTile AddIslandTile(IslandTileSpec spec)
        {
            Spec = spec;
            return Tile = new IslandTileContribution("test", spec);
        }

        public void SetSettingsPage(Func<string?, object> createView) { }
    }

    /// <summary>One settings section per type, kept between starts like settings.json.</summary>
    private sealed class Settings : IModuleSettings
    {
        private readonly Dictionary<Type, object> _saved = [];

        public T Get<T>() where T : class, new() => _saved.TryGetValue(typeof(T), out var value) ? (T)value : new();

        public void Save<T>(T value) where T : class => _saved[typeof(T)] = value;
    }

    private sealed class Island : IIslandPublisher
    {
        public void Publish(IslandActivity activity) { }

        public void Clear(string key) { }

        public void ReportSuccess(string? message = null) { }
    }
}
