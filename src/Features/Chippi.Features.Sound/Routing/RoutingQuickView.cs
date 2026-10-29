// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.Audio;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Routing;

/// <summary>
/// Routing's sections of the Sound page (DESIGN.md §5 Island, revised 2026-10-28). Output: the system
/// volume and mute, then the connected outputs. Microphone: the connected mics. Rows are one line; a
/// pick always works and moves the device to the top of its priority list. Rows that don't fit the
/// column, and unplugged devices, are only in Settings.
/// </summary>
public sealed class RoutingQuickView : UserControl
{
    /// <summary>The rows that fit under the Output card, and above the Mic processing card (fixed-size f1).</summary>
    internal const int OutputRows = 7, MicrophoneRows = 5;

    private readonly AudioRoutingModule _module;
    private readonly AudioFlow _flow;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _error = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
    private readonly LucideIcon _glyph = new();
    private readonly TextBlock _none = new() { Text = "No output", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _mute;
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new() { FontSize = 12, Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly DockPanel _levels = new();
    private string _shape = "";
    private bool _refreshing;

    public RoutingQuickView(AudioRoutingModule module, AudioFlow flow)
    {
        _module = module;
        _flow = flow;
        _mute = RoutingUi.IslandButton("Mute", () =>
        {
            if (_module.InUse(AudioFlow.Render) is { } id)
                Run(_module.SetOutputMutedAsync(id, !Level(id).Muted));
        });
        var root = new StackPanel();
        if (flow == AudioFlow.Render)
        {
            root.Children.Add(OutputCard());
            _rows.Margin = new Thickness(0, 8, 0, 0);
        }
        root.Children.Add(_rows);
        _error.Margin = new Thickness(10, 4, 0, 0);
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        root.Children.Add(_error);
        Content = root;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    /// <summary>One 44 row, like the mic card: the speaker, the system volume of the output in use, and Mute.</summary>
    private Border OutputCard()
    {
        _volume.SetResourceReference(StyleProperty, "HopperSliderStyle");
        AutomationProperties.SetName(_volume, "System volume");
        _volume.ValueChanged += (_, e) =>
        {
            ShowPercent();
            if (!_refreshing && _module.InUse(AudioFlow.Render) is { } id)
                Run(_module.SetOutputVolumeAsync(id, (float)e.NewValue / 100));
        };
        _percent.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        _percent.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(_percent, Dock.Right);
        _levels.Children.Add(_percent);
        _levels.Children.Add(_volume);
        _none.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        return RoutingUi.IslandCard(_glyph, new Grid { Children = { _levels, _none } }, _mute);
    }

    private (float Volume, bool Muted) Level(string id) =>
        _module.Snapshot.Levels.TryGetValue(id, out var level) ? (level.Volume, level.Muted) : (0, false);

    private bool OutputMuted() => _module.InUse(AudioFlow.Render) is { } id && Level(id).Muted;

    private void ShowPercent()
    {
        bool muted = OutputMuted();
        _percent.Text = muted ? "Muted" : $"{Math.Round(_volume.Value).ToString(CultureInfo.CurrentCulture)}%";
        _percent.SetResourceReference(TextBlock.ForegroundProperty, muted ? "WarningTextBrush" : "IslandTextTertiaryBrush");
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _refreshing = true;
        try
        {
            var inUse = _module.InUse(_flow);
            var connected = _module.Settings.For(_flow).Devices.Where(e => _module.Snapshot.Find(e.Id)?.IsActive == true).ToList();
            _error.Text = _module.Error ?? "";
            _error.Visibility = _error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            // Whole rows only: an error line takes one row's place.
            int fit = (_flow == AudioFlow.Render ? OutputRows : MicrophoneRows) - (_error.Text.Length == 0 ? 0 : 1);
            var shown = connected.Take(fit).ToList();
            var shape = string.Join("|", shown.Select(e => e.Id + e.Name)) + ":" + inUse;
            if (shape != _shape)
            {
                _shape = shape;
                _rows.Children.Clear();
                foreach (var entry in shown)
                    _rows.Children.Add(Row(entry, string.Equals(entry.Id, inUse, StringComparison.OrdinalIgnoreCase)));
            }
            _rows.Visibility = shown.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (_flow == AudioFlow.Render)
                RefreshOutput(inUse);
            else // the mic card above says when there's no microphone; nothing shows here then
                Visibility = shown.Count == 0 && _error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
    }

    private void RefreshOutput(string? inUse)
    {
        bool none = _module.Snapshot.Find(inUse ?? "") is null;
        bool known = inUse is not null && _module.Snapshot.Levels.ContainsKey(inUse);
        var (volume, muted) = inUse is null ? (0f, false) : Level(inUse);
        _levels.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _none.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        _volume.IsEnabled = _mute.IsEnabled = known && !none;
        if (!_volume.IsMouseCaptureWithin && !_volume.IsKeyboardFocusWithin)
            _volume.Value = volume * 100;
        ShowPercent();
        _glyph.Data = muted || none ? LucideIcons.VolumeMuted : LucideIcons.Speaker;
        _glyph.SetResourceReference(LucideIcon.BrushProperty, none ? "IslandTextTertiaryBrush" : muted ? "WarningBrush" : "IslandAccentBrush");
        _mute.Content = muted ? "Unmute" : "Mute";
        AutomationProperties.SetName(_mute, muted ? "Unmute system volume" : "Mute system volume");
    }

    /// <summary>A connected device on one line: a radio and its name. Picking it makes it the one in use.</summary>
    private Button Row(PriorityEntry entry, bool selected)
    {
        var radio = new Ellipse
        {
            Width = 14, Height = 14, StrokeThickness = selected ? 4 : 1, Margin = new Thickness(1, 0, 11, 0),
            VerticalAlignment = VerticalAlignment.Center, Fill = selected ? Brushes.Black : Brushes.Transparent,
        };
        radio.SetResourceReference(Shape.StrokeProperty, selected ? "IslandAccentBrush" : "IslandTextTertiaryBrush");
        var name = new TextBlock { Text = entry.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(radio, Dock.Left);
        var button = new Button { Content = new DockPanel { Children = { radio, name } }, ToolTip = entry.Name, Tag = selected ? "Selected" : null };
        button.SetResourceReference(StyleProperty, "IslandListRowStyle");
        AutomationProperties.SetName(button, selected ? $"{entry.Name}, in use" : $"Use {entry.Name}");
        button.Click += (_, _) =>
        {
            if (!selected)
                Run(_module.PreferAsync(_flow, entry.Id));
        };
        return button;
    }

    private async void Run(Task change)
    {
        try { await change; }
        catch { /* The module exposes the error to the view. */ }
        Refresh();
    }
}
