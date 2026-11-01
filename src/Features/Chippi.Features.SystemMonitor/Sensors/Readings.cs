// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.SystemMonitor.Sensors;

/// <summary>
/// CPU. <see cref="Utility"/> is Windows' "% Processor Utility", the measure Task Manager shows: it
/// scales with clock speed, so it is clamped to 100 for display, never averaged with time-based %.
/// </summary>
/// <param name="PerProcessor">Each logical processor's utility, in Windows' group,index order; null unless asked for.</param>
/// <param name="EffectiveMhz">Base frequency × "% Processor Performance"; null when Windows doesn't report it.</param>
public sealed record CpuReading(double Utility, int LogicalProcessors, double? EffectiveMhz, IReadOnlyList<double>? PerProcessor = null);

/// <param name="CommitBytes">System commit charge (GetPerformanceInfo), not this process's.</param>
public sealed record MemoryReading(ulong UsedBytes, ulong TotalBytes, ulong CommitBytes, ulong CommitLimitBytes)
{
    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * UsedBytes / TotalBytes;
}

/// <summary>
/// One graphics adapter. Every optional value is null when the driver doesn't report it; a missing
/// value is never 0.
/// </summary>
/// <param name="Id">PnP device instance ID, stable across boots and different for identical cards.</param>
/// <param name="Load">The busiest single engine, as Task Manager shows; null until a second sample or when not asked for.</param>
/// <param name="FanRpm">Measured fan speed (D3DKMT); null when stopped or not reported, which drivers report alike as 0.</param>
/// <param name="FanPercent">NVIDIA's intended fan speed (NVML), a percentage, never RPM.</param>
/// <param name="ProcessLoads">Each process's busiest engine on this GPU, by process ID; read with the load.</param>
public sealed record GpuReading(
    string Id,
    string Name,
    double? Load = null,
    ulong? DedicatedUsedBytes = null,
    ulong? DedicatedTotalBytes = null,
    ulong? SharedUsedBytes = null,
    double? TemperatureC = null,
    uint? FanRpm = null,
    uint? FanPercent = null,
    IReadOnlyDictionary<int, double>? ProcessLoads = null);

/// <summary>A real network adapter (hardware, not a filter-driver copy).</summary>
/// <param name="Id">InterfaceGuid.</param>
/// <param name="ReceiveBytesPerSecond">Null on the first sample, after a counter reset, or while not connected.</param>
public sealed record NetworkReading(string Id, string Name, bool Connected, double? ReceiveBytesPerSecond, double? SendBytesPerSecond);

/// <param name="Id">The volume GUID path (<c>\\?\Volume{…}\</c>).</param>
/// <param name="Letter">"C:", or empty for a volume mounted without a letter.</param>
public sealed record VolumeReading(string Id, string Letter, ulong FreeBytes, ulong TotalBytes)
{
    public double FreePercent => TotalBytes == 0 ? 100 : 100.0 * FreeBytes / TotalBytes;
}

/// <param name="Number">Windows' physical disk number ("Disk 0").</param>
/// <param name="ActivePercent">Active time, 0–100; null until a second sample.</param>
public sealed record DiskReading(int Number, double? ActivePercent, double? ReadBytesPerSecond, double? WriteBytesPerSecond, IReadOnlyList<VolumeReading> Volumes)
{
    /// <summary>Task Manager's name: "Disk 0 (C: D:)".</summary>
    public string Name => Volumes.Any(v => v.Letter.Length > 0)
        ? $"Disk {Number} ({string.Join(" ", Volumes.Where(v => v.Letter.Length > 0).Select(v => v.Letter))})"
        : $"Disk {Number}";
}

public enum BatteryState { OnBattery, Charging, PluggedIn }

/// <param name="RateWatts">Positive while charging, negative while discharging; null when not reported.</param>
/// <param name="TimeLeft">Time left on battery, only when Windows reports it.</param>
public sealed record BatteryReading(BatteryState State, int? Percent, double? RateWatts, TimeSpan? TimeLeft);

/// <summary>An ACPI thermal zone: a zone or board reading, shown under Temperature, never as CPU.</summary>
public sealed record ThermalZoneReading(string Id, double TemperatureC);

/// <summary>One running process, for a detail page's top apps.</summary>
/// <param name="App">What groups processes into one app: the program file's name, lowercase ("chrome.exe").</param>
/// <param name="Name">The app's name as its program file gives it ("Google Chrome"), or the file's name without ".exe".</param>
/// <param name="Path">The program file, for its icon; null when Windows doesn't say.</param>
/// <param name="CpuPercent">Its share of every logical processor since the last sample, 0–100; null on its first sample.</param>
/// <param name="PrivateBytes">Its active private working set, the memory Task Manager shows.</param>
public sealed record ProcessReading(int Id, string App, string Name, string? Path, double? CpuPercent, ulong PrivateBytes);

/// <summary>How a reading in a snapshot came out.</summary>
public enum ReadingStatus
{
    /// <summary>Read; the value is current.</summary>
    Ok,

    /// <summary>Read, but no value yet: a rate needs a second sample. Shown as nothing, no gap.</summary>
    Pending,

    /// <summary>The read failed: the card shows a short status, the graph a gap.</summary>
    Unavailable,

    /// <summary>The hardware doesn't have it (no battery, no thermal zone, no GPU): the card isn't shown.</summary>
    NotSupported,

    /// <summary>Switched off, or no consumer asked for it this tick: not read.</summary>
    NotRead,
}

/// <summary>One reading in a snapshot: its value (null unless Ok) and when it was read.</summary>
public sealed record Sample<T>(ReadingStatus Status, T? Value, DateTimeOffset At) where T : class
{
    public static Sample<T> NotRead(DateTimeOffset at) => new(ReadingStatus.NotRead, null, at);
}

/// <summary>
/// One sampler tick, immutable. List readings (GPUs, network adapters, disks, thermal zones) are
/// empty when there are none; their status says whether that's because the hardware has none.
/// </summary>
public sealed record SystemSnapshot(
    DateTimeOffset At,
    Sample<CpuReading> Cpu,
    Sample<MemoryReading> Memory,
    Sample<IReadOnlyList<GpuReading>> Gpus,
    Sample<IReadOnlyList<NetworkReading>> Network,
    Sample<IReadOnlyList<DiskReading>> Disks,
    Sample<BatteryReading> Battery,
    Sample<IReadOnlyList<ThermalZoneReading>> ThermalZones)
{
    /// <summary>Every process, read only while a detail page with top apps is open.</summary>
    public Sample<IReadOnlyList<ProcessReading>> Processes { get; init; } = Sample<IReadOnlyList<ProcessReading>>.NotRead(At);

    public static SystemSnapshot Empty(DateTimeOffset at) => new(at,
        Sample<CpuReading>.NotRead(at), Sample<MemoryReading>.NotRead(at), Sample<IReadOnlyList<GpuReading>>.NotRead(at),
        Sample<IReadOnlyList<NetworkReading>>.NotRead(at), Sample<IReadOnlyList<DiskReading>>.NotRead(at),
        Sample<BatteryReading>.NotRead(at), Sample<IReadOnlyList<ThermalZoneReading>>.NotRead(at));
}
