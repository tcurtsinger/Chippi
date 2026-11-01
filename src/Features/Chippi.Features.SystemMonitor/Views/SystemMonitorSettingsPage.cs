// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.UI.Controls;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// Settings › System: the island's widgets, every drive as a storage bar, then General, Readings, Units
/// and Alerts in the shared Settings layout.
/// </summary>
public sealed class SystemMonitorSettingsPage : UserControl
{
    private readonly SystemMonitorModule _module;
    private readonly StackPanel _body = new();
    private readonly SettingsReadings _readings;
    private readonly Card _drives = SettingsLayout.Card();
    private readonly Card _zoneCard = SettingsLayout.Card();
    private readonly List<TextBlock> _zoneValues = [];
    private readonly List<Action<SystemMonitorSettings>> _sync = [];
    private readonly Dictionary<string, string> _gpus = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DriveRow> _driveRows = new(StringComparer.OrdinalIgnoreCase);
    private SensorWatch? _discovery;
    private SensorSampler? _sampler;
    private bool _battery, _zones, _hasGpu, _updating, _attached;
    private string _choices = "";
    private IReadOnlyList<(string Id, string Name)> _gpuChoices = [], _adapterChoices = [];
    private ComboBox _tileStatus = null!;

    public SystemMonitorSettingsPage(SystemMonitorModule module)
    {
        _module = module;
        _readings = new SettingsReadings(module);
        Content = _body;
        Build();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        if (_attached) return;
        _attached = true;
        _module.Changed += Refresh;
        _sampler = _module.Sampler;
        if (_sampler is not null)
        {
            _sampler.Updated += Updated;
            Hardware(_sampler.Latest);
            // Discover conditional rows once, then keep only disk space live while Settings is open.
            _discovery = _sampler.Watch(SensorNeed.Disks | SensorNeed.Battery | SensorNeed.ThermalZones | SensorNeed.GpuSensors);
        }
        Refresh();
    }

    private void Detach()
    {
        _attached = false;
        _module.Changed -= Refresh;
        if (_sampler is not null) _sampler.Updated -= Updated;
        _discovery?.Dispose();
        _discovery = null;
        _sampler = null;
    }

    private void Updated(SystemSnapshot snapshot) => Dispatcher.BeginInvoke(() =>
    {
        if (!_attached) return;
        Hardware(snapshot);
        if (Resolved(snapshot.Battery.Status) && Resolved(snapshot.ThermalZones.Status) && Resolved(snapshot.Gpus.Status))
        {
            if (_discovery is not null) _discovery.Need = SensorNeed.Disks;
        }
    });

    private static bool Resolved(ReadingStatus status) => status is not ReadingStatus.NotRead and not ReadingStatus.Pending;

    // Remember discovered capability across NotRead/temporary failures, without keeping stale values on screen.
    internal void Hardware(SystemSnapshot snapshot)
    {
        Drives(snapshot);
        Zones(snapshot);
        bool changed = false;
        if (!_battery && snapshot.Battery.Value is not null) { _battery = true; changed = true; }
        if (!_zones && snapshot.ThermalZones.Value is { Count: > 0 }) { _zones = true; changed = true; }
        if (!_hasGpu && snapshot.Gpus.Value is { Count: > 0 }) { _hasGpu = true; changed = true; }
        foreach (var gpu in snapshot.Gpus.Value ?? [])
            if (gpu.TemperatureC is not null && (!_gpus.TryGetValue(gpu.Id, out var name) || name != gpu.Name))
            { _gpus[gpu.Id] = gpu.Name; changed = true; }
        // The GPU and adapter dropdowns show only with a choice to make.
        foreach (var gpu in snapshot.Gpus.Value ?? []) if (gpu.Load is not null) _module.GpusWithLoad.Add(gpu.Id);
        if (snapshot.Gpus.Status == ReadingStatus.Ok)
            _gpuChoices = [.. snapshot.Gpus.Value!.Where(g => _module.GpusWithLoad.Contains(g.Id)).Select(g => (g.Id, g.Name))];
        if (snapshot.Network.Status is ReadingStatus.Ok)
            _adapterChoices = [.. snapshot.Network.Value!.Select(a => (a.Id, a.Name))];
        string choices = string.Join("|", _gpuChoices.Select(g => g.Id + g.Name)) + "/" + string.Join("|", _adapterChoices.Select(a => a.Id + a.Name));
        if (choices != _choices) { _choices = choices; changed = true; }
        if (changed) Build();
    }

    // Every thermal zone, which the island's Temperature page lists only up to 8 of.
    private void Zones(SystemSnapshot snapshot)
    {
        if (snapshot.ThermalZones is not { Status: ReadingStatus.Ok, Value: { } zones })
            return;
        if (zones.Count != _zoneValues.Count)
        {
            _zoneCard.Children.Clear();
            _zoneValues.Clear();
            for (int i = 0; i < zones.Count; i++)
            {
                var value = Text("");
                _zoneValues.Add(value);
                _zoneCard.Children.Add(Row($"Zone {i + 1}", value));
            }
            Refresh();
        }
        for (int i = 0; i < zones.Count; i++)
            _zoneValues[i].Text = Format.Temperature(zones[i].TemperatureC, _module.Settings.Temperature);
    }

    // One storage bar per drive, updated in place while the drives stay the same.
    private void Drives(SystemSnapshot snapshot)
    {
        if (snapshot.Disks.Status == ReadingStatus.Unavailable)
        {
            _driveRows.Clear();
            _drives.Children.Clear();
            _drives.Children.Add(SettingsLayout.Empty("Unavailable"));
            return;
        }
        if (snapshot.Disks.Value is not { } disks)
            return;
        var volumes = DrivesWidget.Volumes(disks);
        if (!volumes.Select(v => v.Id).SequenceEqual(_driveRows.Keys, StringComparer.OrdinalIgnoreCase))
        {
            _driveRows.Clear();
            _drives.Children.Clear();
            foreach (var volume in volumes)
                _drives.Children.Add(_driveRows[volume.Id] = new DriveRow());
        }
        foreach (var volume in volumes)
            _driveRows[volume.Id].Set(volume, _module.Alerting("low-disk", volume.Id));
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _updating = true;
        try
        {
            var settings = _module.Settings;
            var choices = Enum.GetValues<TileStatus>().Where(t => settings.IsOn(SystemMonitorSettings.ToReading(t)) && (t != TileStatus.Gpu || _hasGpu)).ToArray();
            var current = _tileStatus.Items.Cast<ComboBoxItem>().Select(i => (TileStatus)i.Tag).ToArray();
            if (!current.SequenceEqual(choices))
            {
                _tileStatus.Items.Clear();
                foreach (var status in choices) _tileStatus.Items.Add(new ComboBoxItem { Tag = status, Content = ReadingName(SystemMonitorSettings.ToReading(status)) });
            }
            _tileStatus.IsEnabled = choices.Length > 0;
            _tileStatus.SelectedItem = _tileStatus.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (TileStatus)i.Tag == settings.EffectiveTileStatus());
            foreach (var sync in _sync) sync(settings);
        }
        finally { _updating = false; }
    }

    private void Save(Func<SystemMonitorSettings, SystemMonitorSettings> change)
    {
        if (!_updating) { _module.Save(change(_module.Settings)); Refresh(); }
    }

    private void Build()
    {
        _updating = true;
        _body.Children.Clear();
        _sync.Clear();
        if (_readings.Parent is Panel oldReadings) oldReadings.Children.Remove(_readings);
        if (_drives.Parent is Panel oldDrives) oldDrives.Children.Remove(_drives);
        if (_zoneCard.Parent is Panel oldZones) oldZones.Children.Remove(_zoneCard);
        _body.Children.Add(_readings);
        var drivesHeader = SettingsLayout.Section("Drives");
        _body.Children.Add(drivesHeader);
        _body.Children.Add(_drives);
        _sync.Add(s => drivesHeader.Visibility = _drives.Visibility = s.IsOn(Reading.Disks) ? Visibility.Visible : Visibility.Collapsed);
        var zonesHeader = SettingsLayout.Section("Thermal zones");
        _body.Children.Add(zonesHeader);
        _body.Children.Add(_zoneCard);
        _sync.Add(s => zonesHeader.Visibility = _zoneCard.Visibility = s.IsOn(Reading.ThermalZone) && _zoneValues.Count > 1 ? Visibility.Visible : Visibility.Collapsed);
        var first = NewCard();
        first.Children.Add(Row("Update speed", Choice("Update speed", SystemMonitorSettings.UpdateChoices,
            n => n == 1 ? "1 second" : $"{n} seconds", s => s.UpdateSeconds, (s, n) => s with { UpdateSeconds = n })));
        _tileStatus = Combo("Home status");
        _tileStatus.SelectionChanged += (_, _) =>
        {
            if (_tileStatus.SelectedItem is ComboBoxItem { Tag: TileStatus status }) Save(s => s with { TileStatus = status });
        };
        first.Children.Add(Row("Home status", _tileStatus));
        if (_gpuChoices.Count > 1)
            first.Children.Add(Row("GPU", Device("GPU", _gpuChoices, s => Pick.Gpu(Readings().Gpus.Value, s.GpuId, _module.GpusWithLoad)?.Id, (s, id) => s with { GpuId = id })));
        if (_adapterChoices.Count > 1)
            first.Children.Add(Row("Network adapter", Device("Network adapter", _adapterChoices, s => Pick.Adapter(Readings().Network.Value, s.NetworkAdapterId)?.Id, (s, id) => s with { NetworkAdapterId = id })));
        _body.Children.Add(SettingsLayout.Section("General"));
        _body.Children.Add(first);

        var readings = Section("Readings");
        foreach (var reading in Order)
        {
            if (reading == Reading.Battery && !_battery || reading == Reading.ThermalZone && !_zones || reading == Reading.Gpu && !_hasGpu) continue;
            readings.Children.Add(Row(ReadingName(reading), Switch(ReadingName(reading), s => s.IsOn(reading), (s, on) =>
                s with { Off = on ? s.Off.Where(r => r != reading).ToList() : s.Off.Append(reading).Distinct().ToList() })));
        }

        var units = Section("Units");
        if (_gpus.Count > 0 || _zones)
            units.Children.Add(Row("Temperature", Choice("Temperature", Enum.GetValues<TemperatureUnit>(),
                v => v.ToString(), s => s.Temperature, (s, v) => s with { Temperature = v })));
        units.Children.Add(Row("Network speed", Choice("Network speed", Enum.GetValues<NetworkUnit>(),
            v => v == NetworkUnit.Bytes ? "Bytes per second" : "Bits per second", s => s.NetworkSpeed, (s, v) => s with { NetworkSpeed = v })));

        var alerts = Section("Alerts");
        Percent(alerts, "CPU load", s => s.CpuLoad, (s, a) => s with { CpuLoad = a }, 50, 100);
        Percent(alerts, "Memory in use", s => s.Memory, (s, a) => s with { Memory = a }, 50, 100);
        Percent(alerts, "Low disk space", s => s.LowDisk, (s, a) => s with { LowDisk = a }, 5, 30, " free");
        if (_battery) Percent(alerts, "Low battery", s => s.LowBattery, (s, a) => s with { LowBattery = a }, 5, 50);
        if (_gpus.Count > 0)
        {
            var toggle = Switch("GPU temperature", s => s.GpuTemperature.On, (s, on) =>
                s with { GpuTemperature = s.GpuTemperature with { On = on } });
            _sync.Add(s => toggle.IsEnabled = _gpus.Keys.Any(s.GpuTemperature.Limits.ContainsKey));
            var summary = Text("");
            // The switch waits for a limit, so say so rather than leave it dim without a reason.
            _sync.Add(s => summary.Text = _gpus.Keys.Any(s.GpuTemperature.Limits.ContainsKey)
                ? string.Join(", ", _gpus.Keys.Where(s.GpuTemperature.Limits.ContainsKey).Select(id => Format.Temperature(s.GpuTemperature.Limits[id], s.Temperature)))
                : "Set a limit");
            alerts.Children.Add(SettingsLayout.Expander("GPU temperature", summary, toggle, [.. _gpus.Select(g => GpuLimit(g.Key, g.Value))]));
        }
        alerts.Children.Add(Row("Repeat interval", Choice("Repeat interval", SystemMonitorSettings.RepeatChoices,
            n => n % 60 == 0 ? $"{n / 60} hour" + (n == 60 ? "" : "s") : $"{n} minutes", s => s.RepeatMinutes, (s, n) => s with { RepeatMinutes = n })));
        _updating = false;
        Refresh();
    }

    private void Percent(Card card, string name, Func<SystemMonitorSettings, PercentAlert> get,
        Func<SystemMonitorSettings, PercentAlert, SystemMonitorSettings> set, int min, int max, string suffix = "")
    {
        var toggle = Switch(name, s => get(s).On, (s, on) => set(s, get(s) with { On = on }));
        var slider = new Slider { Minimum = min, Maximum = max, SmallChange = 5, LargeChange = 5, TickFrequency = 5,
            IsSnapToTickEnabled = true, Width = 200 };
        slider.SetResourceReference(StyleProperty, "HopperSliderStyle");
        AutomationProperties.SetName(slider, name + " threshold");
        var value = Text("");
        value.MinWidth = 72;
        value.TextAlignment = TextAlignment.Right;
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(slider);
        controls.Children.Add(value);
        var row = Row("Threshold", controls, sub: true);
        var summary = Text("");
        card.Children.Add(SettingsLayout.Expander(name, summary, toggle, row));
        _sync.Add(s =>
        {
            slider.Value = get(s).Percent;
            value.Text = $"{get(s).Percent}%{suffix}";
            summary.Text = value.Text;
            row.IsEnabled = slider.IsEnabled = get(s).On;
            row.Opacity = row.IsEnabled ? 1 : 0.45;
        });
        slider.ValueChanged += (_, _) => Save(s => set(s, get(s) with { Percent = (int)slider.Value }));
    }

    private FrameworkElement GpuLimit(string id, string name)
    {
        var input = new TextBox { Width = 90, VerticalContentAlignment = VerticalAlignment.Center };
        input.SetResourceReference(StyleProperty, "TextFieldStyle");
        AutomationProperties.SetName(input, name + " temperature limit");
        var error = Text("40 to 110 °C");
        error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        var number = new StackPanel();
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(input);
        var unit = Text("°C");
        unit.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(unit);
        number.Children.Add(line);
        number.Children.Add(error);
        _sync.Add(s =>
        {
            if (!input.IsKeyboardFocusWithin)
                input.Text = s.GpuTemperature.Limits.TryGetValue(id, out int limit) ? limit.ToString(CultureInfo.CurrentCulture) : "Not set";
        });
        void Commit()
        {
            string text = input.Text.Trim();
            bool empty = text.Length == 0 || text == "Not set";
            if (!empty && (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int n) || n < 40 || n > 110))
            { error.Visibility = Visibility.Visible; return; }
            error.Visibility = Visibility.Collapsed;
            Save(s =>
            {
                var limits = new Dictionary<string, int>(s.GpuTemperature.Limits, StringComparer.OrdinalIgnoreCase);
                if (empty) limits.Remove(id); else limits[id] = int.Parse(text, CultureInfo.CurrentCulture);
                bool hasVisibleLimit = _gpus.Keys.Any(limits.ContainsKey);
                return s with { GpuTemperature = new() { Limits = limits, On = s.GpuTemperature.On && hasVisibleLimit } };
            });
        }
        input.GotKeyboardFocus += (_, _) => { if (input.Text == "Not set") input.Clear(); else input.SelectAll(); };
        input.LostKeyboardFocus += (_, _) => Commit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        return Row(name, number, sub: true);
    }

    private ToggleButton Switch(string name, Func<SystemMonitorSettings, bool> get,
        Func<SystemMonitorSettings, bool, SystemMonitorSettings> set)
    {
        var toggle = new ToggleButton();
        toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(toggle, name);
        _sync.Add(s => { toggle.IsChecked = get(s); toggle.Content = get(s) ? "On" : "Off"; });
        toggle.Checked += (_, _) => Save(s => set(s, true));
        toggle.Unchecked += (_, _) => Save(s => set(s, false));
        return toggle;
    }

    private ComboBox Choice<T>(string name, IEnumerable<T> choices, Func<T, string> label,
        Func<SystemMonitorSettings, T> get, Func<SystemMonitorSettings, T, SystemMonitorSettings> set) where T : notnull
    {
        var combo = Combo(name);
        foreach (var choice in choices) combo.Items.Add(new ComboBoxItem { Tag = choice, Content = label(choice) });
        _sync.Add(s => combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, get(s))));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: T choice }) Save(s => set(s, choice)); };
        return combo;
    }

    // The latest snapshot with readings, for which GPU and adapter the dropdowns show.
    private SystemSnapshot Readings() => _module.Sampler?.Latest is { } latest ? SystemView.Opening(latest, _module.PageSnapshot) : SystemSnapshot.Empty(DateTimeOffset.Now);

    private ComboBox Device(string name, IReadOnlyList<(string Id, string Name)> choices, Func<SystemMonitorSettings, string?> get,
        Func<SystemMonitorSettings, string, SystemMonitorSettings> set)
    {
        var combo = Combo(name);
        combo.Width = 240;
        var names = Parts.Distinct(choices.Select(c => c.Name));
        for (int i = 0; i < choices.Count; i++)
            combo.Items.Add(new ComboBoxItem { Tag = choices[i].Id, Content = names[i], ToolTip = choices[i].Name });
        _sync.Add(s => combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, get(s), StringComparison.OrdinalIgnoreCase)));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string id }) Save(s => set(s, id)); };
        return combo;
    }

    private static ComboBox Combo(string name)
    {
        var combo = new ComboBox { Width = SettingsLayout.ComboWidth };
        combo.SetResourceReference(StyleProperty, "HopperComboBoxStyle");
        AutomationProperties.SetName(combo, name);
        return combo;
    }

    private static SettingRow Row(string name, object content, bool sub = false) =>
        SettingsLayout.Row(name, (UIElement)content);

    private static Card NewCard() => SettingsLayout.Card();

    private Card Section(string name)
    {
        _body.Children.Add(SettingsLayout.Section(name));
        var card = NewCard();
        _body.Children.Add(card);
        return card;
    }

    private static TextBlock Text(string text)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(StyleProperty, "BodyTextStyle");
        return block;
    }

    /// <summary>The readings in the page's order.</summary>
    internal static readonly Reading[] Order = [Reading.Cpu, Reading.Gpu, Reading.Memory, Reading.Network, Reading.Disks, Reading.Battery, Reading.ThermalZone];

    private static string ReadingName(Reading reading) => reading switch
    {
        Reading.Cpu => "CPU", Reading.Gpu => "GPU", Reading.Disks => "Drives", Reading.ThermalZone => "Temperature", _ => reading.ToString(),
    };

    /// <summary>One drive in Settings: its letter, "412 GiB free of 931 GiB", and a storage bar of what's used.</summary>
    private sealed class DriveRow : Border
    {
        private readonly TextBlock _letter = Parts.Text("", 14, Look.Settings.Text), _space = Parts.Text("", 12, Look.Settings.Secondary);
        private readonly Meter _meter = new(Look.Settings) { Margin = new Thickness(0, 8, 0, 0) };

        public DriveRow()
        {
            Padding = new Thickness(16, 12, 16, 14);
            DockPanel.SetDock(_space, Dock.Right);
            Child = new StackPanel { Children = { new DockPanel { Children = { _space, _letter } }, _meter } };
        }

        public void Set(VolumeReading volume, bool caution)
        {
            _letter.Text = DrivesWidget.Letter(volume);
            _space.Text = $"{Format.Size(volume.FreeBytes)} free of {Format.Size(volume.TotalBytes)}";
            _space.SetResourceReference(TextBlock.ForegroundProperty, caution ? Look.Caution : Look.Settings.Secondary);
            _meter.Set(volume.TotalBytes == 0 ? 0 : 1 - (double)volume.FreeBytes / volume.TotalBytes, caution);
            AutomationProperties.SetName(this, $"{_letter.Text}, {_space.Text}");
        }
    }
}
