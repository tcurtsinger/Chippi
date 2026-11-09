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
using Chippi.Core.Modules;
using Chippi.Media;
using Microsoft.Win32;

namespace Chippi.Features.Recording;

/// <summary>The capture menu and explicit-play result share the island's fixed page.</summary>
public sealed class RecordingView : UserControl
{
    private readonly RecordingModel _model;
    private readonly ModuleContext _context;
    private readonly StackPanel _content = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 6, 0, 0) };
    private readonly TextBlock _activeTime = new() { FontSize = 28, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 14, 0, 14) };
    private MediaElement? _player;
    private string? _screen;
    private Guid? _selected;
    private bool _paused;
    public RecordingView(RecordingModel model, ModuleContext context)
    {
        _model = model; _context = context;
        SetResourceReference(ForegroundProperty, "IslandTextBrush");
        SetResourceReference(FontFamilyProperty, "UiFont"); FontSize = 12;
        var layout = new DockPanel(); DockPanel.SetDock(_message, Dock.Bottom); layout.Children.Add(_message); layout.Children.Add(_content); Content = layout;
        Loaded += (_, _) => { model.Changed += Refresh; _screen = null; Refresh(); };
        Unloaded += (_, _) => { model.Changed -= Refresh; _player?.Close(); _player = null; _screen = null; };
    }
    private void Refresh()
    {
        string screen = _model.Active ? "active" : _model.Selected != null ? "result" : "setup";
        _message.Text = _model.Exporting ? $"Exporting {_model.ExportProgress:0}%" : _model.Message ?? "";
        _activeTime.Text = _model.Phase switch { RecordingPhase.Countdown => $"Starting in {_model.Countdown}", RecordingPhase.Starting => "Starting…", RecordingPhase.Stopping => "Saving…", _ => RecordingModule.FormatTime(_model.Elapsed) };
        if (_screen == screen && _selected == _model.Selected?.Id && _paused == _model.IsPaused) return;
        _paused = _model.IsPaused;
        _player?.Close(); _player = null; _screen = screen; _selected = _model.Selected?.Id; _content.Children.Clear();
        if (screen == "setup") Setup(); else if (screen == "active") Active(); else Result();
    }
    private void Setup()
    {
        var targets = Row();
        targets.Children.Add(Button("Window", () => { _screen = null; _model.ChangeTarget(RecordingTarget.Window); }, _model.Target == RecordingTarget.Window));
        targets.Children.Add(Button("Screen", () => { _screen = null; _model.ChangeTarget(RecordingTarget.Screen); }, _model.Target == RecordingTarget.Screen));
        targets.Children.Add(Button("Refresh", () => { _screen = null; _model.RefreshSources(); })); _content.Children.Add(targets);
        if (_model.Target == RecordingTarget.Window || _model.Sources.Count != 1)
        {
            var sources = new ComboBox { ItemsSource = _model.Sources, DisplayMemberPath = nameof(RecordingSource.Name), SelectedItem = _model.Source, Margin = new(0, 8, 0, 8), MinHeight = 32 };
            sources.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
            AutomationProperties.SetName(sources, "Recording source"); sources.SelectionChanged += (_, _) => _model.Source = sources.SelectedItem as RecordingSource;
            _content.Children.Add(sources);
        }
        else _content.Children.Add(Label(_model.Source?.Name ?? "No displays available"));
        _content.Children.Add(AudioSwitch("Include system audio", _model.Devices.Output ?? "No default output device", _model.SystemAudio, value => _model.SystemAudio = value));
        _content.Children.Add(AudioSwitch("Include mic audio", _model.Devices.Input ?? "No default input device", _model.MicAudio, value => _model.MicAudio = value));
        var quality = Row();
        var size = new ComboBox { ItemsSource = new[] { "Up to 1080p", "Original size" }, SelectedIndex = _model.Settings.OriginalSize ? 1 : 0, Width = 170, Margin = new(0, 0, 8, 0) };
        AutomationProperties.SetName(size, "Recording resolution"); size.SelectionChanged += (_, _) => _model.SaveSettings(_model.Settings with { OriginalSize = size.SelectedIndex == 1 });
        var fps = new ComboBox { ItemsSource = new[] { "30 fps", "60 fps" }, SelectedIndex = _model.Settings.FrameRate == 60 ? 1 : 0, Width = 110 };
        size.SetResourceReference(StyleProperty, "HopperComboBoxStyle"); fps.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        AutomationProperties.SetName(fps, "Recording frame rate"); fps.SelectionChanged += (_, _) => _model.SaveSettings(_model.Settings with { FrameRate = fps.SelectedIndex == 1 ? 60 : 30 });
        quality.Children.Add(size); quality.Children.Add(fps); quality.Children.Add(Label("MP4")); _content.Children.Add(quality);
        var actions = Row(); actions.Margin = new(0, 8, 0, 0);
        var start = Button("Start recording", () => _ = _model.StartAsync(), true); start.IsEnabled = _model.Sources.Count > 0; actions.Children.Add(start);
        actions.Children.Add(Button("Settings", () => _context.OpenSettings(null)));
        _content.Children.Add(actions);
    }
    private void Active()
    {
        _content.Children.Add(Label(_model.Source?.Name ?? "")); _content.Children.Add(_activeTime);
        var controls = Row(); controls.HorizontalAlignment = HorizontalAlignment.Center;
        var pause = Button(_model.IsPaused ? "Resume" : "Pause", () => { _screen = null; _model.TogglePause(); }); pause.IsEnabled = _model.Phase is RecordingPhase.Recording or RecordingPhase.Paused;
        controls.Children.Add(pause); controls.Children.Add(Button(_model.Phase is RecordingPhase.Countdown or RecordingPhase.Starting ? "Cancel" : "Stop", () => _ = _model.StopAsync(), true));
        controls.Children.Add(Button("Discard", () =>
        {
            controls.IsEnabled = false;
            var confirm = Row(); confirm.Children.Add(Label("Discard recording?"));
            confirm.Children.Add(Button("Discard", () => _ = _model.DiscardAsync()));
            confirm.Children.Add(Button("Cancel", () => { _content.Children.Remove(confirm); controls.IsEnabled = true; }));
            _content.Children.Add(confirm);
        })); _content.Children.Add(controls);
        _content.Children.Add(Label("You can also pause or stop from the Chippi puck."));
    }
    private void Result()
    {
        if (_model.Selected is not { } entry) return;
        var preview = new Grid { Height = 124, Background = Brushes.Black };
        preview.Children.Add(new Image { Source = _model.Poster(entry), Stretch = Stretch.Uniform });
        var player = _player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        player.MediaFailed += (_, _) => _model.Report("The recording could not be played"); preview.Children.Add(player);
        _content.Children.Add(preview);
        var controls = Row();
        controls.Children.Add(Button("Play", () => { if (!RecordingModel.Available(entry)) { _model.Report("Recording file is missing or changed"); return; } player.Source ??= new Uri(entry.Path); player.Visibility = Visibility.Visible; player.Play(); }));
        controls.Children.Add(Button("Pause", player.Pause));
        controls.Children.Add(Button("Save as", () => SaveCopy(entry)));
        controls.Children.Add(Button("Open folder", () => OpenFolder(entry.Path)));
        _content.Children.Add(controls);
        var range = Row(); var from = new TextBox { Text = "0", Width = 62 }; var to = new TextBox { Text = entry.Duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), Width = 72 };
        AutomationProperties.SetName(from, "Range start in seconds"); AutomationProperties.SetName(to, "Range end in seconds");
        from.SetResourceReference(StyleProperty, "TextFieldStyle"); to.SetResourceReference(StyleProperty, "TextFieldStyle");
        range.Children.Add(Label("Range (seconds)")); range.Children.Add(from); range.Children.Add(Label("to")); range.Children.Add(to);
        _content.Children.Add(range);
        var trimRange = new RecordingRange(entry.Duration.TotalSeconds);
        trimRange.Changed += () => { from.Text = trimRange.Start.ToString("0.###", CultureInfo.InvariantCulture); to.Text = trimRange.End.ToString("0.###", CultureInfo.InvariantCulture); };
        void UpdateRange()
        {
            if (double.TryParse(from.Text, CultureInfo.InvariantCulture, out double a) && double.TryParse(to.Text, CultureInfo.InvariantCulture, out double b) && double.IsFinite(a) && double.IsFinite(b) && b > a && a >= 0 && b <= entry.Duration.TotalSeconds) trimRange.Set(a, b);
        }
        from.LostKeyboardFocus += (_, _) => UpdateRange(); to.LostKeyboardFocus += (_, _) => UpdateRange(); _content.Children.Add(trimRange);
        var exports = Row();
        void Export(bool gif)
        {
            if (_model.Exporting) return;
            if (!double.TryParse(from.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) || !double.TryParse(to.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double b) || !double.IsFinite(a) || !double.IsFinite(b) || a < 0 || b <= a || b > entry.Duration.TotalSeconds)
            { _model.Report("Choose a valid start and end in seconds"); return; }
            if (gif && b - a > 60) { _model.Report("Choose 60 seconds or less for GIF"); return; }
            using var hold = _context.HoldIslandOpen();
            var dialog = new SaveFileDialog { Filter = gif ? "GIF animation|*.gif" : "MP4 video|*.mp4", FileName = Path.GetFileNameWithoutExtension(entry.Path) + (gif ? ".gif" : "-trim.mp4"), AddExtension = true };
            if (dialog.ShowDialog() == true) _ = _model.ExportAsync(dialog.FileName, TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b), gif);
        }
        exports.Children.Add(Button("Trim copy", () => Export(false))); exports.Children.Add(Button("Export GIF", () => Export(true))); exports.Children.Add(Button("Cancel export", _model.CancelExport));
        exports.Children.Add(Button("New", () => { if (!_model.Exporting) _model.Setup(_model.Target); })); _content.Children.Add(exports);
    }
    private void SaveCopy(RecordingEntry entry)
    {
        using var hold = _context.HoldIslandOpen();
        var dialog = new SaveFileDialog { Filter = "MP4 video|*.mp4", FileName = Path.GetFileName(entry.Path), AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        _ = _model.SaveCopyAsync(dialog.FileName);
    }
    private void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { "/select,", path } }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { _model.Report("Open folder failed"); }
    }
    internal static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Margin = new(0, 4, 0, 4) };
    private static FrameworkElement AudioSwitch(string name, string device, bool enabled, Action<bool> changed)
    {
        var row = new Grid { Margin = new(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var labels = new StackPanel(); labels.Children.Add(Label(name));
        var caption = Label(device); caption.FontSize = 10; caption.Margin = new(0); caption.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush"); labels.Children.Add(caption); row.Children.Add(labels);
        var toggle = new ToggleButton { IsChecked = enabled, VerticalAlignment = VerticalAlignment.Center, ToolTip = device };
        toggle.SetResourceReference(StyleProperty, "IslandGallerySwitchStyle"); AutomationProperties.SetName(toggle, name); toggle.Click += (_, _) => changed(toggle.IsChecked == true);
        Grid.SetColumn(toggle, 1); row.Children.Add(toggle); return row;
    }
    internal static TextBlock Label(string text) => new() { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 3, 8, 3), FontSize = 12 };
    internal static Button Button(string text, Action action, bool accent = false)
    {
        var button = new Button { Content = text, Margin = new(0, 0, 6, 0), Padding = new(10, 5, 10, 5), MinHeight = 30 };
        button.SetResourceReference(StyleProperty, accent ? "IslandAccentButtonStyle" : "IslandButtonStyle"); button.Click += (_, _) => action(); return button;
    }
}
