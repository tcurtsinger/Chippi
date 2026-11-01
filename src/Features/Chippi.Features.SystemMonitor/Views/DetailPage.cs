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
/// 120 readings out to its card's sides (and bottom, with no strip under it), then its top apps and
/// facts (or its adapters, drives or zones). The cards under the graph take the height their rows
/// need, and the graph takes the rest. Built once per reading and updated in place.
/// </summary>
internal sealed class DetailPage : Grid
{
    private readonly SystemMonitorModule _module;
    private readonly Present _present;
    private readonly Hero _hero, _second;
    private readonly TextBlock _side = Parts.Text("", 12, Look.Island.Secondary), _scale = Parts.Text("", 10, Look.Island.Tertiary);
    private readonly HistoryGraph _graph;
    private readonly FrameworkElement? _under;
    private readonly RowDefinition _gap = new() { Height = new GridLength(IslandPage.Gap) };
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
            _graph = new HistoryGraph(); // the drives page has no graph
            Children.Add(_drives);
            return;
        }
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(_gap);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // The value, and at the right a second reading over the graph's scale, where the line can't run
        // over it (network: upload as a second value).
        _side.HorizontalAlignment = _scale.HorizontalAlignment = HorizontalAlignment.Right;
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 1), Children = { _side, _scale } };
        DockPanel.SetDock(right, Dock.Right);
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
        FrameworkElement? strip = kind switch
        {
            Detail.Cpu => _cores = new BarStrip(),
            Detail.Gpu => _meterLine = new MeterLine("Dedicated memory"),
            Detail.Memory => _meterLine = new MeterLine("Committed"),
            Detail.Network => Legend(),
            _ => null,
        };
        // The value 12 by 10 inside the card; the graph right under it, across the card's whole width,
        // and down to its bottom edge unless a strip sits under it.
        var top = new Grid();
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.RowDefinitions.Add(new RowDefinition());
        top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        top.Children.Add(new DockPanel { Height = 36, Margin = new Thickness(11, 9, 11, 0), Children = { right, values } });
        _graph = new HistoryGraph { Baseline = strip is not null, BottomRadius = strip is null ? Look.Island.InnerRadius : 0 };
        SetRow(_graph, 1);
        top.Children.Add(_graph);
        if (strip is not null)
        {
            strip.Height = 18;
            strip.Margin = new Thickness(11, 8, 11, 9);
            SetRow(strip, 2);
            top.Children.Add(strip);
        }
        var card = Parts.Card(top);
        card.Padding = new Thickness(0);
        Children.Add(card);

        // Under it: top apps beside the facts, their rows level, or one card across.
        if (kind is Detail.Cpu or Detail.Gpu or Detail.Memory)
        {
            _apps = new AppList();
            _facts = new FactList(labelled: true);
            var pair = new Grid();
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandPage.Gap) });
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            var facts = Parts.Card(_facts);
            SetColumn(facts, 2);
            pair.Children.Add(Parts.Card(_apps));
            pair.Children.Add(facts);
            _under = pair;
        }
        else if (kind == Detail.Network)
            _under = Parts.Card(_adapters = new AdapterList(module));
        else if (kind == Detail.Temperature)
            _under = Parts.Card(_zones = new ZoneGrid());
        else
            _under = Parts.Card(_facts = new FactList());
        SetRow(_under, 2);
        Children.Add(_under);

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

    /// <summary>The graph's scale, written under the second reading.</summary>
    internal string Scale => _scale.Text;

    internal HistoryGraph Graph => _graph;

    internal BarStrip? Cores => _cores;

    internal AppList? Apps => _apps;

    internal FactList? Facts => _facts;

    internal AdapterList? Adapters => _adapters;

    internal DrivesDetail? Drives => _drives;

    internal ZoneGrid? Zones => _zones;

    /// <summary>The card (or pair of cards) under the graph; collapsed on the network page with one adapter.</summary>
    internal FrameworkElement? Under => _under;

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
                var points = History(Series.Cpu);
                double scale = GraphScale.Load(points);
                Draw(points, scale, Format.Percent(scale), caution: caution);
                if (cpu?.PerProcessor is { } each) _cores!.Set(each, scale);
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
                var points = gpu is null ? [] : History(Series.GpuLoad(gpu.Id));
                double scale = GraphScale.Load(points);
                Draw(points, scale, Format.Percent(scale));
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
                Draw(History(Series.Memory), 100, "100%", caution: caution);
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
                var (down, up, scale) = _present.NetworkHistory(adapter);
                Draw(down, scale, Format.Rate(scale, settings.NetworkSpeed), up);
                _legendName!.Text = adapter is null ? "" : _present.AdapterName(s, adapter);
                _adapters!.Set(s, _present);
                // One adapter: the legend names it, and the graph takes the page.
                bool many = (s.Network.Value?.Count ?? 0) > 1;
                _under!.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
                _gap.Height = new GridLength(many ? IslandPage.Gap : 0);
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
                Draw(History(Series.Battery), 100, "100%");
                // Time left is the top card's reading, so the facts don't repeat it.
                _facts!.Set(battery is null ? [] :
                [
                    ("State", Present.State(battery)),
                    ("Rate", battery.RateWatts is { } watts ? Format.Watts(watts) : ""),
                ]);
                break;
            }
            default:
            {
                var zones = s.ThermalZones.Value ?? [];
                var hottest = zones.MaxBy(z => z.TemperatureC);
                _hero.Set(s.ThermalZones.Status == ReadingStatus.Unavailable ? "Unavailable" : hottest is null ? "" : Format.Temperature(hottest.TemperatureC, settings.Temperature));
                _side.Text = zones.Count > 1 ? $"Hottest of {zones.Count} zones" : "";
                var (points, max, label, convert) = _present.ZoneHistory(hottest);
                Draw(points, max, label, convert: convert);
                _zones!.Set(zones, settings.Temperature);
                break;
            }
        }
    }

    private void Draw(IReadOnlyList<HistoryPoint> points, double max, string scale, IReadOnlyList<HistoryPoint>? second = null, bool caution = false, Func<double, double>? convert = null)
    {
        _graph.Set(points, max, second, caution, convert);
        _scale.Text = scale;
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

    /// <param name="labelled">Beside a card with a label (top apps): the rows start under the label's room, level with that card's.</param>
    public FactList(bool labelled = false)
    {
        if (labelled)
            Children.Add(new Border { Height = 16 });
    }

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

/// <summary>Every thermal zone as a 44 tall tile, the tiles sharing the width 4 to a row, up to 8.</summary>
internal sealed class ZoneGrid : StackPanel
{
    public const int Max = 8;
    private readonly Grid _tiles = new() { Margin = new Thickness(0, 4, 0, 0) };
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
        if (_tiles.Children.Count != shown.Count)
        {
            _tiles.Children.Clear();
            _tiles.ColumnDefinitions.Clear();
            _tiles.RowDefinitions.Clear();
            int across = Math.Clamp(shown.Count, 1, 4);
            for (int c = 0; c < across; c++)
            {
                if (c > 0) _tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                _tiles.ColumnDefinitions.Add(new ColumnDefinition());
            }
            for (int r = 0; r < (shown.Count + across - 1) / across; r++)
            {
                if (r > 0) _tiles.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
                _tiles.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            }
            for (int i = 0; i < shown.Count; i++)
            {
                var tile = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 5, 8, 5) };
                tile.SetResourceReference(Border.BackgroundProperty, "IslandHoverBrush");
                tile.Child = new StackPanel { Children = { Parts.Text("", 11, Look.Island.Tertiary), Parts.Text("", 14, Look.Island.Text, semibold: true) } };
                Grid.SetRow(tile, i / across * 2);
                Grid.SetColumn(tile, i % across * 2);
                _tiles.Children.Add(tile);
            }
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

/// <summary>
/// The drives page: each volume's free space over a meter, two to a row, then each disk's activity.
/// Both cards take the height their rows need, from the top, since the list grows with the drives.
/// </summary>
internal sealed class DrivesDetail : Grid
{
    private const int ActivityRows = 4;
    private const double VolumeHeight = 36;
    private readonly SystemMonitorModule _module;
    private readonly Grid _space = new() { Margin = new Thickness(0, 2, 0, 0) };
    private readonly StackPanel _activity = new();
    private readonly CardLabel _spaceLabel = new("Space"), _activityLabel = new("Activity");
    private readonly Border _activityCard;
    private readonly TextBlock _status = Parts.Text("", 12, Look.Island.Tertiary);

    public DrivesDetail(SystemMonitorModule module)
    {
        _module = module;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(IslandPage.Gap) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
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
        _activity.Children.Clear();
        for (int i = 0; i < busy.Count; i++)
            _activity.Children.Add(Disk(busy[i], i == 0));
        // Space: two to a row, as many rows of 36 as fit above it, the fullest first when they don't all
        // fit (as the overview keeps them), then "+N more in Settings". Each card is 20 of padding and
        // border and a 16 label over its rows; Space's start 2 under it.
        var volumes = DrivesWidget.Volumes(disks);
        int across = volumes.Count == 1 ? 1 : 2;
        double activity = busy.Count == 0 ? 0 : 20 + 16 + busy.Count * 28 + IslandPage.Gap;
        int rows = (int)((IslandPage.BodyHeight - activity - 20 - 16 - 2) / VolumeHeight);
        var (shown, more) = DrivesWidget.Choose(volumes, id => _module.Alerting("low-disk", id), Math.Max(0, rows) * across, morePlace: false);
        _spaceLabel.Note.Text = more > 0 ? $"+{more} more in Settings" : "";
        _space.Children.Clear();
        _space.ColumnDefinitions.Clear();
        _space.RowDefinitions.Clear();
        for (int c = 0; c < across; c++)
        {
            if (c > 0) _space.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            _space.ColumnDefinitions.Add(new ColumnDefinition());
        }
        for (int r = 0; r < (shown.Count + across - 1) / across; r++)
            _space.RowDefinitions.Add(new RowDefinition { Height = new GridLength(VolumeHeight) });
        for (int i = 0; i < shown.Count; i++)
        {
            var volume = Volume(shown[i]);
            SetRow(volume, i / across);
            SetColumn(volume, i % across * 2);
            _space.Children.Add(volume);
        }
    }

    // The letter and "412 GiB free of 931 GiB" over a meter of what's used.
    private Grid Volume(VolumeReading volume)
    {
        bool caution = _module.Alerting("low-disk", volume.Id);
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var letter = Parts.Text(DrivesWidget.Letter(volume), 12, Look.Island.Secondary);
        var meter = new Meter(Look.Island);
        meter.Set(volume.TotalBytes == 0 ? 0 : 1 - (double)volume.FreeBytes / volume.TotalBytes, caution);
        var text = Parts.Text("", 11, Look.Island.Tertiary);
        text.HorizontalAlignment = HorizontalAlignment.Right;
        var free = new Run(Format.Size(volume.FreeBytes)) { FontSize = 12, FontWeight = FontWeights.SemiBold };
        free.SetResourceReference(TextElement.ForegroundProperty, caution ? Look.Caution : Look.Island.Text);
        text.Inlines.Add(free);
        text.Inlines.Add(new Run(" free of " + Format.Size(volume.TotalBytes)));
        SetRow(meter, 2);
        row.Children.Add(letter);
        row.Children.Add(text);
        row.Children.Add(meter);
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
