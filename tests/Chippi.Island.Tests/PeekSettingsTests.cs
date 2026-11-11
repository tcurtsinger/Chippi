// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Island;
using Chippi.App.Settings.Pages;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

public sealed class PeekSettingsTests
{
    [Fact]
    public void Peek_choices_include_Home_Audio_subpages_and_shared_Capture_without_duplicates()
    {
        var panels = IslandTileContribution.Group([
            Tile("capture", "Capture"), Tile("capture", "Capture"),
            Tile(IslandTool.AudioPages[0], "Devices"), Tile(IslandTool.AudioPages[1], "App volume"),
            Tile("media-tools", "Convert", home: false)]);
        var choices = PeekSettingsSection.Choices(panels);
        Assert.Equal((null, "Home"), choices[0]);
        Assert.Contains((IslandTool.Audio, "Audio"), choices);
        Assert.Contains((IslandTool.AudioPages[0], "Audio · Devices"), choices);
        Assert.Contains((IslandTool.AudioPages[1], "Audio · App volume"), choices);
        Assert.Contains(("media-tools", "Convert"), choices);
        Assert.Single(choices, c => c.Key == "capture");
        Assert.Single(choices, c => c.Key == IslandPanelWindow.NowPlayingKey);
    }

    [Fact]
    public void Picking_the_mode_and_default_panel_saves_only_the_Peek_choices() => WithSection(f =>
    {
        f.Config.SetSection(AppSettings.SectionName, new AppSettings { IslandLastPage = "clipboard", IslandAudioPage = IslandTool.AudioPages[0] });
        f.Section.Refresh();
        var panel = Named<ComboBox>(f.Section, "Default Peek panel");
        panel.SelectedItem = panel.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "capture"));
        var mode = Named<SegmentedControl>(f.Section, "Peek mode");
        mode.SelectedItem = mode.Items.OfType<SegmentItem>().Single(i => Equals(i.Value, PeekMode.Toggle));
        var saved = f.Config.GetSection<AppSettings>(AppSettings.SectionName);
        Assert.Equal("capture", saved.PeekPage); Assert.Equal(PeekMode.Toggle, saved.PeekMode);
        Assert.Equal("clipboard", saved.IslandLastPage); Assert.Equal(IslandTool.AudioPages[0], saved.IslandAudioPage);
        panel.SelectedItem = panel.Items.OfType<ComboBoxItem>().Single(i => i.Tag is null);
        Assert.Null(f.Config.GetSection<AppSettings>(AppSettings.SectionName).PeekPage);
    });

    [Fact]
    public void The_shortcut_stays_read_only_and_Edit_uses_the_shared_editor_callback() => WithSection(f =>
    {
        var shortcut = Assert.Single(All<ShortcutSetting>(f.Section));
        Assert.Equal("Not set", shortcut.KeysText);
        using var key = f.Shortcuts.Add("peek", "Peek");
        var keys = new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x50);
        Assert.True(key.Register(keys, () => { }));
        Assert.Equal(keys.ToString(), shortcut.KeysText);
        shortcut.EditButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, f.Edits); Assert.Empty(All<TextBox>(f.Section));
    });

    [Fact]
    public void An_uninstalled_destination_stays_saved_and_the_live_choices_refresh() => WithSection(f =>
    {
        f.Config.SetSection(AppSettings.SectionName, new AppSettings { PeekPage = "capture", PeekMode = PeekMode.Toggle });
        f.Panels.Clear(); f.Section.Refresh();
        var panel = Named<ComboBox>(f.Section, "Default Peek panel");
        var unavailable = Assert.IsType<ComboBoxItem>(panel.SelectedItem);
        Assert.Equal("Unavailable panel", unavailable.Content); Assert.False(unavailable.IsEnabled);
        Assert.Equal("capture", f.Config.GetSection<AppSettings>(AppSettings.SectionName).PeekPage);
        Assert.Contains(All<TextBlock>(f.Section), t => t.Text == "Panel unavailable · Peek opens Home" && t.Visibility == Visibility.Visible);
        f.Panels.AddRange(IslandTileContribution.Group([Tile("capture", "Capture")])); f.Section.Refresh();
        Assert.Equal("Capture", Assert.IsType<ComboBoxItem>(panel.SelectedItem).Content);
        f.Section.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        f.Config.SetSection(AppSettings.SectionName, new AppSettings { PeekMode = PeekMode.Hold });
        var mode = Named<SegmentedControl>(f.Section, "Peek mode");
        Assert.Equal(PeekMode.Toggle, Assert.IsType<SegmentItem>(mode.SelectedItem).Value);
        f.Section.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.Equal(PeekMode.Hold, Assert.IsType<SegmentItem>(mode.SelectedItem).Value);
    });

    [Fact]
    public void A_failed_settings_write_restores_the_saved_choice_and_explains_the_failure() => WithSection(f =>
    {
        Directory.CreateDirectory(f.Config.FilePath); // A directory cannot replace the fixture settings file.
        var mode = Named<SegmentedControl>(f.Section, "Peek mode");
        mode.SelectedItem = mode.Items.OfType<SegmentItem>().Single(i => Equals(i.Value, PeekMode.Toggle));
        Assert.Equal(PeekMode.Hold, f.Config.GetSection<AppSettings>(AppSettings.SectionName).PeekMode);
        Assert.Equal(PeekMode.Hold, Assert.IsType<SegmentItem>(mode.SelectedItem).Value);
        Assert.Contains(All<TextBlock>(f.Section), t => t.Text == "Peek settings save failed" && t.Visibility == Visibility.Visible);
    });

    [Fact]
    public void Render_Peek_settings_at_normal_and_narrow_widths_without_a_window() => WithSection(f =>
    {
        string? folder = Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var resources = Application.Current.Resources.MergedDictionaries; var previous = resources.ToArray();
        try
        {
            foreach (string theme in new[] { "Dark", "Light" })
            {
                resources.Clear();
                foreach (string path in new[] { $"Themes/{theme}.xaml", "Themes/Palette.xaml", "Themes/SharedStyles.xaml", "Controls/Controls.xaml" })
                    resources.Add(new() { Source = new Uri("pack://application:,,,/Chippi.UI;component/" + path) });
                foreach (int width in new[] { 800, 460 })
                {
                    var shell = new Border { Width = width, Padding = new Thickness(16), Child = f.Section };
                    shell.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
                    shell.Measure(new Size(width, double.PositiveInfinity)); shell.Arrange(new Rect(new Size(width, shell.DesiredSize.Height))); shell.UpdateLayout();
                    Assert.True(f.Section.DesiredSize.Width <= width - 32);
                    var bitmap = new RenderTargetBitmap(width * 2, (int)Math.Ceiling(shell.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32); bitmap.Render(shell);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(folder, $"peek-settings-{theme.ToLowerInvariant()}-{width}.png")); encoder.Save(file);
                    shell.Child = null;
                }
            }
        }
        finally { resources.Clear(); foreach (var resource in previous) resources.Add(resource); }
    });

    private static IslandTileContribution Tile(string key, string name, bool home = true) => new("fixture", new(key, name, "Settings", 1, 0, () => new Grid()) { OnHome = home });
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Assert.Single(All<T>(root), c => AutomationProperties.GetName(c) == name);
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var nested in All<T>(child)) yield return nested;
    }
    private static void WithSection(Action<Fixture> test) => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture(); test(fixture);
    });
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "chippi-peek-settings-" + Guid.NewGuid().ToString("N"));
        public ConfigStore Config { get; }
        public ShortcutService Shortcuts { get; } = new(new Registrar());
        public List<IslandTileGroup> Panels { get; } = [.. IslandTileContribution.Group([Tile("capture", "Capture"), Tile(IslandTool.AudioPages[0], "Devices")])];
        public PeekSettingsSection Section { get; }
        public int Edits { get; private set; }
        public Fixture()
        {
            Directory.CreateDirectory(_root); Config = new(Path.Combine(_root, "settings.json"));
            Section = new(Config, Shortcuts, () => Panels, () => Edits++);
            Section.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        }
        public void Dispose() { Section.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Directory.Delete(_root, true); }
    }
    private sealed class Registrar : IHotkeyRegistrar
    {
        public bool Register(int id, KeyShortcut shortcut) => true;
        public void Unregister(int id) { }
        public event Action<int>? Pressed { add { } remove { } }
    }
}
