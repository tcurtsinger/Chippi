// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.UI.Controls;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>The compact readings and disks tables. The sampler and alerts retain their own lifetimes.</summary>
internal sealed class SystemMonitorView : UserControl
{
    private readonly SystemMonitorModule _module;
    private readonly bool _settingsPage;
    private readonly StackPanel _readings = new();
    private readonly Border _readingsCard, _disks;
    private readonly FrameworkElement _readingsSurface;
    private readonly Dictionary<string, ReadingRow> _rows = [];
    private readonly Popup _picker = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private SensorWatch? _watch;
    private SensorSampler? _sampler;
    private SystemSnapshot? _shown;
    private string? _gpuKey, _networkKey;
    private FrameworkElement? _gpuPicker, _networkPicker;
    private int _zoneCount;

    public SystemMonitorView(SystemMonitorModule module, bool settingsPage = false)
    {
        _module = module;
        _settingsPage = settingsPage;
        _readingsCard = Card(); _readingsCard.Child = _readings;
        if (settingsPage) { _readingsCard.ClearValue(StyleProperty); _readingsCard.Padding = new Thickness(16, 2, 16, 2); }
        _disks = Card(); _disks.Margin = new Thickness(0, 8, 0, 0);
        _readingsSurface = settingsPage ? SettingsLayout.Card(_readingsCard) : _readingsCard;
        Content = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Children = { _readingsSurface, _disks, _picker } };
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private void Start()
    {
        if (_module.Sampler is not { } sampler) return;
        _sampler = sampler;
        _module.Changed += OnSettingsChanged;
        sampler.Updated += OnUpdated;
        _watch = sampler.Watch(Need(_module.Settings));
        Show(Opening(sampler.Latest, _module.PageSnapshot));
    }

    /// <summary>
    /// What a page opens with: the last page's GPU, network and disks with the tile's current CPU and
    /// memory, so it opens whole and its first update changes values in place, not the layout.
    /// </summary>
    internal static SystemSnapshot Opening(SystemSnapshot latest, SystemSnapshot? page) => page is null ? latest : page with
    {
        Cpu = latest.Cpu.Status == ReadingStatus.NotRead ? page.Cpu : latest.Cpu,
        Memory = latest.Memory.Status == ReadingStatus.NotRead ? page.Memory : latest.Memory,
    };

    private void Stop()
    {
        _module.Changed -= OnSettingsChanged;
        if (_sampler is not null) _sampler.Updated -= OnUpdated;
        _watch?.Dispose(); _watch = null; _sampler = null; _picker.IsOpen = false;
    }

    internal static SensorNeed Need(SystemMonitorSettings s)
    {
        var need = SensorNeed.None;
        if (s.IsOn(Reading.Cpu)) need |= SensorNeed.Cpu;
        if (s.IsOn(Reading.Memory)) need |= SensorNeed.Memory;
        if (s.IsOn(Reading.Gpu)) need |= SensorNeed.GpuSensors | SensorNeed.GpuLoad;
        if (s.IsOn(Reading.Network)) need |= SensorNeed.Network;
        if (s.IsOn(Reading.Disks)) need |= SensorNeed.Disks;
        if (s.IsOn(Reading.Battery)) need |= SensorNeed.Battery;
        if (s.IsOn(Reading.ThermalZone)) need |= SensorNeed.ThermalZones;
        return need;
    }

    private void OnSettingsChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnSettingsChanged); return; }
        if (_watch is not null) _watch.Need = Need(_module.Settings);
        if (_shown is not null) Show(_shown);
    }

    private void OnUpdated(SystemSnapshot snapshot) => Dispatcher.BeginInvoke(() =>
    {
        if (_sampler is null) return;
        if (snapshot.Gpus.Status != ReadingStatus.NotRead || snapshot.Network.Status != ReadingStatus.NotRead || snapshot.Disks.Status != ReadingStatus.NotRead)
            _module.PageSnapshot = snapshot;
        Show(snapshot);
    });

    internal void Show(SystemSnapshot s)
    {
        _shown = s;
        var settings = _module.Settings;
        var visible = new List<ReadingRow>();
        ReadingRow Row(string key, string detail, string value, bool caution = false, FrameworkElement? picker = null)
        {
            if (!_rows.TryGetValue(key, out var row)) _rows[key] = row = new ReadingRow(key, _settingsPage);
            row.Set(detail, value, caution, picker);
            visible.Add(row);
            return row;
        }
        if (settings.IsOn(Reading.Cpu))
            Row("CPU", s.Cpu.Status == ReadingStatus.Unavailable ? "Unavailable" : s.Cpu.Value?.EffectiveMhz is { } mhz ? Format.Clock(mhz) : "",
                s.Cpu.Value is { } cpu ? Format.Percent(cpu.Utility) : "", _module.Alerting("cpu-load", "cpu"));

        foreach (var g in s.Gpus.Value ?? []) if (g.Load is not null) _module.GpusWithLoad.Add(g.Id);
        var gpus = (s.Gpus.Value ?? []).Where(g => _module.GpusWithLoad.Contains(g.Id)).ToList();
        if (settings.IsOn(Reading.Gpu) && (gpus.Count > 0 || s.Gpus.Status == ReadingStatus.Unavailable && _module.GpusWithLoad.Count > 0))
        {
            var gpu = Pick.Gpu(gpus, settings.GpuId, _module.GpusWithLoad);
            string key = gpu?.Id + "|" + string.Join("|", gpus.Select(g => g.Id + g.Name));
            if (key != _gpuKey)
            {
                _gpuKey = key;
                _gpuPicker = gpus.Count > 1 ? PickerButton(gpu!.Name, "GPU", [.. gpus.Select(g => (g.Id, g.Name))], gpu.Id,
                    id => _module.Save(_module.Settings with { GpuId = id })) : null;
            }
            string detail = s.Gpus.Status == ReadingStatus.Unavailable ? "Unavailable" : string.Join(" · ", new[] {
                gpu?.TemperatureC is { } t ? Format.Temperature(t, settings.Temperature) : null,
                gpu?.DedicatedUsedBytes is { } used ? gpu.DedicatedTotalBytes is { } total ? Format.Of(used,total) : Format.Size(used) : null }.OfType<string>());
            Row("GPU", detail, gpu?.Load is { } load ? Format.Percent(load) : "", gpu is not null && _module.Alerting("gpu-temperature", gpu.Id), _gpuPicker);
        }
        if (settings.IsOn(Reading.Memory))
            Row("Memory", s.Memory.Status == ReadingStatus.Unavailable ? "Unavailable" : s.Memory.Value is { } mem ? Format.Of(mem.UsedBytes, mem.TotalBytes) : "",
                s.Memory.Value is { } memory ? Format.Percent(memory.UsedPercent) : "", _module.Alerting("memory", "memory"));

        if (settings.IsOn(Reading.Network))
        {
            var adapters = s.Network.Value ?? [];
            var adapter = Pick.Adapter(adapters, settings.NetworkAdapterId);
            string key = adapter?.Id + "|" + string.Join("|", adapters.Select(a => a.Id + a.Name));
            if (key != _networkKey)
            {
                _networkKey = key;
                _networkPicker = adapters.Count > 1 ? PickerButton(adapter!.Name, "Network adapter", [.. adapters.Select(a => (a.Id, a.Name))], adapter.Id,
                    id => _module.Save(_module.Settings with { NetworkAdapterId = id })) : null;
            }
            var row = Row("Network", s.Network.Status == ReadingStatus.Unavailable ? "Unavailable" : "", "", picker: _networkPicker);
            if (s.Network.Status != ReadingStatus.Unavailable)
            {
                if (adapter is { Connected: true, ReceiveBytesPerSecond: { } down, SendBytesPerSecond: { } up })
                    row.Network(Format.Rate(down, settings.NetworkSpeed), Format.Rate(up, settings.NetworkSpeed));
                else if (s.Network.Status is ReadingStatus.Ok or ReadingStatus.NotSupported && adapter is not { Connected: true })
                    row.Detail.Text = "Not connected";
            }
        }
        if (settings.IsOn(Reading.Battery) && s.Battery.Status is ReadingStatus.Ok or ReadingStatus.Unavailable)
        {
            var battery = s.Battery.Value;
            string detail = battery is null ? "Unavailable" : string.Join(" · ", new[] {
                battery.State == BatteryState.OnBattery ? "On battery" : battery.State == BatteryState.Charging ? "Charging" : "Plugged in",
                battery.RateWatts is { } watts ? Format.Watts(watts) : null, battery.TimeLeft is { } left ? Format.TimeLeft(left) : null }.OfType<string>());
            Row("Battery", detail, battery?.Percent is { } percent ? Format.Percent(percent) : "", _module.Alerting("low-battery", "battery"));
        }
        if (settings.IsOn(Reading.ThermalZone))
        {
            var zones = s.ThermalZones.Value ?? [];
            if (s.ThermalZones.Status == ReadingStatus.Ok) _zoneCount = zones.Count;
            int count = s.ThermalZones.Status is ReadingStatus.Unavailable or ReadingStatus.Pending ? _zoneCount : zones.Count;
            for (int i = 0; i < count; i++)
                Row(count > 1 ? $"Thermal zone {i + 1}" : "Thermal zone", s.ThermalZones.Status == ReadingStatus.Unavailable ? "Unavailable" : "",
                    i < zones.Count ? Format.Temperature(zones[i].TemperatureC, settings.Temperature) : "");
        }
        // Unusually many thermal zones still show whole rows only. Settings owns overflow.
        int capacity = _settingsPage ? int.MaxValue : (int)((IslandPage.BodyHeight - 4) / 28);
        if (visible.Count > capacity) visible.RemoveRange(capacity, visible.Count - capacity);
        // Keep existing controls parented when only their readings change.
        foreach (var old in _readings.Children.OfType<ReadingRow>().Where(r => !visible.Contains(r)).ToList()) _readings.Children.Remove(old);
        for (int i = 0; i < visible.Count; i++)
            if (i >= _readings.Children.Count || _readings.Children[i] != visible[i])
            { _readings.Children.Remove(visible[i]); _readings.Children.Insert(i, visible[i]); }
        _readingsSurface.Visibility = _readingsCard.Visibility = visible.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        double remaining = IslandPage.BodyHeight - (visible.Count == 0 ? 0 : visible.Count * 28 + 4 + 8);
        _disks.Visibility = !_settingsPage && settings.IsOn(Reading.Disks) && remaining >= 26 ? Visibility.Visible : Visibility.Collapsed;
        _disks.Margin = new Thickness(0, visible.Count == 0 ? 0 : 8, 0, 0);
        _disks.Child = s.Disks.Status == ReadingStatus.Unavailable ? DiskTable.Unavailable()
            : DiskTable.Build(s.Disks.Value ?? [], id => _module.Alerting("low-disk", id), Math.Max(0, remaining - 4));
    }

    internal static Border Card()
    {
        var card = new Border { Padding = new Thickness(10, 2, 10, 2) };
        card.SetResourceReference(StyleProperty, "IslandCardStyle");
        return card;
    }

    internal static TextBlock Text(string text, double size = 12, bool secondary = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "IslandTextTertiaryBrush" : "IslandTextBrush");
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    private sealed class ReadingRow : Grid
    {
        private readonly ContentControl _name = new();
        private readonly bool _settingsPage;
        public TextBlock Detail { get; } = Text("", 11, true);
        private readonly TextBlock _value = Text("");
        public ReadingRow(string name, bool settingsPage)
        {
            _settingsPage = settingsPage;
            Height = settingsPage ? 48 : 28;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(settingsPage ? 100 : 80) });
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _name.Content = Text(name, settingsPage ? 14 : 12, true); _name.Tag = name;
            if (settingsPage) { Detail.FontSize = 12; _value.FontSize = 14; }
            _value.FontWeight = FontWeights.SemiBold; _value.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(Detail, 1); Grid.SetColumn(_value, 2);
            var divider = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
            divider.SetResourceReference(BackgroundProperty, settingsPage ? "BorderBrush" : "IslandPanelBrush"); Grid.SetColumnSpan(divider, 3);
            Children.Add(divider); Children.Add(_name); Children.Add(Detail); Children.Add(_value);
        }
        public void Set(string detail, string value, bool caution, FrameworkElement? picker)
        {
            _name.Content = picker ?? (object)Text((string)_name.Tag, _settingsPage ? 14 : 12, true);
            Detail.Text = detail; _value.Inlines.Clear(); _value.Text = value;
            if (_settingsPage) { Detail.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); if (_name.Content is TextBlock name) name.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); }
            _value.SetResourceReference(TextBlock.ForegroundProperty, caution ? "WarningTextBrush" : _settingsPage ? "TextPrimaryBrush" : "IslandTextBrush");
        }
        public void Network(string down, string up)
        {
            void Arrow(Geometry geometry)
            {
                var glyph = new LucideIcon { Data = geometry, Size = 12, Margin = new Thickness(0, 0, 4, 0) };
                glyph.SetResourceReference(LucideIcon.BrushProperty, _settingsPage ? "TextSecondaryBrush" : "IslandTextTertiaryBrush");
                _value.Inlines.Add(new InlineUIContainer(glyph) { BaselineAlignment = BaselineAlignment.Center });
            }
            _value.FontWeight = FontWeights.Normal;
            Arrow(LucideIcons.ArrowDown); _value.Inlines.Add(new Run(down + "   "));
            Arrow(LucideIcons.ArrowUp); _value.Inlines.Add(new Run(up));
        }
    }
    private Button PickerButton(string current, string what, IReadOnlyList<(string Id, string Name)> items, string currentId, Action<string> choose)
    {
        var text = new TextBlock { Text = what == "GPU" ? "GPU" : "Network", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var chevron = new LucideIcon { Data = LucideIcons.ChevronDown, Size = 12, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(LucideIcon.BrushProperty, _settingsPage ? "TextSecondaryBrush" : "IslandTextTertiaryBrush");
        var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { text, chevron } }, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 0, 4, 0), Width = double.NaN, Height = 24 };
        button.SetResourceReference(StyleProperty, _settingsPage ? "SubtleButtonStyle" : "IslandTransportButtonStyle");
        AutomationProperties.SetName(button, $"{what}, {current}");
        button.Click += (_, _) =>
        {
            var list = new StackPanel { Margin = new Thickness(4) };
            foreach (var (id, name) in items)
            {
                var item = new Button { Content = name };
                item.SetResourceReference(StyleProperty, _settingsPage ? "SubtleButtonStyle" : "IslandMenuItemStyle");
                if (id == currentId)
                    item.FontWeight = FontWeights.SemiBold;
                item.Click += (_, _) =>
                {
                    _picker.IsOpen = false;
                    choose(id);
                };
                list.Children.Add(item);
            }
            var menu = new Border { MinWidth = 160, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = list };
            menu.SetResourceReference(Border.BackgroundProperty, _settingsPage ? "FlyoutBrush" : "IslandBrush");
            menu.SetResourceReference(Border.BorderBrushProperty, _settingsPage ? "BorderBrush" : "IslandBorderBrush");
            _picker.PlacementTarget = button;
            _picker.Child = menu;
            _picker.IsOpen = true;
        };
        return button;
    }

}
