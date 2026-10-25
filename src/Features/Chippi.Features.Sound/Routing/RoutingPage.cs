// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Audio;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Routing;

/// <summary>The routing Settings page. All edits save immediately, as in Hopper.</summary>
public sealed class RoutingPage : UserControl
{
    private readonly AudioRoutingModule _module;
    private readonly StackPanel _body = new();
    private AudioFlow _flow;
    private readonly Dictionary<string, DragHandle> _handles = new(StringComparer.OrdinalIgnoreCase);

    public RoutingPage(AudioRoutingModule module, AudioFlow flow = AudioFlow.Render)
    {
        _module = module;
        _flow = flow;
        var root = new StackPanel();

        var output = new SegmentItem { Label = "Output", Value = AudioFlow.Render };
        var microphone = new SegmentItem { Label = "Microphone", Value = AudioFlow.Capture };
        var tabs = new SegmentedControl { ItemsSource = new[] { output, microphone }, SelectedItem = flow == AudioFlow.Render ? output : microphone };
        tabs.SetResourceReference(StyleProperty, "SelectorBarStyle");
        AutomationProperties.SetName(tabs, "Audio routing sections");
        tabs.SelectionChanged += (_, _) =>
        {
            if (tabs.SelectedItem is SegmentItem { Value: AudioFlow selected }) { _flow = selected; Render(); }
        };
        root.Children.Add(tabs);
        root.Children.Add(_body);
        Content = root;
        Loaded += (_, _) => { _module.Changed += Refresh; Render(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Render();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        Render();
    }

    private void Render()
    {
        _body.Children.Clear();
        _handles.Clear();
        var list = _module.Settings.For(_flow);
        var toggle = RoutingUi.Switch(_flow == AudioFlow.Render ? "Switch outputs automatically" : "Pin my microphone", list.Enabled,
            enabled => Run(_module.SetEnabledAsync(_flow, enabled)));
        var top = new SettingRow
        {
            Header = _flow == AudioFlow.Render ? "Switch outputs automatically" : "Pin my microphone",
            Description = list.Enabled ? _module.CurrentName(_flow) : "Off",
            Icon = _flow == AudioFlow.Render ? LucideIcons.Headphones : LucideIcons.Mic,
            Content = toggle,
            Margin = new Thickness(0, 8, 0, 0),
        };
        _body.Children.Add(RoutingUi.Card(top));
        if (_module.Error is { } error)
        {
            var text = RoutingUi.Text(error, "CaptionTextStyle", "WarningTextBrush");
            text.Margin = new Thickness(0, 8, 0, 0);
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            _body.Children.Add(text);
        }

        var add = RoutingUi.Button("Add");
        add.Click += (_, _) =>
        {
            var items = _module.Addable(_flow).Select(endpoint =>
                (endpoint.Name, (Action)(() => Run(_module.AddAsync(_flow, endpoint.Id))), true)).ToList();
            if (items.Count == 0) items.Add(("No devices", () => { }, false));
            RoutingUi.Menu(add, items);
        };
        AutomationProperties.SetName(add, _flow == AudioFlow.Render ? "Add output" : "Add microphone");
        _body.Children.Add(RoutingUi.Section("Priority", add));
        var card = RoutingUi.Card();
        if (list.Devices.Count == 0) card.Children.Add(RoutingUi.Pad(RoutingUi.Text("No devices")));
        for (var i = 0; i < list.Devices.Count; i++)
            card.Children.Add(DeviceRow(list.Devices[i], i, list.Devices.Count));
        _body.Children.Add(card);
        _body.Children.Add(RoutingUi.Section("Options"));
        var callSwitch = RoutingUi.Switch(_flow == AudioFlow.Render ? "Switch call audio too" : "Pin the call mic too", list.Calls,
            calls => Run(_module.SetCallsAsync(_flow, calls)));
        var options = RoutingUi.Card(new SettingRow { Header = _flow == AudioFlow.Render ? "Switch call audio too" : "Pin the call mic too", Content = callSwitch });
        if (_flow == AudioFlow.Render)
        {
            options.Children.Add(new SettingRow { Header = "When none are connected", Content = RoutingUi.Text("Leave to Windows", "CaptionTextStyle", "TextSecondaryBrush") });
            options.Children.Add(new SettingRow { Header = "New devices", Content = RoutingUi.Text("Add to the end", "CaptionTextStyle", "TextSecondaryBrush") });
        }
        else options.Children.Add(new SettingRow { Header = "Headset mics", Content = RoutingUi.Text("Only if listed", "CaptionTextStyle", "TextSecondaryBrush") });
        _body.Children.Add(options);
    }

    private FrameworkElement DeviceRow(PriorityEntry entry, int index, int count)
    {
        var grid = RoutingUi.Columns(GridLength.Auto, GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
        var handle = new DragHandle { Width = 16, Height = 24, Margin = new Thickness(0, 0, 16, 0), ToolTip = "Drag to reorder (Up/Down)" };
        AutomationProperties.SetName(handle, $"Reorder {entry.Name}");
        _handles[entry.Id] = handle;
        handle.MoveRequested += (_, delta) => Run(_module.MoveAsync(_flow, entry.Id, index + delta), entry.Id);
        double distance = 0;
        var target = index;
        handle.DragStarted += (_, _) => { distance = 0; target = index; };
        handle.DragDelta += (_, args) =>
        {
            distance += args.VerticalChange;
            target = Math.Clamp(index + (int)Math.Round(distance / 60), 0, count - 1);
            handle.ToolTip = $"Position {target + 1}";
        };
        handle.DragCompleted += (_, args) => { if (!args.Canceled) Run(_module.MoveAsync(_flow, entry.Id, target), entry.Id); };
        RoutingUi.Add(grid, handle, 0);
        var number = RoutingUi.Text((index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), "NumberTextStyle", "TextSecondaryBrush");
        number.Width = 24;
        RoutingUi.Add(grid, number, 1);
        var icon = new LucideIcon { Data = _flow == AudioFlow.Render ? LucideIcons.Headphones : LucideIcons.Mic, Size = 16, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
        RoutingUi.Add(grid, icon, 2);
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        name.Children.Add(RoutingUi.Text(entry.Name));
        if (_module.Snapshot.Find(entry.Id)?.AdapterName is { Length: > 0 } adapter)
            name.Children.Add(RoutingUi.Text(adapter, "CaptionTextStyle", "TextSecondaryBrush"));
        RoutingUi.Add(grid, name, 3);
        var status = RoutingUi.Text(_module.Status(_flow, entry), "CaptionTextStyle", "TextSecondaryBrush");
        status.Margin = new Thickness(0, 0, 12, 0);
        RoutingUi.Add(grid, status, 4);
        var more = new IconButton { Icon = LucideIcons.Ellipsis, ToolTip = $"Options for {entry.Name}" };
        AutomationProperties.SetName(more, $"Options for {entry.Name}");
        more.Click += (_, _) =>
        {
            RoutingUi.Menu(more, [
                ("Prefer", () => Run(_module.PreferAsync(_flow, entry.Id), entry.Id), index > 0),
                ("Move up", () => Run(_module.MoveAsync(_flow, entry.Id, index - 1), entry.Id), index > 0),
                ("Move down", () => Run(_module.MoveAsync(_flow, entry.Id, index + 1), entry.Id), index + 1 < count),
                ("Remove", () => Run(_module.RemoveAsync(_flow, entry.Id)), true),
            ]);
        };
        RoutingUi.Add(grid, more, 5);
        return RoutingUi.Pad(grid);
    }

    private async void Run(Task change, string? focus = null)
    {
        try { await change; }
        catch { /* The module publishes the inline error and keeps the saved preference. */ }
        Refresh();
        if (focus is not null && _handles.TryGetValue(focus, out var handle)) handle.Focus();
    }
}