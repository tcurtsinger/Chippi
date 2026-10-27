// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.SystemMonitor.Sensors;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.SystemMonitor.Tests;

public sealed class SamplerCoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private const SensorNeed Everything = (SensorNeed)511;

    private readonly FakeSources _fake = new();
    private readonly SamplerCore _core;

    public SamplerCoreTests() => _core = new SamplerCore(_fake.Sources, NullLogger.Instance);

    private SystemSnapshot Tick(int seconds, SensorNeed need = Everything) => _core.Tick(T0 + seconds * Second, need, Second);

    private double?[] Values(string key) => [.. _core.History(key).Select(p => p.Value)];

    [Fact]
    public void A_read_gives_Ok_values_and_history()
    {
        var s = Tick(0);

        Assert.Equal(ReadingStatus.Ok, s.Cpu.Status);
        Assert.Equal(25, s.Cpu.Value!.Utility);
        Assert.Equal(ReadingStatus.Ok, s.Memory.Status);
        Assert.Equal(25, s.Memory.Value!.UsedPercent);
        Assert.Equal([25.0], Values(Series.Cpu));
        Assert.Equal([25.0], Values(Series.Memory));
        Assert.Equal([40.0], Values(Series.GpuLoad("PCI\\VEN_10DE&DEV_2704\\1")));
        Assert.Equal([1000.0], Values(Series.NetworkReceive("{A}")));
        Assert.Equal([500.0], Values(Series.NetworkSend("{A}")));
        Assert.Same(s, _core.Latest);
    }

    [Fact]
    public void Readings_nobody_needs_are_not_read()
    {
        var s = Tick(0, SensorNeed.Memory);

        Assert.Equal(ReadingStatus.NotRead, s.Cpu.Status);
        Assert.Equal(ReadingStatus.NotRead, s.Gpus.Status);
        Assert.Equal(ReadingStatus.NotRead, s.Network.Status);
        Assert.Equal(0, _fake.CpuReads + _fake.GpuReads + _fake.NetworkReads);
        Assert.Equal(1, _fake.MemoryReads);
        Assert.Empty(_core.History(Series.Cpu));
    }

    [Fact]
    public void Gpu_sensors_alone_skip_the_load_counters()
    {
        bool? askedForLoad = null;
        _fake.Gpu = load =>
        {
            askedForLoad = load;
            return [new GpuReading("PCI\\X", "GPU", TemperatureC: 60)];
        };

        var s = Tick(0, SensorNeed.GpuSensors);

        Assert.False(askedForLoad);
        Assert.Equal(60, s.Gpus.Value![0].TemperatureC);
        Assert.Empty(_core.History(Series.GpuLoad("PCI\\X")));
    }

    [Fact]
    public void A_rate_without_its_second_sample_is_Pending_and_adds_no_point()
    {
        _fake.Cpu = _ => null;
        _fake.Disks = () => null;

        var s = Tick(0);

        Assert.Equal(ReadingStatus.Pending, s.Cpu.Status);
        Assert.Null(s.Cpu.Value);
        Assert.Equal(ReadingStatus.Pending, s.Disks.Status);
        Assert.Empty(_core.History(Series.Cpu));
    }

    [Fact]
    public void Pending_after_a_value_ends_the_line()
    {
        Tick(0);
        _fake.Cpu = _ => null;
        Tick(1);
        Tick(2);
        _fake.Cpu = _ => new CpuReading(50, 4, null);
        Tick(3);

        Assert.Equal([25.0, null, 50.0], Values(Series.Cpu));
    }

    [Fact]
    public void Hardware_the_PC_lacks_is_NotSupported()
    {
        _fake.Gpu = _ => [];

        var s = Tick(0);

        Assert.Equal(ReadingStatus.NotSupported, s.Battery.Status);
        Assert.Equal(ReadingStatus.NotSupported, s.ThermalZones.Status);
        Assert.Equal(ReadingStatus.NotSupported, s.Gpus.Status);
        Assert.Equal(ReadingStatus.Ok, s.Disks.Status);
    }

    [Fact]
    public void A_failed_read_is_Unavailable_a_gap_never_0_and_retried_after_10_s()
    {
        Tick(0);
        _fake.Cpu = _ => throw new InvalidOperationException("PDH failed");

        var failed = Tick(1);
        int reads = _fake.CpuReads;
        var waiting = Tick(5);

        Assert.Equal(ReadingStatus.Unavailable, failed.Cpu.Status);
        Assert.Null(failed.Cpu.Value);
        Assert.Equal(ReadingStatus.Unavailable, waiting.Cpu.Status);
        Assert.Equal(reads, _fake.CpuReads);
        Assert.Equal(ReadingStatus.Ok, waiting.Memory.Status);

        _fake.Cpu = _ => new CpuReading(30, 4, null);
        var back = Tick(11);

        Assert.Equal(ReadingStatus.Ok, back.Cpu.Status);
        Assert.Equal([25.0, null, null, 30.0], Values(Series.Cpu));
        Assert.DoesNotContain(0.0, Values(Series.Cpu));
    }

    [Fact]
    public void A_pause_longer_than_the_interval_leaves_a_gap()
    {
        Tick(0);
        Tick(1);
        Tick(2);
        Tick(10);

        var points = _core.History(Series.Cpu);

        Assert.Equal([25.0, 25.0, 25.0, null, 25.0], points.Select(p => p.Value));
        Assert.Equal(T0 + 3 * Second, points[3].At);
    }

    [Fact]
    public void Resume_puts_a_gap_in_every_series()
    {
        Tick(0);
        _core.Resumed();
        Tick(1);
        Tick(2);

        Assert.Equal([25.0, null, 25.0, 25.0], Values(Series.Cpu));
        Assert.Equal([1000.0, null, 1000.0, 1000.0], Values(Series.NetworkReceive("{A}")));
    }

    [Fact]
    public void An_adapter_that_disappears_gets_a_gap()
    {
        Tick(0);
        _fake.Network = () => [new NetworkReading("{B}", "Wi-Fi", true, 10, 20)];
        Tick(1);

        Assert.Equal([1000.0, null], Values(Series.NetworkReceive("{A}")));
        Assert.Equal([10.0], Values(Series.NetworkReceive("{B}")));
    }

    [Fact]
    public void A_disconnected_adapter_has_no_rate_and_no_0()
    {
        _fake.Network = () => [new NetworkReading("{A}", "Ethernet", false, null, null)];

        var s = Tick(0);

        Assert.False(s.Network.Value![0].Connected);
        Assert.Equal([null], Values(Series.NetworkReceive("{A}")));
    }

    [Fact]
    public void History_keeps_the_last_120_points()
    {
        for (int i = 0; i < 130; i++)
        {
            int load = i;
            _fake.Cpu = _ => new CpuReading(load % 100, 4, null);
            Tick(i);
        }

        var points = _core.History(Series.Cpu);

        Assert.Equal(SamplerCore.HistoryLength, points.Count);
        Assert.Equal(T0 + 10 * Second, points[0].At);
        Assert.Equal(T0 + 129 * Second, points[^1].At);
    }

    [Fact]
    public void Utility_above_100_is_kept_in_the_reading_and_clamped_in_history()
    {
        _fake.Cpu = _ => new CpuReading(112, 4, 5200);

        var s = Tick(0);

        Assert.Equal(112, s.Cpu.Value!.Utility);
        Assert.Equal([100.0], Values(Series.Cpu));
    }

    [Fact]
    public void Disposing_disposes_the_sources()
    {
        _core.Dispose();

        Assert.True(_fake.Disposed);
    }
}
