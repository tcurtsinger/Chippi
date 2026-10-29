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
    public async Task The_list_shows_whole_rows_only_keeps_the_open_entry_in_view_and_never_scrolls()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        for (int i = 0; i < 15; i++)
        {
            _hub.CopyText($"copy {i}\nsecond line\nthird line\nfourth line");
            await _history.History!.IdleAsync();
        }
        WpfTestThread.Run(() =>
        {
            var view = (ClipboardView)_historyUi.Spec!.CreateSection();
            Load(view);
            Flush(view);
            void Fit() { view.Measure(new Size(496, 304)); view.Arrange(new Rect(0, 0, 496, 304)); view.UpdateLayout(); }
            Fit();
            var rows = Find<WholeRows>(view).Single();
            // Whole rows only: the shown rows fit, and the first one left out wouldn't have.
            var all = rows.Children.OfType<FrameworkElement>().ToList();
            var shown = all.Where(rows.Shows).ToList();
            double room = 304 - 28 - 8;
            Assert.InRange(shown.Count, 1, all.Count - 1);
            Assert.True(shown.Sum(r => r.DesiredSize.Height) <= room);
            Assert.True(shown.Sum(r => r.DesiredSize.Height) + all[shown.Count].DesiredSize.Height > room);
            Assert.Empty(Find<ScrollViewer>(view));

            // Down past the last whole row opens copy 6, the 9th newest; rows above leave instead.
            var search = Find<SearchBox>(view).Single();
            for (int i = 0; i < 9; i++)
                search.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, new FakeSource(view), 0, System.Windows.Input.Key.Down) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            Fit();
            var open = rows.Children.OfType<Border>().Single();
            Assert.True(rows.Shows(open));
            Assert.True(rows.ActualHeight <= 304 - 28 - 8);
            var preview = Find<TextBlock>(open).Single(t => t.Text.StartsWith("copy 6", StringComparison.Ordinal));
            Assert.Equal(3 * 18, preview.MaxHeight); // at most 3 lines, the last trimmed
            Assert.Equal(TextTrimming.CharacterEllipsis, preview.TextTrimming);
            Assert.Equal("copy 6\nsecond line\nthird line…", preview.Text); // the hidden fourth line shows as "…"
        });
    }

    [Theory]
    [InlineData("one", "one")]
    [InlineData("one\ntwo\nthree", "one\ntwo\nthree")]
    [InlineData("one\ntwo\nthree\n", "one\ntwo\nthree")]
    [InlineData("one\r\ntwo\r\nthree\r\nfour", "one\ntwo\nthree…")]
    [InlineData("one\ntwo\nthree\nfour\nfive", "one\ntwo\nthree…")]
    public void A_preview_past_three_lines_ends_its_third_with_an_ellipsis(string text, string shown) =>
        Assert.Equal(shown, ClipboardView.PreviewText(text, 3));

    private sealed class FakeSource(System.Windows.Media.Visual root) : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get => root; set { } }
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
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
            Assert.DoesNotContain(Find<Button>(view), b => System.Windows.Automation.AutomationProperties.GetName(b) is "Clean URL" or "Pause" or "More"); // the commands are the header's
            Assert.Equal("Turn on", Assert.IsType<Button>(spec.InitialFocus!()).Content);

            _history.SetRecording(true);
            _history.SetPaused(true);
            Flush(view);
            Assert.True(Shows(header, "Paused"));
            Assert.IsType<SearchBox>(spec.InitialFocus!());

            Find<Button>(header).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Clean URL").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            Assert.True(Shows(page, "No sites"));
            Button Named(string text) => Find<Button>(page).First(b => (b.Content as string ?? System.Windows.Automation.AutomationProperties.GetName(b)) == text && IsShown(b));
            Named("Add site").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Named("Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Shows(page, "No sites"));
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
            // The refresh allows three padded lines without scrolling.
            Assert.Equal(TextWrapping.Wrap, field.TextWrapping);
            Assert.Equal(3, field.MaxLines);
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

    [Fact]
    public async Task Long_links_keep_both_ends_and_copy_the_full_cleaned_value()
    {
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var view = new CleanUrlView(_features);
            string clean = "https://example.org/" + string.Concat(Enumerable.Repeat("long-path-segment/", 60)) + "?id=7";
            view.Field.Text = clean + "&utm_source=test";
            view.Measure(new Size(496, 304));
            view.Arrange(new Rect(0, 0, 496, view.DesiredSize.Height));
            view.UpdateLayout();
            var displays = Find<MiddleTrimmedText>(view).ToArray();
            Assert.Equal(2, displays.Length);
            foreach (var display in displays)
            {
                var text = Find<TextBlock>(display).Single();
                Assert.Contains("\u2026", text.Text);
                Assert.StartsWith("https://example.org/", text.Text);
                Assert.EndsWith(display.FullText.EndsWith("test", StringComparison.Ordinal) ? "test" : "?id=7", text.Text);
                Assert.True(display.ActualHeight <= 48);
                Assert.True(text.ActualHeight <= 48);
            }
            Assert.Equal(clean + "&utm_source=test", view.Field.Text);
            view.Field.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice, 0, null, view.Field)
                { RoutedEvent = System.Windows.Input.Keyboard.GotKeyboardFocusEvent });
            Assert.Equal(view.Field.Text, view.Field.SelectedText);
            Find<Button>(view).Single(b => Equals(b.Content, "Copy")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(clean, Assert.Single(_hub.Written).Text);
        });
    }
    [Fact]
    public void Link_field_grows_from_one_to_three_lines_with_the_same_padding() => WpfTestThread.Run(() =>
    {
        var view = new CleanUrlView(_features);
        double Height(string link)
        {
            view.Field.Text = link;
            view.Measure(new Size(496, 304));
            view.Arrange(new Rect(0, 0, 496, view.DesiredSize.Height));
            view.UpdateLayout();
            return view.Field.ActualHeight;
        }
        Assert.Equal(28, Height("https://example.org/"));
        Assert.Equal(60, Height("https://example.org/" + new string('a', 700)));
        var host = (ScrollViewer)view.Field.Template.FindName("PART_ContentHost", view.Field);
        Assert.Equal(new Thickness(9, 5, 9, 5), host.Padding);
        Assert.Equal(new Thickness(0), host.Margin); // TextBox already forwards Padding to its host.
        Assert.Equal(ScrollBarVisibility.Disabled, host.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled, host.VerticalScrollBarVisibility);
        Assert.Equal(28, Height("https://example.org/"));
    });
    [Fact]
    public async Task Sites_empty_row_returns_when_the_last_site_is_removed()
    {
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var page = new CleanUrlSettingsPage(_clean);
            Load(page);
            _clean.Save(_clean.Settings with { SitesToSkip = ["example.org"] });
            Flush(page);
            Assert.False(Shows(page, "No sites"));
            Assert.True(Shows(page, "example.org"));
            _clean.Save(_clean.Settings with { SitesToSkip = [] });
            Flush(page);
            Assert.True(Shows(page, "No sites"));
            Assert.False(Shows(page, "example.org"));
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
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

    // ---- Settings pages (brief F3 #5, #6) ----

    [Fact]
    public async Task Settings_history_lists_every_copy_pinned_first_with_the_count_and_search()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        for (int i = 0; i < 25; i++)
            _hub.CopyText($"note {i}");
        await _history.History!.IdleAsync();
        var pinned = _history.History.Entries.Single(e => e.Text == "note 3");
        await _history.History.PinAsync(pinned.Id, true);
        WpfTestThread.Run(() =>
        {
            var page = new ClipboardHistorySettingsPage(_history);
            Assert.True(Shows(page, "25 items"));
            var rows = Find<Border>(page).Where(b => b.MinHeight == SettingsLayout.RowHeight && b.Child is Grid).ToList();
            Assert.Equal(25, rows.Count); // no island limit: the page scrolls
            Assert.Equal("note 3", Find<TextBlock>(rows[0]).First(t => t.Text.StartsWith("note", StringComparison.Ordinal)).Text);
            Assert.Contains(Find<Button>(page), b => b.Content as string == "Pause");
            var search = Find<SearchBox>(page).Single();
            search.Text = "note 1";
            Assert.Equal(11, Find<Border>(page).Count(b => b.MinHeight == SettingsLayout.RowHeight && b.Child is Grid)); // 1, 10 to 19
            search.Text = "nothing like it";
            Assert.True(Shows(page, "No matches"));
        });
    }

    [Fact]
    public async Task Settings_history_menu_copies_pins_and_deletes_and_paste_without_a_target_copies()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        _hub.CopyText("alpha");
        await _history.History!.IdleAsync();
        var entry = _history.History.Entries.Single();
        WpfTestThread.Run(() =>
        {
            var page = new ClipboardHistorySettingsPage(_history);
            var anchor = Find<IconButton>(page).Single();
            Button Item(string text) => Find<Button>(page.Menu(anchor, _history.History.Entries.Single())).Single(b => b.Content as string == text);
            Assert.Equal(new[] { "Paste", "Copy", "Pin", "Delete" }, Find<Button>(page.Menu(anchor, entry)).Select(b => b.Content));
            Item("Paste").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Settings has no app to paste into here
            Flush(page);
            Assert.Equal("alpha", Assert.Single(_hub.Written).Text);
            Assert.Empty(_hub.Pasted);
        });
        await _history.History.IdleAsync();
        WpfTestThread.Run(() =>
        {
            var page = new ClipboardHistorySettingsPage(_history);
            _features.SettingsPasteTarget = () => new Core.Clipboard.PasteTarget(7, 7);
            var anchor = Find<IconButton>(page).Single();
            Find<Button>(page.Menu(anchor, entry)).Single(b => b.Content as string == "Paste").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Flush(page);
            Assert.Equal((nint)7, Assert.Single(_hub.Pasted).Target.Window);
            Find<Button>(page.Menu(anchor, entry)).Single(b => b.Content as string == "Pin").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        await _history.History.IdleAsync();
        Assert.True(_history.History.Entries.Single().Pinned);
        WpfTestThread.Run(() =>
        {
            var page = new ClipboardHistorySettingsPage(_history);
            var anchor = Find<IconButton>(page).Single();
            Assert.Contains(Find<Button>(page.Menu(anchor, _history.History.Entries.Single())), b => b.Content as string == "Unpin");
            Find<Button>(page.Menu(anchor, _history.History.Entries.Single())).Single(b => b.Content as string == "Delete").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        await _history.History.IdleAsync();
        Assert.Empty(_history.History.Entries);
    }

    [Fact]
    public async Task Settings_history_pause_and_empty_rows_live_in_their_cards()
    {
        await StartHistoryAsync();
        _history.SetRecording(true);
        WpfTestThread.Run(() =>
        {
            var page = new ClipboardHistorySettingsPage(_history);
            Assert.True(Shows(page, "No apps"));
            Assert.True(Shows(page, "No items"));
            Assert.Contains(Find<Button>(page), b => System.Windows.Automation.AutomationProperties.GetName(b) == "Add app");
            Find<Button>(page).Single(b => b.Content as string == "Pause").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(_history.Settings.Paused);
            Assert.Contains(Find<Button>(new ClipboardHistorySettingsPage(_history)), b => b.Content as string == "Resume");
        });
    }

    [Fact]
    public async Task Settings_clean_url_cleans_as_you_type_copies_and_never_says_removed()
    {
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var page = new CleanUrlSettingsPage(_clean);
            var link = Find<TextBox>(page).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Link");
            var copy = Find<Button>(page).Single(b => b.Content as string == "Copy");
            Assert.False(copy.IsEnabled);
            link.Text = "https://example.org/p?utm_source=news&item=7#part";
            Assert.True(Shows(page, "https://example.org/p?item=7#part"));
            Assert.DoesNotContain(Find<TextBlock>(page), t => t.Text.StartsWith("Removed", StringComparison.Ordinal)); // Travis, task 65
            Assert.True(copy.IsEnabled);
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Flush(page);
            Assert.Equal("https://example.org/p?item=7#part", Assert.Single(_hub.Written).Text);
            Assert.Equal("Copied", copy.Content);
            link.Text = "https://example.org/clean";
            Assert.True(Shows(page, "Nothing to remove"));
            link.Text = "not a link";
            Assert.True(Shows(page, "Not a web link"));
            Assert.False(copy.IsEnabled);
        });
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
