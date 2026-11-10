// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.MediaTools;
using Chippi.Island.Tests;
using Chippi.Media;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.MediaTools.Tests;

public sealed class MediaToolsModuleTests
{
    [Fact]
    public void Module_registers_one_capture_result_page_three_shared_shortcuts_and_commands() => WithModule((module, context, ui, _) =>
    {
        Assert.False(ui.Tile!.Spec.OnHome); Assert.Equal(MediaToolsModule.Page, ui.Tile.Spec.Key);
        Assert.Equal(MediaToolsModule.ShortcutNames, context.Shortcuts.List.Select(s => s.Name));
        Assert.Equal(3, ui.Commands!().Count); Assert.NotNull(ui.Settings);
        Assert.NotNull(ui.Tile.Spec.InitialFocus); Assert.NotNull(ui.Tile.Spec.HandleBack);
    });

    [Fact]
    public void Saving_unchanged_options_preserves_results_and_failed_writes_do_not_change_settings() => WithModule((module, _, _, settings) =>
    {
        int changes = 0; module.Batch!.Changed += () => changes++;
        Assert.True(module.SaveOptions(module.Settings.Options)); Assert.Equal(0, changes);
        var original = module.Settings; settings.Fail = true;
        Assert.False(module.SaveOptions(module.Settings.Options with { Quality = MediaQuality.High }));
        Assert.Same(original, module.Settings); Assert.Equal(0, changes);
    });

    [Theory]
    [InlineData(MediaTool.Convert)]
    [InlineData(MediaTool.Resize)]
    [InlineData(MediaTool.Compress)]
    public void Twelve_files_page_five_at_a_time_and_the_top_options_follow_the_selected_tool(MediaTool tool) => WithModule((module, _, ui, _) =>
    {
        module.Open(tool);
        WpfTestThread.Wait(module.AddAsync(Enumerable.Range(1, 12).Select(i => Path.Combine(Path.GetTempPath(), $"fixture-{i}.png"))));
        var view = Assert.IsType<MediaToolsView>(ui.Tile!.Spec.CreateSection());
        Arrange(view, new(496, 296)); Arrange(view.Header, new(496, 32));
        Assert.Equal(5, Descendants<Button>(view).Count(b => AutomationProperties.GetName(b).StartsWith("Remove ", StringComparison.Ordinal)));
        Assert.Contains(Descendants<Button>(view), b => Equals(b.Content, "Next"));
        Assert.All(Descendants<ComboBox>(view), combo => Assert.True(combo.ActualWidth >= 70));
        Assert.True(view.DesiredSize.Height <= 296);
        foreach (var button in Descendants<Button>(view).Where(b => AutomationProperties.GetName(b).StartsWith("Remove ", StringComparison.Ordinal)))
            Assert.True(button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize)).Bottom <= view.ActualHeight);
        var next = Descendants<Button>(view).Single(b => Equals(b.Content, "Next")); next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == " 2 / 3 ");
    });

    [Fact]
    public void Tabs_share_Audio_style_and_keep_the_control_when_selection_changes() => WithModule((module, _, ui, settings) =>
    {
        var view = Assert.IsType<MediaToolsView>(ui.Tile!.Spec.CreateSection());
        view.Header.Resources.MergedDictionaries.Add(new() { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
        Arrange(view.Header, new(496, 32));
        var tabs = Assert.Single(Descendants<IslandTabs>(view.Header));
        Assert.Same(view.Header.FindResource("IslandAudioTabsStyle"), tabs.Style);
        Assert.Equal(32, ui.Tile.Spec.HeaderHeight);
        tabs.SelectedIndex = 1; Assert.Equal(MediaTool.Resize, module.Settings.Options.Tool);
        Assert.Same(tabs, Assert.Single(Descendants<IslandTabs>(view.Header)));
        tabs.SelectedIndex = 2; Assert.Equal(MediaTool.Compress, module.Settings.Options.Tool);
        settings.Fail = true; tabs.SelectedIndex = 0;
        Assert.Equal(2, tabs.SelectedIndex); Assert.Equal(MediaTool.Compress, module.Settings.Options.Tool);
    });

    [Theory]
    [InlineData("#123ABC", true)]
    [InlineData("ff00ff", true)]
    [InlineData("#fff", false)]
    [InlineData("#ffffff00", false)]
    public void Background_colors_require_six_hex_digits(string text, bool valid) => Assert.Equal(valid, MediaToolsView.TryColor(text, out _));

    [Fact]
    public void Custom_percent_and_lossless_formats_describe_the_options_they_use() => WithModule((module, _, ui, _) =>
    {
        module.SaveOptions(module.Settings.Options with { Tool = MediaTool.Resize, Percent = 33 });
        var view = Assert.IsType<MediaToolsView>(ui.Tile!.Spec.CreateSection()); Arrange(view, new(496, 304));
        Assert.Contains(Descendants<ComboBox>(view), c => Equals(c.SelectedItem, "Custom"));
        Assert.Contains(Descendants<TextBox>(view), c => c.Text == "33");
        module.SaveOptions(module.Settings.Options with { Tool = MediaTool.Convert, Format = MediaOutputFormat.Png }); Arrange(view, new(496, 304));
        Assert.Contains(Descendants<TextBlock>(view), c => c.Text == "Lossless"); Assert.Single(Descendants<ComboBox>(view));
    });

    [Fact]
    public void Render_approved_layout_in_dark_and_light_without_showing_a_window()
    {
        var codec = new Codec();
        WithModule((module, _, ui, _) =>
        {
        string? output = Environment.GetEnvironmentVariable("CHIPPI_MEDIA_TOOLS_SHOTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var themes = Application.Current.Resources.MergedDictionaries;
        var old = themes.ToArray();
        try
        {
            foreach (string theme in new[] { "Dark", "Light" })
            {
                themes.Clear();
                foreach (string resource in new[] { $"Themes/{theme}.xaml", "Themes/Palette.xaml", "Themes/SharedStyles.xaml", "Controls/Controls.xaml" })
                    themes.Add(new() { Source = new Uri("pack://application:,,,/Chippi.UI;component/" + resource) });
                themes.Add(new() { Source = new Uri("pack://application:,,,/Chippi;component/Island/IslandStyles.xaml") });
                Save("empty");
                WpfTestThread.Wait(module.AddAsync(Enumerable.Range(1, 12).Select(i => Path.Combine(Path.GetTempPath(), $"fixture-long-name-{i}-example.png"))));
                foreach (var tool in Enum.GetValues<MediaTool>()) { module.Open(tool); Save(tool.ToString().ToLowerInvariant()); }
                foreach (var job in module.Batch!.Jobs.ToArray()) module.Batch.Remove(job);
                string fixtures = Path.Combine(Path.GetTempPath(), "chippi-convert-story-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtures);
                try
                {
                    var picture = BitmapSource.Create(64, 32, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 64 * 32 * 4).ToArray(), 64 * 4);
                    foreach (string name in new[] { "photo.png", "failed.png", "image.png" })
                    {
                        var imageEncoder = new PngBitmapEncoder(); imageEncoder.Frames.Add(BitmapFrame.Create(picture)); using var file = File.Create(Path.Combine(fixtures, name)); imageEncoder.Save(file);
                    }
                    module.Open(MediaTool.Convert); WpfTestThread.Wait(module.AddAsync(Directory.GetFiles(fixtures)));
                    codec.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); codec.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); codec.FailName = "failed.png";
                    var run = module.Batch.RunAsync(module.Settings.Options); WpfTestThread.Wait(codec.Entered.Task); Save("running");
                    codec.Gate.SetResult(); WpfTestThread.Wait(run); Save("results");
                    var view = Assert.IsType<MediaToolsView>(ui.Tile!.Spec.CreateSection());
                    WpfTestThread.Wait(view.CompareAsync(module.Batch.Jobs.First(j => j.State == MediaJobState.Done))); Save("compare");
                    Assert.True(view.Back()); codec.Gate = null; codec.Entered = null;
                    var settingsView = Assert.IsAssignableFrom<FrameworkElement>(ui.Settings!(null));
                    settingsView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    Arrange(settingsView, new(800, 600));
                    var settingsBitmap = new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32); settingsBitmap.Render(settingsView);
                    var settingsEncoder = new PngBitmapEncoder(); settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
                    using (var settingsFile = File.Create(Path.Combine(output, $"{theme.ToLowerInvariant()}-settings.png"))) settingsEncoder.Save(settingsFile);
                    settingsView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                }
                finally { Directory.Delete(fixtures, true); foreach (var job in module.Batch.Jobs.ToArray()) module.Batch.Remove(job); }
                void Save(string state)
                {
                    var view = Assert.IsType<MediaToolsView>(ui.Tile!.Spec.CreateSection());
                    var shell = new Grid { Width = 520, Height = 360 };
                    shell.SetResourceReference(Panel.BackgroundProperty, "IslandBrush");
                    var content = new Grid { Margin = new(12) }; content.RowDefinitions.Add(new() { Height = new(ui.Tile!.Spec.HeaderHeight) }); content.RowDefinitions.Add(new() { Height = new(8) }); content.RowDefinitions.Add(new());
                    var header = view.Header; if (VisualTreeHelper.GetParent(header) is Panel oldHeader) oldHeader.Children.Remove(header);
                    if (VisualTreeHelper.GetParent(view) is Panel oldView) oldView.Children.Remove(view);
                    content.Children.Add(header); Grid.SetRow(view, 2); content.Children.Add(view); shell.Children.Add(content);
                    Arrange(shell, new(520, 360));
                    if (state == "compare") Assert.Equal(2, Descendants<Image>(view).Count(i => i.Source != null));
                    var bitmap = new RenderTargetBitmap(1040, 720, 192, 192, PixelFormats.Pbgra32); bitmap.Render(shell);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"{theme.ToLowerInvariant()}-{state}.png")); encoder.Save(file);
                    content.Children.Remove(view); content.Children.Remove(header);
                }
            }
        }
        finally { themes.Clear(); foreach (var theme in old) themes.Add(theme); }
        }, codec);
    }

    private static void Arrange(FrameworkElement element, Size size) { element.Measure(size); element.Arrange(new Rect(size)); element.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void WithModule(Action<MediaToolsModule, ModuleContext, Ui, Settings> action, Codec? codec = null) => WpfTestThread.Run(() =>
    {
        var settings = new Settings(); var ui = new Ui(); var module = new MediaToolsModule(codec ?? new Codec());
        var context = new ModuleContext(MediaToolsModule.Id, settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher(MediaToolsModule.Id), ui)
        { TryOpenIslandPage = _ => true };
        WpfTestThread.Wait(module.StartAsync(context, default));
        try { action(module, context, ui, settings); }
        finally { WpfTestThread.Wait(module.StopAsync(default)); }
    });
    private sealed class Settings : IModuleSettings
    {
        private object _value = new MediaToolsSettings(); public bool Fail { get; set; }
        public T Get<T>() where T : class, new() => (T)_value;
        public void Save<T>(T value) where T : class { if (Fail) throw new IOException("Fixture"); _value = value; }
    }
    private sealed class Ui : IModuleUi
    {
        public IslandTileContribution? Tile { get; private set; }
        public Func<IReadOnlyList<ChippiCommand>>? Commands { get; private set; }
        public Func<string?, object>? Settings { get; private set; }
        public IIslandTile AddIslandTile(IslandTileSpec spec) => Tile = new(MediaToolsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> createView) => Settings = createView;
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
    }
    private sealed class Codec : IMediaToolsCodec
    {
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource? Entered { get; set; }
        public string? FailName { get; set; }
        public Task<MediaFileInfo> InspectAsync(string path, CancellationToken cancellationToken) => Task.FromResult(new MediaFileInfo(path, 4_800_000, 1920, 1080, false, TimeSpan.Zero, HasAlpha: true));
        public async Task ExportAsync(MediaFileInfo file, string destination, MediaToolOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            Entered?.TrySetResult(); if (Gate != null) await Gate.Task.WaitAsync(cancellationToken);
            if (file.Name == FailName) throw new IOException("Fixture cannot be decoded");
            if (File.Exists(file.Path)) File.Copy(file.Path, destination); else await File.WriteAllBytesAsync(destination, [1, 2, 3], cancellationToken);
        }
    }
}
