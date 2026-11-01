// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.UI.Controls;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// What each reading's widget shows, for the island's overview and Settings alike. Remembers how many
/// thermal zones there were, so a failed read keeps its widget.
/// </summary>
internal sealed class Present(SystemMonitorModule module)
{
    private int _zoneCount;
    private SystemSnapshot? _gpusOf;
    private IReadOnlyList<GpuReading> _gpus = [];

    public SystemMonitorSettings Settings => module.Settings;

    /// <summary>A series' last 120 readings, which the module keeps for every reading the overview graphs.</summary>
    public IReadOnlyList<HistoryPoint> History(string key) => module.Sampler?.History(key) ?? [];

    /// <summary>The GPUs that have reported load this session, in the order the snapshot lists them; worked out once a snapshot.</summary>
    public IReadOnlyList<GpuReading> Gpus(SystemSnapshot s)
    {
        if (ReferenceEquals(s, _gpusOf))
            return _gpus;
        foreach (var g in s.Gpus.Value ?? []) if (g.Load is not null) module.GpusWithLoad.Add(g.Id);
        _gpusOf = s;
        return _gpus = [.. (s.Gpus.Value ?? []).Where(g => module.GpusWithLoad.Contains(g.Id))];
    }

    public GpuReading? Gpu(SystemSnapshot s) => Pick.Gpu(Gpus(s), Settings.GpuId, module.GpusWithLoad);

    /// <summary>A GPU's short name, with "#2" when another has the same.</summary>
    public string GpuName(SystemSnapshot s, GpuReading gpu)
    {
        var gpus = Gpus(s);
        var names = Parts.Distinct(gpus.Select(g => Parts.ShortName(g.Name)));
        int i = gpus.ToList().FindIndex(g => g.Id == gpu.Id);
        return i >= 0 ? names[i] : Parts.ShortName(gpu.Name);
    }

    public NetworkReading? Adapter(SystemSnapshot s) => Pick.Adapter(s.Network.Value, Settings.NetworkAdapterId);

    public string AdapterName(SystemSnapshot s, NetworkReading adapter)
    {
        var adapters = s.Network.Value ?? [];
        var names = Parts.Distinct(adapters.Select(a => a.Name));
        int i = adapters.ToList().FindIndex(a => a.Id == adapter.Id);
        return i >= 0 ? names[i] : adapter.Name;
    }

    public bool HasGpu(SystemSnapshot s) =>
        Settings.IsOn(Reading.Gpu) && (Gpus(s).Count > 0 || s.Gpus.Status == ReadingStatus.Unavailable && module.GpusWithLoad.Count > 0);

    public bool HasBattery(SystemSnapshot s) => Settings.IsOn(Reading.Battery) && s.Battery.Status is ReadingStatus.Ok or ReadingStatus.Unavailable;

    public int ZoneCount(SystemSnapshot s)
    {
        if (s.ThermalZones.Status == ReadingStatus.Ok) _zoneCount = s.ThermalZones.Value!.Count;
        return !Settings.IsOn(Reading.ThermalZone) ? 0
            : s.ThermalZones.Status is ReadingStatus.Unavailable or ReadingStatus.Pending or ReadingStatus.NotRead ? _zoneCount
            : s.ThermalZones.Value?.Count ?? 0;
    }

    /// <summary>Whether the overview has <paramref name="kind"/>'s widget for the snapshot (a detail page's reading still on).</summary>
    public bool Shows(SystemSnapshot s, Detail kind)
    {
        var (loads, rest, drives) = Plan(s);
        return kind == Detail.Drives ? drives : loads.Contains(kind) || rest.Contains(kind);
    }

    /// <summary>The widgets the snapshot has, in their rows: loads, then the rest, then drives.</summary>
    public (List<Detail> Loads, List<Detail> Others, bool Drives) Plan(SystemSnapshot s)
    {
        var loads = new List<Detail>();
        if (Settings.IsOn(Reading.Cpu)) loads.Add(Detail.Cpu);
        if (HasGpu(s)) loads.Add(Detail.Gpu);
        if (Settings.IsOn(Reading.Memory)) loads.Add(Detail.Memory);
        var rest = new List<Detail>();
        if (Settings.IsOn(Reading.Network)) rest.Add(Detail.Network);
        if (HasBattery(s)) rest.Add(Detail.Battery);
        if (ZoneCount(s) > 0) rest.Add(Detail.Temperature);
        return (loads, rest, Settings.IsOn(Reading.Disks));
    }

    public void Cpu(ReadingWidget w, SystemSnapshot s)
    {
        bool caution = module.Alerting("cpu-load", "cpu");
        var points = History(Series.Cpu);
        w.Graph?.Set(points, GraphScale.Load(points), caution: caution);
        if (s.Cpu.Status == ReadingStatus.Unavailable) { w.Set("Unavailable", "", null, quiet: true); return; }
        var cpu = s.Cpu.Value;
        w.Set(cpu is null ? "" : Format.Percent(cpu.Utility), cpu?.EffectiveMhz is { } mhz ? Format.Clock(mhz) : "", null, caution);
    }

    public void Gpu(ReadingWidget w, SystemSnapshot s)
    {
        var gpu = Gpu(s);
        if (s.Gpus.Status == ReadingStatus.Unavailable || gpu is null)
        {
            w.SetFact("");
            w.Set(s.Gpus.Status == ReadingStatus.Unavailable ? "Unavailable" : "", "", null, quiet: true);
            return;
        }
        w.SetFact(gpu.TemperatureC is { } t ? Format.Temperature(t, Settings.Temperature) : "", module.Alerting("gpu-temperature", gpu.Id));
        w.Set(gpu.Load is { } load ? Format.Percent(load) : "", GpuName(s, gpu), null);
        w.Context.ToolTip = gpu.Name;
        var points = History(Series.GpuLoad(gpu.Id));
        w.Graph?.Set(points, GraphScale.Load(points));
    }

    public void Memory(ReadingWidget w, SystemSnapshot s)
    {
        bool caution = module.Alerting("memory", "memory");
        w.Graph?.Set(History(Series.Memory), 100, caution: caution);
        if (s.Memory.Status == ReadingStatus.Unavailable) { w.Set("Unavailable", "", null, quiet: true); return; }
        var memory = s.Memory.Value;
        w.Set(memory is null ? "" : Format.Percent(memory.UsedPercent), memory is null ? "" : Format.Of(memory.UsedBytes, memory.TotalBytes), null, caution);
    }

    public void Network(NetworkWidget w, SystemSnapshot s)
    {
        var adapter = Adapter(s);
        string name = adapter is null ? "" : AdapterName(s, adapter);
        var (received, sent, scale) = NetworkHistory(adapter);
        w.Graph.Set(received, scale, sent);
        if (s.Network.Status == ReadingStatus.Unavailable)
            w.Set(name, null, null, "Unavailable");
        else if (adapter is { Connected: true, ReceiveBytesPerSecond: { } down, SendBytesPerSecond: { } up })
            w.Set(name, Format.Rate(down, Settings.NetworkSpeed), Format.Rate(up, Settings.NetworkSpeed));
        else
            w.Set(name, null, null, s.Network.Status is ReadingStatus.Ok or ReadingStatus.NotSupported && adapter is not { Connected: true } ? "Not connected" : "");
    }

    public void Battery(ReadingWidget w, SystemSnapshot s)
    {
        if (s.Battery.Value is not { } battery) { w.Set(s.Battery.Status == ReadingStatus.Unavailable ? "Unavailable" : "", "", null, quiet: true); return; }
        w.Set(battery.Percent is { } p ? Format.Percent(p) : "", BatteryLine(battery), battery.Percent / 100.0, module.Alerting("low-battery", "battery"));
    }

    /// <summary>An adapter's receive and send histories, and the scale that fits them both in the unit shown.</summary>
    public (IReadOnlyList<HistoryPoint> Down, IReadOnlyList<HistoryPoint> Up, double Scale) NetworkHistory(NetworkReading? adapter)
    {
        var down = adapter is null ? [] : History(Series.NetworkReceive(adapter.Id));
        var up = adapter is null ? [] : History(Series.NetworkSend(adapter.Id));
        return (down, up, GraphScale.Rate(GraphScale.Busiest(down.Concat(up)), Settings.NetworkSpeed));
    }

    /// <summary>The hottest zone's history in the unit shown, its scale (by 25 °C or 50 °F) and that scale written out.</summary>
    public (IReadOnlyList<HistoryPoint> Points, double Max, string Label, Func<double, double>? Convert) ZoneHistory(ThermalZoneReading? hottest)
    {
        var points = hottest is null ? [] : History(Series.ThermalZone(hottest.Id));
        bool fahrenheit = Settings.Temperature == TemperatureUnit.Fahrenheit;
        double busiest = GraphScale.Busiest(points);
        double top = fahrenheit ? GraphScale.Temperature(busiest * 9 / 5 + 32, 50) : GraphScale.Temperature(busiest);
        return (points, top, Format.Temperature(fahrenheit ? (top - 32) * 5 / 9 : top, Settings.Temperature), fahrenheit ? c => c * 9 / 5 + 32 : null);
    }

    public static string State(BatteryReading battery) =>
        battery.State == BatteryState.OnBattery ? "On battery" : battery.State == BatteryState.Charging ? "Charging" : "Plugged in";

    /// <summary>"On battery · 3 hr 10 min", "Charging".</summary>
    public static string BatteryLine(BatteryReading battery) =>
        battery.TimeLeft is { } left && battery.State == BatteryState.OnBattery ? State(battery) + " · " + Format.TimeLeft(left)[..^5] : State(battery);

    public void Temperature(ReadingWidget w, SystemSnapshot s)
    {
        int count = ZoneCount(s);
        var (points, max, _, convert) = ZoneHistory(s.ThermalZones.Value?.MaxBy(z => z.TemperatureC));
        w.Graph?.Set(points, max, convert: convert);
        if (s.ThermalZones.Status == ReadingStatus.Unavailable || s.ThermalZones.Value is not { Count: > 0 } zones)
        {
            w.Set(s.ThermalZones.Status == ReadingStatus.Unavailable ? "Unavailable" : "", "", null, quiet: true);
            return;
        }
        w.Set(Format.Temperature(zones.Max(z => z.TemperatureC), Settings.Temperature), count > 1 ? $"Hottest of {count} zones" : "", null);
    }

    public ReadingWidget NewReading(Detail kind, Look look, Action<Detail>? open) => kind switch
    {
        Detail.Cpu => new ReadingWidget(kind, LucideIcons.Cpu, "CPU", look, open),
        Detail.Gpu => new ReadingWidget(kind, Icons.Gpu, "GPU", look, open),
        Detail.Memory => new ReadingWidget(kind, Icons.MemoryStick, "Memory", look, open),
        Detail.Battery => new ReadingWidget(kind, Icons.Battery, "Battery", look, open),
        _ => new ReadingWidget(kind, Icons.Thermometer, "Temperature", look, open),
    };

    public void Fill(Widget widget, SystemSnapshot s)
    {
        switch (widget)
        {
            case NetworkWidget network: Network(network, s); break;
            case DrivesWidget drives: drives.Set(s); break;
            case ReadingWidget reading:
                switch (reading.Kind)
                {
                    case Detail.Cpu: Cpu(reading, s); break;
                    case Detail.Gpu: Gpu(reading, s); break;
                    case Detail.Memory: Memory(reading, s); break;
                    case Detail.Battery: Battery(reading, s); break;
                    default: Temperature(reading, s); break;
                }
                break;
        }
    }
}

/// <summary>
/// The island's System page, in rows by meaning: CPU, GPU and Memory across the top; Network, Battery
/// and Temperature in the next row (Network alone is a 72 strip across the whole width, its graph
/// beside its rates); Drives across the bottom, as many rows of two as it needs, up to 3. The graph
/// rows share what's left of the 304 tall body, and Drives give up a row before a graph row would be
/// under 104.
/// </summary>
internal sealed class OverviewPage : Grid
{
    /// <summary>Network alone in its row.</summary>
    public const double WideNetworkHeight = 72;

    /// <summary>The least a row of graphs gets before Drives give up a row.</summary>
    public const double MinGraphRow = 104;

    private readonly SystemMonitorModule _module;
    private readonly Present _present;
    private readonly Action<Detail> _open;
    private readonly Dictionary<Detail, Widget> _widgets = [];
    private string _key = "";
    private int _volumes = 1;

    public OverviewPage(SystemMonitorModule module, Present present, Action<Detail> open)
    {
        _module = module;
        _present = present;
        _open = open;
        Width = IslandPage.BodyWidth;
        Height = IslandPage.BodyHeight;
    }

    /// <summary>The widgets showing, top row first (tests read them).</summary>
    internal IReadOnlyList<Widget> Widgets { get; private set; } = [];

    internal Widget? WidgetFor(Detail kind) => _widgets.GetValueOrDefault(kind);

    public void Update(SystemSnapshot s)
    {
        var (loads, rest, drives) = _present.Plan(s);
        if (s.Disks.Value is { } disks)
            _volumes = Math.Max(1, DrivesWidget.Volumes(disks).Count);
        int driveRows = drives ? DriveRows(loads.Count > 0, rest, _volumes) : 0;
        string key = string.Join(",", loads) + "|" + string.Join(",", rest) + "|" + driveRows;
        if (key != _key)
        {
            _key = key;
            Layout(loads, rest, driveRows);
        }
        foreach (var widget in Widgets)
            _present.Fill(widget, s);
    }

    /// <summary>Drives' rows: one per two drives, up to 3, giving one up while a graph row would be under 104.</summary>
    internal static int DriveRows(bool loads, IReadOnlyList<Detail> rest, int volumes)
    {
        bool wide = rest is [Detail.Network];
        int graphRows = (loads ? 1 : 0) + (rest.Count > 0 && !wide ? 1 : 0);
        int rows = Math.Clamp((volumes + 1) / 2, 1, 3);
        int gaps = (loads ? 1 : 0) + (rest.Count > 0 ? 1 : 0);
        double left(int r) => IslandPage.BodyHeight - gaps * IslandPage.Gap - (wide ? WideNetworkHeight : 0) - DrivesWidget.Height(r);
        while (rows > 1 && graphRows > 0 && left(rows) / graphRows < MinGraphRow)
            rows--;
        return rows;
    }

    private void Layout(List<Detail> loads, List<Detail> rest, int driveRows)
    {
        Children.Clear();
        RowDefinitions.Clear();
        bool wide = rest is [Detail.Network];
        // A narrow network widget and a wide one are laid out differently.
        if (_widgets.TryGetValue(Detail.Network, out var old) && old is NetworkWidget network && network.Wide != wide)
            _widgets.Remove(Detail.Network);
        var rows = new List<(GridLength Height, List<Widget> Widgets)>();
        if (loads.Count > 0)
            rows.Add((new GridLength(1, GridUnitType.Star), [.. loads.Select(Widget)]));
        if (rest.Count > 0)
            rows.Add((wide ? new GridLength(WideNetworkHeight) : new GridLength(1, GridUnitType.Star), [.. rest.Select(Widget)]));
        if (driveRows > 0)
        {
            var widget = (DrivesWidget)Widget(Detail.Drives);
            widget.Rows = driveRows;
            rows.Add((new GridLength(DrivesWidget.Height(driveRows)), [widget]));
        }
        if (rows.Count == 0)
        {
            Widgets = [];
            Children.Add(Empty());
            return;
        }
        // Without a graph row, the last row takes the rest.
        if (!rows.Any(r => r.Height.IsStar))
            rows[^1] = (new GridLength(1, GridUnitType.Star), rows[^1].Widgets);
        for (int r = 0; r < rows.Count; r++)
        {
            if (r > 0) RowDefinitions.Add(new RowDefinition { Height = new GridLength(IslandPage.Gap) });
            RowDefinitions.Add(new RowDefinition { Height = rows[r].Height });
            var row = new Grid();
            var widgets = rows[r].Widgets;
            for (int c = 0; c < widgets.Count; c++)
            {
                if (c > 0) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandPage.Gap) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var root = widgets[c].Root;
                if (root.Parent is Panel parent) parent.Children.Remove(root);
                SetColumn(root, c * 2);
                row.Children.Add(root);
            }
            SetRow(row, r * 2);
            Children.Add(row);
        }
        Widgets = [.. rows.SelectMany(r => r.Widgets)];

        Widget Widget(Detail kind)
        {
            if (_widgets.TryGetValue(kind, out var widget))
                return widget;
            widget = kind switch
            {
                Detail.Network => new NetworkWidget(Look.Island, _open, wide),
                Detail.Drives => new DrivesWidget(Look.Island, _open, id => _module.Alerting("low-disk", id)),
                _ => _present.NewReading(kind, Look.Island, _open),
            };
            return _widgets[kind] = widget;
        }
    }

    // Every reading switched off: say so, and where to switch them on.
    private FrameworkElement Empty()
    {
        var glyph = Parts.Glyph(LucideIcons.Activity, 20, Look.Island.Tertiary);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Parts.Text("Every reading is off", 12, Look.Island.Tertiary);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, 8, 0, 12);
        var settings = new Button { Content = "Settings", HorizontalAlignment = HorizontalAlignment.Center };
        settings.SetResourceReference(StyleProperty, "IslandButtonStyle");
        settings.Click += (_, _) => _module.OpenSettings();
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { glyph, text, settings } };
    }
}
