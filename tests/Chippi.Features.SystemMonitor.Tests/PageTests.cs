// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Views;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.SystemMonitor.Tests;

/// <summary>The System page with fake sources, on a WPF thread, without a window (Karl's review of 6b0b142).</summary>
public sealed class PageTests
{
    private const ulong GiB = 1024UL * 1024 * 1024, MiB = 1024UL * 1024;

    private static (SystemMonitorModule Module, SystemView View, IslandTileSpec Spec) Start(FakeSources? fake = null)
    {
        fake ??= new FakeSources();
        var module = new SystemMonitorModule(() => fake.Sources);
        var ui = new Ui();
        module.StartAsync(new ModuleContext("system-monitor", new Settings(), NullLogger.Instance, new Island(), ui), default).GetAwaiter().GetResult();
        return (module, (SystemView)ui.Spec!.CreateSection(), ui.Spec);
    }

    /// <summary>A desktop: two GPUs, Ethernet and Wi-Fi, <paramref name="drives"/> drives, and the processes behind top apps.</summary>
    internal static SystemSnapshot Desktop(int drives = 4, bool laptop = false)
    {
        var at = DateTimeOffset.UtcNow;
        return SystemSnapshot.Empty(at) with
        {
            Cpu = new(ReadingStatus.Ok, new(23, 16, 4180, [.. Enumerable.Repeat(20d, 16)]), at),
            Gpus = new(ReadingStatus.Ok, laptop ? [] :
            [
                new("rtx", "NVIDIA GeForce RTX 4080", Load: 34, DedicatedUsedBytes: 6 * GiB, DedicatedTotalBytes: 16 * GiB, TemperatureC: 51, FanRpm: 1450,
                    ProcessLoads: new Dictionary<int, double> { [401] = 26, [101] = 3, [102] = 1 }),
                new("igpu", "AMD Radeon(TM) Graphics", Load: 3),
            ], at),
            Memory = new(ReadingStatus.Ok, new(21 * GiB, 32 * GiB, 26 * GiB, 48 * GiB), at),
            Network = new(ReadingStatus.Ok, [new("eth", "Ethernet", true, 2.4 * MiB, 180 * 1024), new("wifi", "Wi-Fi", false, null, null)], at),
            Disks = new(ReadingStatus.Ok, [.. Enumerable.Range(0, drives).Select(i => new DiskReading(i, i, 0, 0,
                [new VolumeReading("v" + i, (char)('C' + i) + ":", (ulong)(10 + i * 30) * GiB, 300 * GiB)]))], at),
            Battery = new(laptop ? ReadingStatus.Ok : ReadingStatus.NotSupported, laptop ? new(BatteryState.OnBattery, 80, -12.4, TimeSpan.FromMinutes(190)) : null, at),
            ThermalZones = new(laptop ? ReadingStatus.Ok : ReadingStatus.NotSupported, laptop ? [new("z1", 52), new("z2", 47)] : [], at),
            Processes = new(ReadingStatus.Ok,
            [
                new(101, "chrome.exe", "Google Chrome", null, 3.2, 1400 * MiB), new(102, "chrome.exe", "Google Chrome", null, 2.9, 1700 * MiB),
                new(201, "devenv.exe", "Visual Studio", null, 3.4, 1800 * MiB), new(301, "discord.exe", "Discord", null, 1.2, 640 * MiB),
                new(401, "game.exe", "Game", null, 0.8, 300 * MiB), new(501, "idle.exe", "Idle", null, 0.0, 100 * MiB),
            ], at),
        };
    }

    private static string Value(SystemView view, Detail kind) => ((ReadingWidget)view.Overview.WidgetFor(kind)!).Hero.Value;

    // The module reads the overview's readings from the start; wait for the first of them to reach the history.
    private static void WaitForHistory(SystemMonitorModule module)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (module.Sampler!.History(Series.Cpu).Count == 0 && waited.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(20);
        Assert.NotEmpty(module.Sampler.History(Series.Cpu));
    }

    private static void OnCulture(Action run)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try { run(); }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void The_overview_has_a_widget_for_each_reading_the_pc_has_in_its_rows() => WpfTestThread.Run(() =>
    {
        var (module, view, _) = Start();
        try
        {
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); // a page that shows hears settings change
            view.Show(Desktop());
            Assert.Equal([Detail.Cpu, Detail.Gpu, Detail.Memory, Detail.Network, Detail.Drives], view.Overview.Widgets.Select(w => w.Kind));
            view.Show(Desktop(laptop: true));
            // No GPU now: CPU and Memory share the top row; battery and zones join the network.
            Assert.Equal([Detail.Cpu, Detail.Memory, Detail.Network, Detail.Battery, Detail.Temperature, Detail.Drives], view.Overview.Widgets.Select(w => w.Kind));
            module.Save(module.Settings with { Off = [Reading.Gpu, Reading.Network] });
            Assert.Equal([Detail.Cpu, Detail.Memory, Detail.Battery, Detail.Temperature, Detail.Drives], view.Overview.Widgets.Select(w => w.Kind));
            module.Save(module.Settings with { Off = [.. Enum.GetValues<Reading>()] });
            Assert.Empty(view.Overview.Widgets);
            Assert.Contains(Find<TextBlock>(view.Overview), t => t.Text == "Every reading is off");
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    });

    [Fact]
    public void Widgets_show_values_context_and_graphs_and_clear_a_missing_reading() => WpfTestThread.Run(() => OnCulture(() =>
    {
        var (module, view, _) = Start();
        try
        {
            WaitForHistory(module);
            var s = Desktop();
            view.Show(s);
            Assert.Equal(("23%", "34%", "66%"), (Value(view, Detail.Cpu), Value(view, Detail.Gpu), Value(view, Detail.Memory)));
            var gpu = (ReadingWidget)view.Overview.WidgetFor(Detail.Gpu)!;
            Assert.Equal(("51 °C", "GeForce RTX 4080"), (gpu.Fact, gpu.Context.Text));
            // Loads draw their history (the fake CPU's 25%, on a 25% scale); a level keeps its meter.
            var cpu = (ReadingWidget)view.Overview.WidgetFor(Detail.Cpu)!;
            Assert.Null(cpu.Meter);
            Assert.Equal(25, Assert.Single(cpu.Graph!.Points.Distinct()));
            Assert.Equal(25, cpu.Graph.Max);
            var network = (NetworkWidget)view.Overview.WidgetFor(Detail.Network)!;
            Assert.Equal(("Ethernet", "2.4 MiB/s", "180 KiB/s", true), (network.Fact, network.Down, network.Up, network.Wide));

            view.Show(s with { Cpu = new(ReadingStatus.Pending, null, s.At), Network = new(ReadingStatus.Ok, [new("eth", "Ethernet", true, null, null)], s.At) });
            Assert.Equal("", Value(view, Detail.Cpu));
            Assert.Equal(("", ""), (network.Down, network.Up));
            view.Show(s with { Cpu = new(ReadingStatus.Unavailable, null, s.At), Network = new(ReadingStatus.Ok, [new("eth", "Ethernet", false, null, null)], s.At) });
            Assert.Equal(("Unavailable", "Not connected"), (Value(view, Detail.Cpu), network.Down));
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    }));

    [Fact]
    public void Drives_show_every_drive_that_fits_then_the_fullest_with_an_alerting_one_first() => WpfTestThread.Run(() =>
    {
        var volumes = Enumerable.Range(0, 8).Select(i => new VolumeReading("v" + i, (char)('C' + i) + ":", (ulong)(i + 1) * GiB, 10 * GiB)).ToList();
        Assert.Equal(6, DrivesWidget.Choose(volumes[..6], _ => false, 6).Shown.Count);
        var (shown, more) = DrivesWidget.Choose(volumes, _ => false, 6);
        Assert.Equal(("C: D: E: F: G:", 3), (string.Join(" ", shown.Select(v => v.Letter)), more));
        (shown, more) = DrivesWidget.Choose(volumes, id => id == "v7", 6);
        Assert.Equal(("C: D: E: F: J:", 3), (string.Join(" ", shown.Select(v => v.Letter)), more));
        (shown, more) = DrivesWidget.Choose(volumes, _ => false, 6, morePlace: false); // the drives page: "+N more" is in its label
        Assert.Equal(("C: D: E: F: G: H:", 2), (string.Join(" ", shown.Select(v => v.Letter)), more));

        var widget = new DrivesWidget(Look.Island, null, _ => false);
        widget.Set(Desktop(drives: 8));
        Assert.Equal(("C: D: E: F: G:", "+3", "8 drives"), (string.Join(" ", widget.Shown), widget.More, widget.Fact));
        widget.Rows = 1;
        widget.Set(Desktop(drives: 8));
        Assert.Equal(("C:", "+7"), (string.Join(" ", widget.Shown), widget.More));
        widget.Set(Desktop(drives: 1));
        Assert.Equal(("C:", "", ""), (string.Join(" ", widget.Shown), widget.Fact, widget.More));
        widget.Set(Desktop() with { Disks = new(ReadingStatus.Unavailable, null, DateTimeOffset.UtcNow) });
        Assert.Empty(widget.Shown);
    });

    [Fact]
    public void A_widget_opens_its_page_and_back_and_esc_return_to_the_overview_it_reopens_on() => WpfTestThread.Run(() =>
    {
        var (module, view, spec) = Start();
        try
        {
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            view.Show(Desktop());
            var cpu = (Button)view.Overview.WidgetFor(Detail.Cpu)!.Root;
            cpu.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal((Detail.Cpu, "CPU"), (view.Current!.Kind, view.Title));
            Assert.True(module.Sampler!.Need.HasFlag(SensorNeed.PerProcessor | SensorNeed.Processes));
            Assert.True(spec.HandleBack!());
            Assert.Equal((null, "System"), (view.Current, view.Title));
            Assert.False(module.Sampler.Need.HasFlag(SensorNeed.Processes));
            Assert.False(spec.HandleBack!()); // on the overview, Back goes home
            view.Open(Detail.Memory);
            Assert.True(spec.HandleEscape!());
            Assert.Null(view.Current);
            Assert.False(spec.HandleEscape!());
            view.Open(Detail.Network);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.Null(view.Current); // the island reopens on the overview
            // Only the module's history reads on (and the drives, until the first read of them).
            Assert.Equal(SystemMonitorModule.HistoryNeed(module.Settings), module.Sampler.Need & ~SensorNeed.Disks);
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    });

    [Fact]
    public void A_reading_switched_off_under_its_page_returns_to_the_overview() => WpfTestThread.Run(() =>
    {
        var (module, view, _) = Start();
        try
        {
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            view.Show(Desktop());
            view.Open(Detail.Gpu);
            module.Save(module.Settings with { Off = [Reading.Gpu] });
            Assert.Null(view.Current);
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    });

    [Fact]
    public void Detail_pages_show_top_apps_facts_and_the_graph() => WpfTestThread.Run(() => OnCulture(() =>
    {
        var (module, view, _) = Start();
        try
        {
            WaitForHistory(module);
            var s = Desktop();
            view.Show(s);
            view.Open(Detail.Cpu);
            var page = view.Current!;
            Assert.Equal(("23%", "4.18 GHz"), (page.Hero.Value, page.Side));
            Assert.Equal(["Google Chrome 6.1%", "Visual Studio 3.4%", "Discord 1.2%"], page.Apps!.Shown);
            Assert.Contains("Logical processors 16", page.Facts!.Shown);
            Assert.Contains(page.Facts.Shown, f => f.StartsWith("Up time ", StringComparison.Ordinal));
            Assert.Equal(16, page.Cores!.Values.Count);
            Assert.Equal("25%", page.Scale); // the fake CPU's 25% fits the 25% scale

            view.Open(Detail.Gpu);
            page = view.Current!;
            Assert.Equal(("34%", "51 °C"), (page.Hero.Value, page.Side));
            Assert.Equal(["Game 26%", "Google Chrome 4.0%"], page.Apps!.Shown);
            Assert.Contains("Fan 1,450 RPM", page.Facts!.Shown);
            Assert.Equal(Visibility.Visible, view.GpuPicker.Visibility); // two GPUs: the header names the one shown

            view.Open(Detail.Memory);
            page = view.Current!;
            Assert.Equal(["Google Chrome 3.0 GiB", "Visual Studio 1.8 GiB", "Discord 640 MiB"], page.Apps!.Shown);
            Assert.Equal(Visibility.Collapsed, view.GpuPicker.Visibility);
            Assert.Equal("100%", page.Scale); // a level stays at 100%

            // Processes that fail to read say so; the rows don't keep old apps.
            view.Show(s with { Processes = new(ReadingStatus.Unavailable, null, s.At) });
            Assert.Equal(("Unavailable", 0), (page.Apps.Status, page.Apps.Shown.Count));
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    }));

    [Fact]
    public void The_network_page_lists_adapters_and_a_click_graphs_another() => WpfTestThread.Run(() => OnCulture(() =>
    {
        var (module, view, _) = Start();
        try
        {
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            view.Show(Desktop());
            view.Open(Detail.Network);
            var page = view.Current!;
            Assert.Equal(("2.4 MiB/s", "64 KiB/s"), (page.Hero.Value, page.Scale));
            Assert.Equal(["Ethernet, down 2.4 MiB/s, up 180 KiB/s", "Wi-Fi, Not connected"], page.Adapters!.Shown);
            Assert.Equal(Visibility.Visible, page.Under!.Visibility);
            var wifi = Find<Button>(page.Adapters).Last();
            wifi.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("wifi", module.Settings.NetworkAdapterId);
            Assert.Equal("Not connected", view.Current!.Hero.Value);
            // One adapter: the legend names it, and the graph takes the page.
            module.Save(module.Settings with { NetworkAdapterId = null });
            var s = Desktop();
            view.Show(s with { Network = new(ReadingStatus.Ok, [s.Network.Value![0]], s.At) });
            Assert.Equal(Visibility.Collapsed, page.Under.Visibility);
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    }));

    [Fact]
    public void The_drives_page_fits_whole_rows_and_sends_the_rest_to_settings() => WpfTestThread.Run(() =>
    {
        var (module, view, _) = Start();
        try
        {
            view.Show(Desktop());
            view.Open(Detail.Drives);
            var drives = view.Current!.Drives!;
            Assert.Equal((4, "", 4), (drives.Volumes.Count, drives.More, drives.Disks.Count));
            // Two to a row above 4 disks' activity: 3 rows, 6 drives, the fullest first.
            view.Show(Desktop(drives: 8));
            Assert.Equal((6, "+2 more in Settings", 4), (drives.Volumes.Count, drives.More, drives.Disks.Count));
            Assert.StartsWith("C: ", drives.Volumes[0], StringComparison.Ordinal);
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    });

    [Fact]
    public void Laptop_pages_show_battery_and_every_zone_up_to_eight() => WpfTestThread.Run(() => OnCulture(() =>
    {
        var (module, view, _) = Start();
        try
        {
            var s = Desktop(laptop: true);
            view.Show(s);
            var battery = (ReadingWidget)view.Overview.WidgetFor(Detail.Battery)!;
            Assert.Equal(("80%", "On battery · 3 hr 10 min"), (battery.Hero.Value, battery.Context.Text));
            Assert.Equal((0.8, null), (battery.Meter!.Value, battery.Graph));
            view.Open(Detail.Battery);
            Assert.Equal("3 hr 10 min left", view.Current!.Side);
            Assert.DoesNotContain(view.Current.Facts!.Shown, f => f.StartsWith("Time left", StringComparison.Ordinal)); // the top card has it
            view.Open(Detail.Temperature);
            Assert.Equal(("52 °C", "Hottest of 2 zones"), (view.Current!.Hero.Value, view.Current.Side));
            view.Show(s with { ThermalZones = new(ReadingStatus.Ok, [.. Enumerable.Range(0, 12).Select(i => new ThermalZoneReading("z" + i, 40 + i))], s.At) });
            Assert.Equal(8, view.Current.Zones!.Shown.Count);
            Assert.Equal("51 °C", view.Current.Hero.Value);
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    }));

    // The module keeps the overview's readings current; the last page's drives (and anything else it
    // didn't read) fill the rest until the page's own arrive.
    [Fact]
    public void A_page_opens_with_the_latest_readings_and_the_last_pages_drives()
    {
        var at = DateTimeOffset.UtcNow;
        var latest = SystemSnapshot.Empty(at) with
        {
            Cpu = new(ReadingStatus.Ok, new(61, 8, 4000), at),
            Network = new(ReadingStatus.Ok, [new("eth", "Ethernet", true, 1, 1)], at),
        };
        var last = Desktop();

        Assert.Same(latest, SystemView.Opening(latest, null));
        var opening = SystemView.Opening(latest, last);
        Assert.Same(latest.Cpu, opening.Cpu);
        Assert.Same(latest.Network, opening.Network);
        Assert.Same(last.Memory, opening.Memory); // the latest didn't read it
        Assert.Same(last.Disks, opening.Disks);
        Assert.Same(last.Gpus, opening.Gpus);
    }

    [Fact]
    public void A_page_unloading_after_its_module_stopped_lets_go_cleanly()
    {
        WpfTestThread.Run(() =>
        {
            var (module, view, _) = Start();
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.NotEqual(SensorNeed.None, module.Sampler!.Need);

            module.StopAsync(default).GetAwaiter().GetResult();

            Assert.Null(Record.Exception(() => view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent))));
        });
    }

    internal static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child)) yield return nested;
    }

    private sealed class Ui : IModuleUi
    {
        public IslandTileSpec? Spec;
        public IIslandTile AddIslandTile(IslandTileSpec spec) { Spec = spec; return new IslandTileContribution("system-monitor", spec); }
        public void SetSettingsPage(Func<string?, object> createView) { }
    }

    private sealed class Settings : IModuleSettings
    {
        private object? _value;
        public T Get<T>() where T : class, new() => _value as T ?? new();
        public void Save<T>(T value) where T : class => _value = value;
    }

    private sealed class Island : IIslandPublisher
    {
        public void Publish(IslandActivity activity) { }
        public void Clear(string key) { }
        public void ReportSuccess(string? message = null) { }
    }
}
