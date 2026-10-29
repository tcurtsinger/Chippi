// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Tests;

public sealed class NotificationAvailabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hardware_discovery_reads_once_without_load_counters_and_disposes_sources(bool present)
    {
        int batteryReads = 0;
        var sources = new FakeSources
        {
            Battery = () => { batteryReads++; return present ? new BatteryReading(BatteryState.OnBattery, 65, null, null) : null; },
            Gpu = load => { Assert.False(load); return [new GpuReading("gpu", "Graphics", TemperatureC: present ? 50 : null)]; },
        };
        var module = new SystemMonitorModule(() => sources.Sources);
        var kinds = module.AvailableNoticeKinds;
        Assert.Equal(present, kinds.Contains("low-battery"));
        Assert.Equal(present, kinds.Contains("gpu-temperature"));
        Assert.Contains("low-disk", kinds);
        Assert.Equal(1, batteryReads);
        Assert.Equal(1, sources.GpuReads);
        Assert.Equal(0, sources.CpuReads);
        Assert.True(sources.Disposed);
    }
}
