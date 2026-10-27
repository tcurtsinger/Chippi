// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.SystemMonitor.Sensors;

// Every source is called only on the sampler thread. A source throws when a read fails; the sampler
// turns that into ReadingStatus.Unavailable. Rates need two samples, so a source's first read returns
// null rates. A later privileged source (CPU package, board sensors) is one more implementation.

public interface ICpuSource : IDisposable
{
    /// <param name="perProcessor">Also read each logical processor (only while the page's strip is open).</param>
    /// <returns>Null until a second sample exists (utility is a rate).</returns>
    CpuReading? Read(bool perProcessor);
}

public interface IMemorySource
{
    MemoryReading Read();
}

public interface IGpuSource : IDisposable
{
    /// <param name="load">Also read engine load and adapter memory, the costly counters: only while a GPU reading is shown.</param>
    /// <returns>Adapters with a display driver, without the Microsoft Basic Render Driver; empty when none.</returns>
    IReadOnlyList<GpuReading> Read(bool load);
}

public interface INetworkSource
{
    /// <returns>Real adapters only (hardware, not filter-driver copies), connected or not.</returns>
    IReadOnlyList<NetworkReading> Read();
}

public interface IDiskSource : IDisposable
{
    /// <returns>Null until a second sample exists (the disk counters are all rates).</returns>
    IReadOnlyList<DiskReading>? Read();
}

public interface IBatterySource
{
    /// <returns>Null when the PC has no battery.</returns>
    BatteryReading? Read();
}

public interface ITemperatureSource : IDisposable
{
    /// <returns>ACPI thermal zones; empty when the board exposes none.</returns>
    IReadOnlyList<ThermalZoneReading> Read();
}

/// <summary>Every source the sampler reads. Tests pass fakes; the app passes <c>WindowsSources.Create()</c>.</summary>
public sealed record SensorSources(
    ICpuSource Cpu,
    IMemorySource Memory,
    IGpuSource Gpu,
    INetworkSource Network,
    IDiskSource Disks,
    IBatterySource Battery,
    ITemperatureSource Temperature) : IDisposable
{
    public void Dispose()
    {
        Cpu.Dispose();
        Gpu.Dispose();
        Disks.Dispose();
        Temperature.Dispose();
    }
}
