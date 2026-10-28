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
        Assert.Equal(ClipboardFeatures.PageWidth, tile.PageWidth);
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
    public async Task Without_history_the_page_is_clean_url_and_cleans_as_you_type()
    {
        await StartCleanAsync();
        WpfTestThread.Run(() =>
        {
            var section = (FrameworkElement)_cleanUi.Spec!.CreateSection();
            Load(section);
            Assert.Equal(Visibility.Visible, section.Visibility);
            var field = Find<TextBox>(section).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Link");
            field.Text = "https://youtu.be/abc?si=share&t=30";
            Assert.True(Shows(section, "https://youtu.be/abc?t=30"));
            Assert.True(Shows(section, "Removed · si"));
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
