// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.SystemMonitor.Tests;

/// <summary>The tile, its status and what the module reads (design notes #1 to #4).</summary>
public sealed class ModuleTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chippi-monitor-" + Guid.NewGuid().ToString("N"));
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public ModuleTests()
    {
        Directory.CreateDirectory(_root);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        Directory.Delete(_root, true);
    }

    private static SystemSnapshot Snapshot(CpuReading? cpu = null, IReadOnlyList<GpuReading>? gpus = null, IReadOnlyList<NetworkReading>? network = null)
    {
        var at = DateTimeOffset.UnixEpoch;
        return SystemSnapshot.Empty(at) with
        {
            Cpu = cpu is null ? Sample<CpuReading>.NotRead(at) : new(ReadingStatus.Ok, cpu, at),
            Memory = new(ReadingStatus.Ok, new MemoryReading(39, 100, 1, 2), at),
            Gpus = gpus is null ? Sample<IReadOnlyList<GpuReading>>.NotRead(at) : new(ReadingStatus.Ok, gpus, at),
            Network = network is null ? Sample<IReadOnlyList<NetworkReading>>.NotRead(at) : new(ReadingStatus.Ok, network, at),
        };
    }

    [Fact]
    public void The_tile_shows_the_chosen_reading()
    {
        var s = Snapshot(new CpuReading(12.4, 4, null),
            [new GpuReading("iGPU", "Radeon", Load: 2, DedicatedTotalBytes: 2), new GpuReading("dGPU", "RTX", Load: 31, DedicatedTotalBytes: 16)],
            [new NetworkReading("{B}", "Wi-Fi", false, null, null), new NetworkReading("{A}", "Ethernet", true, 1.2 * 1024 * 1024, 10)]);
        var settings = new SystemMonitorSettings();

        Assert.Equal("CPU 12%", SystemMonitorModule.TileText(s, settings, null, null));
        Assert.Equal("Memory 39%", SystemMonitorModule.TileText(s, settings with { TileStatus = TileStatus.Memory }, null, null));
        Assert.Equal("GPU 31%", SystemMonitorModule.TileText(s, settings with { TileStatus = TileStatus.Gpu }, null, null));
        Assert.Equal("GPU 2%", SystemMonitorModule.TileText(s, settings with { TileStatus = TileStatus.Gpu }, "IGPU", null));
        Assert.Equal("↓ 1.2 MiB/s", SystemMonitorModule.TileText(s, settings with { TileStatus = TileStatus.Network }, null, null));
        Assert.Equal("Memory 39%", SystemMonitorModule.TileText(s, settings with { Off = [Reading.Cpu] }, null, null));
    }

    [Fact]
    public void A_reading_the_snapshot_lacks_gives_no_tile_text()
    {
        var s = Snapshot(network: [new NetworkReading("{A}", "Ethernet", false, null, null)]);

        Assert.Null(SystemMonitorModule.TileText(s, new SystemMonitorSettings(), null, null));
        Assert.Null(SystemMonitorModule.TileText(s, new SystemMonitorSettings { TileStatus = TileStatus.Network }, null, null));
    }

    [Fact]
    public void The_history_reads_what_the_overview_graphs_and_the_page_adds_what_it_shows()
    {
        var settings = new SystemMonitorSettings();

        Assert.Equal(SensorNeed.Cpu | SensorNeed.Memory | SensorNeed.GpuLoad | SensorNeed.Network | SensorNeed.Battery | SensorNeed.ThermalZones,
            SystemMonitorModule.HistoryNeed(settings));
        var fewer = SystemMonitorModule.HistoryNeed(settings with { Off = [Reading.Gpu, Reading.Battery] });
        Assert.False(fewer.HasFlag(SensorNeed.GpuLoad) || fewer.HasFlag(SensorNeed.Battery));
        Assert.Equal(SensorNeed.None, SystemMonitorModule.HistoryNeed(settings with { Off = [.. Enum.GetValues<Reading>()] }));
        Assert.Equal((SensorNeed)0b1_1111_1101, SystemView.Need(settings));
        Assert.False(SystemView.Need(settings).HasFlag(SensorNeed.PerProcessor));
        var some = SystemView.Need(settings with { Off = [Reading.Gpu, Reading.Battery] });
        Assert.False(some.HasFlag(SensorNeed.GpuLoad) || some.HasFlag(SensorNeed.GpuSensors) || some.HasFlag(SensorNeed.Battery));
    }

    [Fact]
    public void The_page_picks_the_chosen_device_or_falls_back()
    {
        GpuReading[] gpus = [new("iGPU", "Radeon", Load: 1, DedicatedTotalBytes: 2), new("dGPU", "RTX", Load: 5, DedicatedTotalBytes: 16), new("off", "Idle")];
        NetworkReading[] adapters = [new("{B}", "Wi-Fi", false, null, null), new("{A}", "Ethernet", true, 1, 1)];

        Assert.Equal("dGPU", Pick.Gpu(gpus, null, null)!.Id);
        Assert.Equal("iGPU", Pick.Gpu(gpus, "igpu", null)!.Id);
        Assert.Equal("dGPU", Pick.Gpu(gpus, "gone", null)!.Id);
        Assert.Null(Pick.Gpu(gpus, "off", new HashSet<string>()));
        Assert.Equal("{A}", Pick.Adapter(adapters, null)!.Id);
        Assert.Equal("{B}", Pick.Adapter(adapters, "{B}")!.Id);
        Assert.Equal("{A}", Pick.Adapter(adapters, "{gone}")!.Id);
        Assert.Null(Pick.Adapter([], "{A}"));
    }

    [Fact]
    public async Task The_history_reads_from_the_start_and_the_page_has_no_note()
    {
        var fake = new FakeSources();
        var module = new SystemMonitorModule(() => fake.Sources);
        string? opened = null;
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.Load();
        var services = new ModuleServices { DataRoot = Path.Combine(_root, "data"), OpenIslandPage = key => opened = key };
        var registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance, services);
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await registry.SetInstalledAsync("system-monitor", true);
            var tile = Assert.Single(registry.Find("system-monitor")!.IslandTiles);

            Assert.Equal(("system-monitor", "System", "Activity", 60), (tile.Spec.Key, tile.Spec.Name, tile.Spec.Icon, tile.Spec.Order));
            Assert.Equal("", tile.PageNote);
            // With nothing on screen: the overview's readings for their history, and the drives and the
            // GPU's sensors once.
            using var status = new SemaphoreSlim(0);
            tile.StatusChanged += (_, _) => status.Release();
            Assert.Equal(SystemMonitorModule.HistoryNeed(module.Settings), module.Sampler!.Need & ~SystemMonitorModule.FirstNeed(module.Settings));
            Assert.Equal(SensorNeed.Disks | SensorNeed.GpuSensors, SystemMonitorModule.FirstNeed(module.Settings));
            Assert.Equal(SensorNeed.Disks, SystemMonitorModule.FirstNeed(module.Settings with { Off = [Reading.Gpu] }));
            while (tile.Status != "CPU 25%")
                Assert.True(status.Wait(Wait));
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (module.Sampler.Need != SystemMonitorModule.HistoryNeed(module.Settings) && waited.Elapsed < Wait)
                Thread.Sleep(20);
            Assert.Equal(SystemMonitorModule.HistoryNeed(module.Settings), module.Sampler.Need);
            Assert.NotEmpty(module.Sampler.History(Series.Cpu));

            module.Save(module.Settings with { UpdateSeconds = 5, TileStatus = TileStatus.Memory });
            Assert.Equal("", tile.PageNote);
            Assert.Equal(TimeSpan.FromSeconds(5), module.Sampler.Interval);
            Assert.Equal(5, config.GetModuleSettings<SystemMonitorSettings>("system-monitor").UpdateSeconds);

            Assert.True(services.Activation.TryRoute(["--system-monitor"]));
            Assert.Equal("system-monitor", opened);
        }
        finally { await registry.StopAllAsync(); }
        Assert.True(fake.Disposed);
    }

    [Fact]
    public async Task A_failed_read_clears_the_tile_status_rather_than_keep_the_old_number()
    {
        int reads = 0;
        // A new need's first two reads are the unpublished first sample and the published one.
        var fake = new FakeSources { Cpu = _ => Interlocked.Increment(ref reads) <= 2 ? new CpuReading(25, 4, null) : throw new InvalidOperationException("PDH failed") };
        var module = new SystemMonitorModule(() => fake.Sources);
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.Load();
        config.SetModuleSettings("system-monitor", new SystemMonitorSettings { UpdateSeconds = 1 });
        var registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance, new ModuleServices { DataRoot = Path.Combine(_root, "data") });
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await registry.SetInstalledAsync("system-monitor", true);
            var tile = Assert.Single(registry.Find("system-monitor")!.IslandTiles);
            using var status = new SemaphoreSlim(0);
            tile.StatusChanged += (_, _) => status.Release();

            tile.SetOnScreen(true);
            while (tile.Status != "CPU 25%")
                Assert.True(status.Wait(Wait));
            while (tile.Status != "")
                Assert.True(status.Wait(Wait));
        }
        finally { await registry.StopAllAsync(); }
    }

    [Fact]
    public async Task An_alert_past_its_threshold_is_the_tile_status_in_Caution()
    {
        var fake = new FakeSources { Memory = () => new MemoryReading(93, 100, 1, 2) };
        var module = new SystemMonitorModule(() => fake.Sources);
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.Load();
        config.SetModuleSettings("system-monitor", new SystemMonitorSettings { Memory = new() { On = true, Percent = 90 } });
        var island = new IslandActivityHub();
        var registry = new ModuleRegistry(config, island, NullLoggerFactory.Instance, new ModuleServices { DataRoot = Path.Combine(_root, "data") });
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            using var status = new SemaphoreSlim(0);
            await registry.SetInstalledAsync("system-monitor", true);
            var tile = Assert.Single(registry.Find("system-monitor")!.IslandTiles);
            tile.StatusChanged += (_, _) => status.Release();

            // The alert reads memory, which the history reads too.
            Assert.True(module.Sampler!.Need.HasFlag(SensorNeed.Memory));
            while (tile.Status != "Memory 93%")
                Assert.True(status.Wait(Wait));
            Assert.True(module.Alerting("memory", "memory"));
            var caution = Assert.Single(island.Activities, a => a.TileKey == SystemMonitorModule.TileKey);
            Assert.True(caution.NeedsAttention);

            module.Save(module.Settings with { Memory = new() { On = false, Percent = 90 } });
            Assert.False(module.Alerting("memory", "memory"));
            Assert.DoesNotContain(island.Activities, a => a.TileKey == SystemMonitorModule.TileKey);
            Assert.Equal(SystemMonitorModule.HistoryNeed(module.Settings), module.Sampler.Need & ~SensorNeed.Disks);
        }
        finally { await registry.StopAllAsync(); }
    }

    [Fact]
    public void The_module_replaces_the_catalog_placeholder()
    {
        var descriptor = new SystemMonitorModule(() => new FakeSources().Sources).Descriptor;

        Assert.Equal(("system-monitor", "System", FeatureGroup.System, "Activity"), (descriptor.Id, descriptor.Name, descriptor.Group, descriptor.Icon));
        Assert.DoesNotContain("tray", descriptor.Description, StringComparison.OrdinalIgnoreCase);
    }
}
