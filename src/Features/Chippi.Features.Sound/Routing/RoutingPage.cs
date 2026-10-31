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
/// The routing Settings page (revised 2026-10-31): Output's Volume, captioned with the output it sets,
/// then Priority: its switches in one card and, 8 below, the one list whose radios pick the device in
/// use, as the island's rows do. All edits save immediately, as in Hopper.
/// </summary>
public sealed class RoutingPage : UserControl
{
    private readonly AudioRoutingModule _module;
    private readonly StackPanel _body = new();
    private readonly AudioFlow _flow;
    private readonly Dictionary<string, DragHandle> _handles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly LatestWrite _volumeWrite = new();
    private readonly LevelTarget _shown = new();
    private readonly TextBlock _percent = RoutingUi.Text("", "BodyTextStyle", "TextSecondaryBrush");
    private readonly Button _mute;
    private readonly StackPanel _levels = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _none = RoutingUi.Text("No output", "BodyTextStyle", "TextTertiaryBrush");
    private Card? _volumeCard;
    private SettingRow? _volumeRow;
    private string _shape = "";
    private bool _refreshing;

    /// <summary>One flow's settings: Settings › Output, or the routing sections of Settings › Microphone.</summary>
    public RoutingPage(AudioRoutingModule module, AudioFlow flow = AudioFlow.Render)
    {
        _module = module;
        _flow = flow;
        _mute = RoutingUi.Button("Mute", () =>
        {
            if (_module.InUse(AudioFlow.Render) is { } id)
                Run(_module.SetOutputMutedAsync(id, !Level(id).Muted));
        });
        Content = _body;
        Loaded += (_, _) => { _module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _refreshing = true;
        try
        {
            // A level change only moves the slider; rebuilding under a drag would drop it.
            var shape = Layout();
            if (shape != _shape)
            {
                _shape = shape;
                Render();
            }
            if (_flow == AudioFlow.Render)
                RefreshOutput();
        }
        finally { _refreshing = false; }
    }

    private string Layout()
    {
        var list = _module.Settings.For(_flow);
        return string.Join("|", list.Enabled, list.Calls, _module.InUse(_flow), _module.Error,
            string.Join(",", list.Devices.Select(e => e.Id + e.Name + _module.Snapshot.Find(e.Id)?.AdapterName + Connected(e))));
    }

    private bool Connected(PriorityEntry entry) => _module.Snapshot.Find(entry.Id)?.IsActive == true;

    private void Render()
    {
        _body.Children.Clear();
        _handles.Clear();
        var list = _module.Settings.For(_flow);
        if (_flow == AudioFlow.Render)
            _body.Children.Add(_volumeCard ??= SettingsLayout.Card(_volumeRow = SettingsLayout.Row("Volume", VolumeControls())));

        Button? add = null;
        add = SettingsLayout.AddButton(_flow == AudioFlow.Render ? "device" : "mic", () =>
        {
            var items = _module.Addable(_flow).Select(endpoint =>
                (endpoint.Name, (Action)(() => Run(_module.AddAsync(_flow, endpoint.Id))), true)).ToList();
            if (items.Count == 0) items.Add(("No devices", () => { }, false));
            RoutingUi.Menu(add!, items);
        });
        _body.Children.Add(SettingsLayout.Section("Priority", add));
        _body.Children.Add(SettingsLayout.Card(
            SettingsLayout.Row("Priority switching", RoutingUi.Switch("Priority switching", list.Enabled, enabled => Run(_module.SetEnabledAsync(_flow, enabled)))),
            SettingsLayout.Row("Communications device", RoutingUi.Switch("Communications device", list.Calls, on => Run(_module.SetCallsAsync(_flow, on))), "Calls follow this list")));
        // The list is the section's content, so it's a card of its own under the switches that drive it.
        var devices = SettingsLayout.Card();
        devices.Margin = new Thickness(0, 8, 0, 0);
        if (list.Devices.Count == 0) devices.Children.Add(SettingsLayout.Empty("No devices"));
        var inUse = _module.InUse(_flow);
        for (var i = 0; i < list.Devices.Count; i++)
            devices.Children.Add(DeviceRow(list.Devices[i], i, list.Devices.Count, inUse));
        _body.Children.Add(devices);
        if (_module.Error is { } error)
        {
            var text = RoutingUi.Text(error, "CaptionTextStyle", "WarningTextBrush");
            text.Margin = new Thickness(0, 8, 0, 0);
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            _body.Children.Add(text);
        }
    }

    /// <summary>The island's Output card as a row: the system volume of the output in use, its percentage, and Mute.</summary>
    private Grid VolumeControls()
    {
        _volume.SetResourceReference(StyleProperty, "HopperSliderStyle");
        AutomationProperties.SetName(_volume, "System volume");
        _volume.ValueChanged += (_, e) =>
        {
            ShowPercent();
            if (!_refreshing && _shown.Id is { } id)
                _volumeWrite.Send(() => _module.SetOutputVolumeAsync(id, (float)e.NewValue / 100));
        };
        _percent.MinWidth = 52;
        _percent.TextAlignment = TextAlignment.Right;
        _percent.Margin = new Thickness(12, 0, 12, 0);
        System.Windows.Documents.Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        _levels.Children.Add(_volume);
        _levels.Children.Add(_percent);
        _levels.Children.Add(_mute);
        return new Grid { Children = { _levels, _none } };
    }

    private (float Volume, bool Muted) Level(string id) =>
        _module.Snapshot.Levels.TryGetValue(id, out var level) ? (level.Volume, level.Muted) : (0, false);

    private void ShowPercent()
    {
        bool muted = _module.InUse(AudioFlow.Render) is { } id && Level(id).Muted;
        _percent.Text = muted ? "Muted" : $"{Math.Round(_volume.Value).ToString(CultureInfo.CurrentCulture)}%";
        _percent.SetResourceReference(TextBlock.ForegroundProperty, muted ? "WarningTextBrush" : "TextSecondaryBrush");
    }

    private void RefreshOutput()
    {
        var inUse = _module.InUse(AudioFlow.Render);
        bool none = _module.Snapshot.Find(inUse ?? "") is null;
        bool known = inUse is not null && _module.Snapshot.Levels.ContainsKey(inUse);
        var (volume, muted) = inUse is null ? (0f, false) : Level(inUse);
        _levels.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _none.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        _volume.IsEnabled = _mute.IsEnabled = known && !none;
        _shown.Show(_volume, known && !none ? inUse : null, volume);
        ShowPercent();
        // The caption names the output the slider sets: its full name, as the list shows it.
        if (_volumeRow is not null)
            _volumeRow.Description = none ? "" : _module.CurrentName(AudioFlow.Render);
        _mute.Content = muted ? "Unmute" : "Mute";
        AutomationProperties.SetName(_mute, muted ? "Unmute system volume" : "Mute system volume");
    }

    /// <summary>
    /// A priority row: the grip, its place, a radio and the name, "Not connected" when it isn't, and its
    /// menu. A connected row's radio and name use it now, as the island's row does; the order stays.
    /// </summary>
    private FrameworkElement DeviceRow(PriorityEntry entry, int index, int count, string? inUse)
    {
        bool connected = Connected(entry);
        bool selected = connected && string.Equals(entry.Id, inUse, StringComparison.OrdinalIgnoreCase);
        var grid = RoutingUi.Columns(GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
        var handle = new DragHandle { Width = 16, Height = 24, Margin = new Thickness(0, 0, 16, 0), ToolTip = "Reorder" };
        AutomationProperties.SetName(handle, $"Reorder {entry.Name}");
        _handles[entry.Id] = handle;
        handle.MoveRequested += (_, delta) => Run(_module.MoveAsync(_flow, entry.Id, index + delta), entry.Id);
        double distance = 0;
        var target = index;
        handle.DragStarted += (_, _) => { distance = 0; target = index; };
        handle.DragDelta += (_, args) =>
        {
            distance += args.VerticalChange;
            target = Math.Clamp(index + (int)Math.Round(distance / SettingsLayout.RowHeight), 0, count - 1);
            handle.ToolTip = $"Position {target + 1}";
        };
        handle.DragCompleted += (_, args) => { if (!args.Canceled) Run(_module.MoveAsync(_flow, entry.Id, target), entry.Id); };
        RoutingUi.Add(grid, handle, 0);
        var number = RoutingUi.Text((index + 1).ToString(CultureInfo.CurrentCulture), "NumberTextStyle", "TextSecondaryBrush");
        number.Width = 24;
        RoutingUi.Add(grid, number, 1);
        var radio = new Ellipse { Width = 20, Height = 20, StrokeThickness = selected ? 5 : 1, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        radio.SetResourceReference(Shape.StrokeProperty, selected ? "AccentBrush" : connected ? "TextSecondaryBrush" : "TextTertiaryBrush");
        if (selected) radio.SetResourceReference(Shape.FillProperty, "OnAccentBrush");
        else radio.Fill = Brushes.Transparent;
        DockPanel.SetDock(radio, Dock.Left);
        // Windows often puts the adapter in the name already ("Speakers (INZONE Buds)"); say it once.
        var label = entry.Name;
        if (_module.Snapshot.Find(entry.Id)?.AdapterName is { Length: > 0 } adapter && !entry.Name.Contains(adapter, StringComparison.OrdinalIgnoreCase))
            label = $"{entry.Name} · {adapter}";
        var name = RoutingUi.Text(label, brush: connected ? null : "TextTertiaryBrush");
        name.Margin = new Thickness(0, 0, 12, 0);
        var pick = new Button { Content = new DockPanel { Children = { radio, name } }, IsEnabled = connected, ToolTip = label };
        pick.SetResourceReference(StyleProperty, "RadioRowButtonStyle");
        AutomationProperties.SetName(pick, selected ? $"{entry.Name}, in use" : $"Use {entry.Name}");
        pick.Click += (_, _) =>
        {
            if (!selected)
                Run(_module.UseAsync(_flow, entry.Id));
        };
        RoutingUi.Add(grid, pick, 2);
        if (!connected)
        {
            var status = RoutingUi.Text("Not connected", "CaptionTextStyle", "TextSecondaryBrush");
            status.Margin = new Thickness(0, 0, 12, 0);
            RoutingUi.Add(grid, status, 3);
        }
        var more = new IconButton { Icon = LucideIcons.Ellipsis, ToolTip = $"Options for {entry.Name}" };
        AutomationProperties.SetName(more, $"Options for {entry.Name}");
        more.Click += (_, _) =>
        {
            RoutingUi.Menu(more, [
                ("Move to top", () => Run(_module.MoveAsync(_flow, entry.Id, 0), entry.Id), index > 0),
                ("Move up", () => Run(_module.MoveAsync(_flow, entry.Id, index - 1), entry.Id), index > 0),
                ("Move down", () => Run(_module.MoveAsync(_flow, entry.Id, index + 1), entry.Id), index + 1 < count),
                ("Remove", () => Run(_module.RemoveAsync(_flow, entry.Id)), true),
            ]);
        };
        RoutingUi.Add(grid, more, 4);
        return new Border { Child = grid, MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding };
    }

    private async void Run(Task change, string? focus = null)
    {
        try { await change; }
        catch { /* The module publishes the inline error and keeps the saved preference. */ }
        Refresh();
        if (focus is not null && _handles.TryGetValue(focus, out var handle)) handle.Focus();
    }
}
