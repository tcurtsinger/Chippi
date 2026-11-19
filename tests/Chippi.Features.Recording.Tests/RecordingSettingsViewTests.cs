// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.ScreenText;
using Chippi.Features.ScreenText.Views;
using Chippi.Island.Tests;
using Chippi.Media;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Recording.Tests;

/// <summary>
/// Settings › Screen recording, offscreen on fixtures: nothing shows a window or records. With CHIPPI_ISLAND_SHOTS naming a
/// folder, the page is saved next to Settings › Text and QR, a page built the standard way (dark, or light with
/// CHIPPI_SHOTS_THEME=light).
/// </summary>
public sealed class RecordingSettingsViewTests
{
    [Fact]
    public void Render_page_next_to_a_standard_page_offscreen()
    {
        string? output = Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        string theme = WpfTestThread.ShotsTheme.ToLowerInvariant();
        WithPage((view, _, _, _) =>
        {
            Snapshot(Page("Screen recording", view), Path.Combine(output, $"recording-settings-{theme}.png"));
            Snapshot(Page("Text and QR", new ScreenTextSettingsPage(new TextModel())), Path.Combine(output, $"reference-text-and-qr-{theme}.png"));
        }, defaultFolder: true);
    }

    [Fact]
    public void Rows_show_and_save_each_setting() => WithPage((view, model, settings, _) =>
    {
        Assert.Equal(["Up to 1080p", "Original size"], view.Resolution.Items.Cast<string>());
        Assert.Equal(["30 fps", "60 fps"], view.FrameRate.Items.Cast<string>());
        Assert.Equal((0, 0, true, "On"), (view.Resolution.SelectedIndex, view.FrameRate.SelectedIndex, view.IncludeCursor.IsChecked, view.IncludeCursor.Content));

        view.Resolution.SelectedIndex = 1;
        view.FrameRate.SelectedIndex = 1;
        view.IncludeCursor.IsChecked = false;
        Assert.Equal((true, 60, false), (settings.Value.OriginalSize, settings.Value.FrameRate, settings.Value.IncludeCursor));
        Assert.Equal("Off", view.IncludeCursor.Content);

        string before = model.SaveFolder, chosen = Path.Combine(before, "sub");
        string? offered = null;
        view.PickFolder = current => { offered = current; return chosen; };
        view.ChangeFolder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal((before, chosen), (offered, settings.Value.SaveFolder));
        Assert.Equal(chosen, view.Folder.ToolTip);
        Assert.Equal(Visibility.Collapsed, view.Error.Visibility);
    });

    [Fact]
    public void A_refused_save_puts_the_row_back_and_says_so() => WithPage((view, _, settings, _) =>
    {
        settings.Refuse = true;
        view.IncludeCursor.IsChecked = false;
        Assert.Equal((true, "On"), (view.IncludeCursor.IsChecked, view.IncludeCursor.Content));
        Assert.Equal(("Save failed", Visibility.Visible), (view.Error.Text, view.Error.Visibility));
        view.PickFolder = _ => null;
        view.ChangeFolder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Visible, view.Error.Visibility);
    });

    [Fact]
    public void Shortcuts_are_read_only_rows_with_edit() => WithPage((view, _, _, fixture) =>
    {
        Assert.Equal("Ctrl+Alt+R", view.ShortcutOf("Record screen").KeysText);
        Assert.Equal("Not set", view.ShortcutOf("Stop recording").KeysText);
        view.ShortcutOf("Stop recording").EditButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["Stop recording"], fixture.Edited);
    });

    [Fact]
    public void A_shown_page_follows_changes_made_elsewhere_until_it_closes() => WithPage((view, model, _, fixture) =>
    {
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        fixture.Keys["Stop recording"].Register(new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x53), () => { });
        Assert.Equal("Ctrl+Alt+S", view.ShortcutOf("Stop recording").KeysText);
        Assert.True(model.SaveSettings(model.Settings with { FrameRate = 60 })); // the island's picker
        Assert.Equal(1, view.FrameRate.SelectedIndex);

        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.True(model.SaveSettings(model.Settings with { FrameRate = 30 }));
        fixture.Keys["Stop recording"].Unregister();
        Assert.Equal((1, "Ctrl+Alt+S"), (view.FrameRate.SelectedIndex, view.ShortcutOf("Stop recording").KeysText));
    });

    [Fact]
    public void Folders_read_short() =>
        Assert.Equal(@"Videos\Chippi", RecordingSettingsView.Display(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Chippi")));

    internal static void WithPage(Action<RecordingSettingsView, RecordingModel, Settings, Fixture> test, bool defaultFolder = false) => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-record-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var settings = new Settings { Value = new() { SaveFolder = defaultFolder ? null : folder } };
        var shortcuts = new ShortcutService(new Registrar());
        var edited = new List<string>();
        var keys = new Dictionary<string, IHotkey>();
        var context = new ModuleContext(RecordingModule.Id, settings, NullLogger.Instance, null!, null!) { Shortcuts = shortcuts, EditShortcut = edited.Add };
        foreach (string name in RecordingModule.ShortcutNames)
        {
            var key = keys[name] = shortcuts.Add(RecordingModule.Id, name);
            if (name == "Record screen") key.Register(new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x52), () => { });
        }
        using var model = new RecordingModel(context, new NoBackend(), new NoStore());
        try { test(new RecordingSettingsView(model, context), model, settings, new(edited, keys)); }
        finally { WpfTestThread.Wait(model.ShutdownAsync()); Directory.Delete(folder, true); }
    });

    /// <summary>The Settings window's page: its title (PageTitleStyle is DisplayTextStyle) over the feature's view, 40 in from each side.</summary>
    private static Border Page(string title, FrameworkElement view)
    {
        var heading = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 14) };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "DisplayTextStyle");
        var shell = new Border { Padding = new Thickness(40, 4, 40, 28), Child = new StackPanel { MaxWidth = 1000, Children = { heading, view } } };
        shell.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        return shell;
    }

    private static void Snapshot(FrameworkElement element, string path)
    {
        var size = new Size(1080, 640);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)size.Width * 2, (int)size.Height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    internal sealed record Fixture(List<string> Edited, Dictionary<string, IHotkey> Keys);

    internal sealed class Settings : IModuleSettings
    {
        public RecordingSettings Value = new();
        public bool Refuse { get; set; }
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class
        {
            if (Refuse) throw new IOException("Fixture refuses");
            Value = (RecordingSettings)(object)value;
        }
    }

    private sealed class Registrar : IHotkeyRegistrar
    {
        public bool Register(int id, KeyShortcut shortcut) => true;
        public void Unregister(int id) { }
        public event Action<int>? Pressed { add { } remove { } }
    }

    private sealed class NoStore : IRecordingStore
    {
        public IReadOnlyList<RecordingEntry> Read() => [];
        public void Add(RecordingEntry entry, byte[] poster) { }
        public byte[]? Poster(Guid id) => null;
        public void Dispose() { }
    }

    private sealed class NoBackend : IRecordingBackend
    {
        public IReadOnlyList<RecordingSource> Sources(RecordingTarget target) => [];
        public RecordingDeviceNames Devices() => new("Fixture output", "Fixture input");
        public Task<IRecordingSession> StartAsync(RecordingSource source, RecordingOptions options, string partialPath, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TextModel : IScreenTextSettingsModel
    {
        public IReadOnlyList<OcrLanguage> Languages { get; } = [new("en-US", "English (United States)")];
        public string? Language => "en-US";
        public bool KeepLineBreaks => true;
        public string? SettingsError => null;
        public event Action? Changed { add { } remove { } }
        public (KeyShortcut? Keys, string? Error) Shortcut(string name) => name == ScreenTextShortcuts.All[0] ? (new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x54), null) : (null, null);
        public bool SetLanguage(string tag) => true;
        public bool SetKeepLineBreaks(bool on) => true;
        public void OpenLanguageSettings() { }
        public void EditShortcut(string name) { }
    }
}
