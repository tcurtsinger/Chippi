// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Sensors.Platform;
using Chippi.UI.Controls;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// One reading's page on the island: its value with one more reading beside it, a graph of the last
/// 120 readings and a strip under it, then its top apps and facts (or its adapters, drives or zones).
/// Built once per reading and updated in place.
/// </summary>
internal sealed class DetailPage : Grid
{
    private const double TopHeight = 166, TallTopHeight = 192, GraphHeight = 76, TallGraphHeight = 128;

    private readonly SystemMonitorModule _module;
    private readonly Present _present;
    private readonly Hero _hero, _second;
    private readonly TextBlock _side = Parts.Text("", 12, Look.Island.Secondary);
    private readonly HistoryGraph _graph = new();
    private readonly BarStrip? _cores;
    private readonly MeterLine? _meterLine;
    private readonly AppList? _apps;
    private readonly FactList? _facts;
    private readonly AdapterList? _adapters;
    private readonly ZoneGrid? _zones;
    private readonly DrivesDetail? _drives;

    public DetailPage(Detail kind, SystemMonitorModule module, Present present)
    {
        Kind = kind;
        _module = module;
        _present = present;
        Width = IslandPage.BodyWidth;
        Height = IslandPage.BodyHeight;
        _hero = new Hero(Look.Island, 32, 18);
        _second = new Hero(Look.Island, 32, 18) { Margin = new Thickness(24, 0, 0, 0) };
        if (kind == Detail.Drives)
        {
            _drives = new DrivesDetail(module);
            Children.Add(_drives);
            return;
        }
        bool tall = kind is Detail.Battery or Detail.Temperature;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(tall ? TallTopHeight : TopHeight) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(IslandPage.Gap) });
        RowDefinitions.Add(new RowDefinition());

        // The value, with a second reading at the right (network: upload as a second value).
        DockPanel.SetDock(_side, Dock.Right);
        _side.VerticalAlignment = VerticalAlignment.Bottom;
        _side.Margin = new Thickness(12, 0, 0, 5);
        var values = new StackPanel { Orientation = Orientation.Horizontal };
        if (kind == Detail.Network)
        {
            values.Children.Add(Arrow(LucideIcons.ArrowDown));
            values.Children.Add(_hero);
            var up = Arrow(LucideIcons.ArrowUp);
            up.Margin = new Thickness(24, 0, 4, 0);
            _second.Margin = new Thickness(0);
            values.Children.Add(up);
            values.Children.Add(_second);
        }
        else
            values.Children.Add(_hero);
        var top = new StackPanel();
        top.Children.Add(new DockPanel { Height = 36, Children = { _side, values } });
        _graph.Height = tall ? TallGraphHeight : GraphHeight;
        _graph.Margin = new Thickness(0, 8, 0, 0);
        top.Children.Add(_graph);
        FrameworkElement? strip = kind switch
        {
            Detail.Cpu => _cores = new BarStrip(),
            Detail.Gpu => _meterLine = new MeterLine("Dedicated memory"),
            Detail.Memory => _meterLine = new MeterLine("Committed"),
            Detail.Network => Legend(),
            _ => null,
        };
        if (strip is not null)
        {
            strip.Height = 18;
            strip.Margin = new Thickness(0, 8, 0, 0);
            top.Children.Add(strip);
        }
        var card = Parts.Card(top);
        Children.Add(card);

        // Under it: top apps beside the facts, or one card across.
        FrameworkElement bottom;
        if (kind is Detail.Cpu or Detail.Gpu or Detail.Memory)
        {
            _apps = new AppList();
            _facts = new FactList();
            var pair = new Grid();
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandPage.Gap) });
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            var facts = Parts.Card(_facts);
            SetColumn(facts, 2);
            pair.Children.Add(Parts.Card(_apps));
            pair.Children.Add(facts);
            bottom = pair;
        }
        else if (kind == Detail.Network)
            bottom = Parts.Card(_adapters = new AdapterList(module));
        else if (kind == Detail.Temperature)
            bottom = Parts.Card(_zones = new ZoneGrid());
        else
            bottom = Parts.Card(_facts = new FactList());
        SetRow(bottom, 2);
        Children.Add(bottom);

        FrameworkElement Arrow(Geometry data)
        {
            var glyph = Parts.Glyph(data, 18, Look.Island.Tertiary);
            glyph.Margin = new Thickness(0, 0, 4, 0);
            return glyph;
        }
    }

    public Detail Kind { get; }

    public string Title => Kind switch
    {
        Detail.Cpu => "CPU", Detail.Gpu => "GPU", Detail.Network => "Network", Detail.Drives => "Drives",
        Detail.Battery => "Battery", Detail.Temperature => "Temperature", _ => "Memory",
    };

    internal Hero Hero => _hero;

    internal string Side => _side.Text;

    internal HistoryGraph Graph => _graph;

    internal BarStrip? Cores => _cores;

    internal AppList? Apps => _apps;

    internal FactList? Facts => _facts;

    internal AdapterList? Adapters => _adapters;

    internal DrivesDetail? Drives => _drives;

    internal ZoneGrid? Zones => _zones;

    /// <summary>What this page needs read on top of the overview's readings.</summary>
    public SensorNeed Need => Kind switch
    {
        Detail.Cpu => SensorNeed.PerProcessor | SensorNeed.Processes,
        Detail.Gpu or Detail.Memory => SensorNeed.Processes,
        _ => SensorNeed.None,
    };

    public void Update(SystemSnapshot s)
    {
        var settings = _module.Settings;
        var sampler = _module.Sampler;
        IReadOnlyList<HistoryPoint> History(string key) => sampler?.History(key) ?? [];
        var processes = s.Processes;
        switch (Kind)
        {
            case Detail.Cpu:
            {
                var cpu = s.Cpu.Value;
                bool caution = _module.Alerting("cpu-load", "cpu");
                _hero.Set(s.Cpu.Status == ReadingStatus.Unavailable ? "Unavailable" : cpu is null ? "" : Format.Percent(cpu.Utility), caution);
                _side.Text = cpu?.EffectiveMhz is { } mhz ? Format.Clock(mhz) : "";
                _graph.Set(History(Series.Cpu), 100, "100%", caution: caution);
                if (cpu?.PerProcessor is { } each) _cores!.Set(each);
                _apps!.Set(processes, TopApps.Cpu, Share);
                _facts!.Set(
                [
                    ("Processor", Machine.ProcessorName is { } processor ? Parts.ProcessorName(processor) : ""),
                    ("Logical processors", cpu is null ? "" : cpu.LogicalProcessors.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                    ("Up time", Parts.UpTime(Machine.UpTime)),
                ]);
                break;
            }
            case Detail.Gpu:
            {
                var gpu = _present.Gpu(s);
                _hero.Set(s.Gpus.Status == ReadingStatus.Unavailable ? "Unavailable" : gpu?.Load is { } load ? Format.Percent(load) : "");
                bool hot = gpu is not null && _module.Alerting("gpu-temperature", gpu.Id);
                _side.Text = gpu?.TemperatureC is { } t ? Format.Temperature(t, settings.Temperature) : "";
                _side.SetResourceReference(TextBlock.ForegroundProperty, hot ? Look.Caution : Look.Island.Secondary);
                _graph.Set(gpu is null ? [] : History(Series.GpuLoad(gpu.Id)), 100, "100%");
                bool dedicated = gpu?.DedicatedTotalBytes is > 0;
                _meterLine!.Label.Text = dedicated ? "Dedicated memory" : "Shared memory";
                _meterLine.Amount.Text = gpu is null ? "" : dedicated
                    ? gpu.DedicatedUsedBytes is { } used ? Format.Of(used, gpu.DedicatedTotalBytes!.Value) : ""
                    : gpu.SharedUsedBytes is { } shared ? Format.Size(shared) : "";
                _meterLine.Meter.Visibility = dedicated ? Visibility.Visible : Visibility.Hidden;
                if (dedicated) _meterLine.Meter.Set((double)(gpu!.DedicatedUsedBytes ?? 0) / gpu.DedicatedTotalBytes!.Value);
                _apps!.Set(processes, list => TopApps.Gpu(list, gpu?.ProcessLoads), Share);
                var facts = new List<(string, string)>();
                if (_present.Gpus(s).Count <= 1 && gpu is not null) facts.Add(("Name", gpu.Name));
                if (Fan(gpu) is { } fan) facts.Add(("Fan", fan));
                if (dedicated && gpu!.SharedUsedBytes is { } sharedBytes) facts.Add(("Shared memory", Format.Size(sharedBytes)));
                _facts!.Set(facts);
                break;
            }
            case Detail.Memory:
            {
                var memory = s.Memory.Value;
                bool caution = _module.Alerting("memory", "memory");
                _hero.Set(s.Memory.Status == ReadingStatus.Unavailable ? "Unavailable" : memory is null ? "" : Format.Percent(memory.UsedPercent), caution);
                _side.Text = memory is null ? "" : Format.Of(memory.UsedBytes, memory.TotalBytes);
                _graph.Set(History(Series.Memory), 100, "100%", caution: caution);
                _meterLine!.Amount.Text = memory is null ? "" : Format.Of(memory.CommitBytes, memory.CommitLimitBytes);
                _meterLine.Meter.Set(memory is { CommitLimitBytes: > 0 } ? (double)memory.CommitBytes / memory.CommitLimitBytes : 0);
                _apps!.Set(processes, TopApps.Memory, a => Format.Size((ulong)a.Amount));
                _facts!.Set(memory is null ? [] :
                [
                    ("Available", Format.Size(memory.TotalBytes - Math.Min(memory.UsedBytes, memory.TotalBytes))),
                    ("Installed", Format.Size(memory.TotalBytes)),
                ]);
                break;
            }
            case Detail.Network:
            {
                var adapter = _present.Adapter(s);
                bool rates = adapter is { Connected: true, ReceiveBytesPerSecond: not null, SendBytesPerSecond: not null };
                _hero.Set(s.Network.Status == ReadingStatus.Unavailable ? "Unavailable" : rates ? Format.Rate(adapter!.ReceiveBytesPerSecond!.Value, settings.NetworkSpeed)
                    : adapter is { Connected: false } ? "Not connected" : "");
                _second.Set(rates ? Format.Rate(adapter!.SendBytesPerSecond!.Value, settings.NetworkSpeed) : "");
                ((FrameworkElement)_hero.Parent).Visibility = Visibility.Visible;
                foreach (var arrow in ((Panel)_hero.Parent).Children.OfType<LucideIcon>()) arrow.Visibility = rates ? Visibility.Visible : Visibility.Collapsed;
                var down = adapter is null ? [] : History(Series.NetworkReceive(adapter.Id));
                var up = adapter is null ? [] : History(Series.NetworkSend(adapter.Id));
                double scale = RateScale(down.Concat(up).Select(p => p.Value ?? 0).DefaultIfEmpty(0).Max());
                _graph.Set(down, scale, Format.Rate(scale, settings.NetworkSpeed), up);
                _legendName!.Text = adapter is null ? "" : _present.AdapterName(s, adapter);
                _adapters!.Set(s, _present);
                break;
            }
            case Detail.Drives:
                _drives!.Update(s);
                break;
            case Detail.Battery:
            {
                var battery = s.Battery.Value;
                _hero.Set(s.Battery.Status == ReadingStatus.Unavailable ? "Unavailable" : battery?.Percent is { } p ? Format.Percent(p) : "",
                    _module.Alerting("low-battery", "battery"));
                _side.Text = battery is null ? "" : battery.TimeLeft is { } left && battery.State == BatteryState.OnBattery ? Format.TimeLeft(left) : Present.State(battery);
                _graph.Set(History(Series.Battery), 100, "100%");
                _facts!.Set(battery is null ? [] :
                [
                    ("State", Present.State(battery)),
                    ("Rate", battery.RateWatts is { } watts ? Format.Watts(watts) : ""),
                    ("Time left", battery.TimeLeft is { } t && battery.State == BatteryState.OnBattery ? Format.TimeLeft(t)[..^5] : ""),
                ]);
                break;
            }
            default:
            {
                var zones = s.ThermalZones.Value ?? [];
                var hottest = zones.OrderByDescending(z => z.TemperatureC).FirstOrDefault();
                bool fahrenheit = settings.Temperature == TemperatureUnit.Fahrenheit;
                _hero.Set(s.ThermalZones.Status == ReadingStatus.Unavailable ? "Unavailable" : hottest is null ? "" : Format.Temperature(hottest.TemperatureC, settings.Temperature));
                _side.Text = zones.Count > 1 ? $"Hottest of {zones.Count} zones" : "";
                _graph.Set(hottest is null ? [] : History(Series.ThermalZone(hottest.Id)), fahrenheit ? 212 : 100,
                    Format.Temperature(100, settings.Temperature), convert: fahrenheit ? c => c * 9 / 5 + 32 : null);
                _zones!.Set(zones, settings.Temperature);
                break;
            }
        }
    }

    /// <summary>An app's share of the CPU or a GPU: "6.1%" under 10, "26%" above, so a small share never reads "0%".</summary>
    private static string Share(AppUse app) =>
        app.Amount.ToString(app.Amount < 10 ? "0.0" : "0", System.Globalization.CultureInfo.CurrentCulture) + "%";

    /// <summary>"1,450 RPM · 38%"; null when the driver reports neither.</summary>
    private static string? Fan(GpuReading? gpu)
    {
        var parts = new List<string>();
        if (gpu?.FanRpm is { } rpm) parts.Add(Format.Rpm(rpm));
        if (gpu?.FanPercent is { } percent) parts.Add(Format.Percent(percent));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The graph's top: the next of 64 KiB/s, 128, 256… above the busiest point, so lines sit low but never clip.</summary>
    internal static double RateScale(double busiest)
    {
        double scale = 64 * 1024;
        while (scale < busiest) scale *= 2;
        return scale;
    }

    private TextBlock? _legendName;

    private DockPanel Legend()
    {
        StackPanel Key(string text, bool dashed)
        {
            // The graph's own strokes: the accent line, and the dashed one at the graph's dash.
            var swatch = new System.Windows.Shapes.Line { X2 = 14, Y1 = 1, Y2 = 1, StrokeThickness = 2, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (dashed) swatch.StrokeDashArray = [1.2, 1.2];
            swatch.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, dashed ? Look.Island.Secondary : "IslandAccentBrush");
            return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), Children = { swatch, Parts.Text(text, 11, Look.Island.Tertiary) } };
        }
        _legendName = Parts.Text("", 11, Look.Island.Tertiary);
        DockPanel.SetDock(_legendName, Dock.Right);
        return new DockPanel { LastChildFill = false, Children = { _legendName, Key("Receive", false), Key("Send", true) } };
    }
}

/// <summary>A small card label: 11, tertiary, with an optional note at the right.</summary>
internal sealed class CardLabel : DockPanel
{
    public CardLabel(string text)
    {
        Height = 16;
        Note = Parts.Text("", 11, Look.Island.Tertiary);
        SetDock(Note, Dock.Right);
        Children.Add(Note);
        Children.Add(Parts.Text(text, 11, Look.Island.Tertiary));
    }

    public TextBlock Note { get; }
}

/// <summary>Rows of 28 with hairlines between them.</summary>
internal static class RowLines
{
    public static Grid Row(bool first, double height = 28)
    {
        var row = new Grid { Height = height };
        if (!first)
        {
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
            line.SetResourceReference(Border.BackgroundProperty, Look.Island.Hair);
            Grid.SetColumnSpan(line, 8);
            row.Children.Add(line);
        }
        return row;
    }
}

/// <summary>The top 3 apps: icon, name and amount.</summary>
internal sealed class AppList : StackPanel
{
    private readonly List<(Grid Row, ContentControl Icon, TextBlock Name, TextBlock Amount)> _rows = [];
    private readonly TextBlock _status = Parts.Text("", 12, Look.Island.Tertiary);

    public AppList()
    {
        Children.Add(new CardLabel("Top apps"));
        _status.Height = 28;
        Children.Add(_status);
        for (int i = 0; i < TopApps.Count; i++)
        {
            var row = RowLines.Row(i == 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new ContentControl { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, IsTabStop = false };
            var name = Parts.Text("", 12, Look.Island.Text);
            var amount = Parts.Text("", 12, Look.Island.Text, semibold: true);
            amount.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(amount, 2);
            row.Children.Add(icon);
            row.Children.Add(name);
            row.Children.Add(amount);
            Children.Add(row);
            _rows.Add((row, icon, name, amount));
        }
    }

    /// <summary>The apps shown, "Name Amount" (tests read them).</summary>
    internal IReadOnlyList<string> Shown => [.. _rows.Where(r => r.Row.Visibility == Visibility.Visible).Select(r => $"{r.Name.Text} {r.Amount.Text}")];

    internal string Status => _status.Visibility == Visibility.Visible ? _status.Text : "";

    public void Set(Sample<IReadOnlyList<ProcessReading>> processes, Func<IReadOnlyList<ProcessReading>, IReadOnlyList<AppUse>> rank, Func<AppUse, string> amount)
    {
        bool failed = processes.Status == ReadingStatus.Unavailable;
        // Until the first read, keep what's there rather than blanking the rows.
        if (processes.Status is ReadingStatus.NotRead or ReadingStatus.Pending)
            return;
        var apps = failed ? [] : rank(processes.Value ?? []);
        _status.Text = failed ? "Unavailable" : "";
        _status.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _rows.Count; i++)
        {
            var (row, icon, name, value) = _rows[i];
            row.Visibility = i < apps.Count ? Visibility.Visible : failed ? Visibility.Collapsed : Visibility.Hidden;
            if (i >= apps.Count)
                continue;
            var app = apps[i];
            if (name.Text != app.Name || icon.Tag as string != app.App)
            {
                icon.Tag = app.App;
                icon.Content = Icon(app);
            }
            name.Text = app.Name;
            name.ToolTip = app.Name;
            value.Text = amount(app);
            AutomationProperties.SetName(row, $"{app.Name}, {value.Text}");
        }
    }

    // The program's own icon, or its initials on a quiet tile.
    private static FrameworkElement Icon(AppUse app)
    {
        if (AppIcon.Load(app.Path) is { } image)
            return new Image { Source = image, Width = 16, Height = 16 };
        var tile = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4) };
        tile.SetResourceReference(Border.BackgroundProperty, "IslandMutedFillBrush");
        tile.Child = new TextBlock { Text = Parts.Initials(app.Name), FontSize = 8, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ((TextBlock)tile.Child).SetResourceReference(TextBlock.ForegroundProperty, Look.Island.Text);
        return tile;
    }
}

/// <summary>Name and value rows; an empty value hides its row.</summary>
internal sealed class FactList : StackPanel
{
    private readonly List<(Grid Row, TextBlock Name, TextBlock Value)> _rows = [];

    /// <summary>The facts shown, "Name Value" (tests read them).</summary>
    internal IReadOnlyList<string> Shown => [.. _rows.Where(r => r.Row.Visibility == Visibility.Visible).Select(r => $"{r.Name.Text} {r.Value.Text}")];

    public void Set(IReadOnlyList<(string Name, string Value)> facts)
    {
        var shown = facts.Where(f => f.Value.Length > 0).ToList();
        while (_rows.Count < shown.Count)
        {
            var row = RowLines.Row(_rows.Count == 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var name = Parts.Text("", 12, Look.Island.Tertiary);
            var value = Parts.Text("", 12, Look.Island.Text, semibold: true);
            value.TextAlignment = TextAlignment.Right;
            value.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(value, 1);
            row.Children.Add(name);
            row.Children.Add(value);
            Children.Add(row);
            _rows.Add((row, name, value));
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            var (row, name, value) = _rows[i];
            row.Visibility = i < shown.Count ? Visibility.Visible : Visibility.Collapsed;
            if (i >= shown.Count)
                continue;
            name.Text = shown[i].Name;
            value.Text = shown[i].Value;
            value.ToolTip = shown[i].Value;
        }
    }
}

/// <summary>The network page's adapters: one row each, the one graphed tinted; a click graphs another.</summary>
internal sealed class AdapterList : StackPanel
{
    public const int Rows = 3;
    private readonly SystemMonitorModule _module;
    private readonly CardLabel _label = new("Adapters");
    private readonly StackPanel _rows = new();
    private string _key = "";

    public AdapterList(SystemMonitorModule module)
    {
        _module = module;
        Children.Add(_label);
        _rows.Margin = new Thickness(-8, 2, -8, 0);
        Children.Add(_rows);
    }

    internal IReadOnlyList<string> Shown => [.. _rows.Children.OfType<Border>().Select(b => AutomationProperties.GetName((Button)b.Child))];

    public void Set(SystemSnapshot s, Present present)
    {
        var adapters = s.Network.Value ?? [];
        var chosen = present.Adapter(s);
        var shown = adapters.Take(Rows).ToList();
        _label.Note.Text = adapters.Count > Rows ? $"+{adapters.Count - Rows} more in Settings" : "";
        string key = string.Join("|", shown.Select(a => a.Id));
        if (key != _key)
        {
            _key = key;
            _rows.Children.Clear();
            foreach (var adapter in shown)
                _rows.Children.Add(Row(adapter.Id));
        }
        var names = Parts.Distinct(adapters.Select(a => a.Name));
        for (int i = 0; i < shown.Count; i++)
        {
            var adapter = shown[i];
            var border = (Border)_rows.Children[i];
            var (name, down, up, off) = ((TextBlock, TextBlock, TextBlock, TextBlock))border.Tag;
            bool on = adapter.Id == chosen?.Id;
            if (on) border.SetResourceReference(Border.BackgroundProperty, "IslandAccentTintBrush");
            else border.Background = null;
            name.Text = names[i];
            bool rates = adapter is { Connected: true, ReceiveBytesPerSecond: not null, SendBytesPerSecond: not null };
            down.Text = rates ? Format.Rate(adapter.ReceiveBytesPerSecond!.Value, _module.Settings.NetworkSpeed) : "";
            up.Text = rates ? Format.Rate(adapter.SendBytesPerSecond!.Value, _module.Settings.NetworkSpeed) : "";
            off.Text = adapter.Connected ? "" : "Not connected";
            ((FrameworkElement)down.Parent).Visibility = ((FrameworkElement)up.Parent).Visibility = rates ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName((Button)border.Child, rates ? $"{names[i]}, down {down.Text}, up {up.Text}" : $"{names[i]}, {off.Text}".TrimEnd(',', ' '));
        }
    }

    private Border Row(string id)
    {
        var name = Parts.Text("", 12, Look.Island.Text);
        var down = Parts.Text("", 12, Look.Island.Secondary);
        var up = Parts.Text("", 12, Look.Island.Secondary);
        var off = Parts.Text("", 12, Look.Island.Tertiary);
        StackPanel Rate(Geometry arrow, TextBlock text)
        {
            var glyph = Parts.Glyph(arrow, 11, Look.Island.Tertiary);
            glyph.Margin = new Thickness(0, 0, 3, 0);
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = 96, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Right };
            panel.Children.Add(glyph);
            panel.Children.Add(text);
            return panel;
        }
        var downRate = Rate(LucideIcons.ArrowDown, down);
        var upRate = Rate(LucideIcons.ArrowUp, up);
        var grid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(downRate, 1);
        Grid.SetColumn(upRate, 2);
        Grid.SetColumn(off, 3);
        grid.Children.Add(name);
        grid.Children.Add(downRate);
        grid.Children.Add(upRate);
        grid.Children.Add(off);
        var button = new Button { Content = grid, Height = 30, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        button.SetResourceReference(StyleProperty, "IslandBareButtonStyle");
        button.Click += (_, _) => _module.Save(_module.Settings with { NetworkAdapterId = id });
        return new Border { CornerRadius = new CornerRadius(4), Child = button, Tag = (name, down, up, off) };
    }
}

/// <summary>Every thermal zone as a small tile, 4 to a row.</summary>
internal sealed class ZoneGrid : StackPanel
{
    public const int Max = 8;
    private readonly WrapPanel _tiles = new() { Margin = new Thickness(0, 6, -8, 0) };
    private readonly CardLabel _label = new("Thermal zones");

    public ZoneGrid()
    {
        Children.Add(_label);
        Children.Add(_tiles);
    }

    internal IReadOnlyList<string> Shown => [.. _tiles.Children.OfType<Border>().Select(b => AutomationProperties.GetName(b))];

    public void Set(IReadOnlyList<ThermalZoneReading> zones, TemperatureUnit unit)
    {
        _label.Note.Text = zones.Count > Max ? $"+{zones.Count - Max} more in Settings" : "";
        var shown = zones.Take(Max).ToList();
        while (_tiles.Children.Count > shown.Count) _tiles.Children.RemoveAt(_tiles.Children.Count - 1);
        while (_tiles.Children.Count < shown.Count)
        {
            var tile = new Border { Width = (IslandPage.BodyWidth - 24 - 3 * 8) / 4, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 5, 8, 5) };
            tile.SetResourceReference(Border.BackgroundProperty, "IslandHoverBrush");
            tile.Child = new StackPanel { Children = { Parts.Text("", 11, Look.Island.Tertiary), Parts.Text("", 14, Look.Island.Text, semibold: true) } };
            _tiles.Children.Add(tile);
        }
        for (int i = 0; i < shown.Count; i++)
        {
            var tile = (Border)_tiles.Children[i];
            var lines = ((StackPanel)tile.Child).Children.OfType<TextBlock>().ToList();
            lines[0].Text = zones.Count > 1 ? $"Zone {i + 1}" : "Thermal zone";
            lines[1].Text = Format.Temperature(shown[i].TemperatureC, unit);
            AutomationProperties.SetName(tile, $"{lines[0].Text} {lines[1].Text}");
        }
    }
}

/// <summary>The drives page: each volume's free space with a meter, then each disk's activity.</summary>
internal sealed class DrivesDetail : Grid
{
    private const int ActivityRows = 4;
    private readonly SystemMonitorModule _module;
    private readonly StackPanel _space = new(), _activity = new();
    private readonly CardLabel _spaceLabel = new("Space"), _activityLabel = new("Activity");
    private readonly Border _activityCard;
    private readonly TextBlock _status = Parts.Text("", 12, Look.Island.Tertiary);

    public DrivesDetail(SystemMonitorModule module)
    {
        _module = module;
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(IslandPage.Gap) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _activityLabel.Note.Text = "Read · Write";
        var space = new StackPanel { Children = { _spaceLabel, _status, _space } };
        _status.Height = 28;
        Children.Add(Parts.Card(space));
        _activityCard = Parts.Card(new StackPanel { Children = { _activityLabel, _activity } });
        SetRow(_activityCard, 2);
        Children.Add(_activityCard);
    }

    internal IReadOnlyList<string> Volumes => [.. _space.Children.OfType<Grid>().Select(g => AutomationProperties.GetName(g))];

    internal IReadOnlyList<string> Disks => [.. _activity.Children.OfType<Grid>().Select(g => AutomationProperties.GetName(g))];

    internal string More => _spaceLabel.Note.Text;

    public void Update(SystemSnapshot s)
    {
        bool failed = s.Disks.Status == ReadingStatus.Unavailable;
        _status.Text = failed ? "Unavailable" : "";
        _status.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        if (s.Disks.Value is not { } disks)
        {
            if (failed) { _space.Children.Clear(); _activity.Children.Clear(); _activityCard.Visibility = Visibility.Collapsed; }
            return;
        }
        // Activity: up to 4 disks, busiest first when there are more.
        var busy = disks.Count <= ActivityRows ? disks : [.. disks.OrderByDescending(d => d.ActivePercent ?? 0).Take(ActivityRows).OrderBy(d => d.Number)];
        _activityCard.Visibility = busy.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Fill(_activity, busy.Count, i => Disk(busy[i], i == 0));
        // Space: as many volumes as fit above it, then "+N more in Settings".
        var volumes = DrivesWidget.Volumes(disks);
        // Each card is 20 of padding and border and a 16 label over its 28 rows.
        double activity = busy.Count == 0 ? 0 : 20 + 16 + busy.Count * 28 + IslandPage.Gap;
        int fits = (int)((IslandPage.BodyHeight - activity - 20 - 16) / 28);
        int count = volumes.Count <= fits ? volumes.Count : Math.Max(0, fits - 1);
        _spaceLabel.Note.Text = volumes.Count > count ? $"+{volumes.Count - count} more in Settings" : "";
        Fill(_space, count, i => Volume(volumes[i]));
    }

    private static void Fill(StackPanel panel, int count, Func<int, Grid> row)
    {
        panel.Children.Clear();
        for (int i = 0; i < count; i++)
            panel.Children.Add(row(i));
    }

    private Grid Volume(VolumeReading volume)
    {
        bool caution = _module.Alerting("low-disk", volume.Id);
        var row = new Grid { Height = 28 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var letter = Parts.Text(DrivesWidget.Letter(volume), 12, Look.Island.Secondary);
        var meter = new Meter(Look.Island) { VerticalAlignment = VerticalAlignment.Center };
        meter.Set(volume.TotalBytes == 0 ? 0 : 1 - (double)volume.FreeBytes / volume.TotalBytes, caution);
        var text = Parts.Text("", 11, Look.Island.Tertiary);
        text.Margin = new Thickness(12, 0, 0, 0);
        var free = new Run(Format.Size(volume.FreeBytes)) { FontSize = 12, FontWeight = FontWeights.SemiBold };
        free.SetResourceReference(TextElement.ForegroundProperty, caution ? Look.Caution : Look.Island.Text);
        text.Inlines.Add(free);
        text.Inlines.Add(new Run(" free of " + Format.Size(volume.TotalBytes)));
        Grid.SetColumn(meter, 1);
        Grid.SetColumn(text, 2);
        row.Children.Add(letter);
        row.Children.Add(meter);
        row.Children.Add(text);
        AutomationProperties.SetName(row, $"{DrivesWidget.Letter(volume)} {Format.Size(volume.FreeBytes)} free of {Format.Size(volume.TotalBytes)}");
        return row;
    }

    private Grid Disk(DiskReading disk, bool first)
    {
        var row = RowLines.Row(first);
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        var name = Parts.Text("", 12, Look.Island.Text);
        name.Inlines.Add(new Run($"Disk {disk.Number}"));
        string letters = string.Join(" ", disk.Volumes.Where(v => v.Letter.Length > 0).Select(v => v.Letter));
        if (letters.Length > 0)
        {
            var run = new Run(" · " + letters);
            run.SetResourceReference(TextElement.ForegroundProperty, Look.Island.Tertiary);
            name.Inlines.Add(run);
        }
        string active = disk.ActivePercent is { } a ? Format.Percent(a) + " active" : "";
        string io = disk.ReadBytesPerSecond is { } r && disk.WriteBytesPerSecond is { } w
            ? Format.Rate(r, NetworkUnit.Bytes) + " · " + Format.Rate(w, NetworkUnit.Bytes) : "";
        var activeText = Parts.Text(active, 12, Look.Island.Secondary);
        activeText.TextAlignment = TextAlignment.Right;
        var ioText = Parts.Text(io, 12, Look.Island.Secondary);
        ioText.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(activeText, 1);
        Grid.SetColumn(ioText, 2);
        row.Children.Add(name);
        row.Children.Add(activeText);
        row.Children.Add(ioText);
        AutomationProperties.SetName(row, string.Join(", ", new[] { disk.Name, active, io }.Where(t => t.Length > 0)));
        return row;
    }
}
