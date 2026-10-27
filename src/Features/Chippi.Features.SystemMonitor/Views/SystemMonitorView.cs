// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.UI.Controls;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// The System monitor page (design notes #5 to #22): CPU, Memory and Battery on the left, GPU,
/// Network and Thermal zone on the right, Disks across the bottom. While the page is on screen it
/// watches the sampler for the readings that are on, and it redraws on each snapshot.
/// </summary>
internal sealed class SystemMonitorView : UserControl
{
    public const double PageWidth = 640;
    private const double ColumnGap = 16; // the cards above Disks keep 8 below them

    private readonly SystemMonitorModule _module;
    private SensorWatch? _watch;
    private SensorSampler? _sampler;
    private SystemSnapshot? _shown;

    private readonly ReadingCard _cpu = new("CPU", graph: true);
    private readonly ReadingCard _memory = new("Memory", graph: true);
    private readonly ReadingCard _battery = new("Battery", graph: false);
    private readonly ReadingCard _gpu = new("GPU", graph: true);
    private readonly ReadingCard _network = new("Network", graph: true);
    private readonly StackPanel _zones = new();
    private readonly Border _disks = Card();
    private readonly Button _processorsButton = new() { Width = double.NaN, Height = 20, Padding = new Thickness(6, 0, 4, 0) };
    private readonly UniformGrid _processors = new() { Columns = 16, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _networkScale = Small();
    private readonly Popup _picker = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };

    public SystemMonitorView(SystemMonitorModule module)
    {
        _module = module;
        _cpu.Detail.Children.Add(_processors);
        _processorsButton.SetResourceReference(StyleProperty, "IslandTransportButtonStyle");
        _processorsButton.Click += (_, _) =>
        {
            _module.ProcessorsOpen = !_module.ProcessorsOpen;
            UpdateNeed();
            if (_shown is not null)
                Show(_shown);
        };
        _networkScale.HorizontalAlignment = HorizontalAlignment.Right;
        _networkScale.Margin = new Thickness(0, 0, 0, 2);
        _network.Body.Children.Insert(0, _networkScale);

        var left = new StackPanel { Children = { _cpu.Card, _memory.Card, _battery.Card } };
        var right = new StackPanel { Children = { _gpu.Card, _network.Card, _zones } };
        var columns = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(ColumnGap) }, new ColumnDefinition() },
            Children = { left, right },
        };
        Grid.SetColumn(right, 2);
        Content = new StackPanel { Children = { columns, _disks, _picker } };

        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private void Start()
    {
        if (_module.Sampler is not { } sampler)
            return;
        _sampler = sampler;
        _module.Changed += OnSettingsChanged;
        sampler.Updated += OnUpdated;
        _watch = sampler.Watch(Need());
        Show(sampler.Latest);
    }

    private void Stop()
    {
        _module.Changed -= OnSettingsChanged;
        if (_sampler is not null)
            _sampler.Updated -= OnUpdated;
        _watch?.Dispose();
        _watch = null;
        _sampler = null;
        _picker.IsOpen = false;
    }

    /// <summary>What the page reads: each reading that's on; the processors only while their strip is open.</summary>
    internal static SensorNeed Need(SystemMonitorSettings s, bool processorsOpen)
    {
        var need = SensorNeed.None;
        if (s.IsOn(Reading.Cpu))
            need |= SensorNeed.Cpu | (processorsOpen ? SensorNeed.PerProcessor : 0);
        if (s.IsOn(Reading.Memory))
            need |= SensorNeed.Memory;
        if (s.IsOn(Reading.Gpu))
            need |= SensorNeed.GpuSensors | SensorNeed.GpuLoad;
        if (s.IsOn(Reading.Network))
            need |= SensorNeed.Network;
        if (s.IsOn(Reading.Disks))
            need |= SensorNeed.Disks;
        if (s.IsOn(Reading.Battery))
            need |= SensorNeed.Battery;
        if (s.IsOn(Reading.ThermalZone))
            need |= SensorNeed.ThermalZones;
        return need;
    }

    private SensorNeed Need() => Need(_module.Settings, _module.ProcessorsOpen);

    private void UpdateNeed()
    {
        if (_watch is not null)
            _watch.Need = Need();
    }

    private void OnSettingsChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnSettingsChanged);
            return;
        }
        UpdateNeed();
        if (_shown is not null)
            Show(_shown);
    }

    private void OnUpdated(SystemSnapshot snapshot) => Dispatcher.BeginInvoke(() =>
    {
        if (_sampler is not null)
            Show(snapshot);
    });

    // ----- Drawing -----

    internal void Show(SystemSnapshot s)
    {
        _shown = s;
        var settings = _module.Settings;
        var span = settings.UpdateInterval * SamplerCore.HistoryLength;
        ShowCpu(s, settings, span);
        ShowMemory(s, settings, span);
        ShowGpu(s, settings, span);
        ShowNetwork(s, settings, span);
        ShowBattery(s, settings);
        ShowZones(s, settings);
        ShowDisks(s, settings);
    }

    private IReadOnlyList<HistoryPoint> HistoryOf(string key) => _sampler?.History(key) ?? [];

    private void ShowCpu(SystemSnapshot s, SystemMonitorSettings settings, TimeSpan span)
    {
        if (!_cpu.Visible(settings.IsOn(Reading.Cpu)))
            return;
        var sample = s.Cpu;
        // The graph follows every snapshot, so a failed or restarting read shows as its gap.
        _cpu.Graph!.Show(HistoryOf(Series.Cpu), s.At, span, 100);
        if (sample.Status == ReadingStatus.Unavailable)
        {
            _cpu.Unavailable();
            return;
        }
        if (sample.Value is not { } cpu)
        {
            // A rate starting over: no number until it's back, never the old one.
            _cpu.ClearHeadline();
            _cpu.SetLine(Line(), _processorsButton);
            return;
        }
        _cpu.Headline(Format.Percent(cpu.Utility), _module.Alerting("cpu-load", "cpu"));
        var line = Line();
        if (cpu.EffectiveMhz is { } mhz)
            Value(line, Format.Clock(mhz));
        _processorsButton.Content = ProcessorsLabel(cpu.LogicalProcessors, _module.ProcessorsOpen);
        AutomationProperties.SetName(_processorsButton, $"{cpu.LogicalProcessors} logical processors");
        _cpu.SetLine(line, _processorsButton);
        _processors.Visibility = _module.ProcessorsOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_module.ProcessorsOpen && cpu.PerProcessor is { } each)
            ShowProcessors(each);
    }

    private static StackPanel ProcessorsLabel(int count, bool open)
    {
        var text = Small($"{count} logical processors");
        text.FontSize = 12;
        var chevron = new LucideIcon { Data = LucideIcons.ChevronDown, Size = 12, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(open ? 180 : 0) };
        chevron.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { text, chevron } };
    }

    private void ShowProcessors(IReadOnlyList<double> each)
    {
        while (_processors.Children.Count > each.Count)
            _processors.Children.RemoveAt(_processors.Children.Count - 1);
        while (_processors.Children.Count < each.Count)
            _processors.Children.Add(new ProcessorCell());
        for (int i = 0; i < each.Count; i++)
            ((ProcessorCell)_processors.Children[i]).Show(i, each[i]);
    }

    private void ShowMemory(SystemSnapshot s, SystemMonitorSettings settings, TimeSpan span)
    {
        if (!_memory.Visible(settings.IsOn(Reading.Memory)))
            return;
        _memory.Graph!.Show(HistoryOf(Series.Memory), s.At, span, 100);
        if (s.Memory.Status == ReadingStatus.Unavailable)
        {
            _memory.Unavailable();
            return;
        }
        if (s.Memory.Value is not { } memory)
        {
            _memory.ClearHeadline();
            _memory.SetLine(Line());
            return;
        }
        _memory.Headline(Format.Percent(memory.UsedPercent), _module.Alerting("memory", "memory"));
        var line = Line();
        Value(line, Format.Of(memory.UsedBytes, memory.TotalBytes));
        Fact(line, "Committed", Format.Of(memory.CommitBytes, memory.CommitLimitBytes));
        _memory.SetLine(line);
    }

    private void ShowGpu(SystemSnapshot s, SystemMonitorSettings settings, TimeSpan span)
    {
        foreach (var g in s.Gpus.Value ?? [])
            if (g.Load is not null)
                _module.GpusWithLoad.Add(g.Id);
        var listed = (s.Gpus.Value ?? []).Where(g => _module.GpusWithLoad.Contains(g.Id)).ToList();
        bool unavailable = s.Gpus.Status == ReadingStatus.Unavailable && _module.GpusWithLoad.Count > 0;
        // The card hides while no adapter reports load (design notes #13).
        if (!_gpu.Visible(settings.IsOn(Reading.Gpu) && (listed.Count > 0 || unavailable)))
            return;
        if (unavailable)
        {
            _gpu.Unavailable();
            return;
        }
        var gpu = Pick.Gpu(listed, settings.GpuId, _module.GpusWithLoad)!;
        // Rebuilt only when the choice or the list changes, so a focused picker keeps its focus.
        string gpuKey = gpu.Id + "|" + string.Join("|", listed.Select(g => g.Id));
        if (gpuKey != _gpuKey)
        {
            _gpuKey = gpuKey;
            _gpu.SetName(listed.Count > 1
                ? PickerButton(gpu.Name, "GPU", [.. listed.Select(g => (g.Id, g.Name))], gpu.Id, id => _module.Save(_module.Settings with { GpuId = id }))
                : DeviceName(gpu.Name));
        }
        if (gpu.Load is { } load)
            _gpu.Headline(Format.Percent(load), false);
        else
            _gpu.ClearHeadline();
        _gpu.Graph!.Show(HistoryOf(Series.GpuLoad(gpu.Id)), s.At, span, 100);

        var first = Line();
        bool alert = _module.Alerting("gpu-temperature", gpu.Id);
        if (gpu.TemperatureC is { } celsius)
            Value(first, Format.Temperature(celsius, settings.Temperature), alert);
        if (gpu.FanRpm is not null || gpu.FanPercent is not null)
        {
            string fan = string.Join(" · ", new[] { gpu.FanRpm is { } rpm ? Format.Rpm(rpm) : null, gpu.FanPercent is { } pct ? pct + "%" : null }.OfType<string>());
            Fact(first, "Fan", fan);
        }
        var second = Line();
        if (gpu.DedicatedUsedBytes is { } used)
        {
            if (gpu.DedicatedTotalBytes is { } total)
                Fact(second, "Dedicated", Format.Of(used, total));
            else
                Fact(second, "Dedicated", Format.Size(used));
        }
        if (gpu.SharedUsedBytes is { } shared)
            Fact(second, "Shared", Format.Size(shared));
        _gpu.SetLines(first, second);
    }

    private string? _lastAdapterName;
    private string? _gpuKey, _networkKey;

    private void ShowNetwork(SystemSnapshot s, SystemMonitorSettings settings, TimeSpan span)
    {
        var adapters = s.Network.Value ?? [];
        var adapter = Pick.Adapter(adapters, settings.NetworkAdapterId);
        bool gone = s.Network.Status is ReadingStatus.Ok or ReadingStatus.NotSupported && adapter is null;
        if (!_network.Visible(settings.IsOn(Reading.Network) && (adapter is not null || _lastAdapterName is not null)))
            return;
        if (s.Network.Status == ReadingStatus.Unavailable)
        {
            _network.Unavailable();
            return;
        }
        if (adapter is null && !gone)
            return;
        _lastAdapterName = adapter?.Name ?? _lastAdapterName;
        string networkKey = adapter?.Id + "|" + _lastAdapterName + "|" + string.Join("|", adapters.Select(a => a.Id));
        if (networkKey != _networkKey)
        {
            _networkKey = networkKey;
            _network.SetName(adapters.Count > 1
                ? PickerButton(adapter!.Name, "Network adapter", [.. adapters.Select(a => (a.Id, a.Name))], adapter.Id,
                    id => _module.Save(_module.Settings with { NetworkAdapterId = id }))
                : DeviceName(_lastAdapterName!));
        }
        var unit = settings.NetworkSpeed;
        if (adapter is not { Connected: true })
        {
            // No real adapter left, or it's unplugged: the name stays and the graph stops (design notes #14).
            _networkScale.Text = "";
            _network.Graph!.Visibility = Visibility.Collapsed;
            _network.SetLine(Status("Not connected"));
            return;
        }
        _network.Graph!.Visibility = Visibility.Visible;
        var rx = HistoryOf(Series.NetworkReceive(adapter.Id));
        var tx = HistoryOf(Series.NetworkSend(adapter.Id));
        double peak = rx.Concat(tx).Where(p => p.At > s.At - span).Select(p => p.Value ?? 0).DefaultIfEmpty(0).Max();
        double max = Format.RateScale(peak, unit);
        _networkScale.Text = Format.RateLabel(max, unit);
        _network.Graph.Show(rx, s.At, span, max, tx);
        if (adapter.ReceiveBytesPerSecond is not { } down || adapter.SendBytesPerSecond is not { } up)
        {
            _network.SetLine(Line()); // a rate starting over: no old speeds
            return;
        }
        var line = Line();
        line.Inlines.Add(new InlineUIContainer(Legend(dashed: false)) { BaselineAlignment = BaselineAlignment.Center });
        Fact(line, "Receive", Format.Rate(down, unit), separator: false);
        line.Inlines.Add(new Run("   "));
        line.Inlines.Add(new InlineUIContainer(Legend(dashed: true)) { BaselineAlignment = BaselineAlignment.Center });
        Fact(line, "Send", Format.Rate(up, unit), separator: false);
        _network.SetLine(line);
    }

    private void ShowBattery(SystemSnapshot s, SystemMonitorSettings settings)
    {
        if (!_battery.Visible(settings.IsOn(Reading.Battery) && s.Battery.Status is ReadingStatus.Ok or ReadingStatus.Unavailable))
            return;
        if (s.Battery.Value is not { } battery)
        {
            _battery.Unavailable();
            return;
        }
        bool alert = _module.Alerting("low-battery", "battery");
        if (battery.Percent is { } percent)
            _battery.Headline(Format.Percent(percent), alert);
        else
            _battery.ClearHeadline();
        var line = Line();
        Value(line, battery.State switch
        {
            BatteryState.OnBattery => "On battery",
            BatteryState.Charging => "Charging",
            _ => "Plugged in",
        });
        if (battery.RateWatts is { } watts)
            Value(line, Format.Watts(watts));
        if (battery.TimeLeft is { } left)
            Value(line, Format.TimeLeft(left));
        _battery.SetLine(line);
    }

    private void ShowZones(SystemSnapshot s, SystemMonitorSettings settings)
    {
        var zones = settings.IsOn(Reading.ThermalZone) && s.ThermalZones.Status == ReadingStatus.Ok ? s.ThermalZones.Value ?? [] : [];
        if (s.ThermalZones.Status == ReadingStatus.Pending)
            return;
        while (_zones.Children.Count > zones.Count)
            _zones.Children.RemoveAt(_zones.Children.Count - 1);
        for (int i = 0; i < zones.Count; i++)
        {
            if (i >= _zones.Children.Count)
                _zones.Children.Add(new ReadingCard("", graph: false).Card);
            var card = (ReadingCard)((Border)_zones.Children[i]).Tag;
            // Never "CPU": a zone is a board reading (design notes #16). Several are numbered.
            card.Rename(zones.Count > 1 ? $"Thermal zone {i + 1}" : "Thermal zone");
            card.Headline(Format.Temperature(zones[i].TemperatureC, settings.Temperature), false);
            card.Visible(true);
        }
    }

    private void ShowDisks(SystemSnapshot s, SystemMonitorSettings settings)
    {
        bool on = settings.IsOn(Reading.Disks) && s.Disks.Status is ReadingStatus.Ok or ReadingStatus.Unavailable or ReadingStatus.Pending;
        if (!on || (s.Disks.Status == ReadingStatus.Pending && _disks.Child is null))
        {
            _disks.Visibility = on && _disks.Child is not null ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        _disks.Visibility = Visibility.Visible;
        if (s.Disks.Status == ReadingStatus.Pending)
            return;
        if (s.Disks.Value is not { } disks)
        {
            _disks.Child = new StackPanel { Children = { Header("Disks"), Status("Unavailable") } };
            return;
        }
        _disks.Child = DiskTable.Build(disks, volume => _module.Alerting("low-disk", volume));
    }

    // ----- Pieces -----

    internal static Border Card()
    {
        var card = new Border();
        card.SetResourceReference(StyleProperty, "IslandCardStyle");
        return card;
    }

    internal static TextBlock Header(string text)
    {
        var name = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        return name;
    }

    private static TextBlock DeviceName(string text)
    {
        var name = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
        return name;
    }

    internal static TextBlock Small(string text = "")
    {
        var small = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        small.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        return small;
    }

    internal static TextBlock Line()
    {
        var line = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        Typography.SetNumeralAlignment(line, FontNumeralAlignment.Tabular);
        line.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
        return line;
    }

    internal static TextBlock Status(string text)
    {
        var status = Line();
        status.Text = text;
        status.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        return status;
    }

    /// <summary>A value in Text, after a " · " when the line already has something.</summary>
    internal static void Value(TextBlock line, string value, bool caution = false)
    {
        Separate(line);
        var run = new Run(value);
        if (caution)
            run.SetResourceReference(TextElement.ForegroundProperty, "WarningTextBrush");
        line.Inlines.Add(run);
    }

    /// <summary>"Committed 33.6 of 65.6 GiB": the label in Caption, the value in Text.</summary>
    internal static void Fact(TextBlock line, string label, string value, bool separator = true)
    {
        if (separator)
            Separate(line);
        var name = new Run(label + " ");
        name.SetResourceReference(TextElement.ForegroundProperty, "IslandTextTertiaryBrush");
        line.Inlines.Add(name);
        line.Inlines.Add(new Run(value));
    }

    private static void Separate(TextBlock line)
    {
        if (line.Inlines.Count == 0)
            return;
        var dot = new Run(" · ");
        dot.SetResourceReference(TextElement.ForegroundProperty, "IslandTextTertiaryBrush");
        line.Inlines.Add(dot);
    }

    // Receive's solid line and Send's dashed one, as the legend before each value (design notes #9).
    private static FrameworkElement Legend(bool dashed)
    {
        var line = new System.Windows.Shapes.Line { X1 = 0, X2 = 12, Y1 = 1, Y2 = 1, StrokeThickness = 1.5, Width = 12, Height = 2, Margin = new Thickness(0, 0, 6, 0) };
        if (dashed)
            line.StrokeDashArray = [2, 1.33];
        line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, dashed ? "IslandTextTertiaryBrush" : "AccentBrush");
        return line;
    }

    /// <summary>The GPU or adapter name as a picker, in Now Playing's source-picker style.</summary>
    private Button PickerButton(string current, string what, IReadOnlyList<(string Id, string Name)> items, string currentId, Action<string> choose)
    {
        var text = new TextBlock { Text = current, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var chevron = new LucideIcon { Data = LucideIcons.ChevronDown, Size = 12, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { text, chevron } }, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 0, 4, 0), Width = double.NaN, Height = 24 };
        button.SetResourceReference(StyleProperty, "IslandTransportButtonStyle");
        AutomationProperties.SetName(button, $"{what}, {current}");
        button.Click += (_, _) =>
        {
            var list = new StackPanel { Margin = new Thickness(4) };
            foreach (var (id, name) in items)
            {
                var item = new Button { Content = name };
                item.SetResourceReference(StyleProperty, "IslandMenuItemStyle");
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
            menu.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
            menu.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
            _picker.PlacementTarget = button;
            _picker.Child = menu;
            _picker.IsOpen = true;
        };
        return button;
    }

    /// <summary>One processor's cell: filled from the bottom to its load, with its own accessible name (design notes #12).</summary>
    private sealed class ProcessorCell : Border
    {
        private readonly Border _fill = new() { VerticalAlignment = VerticalAlignment.Bottom };

        public ProcessorCell()
        {
            Height = 14;
            Margin = new Thickness(0, 0, 2, 2);
            Child = _fill;
            SetResourceReference(BackgroundProperty, "IslandBorderBrush");
            _fill.SetResourceReference(BackgroundProperty, "AccentBrush");
            Focusable = false;
        }

        public void Show(int index, double load)
        {
            double share = Math.Clamp(load / 100, 0, 1);
            _fill.Height = 14 * share;
            AutomationProperties.SetName(this, $"Logical processor {index}, {Format.Percent(load)}");
        }
    }
}
