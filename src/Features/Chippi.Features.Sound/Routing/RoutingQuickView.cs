// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.Audio;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Routing;

/// <summary>
/// Routing's sections of the Sound page (DESIGN.md §5 Island). Output: the device in use with the
/// system volume and mute, then every connected output. Microphone: every connected mic. A pick
/// always works and moves the device to the top of its priority list; unplugged devices are only in
/// Settings.
/// </summary>
public sealed class RoutingQuickView : UserControl
{
    private readonly AudioRoutingModule _module;
    private readonly AudioFlow _flow;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _error = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
    private readonly TextBlock _output = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly IconButton _mute = new() { Width = 28, Height = 28 };
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new() { FontSize = 12, Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private string _shape = "";
    private bool _refreshing;

    public RoutingQuickView(AudioRoutingModule module, AudioFlow flow)
    {
        _module = module;
        _flow = flow;
        var root = new StackPanel();
        if (flow == AudioFlow.Render)
            root.Children.Add(OutputCard());
        root.Children.Add(_rows);
        _error.Margin = new Thickness(2, 6, 0, 0);
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        root.Children.Add(_error);
        Content = root;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    /// <summary>The output in use, its mute and the system volume, which sets that output.</summary>
    private Border OutputCard()
    {
        var glyph = new LucideIcon { Data = LucideIcons.Speaker, Size = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandAccentBrush");
        _mute.Click += (_, _) =>
        {
            if (_module.InUse(AudioFlow.Render) is { } id)
                Run(_module.SetOutputMutedAsync(id, !Level(id).Muted));
        };
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(_mute, Dock.Right);
        var top = new DockPanel { Children = { glyph, _mute, _output } };

        _volume.SetResourceReference(StyleProperty, "HopperSliderStyle");
        AutomationProperties.SetName(_volume, "System volume");
        _volume.ValueChanged += (_, e) =>
        {
            ShowPercent();
            if (!_refreshing && _module.InUse(AudioFlow.Render) is { } id)
                Run(_module.SetOutputVolumeAsync(id, (float)e.NewValue / 100));
        };
        _percent.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        DockPanel.SetDock(_percent, Dock.Right);
        var levels = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Children = { _percent, _volume } };

        var card = new Border { Child = new StackPanel { Children = { top, levels } }, Margin = new Thickness(0, 0, 0, 6) };
        card.SetResourceReference(StyleProperty, "IslandCardStyle");
        return card;
    }

    private (float Volume, bool Muted) Level(string id) =>
        _module.Snapshot.Levels.TryGetValue(id, out var level) ? (level.Volume, level.Muted) : (0, false);

    private void ShowPercent() =>
        _percent.Text = _module.InUse(AudioFlow.Render) is { } id && Level(id).Muted ? "Muted" : $"{Math.Round(_volume.Value).ToString(CultureInfo.CurrentCulture)}%";

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _refreshing = true;
        try
        {
            var inUse = _module.InUse(_flow);
            var connected = _module.Settings.For(_flow).Devices.Where(e => _module.Snapshot.Find(e.Id)?.IsActive == true).ToList();
            var shape = string.Join("|", connected.Select(e => e.Id + e.Name)) + ":" + inUse;
            if (shape != _shape)
            {
                _shape = shape;
                _rows.Children.Clear();
                foreach (var entry in connected)
                    _rows.Children.Add(Row(entry, string.Equals(entry.Id, inUse, StringComparison.OrdinalIgnoreCase)));
                if (connected.Count == 0)
                {
                    var none = RoutingUi.Text("None connected", "CaptionTextStyle", "IslandTextTertiaryBrush");
                    none.Margin = new Thickness(2, 4, 0, 4);
                    _rows.Children.Add(none);
                }
            }
            if (_flow == AudioFlow.Render)
            {
                _output.Text = _module.CurrentName(AudioFlow.Render);
                bool known = inUse is not null && _module.Snapshot.Levels.ContainsKey(inUse);
                var (volume, muted) = inUse is null ? (0f, false) : Level(inUse);
                _volume.IsEnabled = _mute.IsEnabled = known;
                if (!_volume.IsMouseCaptureWithin && !_volume.IsKeyboardFocusWithin)
                    _volume.Value = volume * 100;
                ShowPercent();
                _mute.Icon = muted ? LucideIcons.VolumeMuted : LucideIcons.Volume;
                _mute.ToolTip = muted ? "Unmute" : "Mute";
                AutomationProperties.SetName(_mute, muted ? "Unmute system volume" : "Mute system volume");
            }
            _error.Text = _module.Error ?? "";
            _error.Visibility = _error.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
    }

    /// <summary>A connected device: a radio, its name and state. Picking it makes it the one in use.</summary>
    private Button Row(PriorityEntry entry, bool selected)
    {
        var radio = new Ellipse
        {
            Width = 16, Height = 16, StrokeThickness = selected ? 5 : 1, Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center, Fill = selected ? Brushes.Black : Brushes.Transparent,
        };
        radio.SetResourceReference(Shape.StrokeProperty, selected ? "IslandAccentBrush" : "IslandTextTertiaryBrush");
        var name = new TextBlock { Text = entry.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        var state = RoutingUi.Text(_module.Status(_flow, entry), "CaptionTextStyle", "IslandTextTertiaryBrush");
        DockPanel.SetDock(radio, Dock.Left);
        var content = new DockPanel { Children = { radio, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, state } } } };
        var button = new Button { Content = content, ToolTip = entry.Name, Tag = selected ? "Selected" : null };
        button.SetResourceReference(StyleProperty, "IslandRowButtonStyle");
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
