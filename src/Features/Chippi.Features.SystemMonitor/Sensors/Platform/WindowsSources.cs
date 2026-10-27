// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>The real sources, all readable as a standard user.</summary>
public static class WindowsSources
{
    public static SensorSources Create() => new(
        new CpuSource(),
        new MemorySource(),
        new GpuSource(),
        new NetworkSource(),
        new DiskSource(),
        new BatterySource(),
        new ThermalZoneSource());
}
