// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.UI.Controls;
using Chippi.Audio;

namespace Chippi.Features.Sound.Routing;

/// <summary>Routing's flyout section, or its part of one shared island page.</summary>
public sealed class RoutingQuickView : UserControl
{
    private readonly AudioRoutingModule _module;
    private readonly AudioFlow? _flow;
    private readonly bool _island;
    private readonly StackPanel _root = new();
    private readonly Dictionary<AudioFlow, ComboBox> _choices = [];
    private readonly TextBlock _error = RoutingUi.Text("", "CaptionTextStyle", "WarningTextBrush");
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, Width = 180 };
    private readonly TextBlock _percent = RoutingUi.Text("", "NumberTextStyle", "TextSecondaryBrush");
    private bool _refreshing;
    private string _choiceKey = "";

    public RoutingQuickView(AudioRoutingModule module, AudioFlow? flow = null, bool island = false)
    {
        _module = module;
        _flow = flow;
        _island = island;
        if (island) Content = _root;
        if (!island)
        {
            BuildFlyout();
            var card = RoutingUi.Card(_root);
            card.SetResourceReference(StyleProperty, "FlyoutCardStyle");
            var panel = new StackPanel();
            var heading = RoutingUi.Text("Sound", "HeadlineTextStyle");
            heading.Margin = new Thickness(4, 0, 0, 8);
            panel.Children.Add(heading);
            panel.Children.Add(card);
            Content = panel;
        }
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    private void BuildFlyout()
    {
        foreach (var flow in new[] { AudioFlow.Render, AudioFlow.Capture })
        {
            var combo = new ComboBox { MinWidth = 180, MaxWidth = 240, DisplayMemberPath = nameof(PriorityEntry.Name), SelectedValuePath = nameof(PriorityEntry.Id) };
            combo.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
            AutomationProperties.SetName(combo, flow == AudioFlow.Render ? "Preferred output" : "Preferred microphone");
            combo.SelectionChanged += (_, _) =>
            {
                if (!_refreshing && combo.SelectedItem is PriorityEntry entry) Run(_module.PreferAsync(flow, entry.Id));
            };
            _choices[flow] = combo;
            var grid = RoutingUi.Columns(GridLength.Auto, new(1, GridUnitType.Star));
            var label = RoutingUi.Text(flow == AudioFlow.Render ? "Output" : "Microphone");
            label.Margin = new Thickness(0, 0, 12, 0);
            RoutingUi.Add(grid, label, 0);
            RoutingUi.Add(grid, combo, 1);
            _root.Children.Add(RoutingUi.Pad(grid, 48));
            if (flow == AudioFlow.Render)
            {
                _volume.SetResourceReference(StyleProperty, "HopperSliderStyle");
                AutomationProperties.SetName(_volume, "Output volume");
                _volume.ValueChanged += (_, _) =>
                {
                    if (!_refreshing && _module.Snapshot.Default(AudioFlow.Render, AudioRole.Multimedia) is { } id)
                        Run(SetVolume(id, (float)_volume.Value / 100));
                };
                var levels = RoutingUi.Columns(new(1, GridUnitType.Star), GridLength.Auto);
                _volume.HorizontalAlignment = HorizontalAlignment.Stretch;
                _volume.Width = double.NaN;
                RoutingUi.Add(levels, _volume, 0);
                _percent.Margin = new Thickness(16, 0, 0, 0);
                _percent.MinWidth = 28;
                RoutingUi.Add(levels, _percent, 1);
                _root.Children.Add(RoutingUi.Pad(levels, 40));
            }
        }
        _root.Children.Add(_error);
    }

    private Task SetVolume(string id, float volume) => _module.SetOutputVolumeAsync(id, volume);

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_island) { RenderIsland(); return; }
        _refreshing = true;
        try
        {
            var key = string.Join("|", _module.Settings.Output.Devices.Concat(_module.Settings.Microphone.Devices)) + string.Join("|", _module.Snapshot.Endpoints);
            foreach (var (flow, combo) in _choices)
            {
                if (_choiceKey != key) combo.ItemsSource = _module.Settings.For(flow).Devices.Where(e => _module.Snapshot.Find(e.Id)?.IsActive == true).ToList();
                combo.SelectedValue = _module.Snapshot.Default(flow, AudioRole.Multimedia);
                combo.IsEnabled = _module.Settings.For(flow).Enabled && combo.Items.Count > 0;
            }
            _choiceKey = key;
            var output = _module.Snapshot.Default(AudioFlow.Render, AudioRole.Multimedia);
            _volume.IsEnabled = output is not null && _module.Snapshot.Levels.ContainsKey(output);
            _volume.Value = output is not null && _module.Snapshot.Levels.TryGetValue(output, out var level) ? level.Volume * 100 : 0;
            _percent.Text = Math.Round(_volume.Value).ToString(System.Globalization.CultureInfo.CurrentCulture);
            _error.Text = _module.Error ?? "";
            _error.Visibility = string.IsNullOrEmpty(_error.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
    }

    private void RenderIsland()
    {
        var flow = _flow ?? AudioFlow.Render;
        _root.Children.Clear();
        var header = RoutingUi.Text(flow == AudioFlow.Render ? "Output" : "Microphone", "HeadlineTextStyle");
        header.Margin = new Thickness(0, 0, 0, 8);
        _root.Children.Add(header);
        foreach (var entry in _module.Settings.For(flow).Devices)
        {
            var stack = new StackPanel();
            stack.Children.Add(RoutingUi.Text(entry.Name));
            stack.Children.Add(RoutingUi.Text(_module.Status(flow, entry), "CaptionTextStyle", "TextSecondaryBrush"));
            var content = RoutingUi.Columns(GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto);
            var selected = string.Equals(_module.Snapshot.Default(flow, AudioRole.Multimedia), entry.Id, StringComparison.OrdinalIgnoreCase);
            var circle = new Ellipse { Width = 14, Height = 14, StrokeThickness = selected ? 4 : 1, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            circle.SetResourceReference(Shape.StrokeProperty, selected ? "IslandAccentBrush" : "TextSecondaryBrush");
            RoutingUi.Add(content, circle, 0);
            RoutingUi.Add(content, stack, 1);
            var rank = _module.Settings.For(flow).Devices.ToList().IndexOf(entry) + 1;
            RoutingUi.Add(content, RoutingUi.Text($"#{rank}", "CaptionTextStyle", "TextSecondaryBrush"), 2);
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = _module.Snapshot.Find(entry.Id)?.IsActive == true };
            button.SetResourceReference(StyleProperty, "FlyoutRowButtonStyle");
            if (selected) button.SetResourceReference(BackgroundProperty, "IslandPanelBrush");
            if (!button.IsEnabled) button.Opacity = 0.5;
            AutomationProperties.SetName(button, $"Prefer {entry.Name}");
            button.Click += (_, _) => Run(_module.PreferAsync(flow, entry.Id));
            _root.Children.Add(button);
        }
        if (_module.Settings.For(flow).Devices.Count == 0) _root.Children.Add(RoutingUi.Text("No devices"));
        var toggle = RoutingUi.Switch(flow == AudioFlow.Render ? "Switch by priority" : "Pin my microphone", _module.Settings.For(flow).Enabled,
            on => Run(_module.SetEnabledAsync(flow, on)));
        var footer = RoutingUi.Columns(new(1, GridUnitType.Star), GridLength.Auto);
        footer.Margin = new Thickness(0, 12, 0, 0);
        RoutingUi.Add(footer, RoutingUi.Text(flow == AudioFlow.Render ? "Switch by priority" : "Pin my microphone"), 0);
        RoutingUi.Add(footer, toggle, 1);
        _root.Children.Add(footer);
        if (_module.Error is { } error) _root.Children.Add(RoutingUi.Text(error, "CaptionTextStyle", "WarningTextBrush"));
    }

    private async void Run(Task change)
    {
        try { await change; }
        catch { /* The module exposes the error to the view. */ }
        Refresh();
    }
}