// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.SystemMonitor;

/// <summary>The cards on the page, each with its own switch in Settings › System monitor › Readings.</summary>
public enum Reading { Cpu, Memory, Gpu, Network, Disks, Battery, ThermalZone }

/// <summary>Settings › Tile status: the one reading the island tile shows.</summary>
public enum TileStatus { Cpu, Memory, Gpu, Network }

public enum TemperatureUnit { Celsius, Fahrenheit }

public enum NetworkUnit { Bytes, Bits }

/// <summary>An alert's switch and its threshold (a percentage; "free" for low disk space).</summary>
public sealed record PercentAlert
{
    public bool On { get; init; }
    public int Percent { get; init; }
}

/// <summary>GPU temperature: no default limit. A GPU without a limit (its device instance ID absent) isn't watched.</summary>
public sealed record GpuTemperatureAlert
{
    public bool On { get; init; }
    public Dictionary<string, int> Limits { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The module's settings section (design notes #23 to #32, brief §2). Read it through
/// <see cref="Normalized"/>: an edited or older settings file can hold values out of range.
/// </summary>
public sealed record SystemMonitorSettings
{
    public static readonly int[] UpdateChoices = [1, 2, 5];
    public static readonly int[] RepeatChoices = [2, 5, 15, 30, 60];
    public const int PercentStep = 5;
    public const int GpuLimitMin = 40, GpuLimitMax = 110;

    /// <summary>Settings › Update speed, in seconds: 1, 2 (default) or 5.</summary>
    public int UpdateSeconds { get; init; } = 2;

    public TileStatus TileStatus { get; init; } = TileStatus.Cpu;

    /// <summary>Readings switched off. Every reading is on by default.</summary>
    public List<Reading> Off { get; init; } = [];

    public TemperatureUnit Temperature { get; init; } = TemperatureUnit.Celsius;

    public NetworkUnit NetworkSpeed { get; init; } = NetworkUnit.Bytes;

    /// <summary>CPU load at or above, sustained 12 s: 50–100%, default 90%.</summary>
    public PercentAlert CpuLoad { get; init; } = new() { Percent = 90 };

    /// <summary>Memory in use at or above: 50–100%, default 90%.</summary>
    public PercentAlert Memory { get; init; } = new() { Percent = 90 };

    /// <summary>A volume's free space below: 5–30%, default 10%.</summary>
    public PercentAlert LowDisk { get; init; } = new() { Percent = 10 };

    /// <summary>Battery at or below while discharging: 5–50%, default 15%.</summary>
    public PercentAlert LowBattery { get; init; } = new() { Percent = 15 };

    /// <summary>Per-GPU limits in °C (40–110), sustained 12 s.</summary>
    public GpuTemperatureAlert GpuTemperature { get; init; } = new();

    /// <summary>Settings › Repeat interval, in minutes, shared by every alert: 2, 5, 15 (default), 30 or 60.</summary>
    public int RepeatMinutes { get; init; } = 15;

    /// <summary>The GPU chosen on the page, by PnP device instance ID; null for the first that reports load.</summary>
    public string? GpuId { get; init; }

    /// <summary>The network adapter chosen on the page, by InterfaceGuid; null for the first real adapter.</summary>
    public string? NetworkAdapterId { get; init; }

    public bool IsOn(Reading reading) => !Off.Contains(reading);

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(UpdateSeconds);

    public TimeSpan RepeatInterval => TimeSpan.FromMinutes(RepeatMinutes);

    /// <summary>
    /// The tile's reading: the chosen one while it's on, otherwise the first reading still on
    /// (design notes #25), or null when none of the four is on.
    /// </summary>
    public TileStatus? EffectiveTileStatus()
    {
        if (IsOn(ToReading(TileStatus)))
            return TileStatus;
        foreach (var status in Enum.GetValues<TileStatus>())
            if (IsOn(ToReading(status)))
                return status;
        return null;
    }

    public static Reading ToReading(TileStatus status) => status switch
    {
        TileStatus.Memory => Reading.Memory,
        TileStatus.Gpu => Reading.Gpu,
        TileStatus.Network => Reading.Network,
        _ => Reading.Cpu,
    };

    /// <summary>A copy with every value in range: unknown choices fall back to defaults, thresholds snap to their step.</summary>
    public SystemMonitorSettings Normalized()
    {
        var defaults = new SystemMonitorSettings();
        return this with
        {
            UpdateSeconds = UpdateChoices.Contains(UpdateSeconds) ? UpdateSeconds : defaults.UpdateSeconds,
            TileStatus = Enum.IsDefined(TileStatus) ? TileStatus : defaults.TileStatus,
            Off = [.. (Off ?? []).Where(Enum.IsDefined).Distinct()],
            Temperature = Enum.IsDefined(Temperature) ? Temperature : defaults.Temperature,
            NetworkSpeed = Enum.IsDefined(NetworkSpeed) ? NetworkSpeed : defaults.NetworkSpeed,
            CpuLoad = Snap(CpuLoad, defaults.CpuLoad, 50, 100),
            Memory = Snap(Memory, defaults.Memory, 50, 100),
            LowDisk = Snap(LowDisk, defaults.LowDisk, 5, 30),
            LowBattery = Snap(LowBattery, defaults.LowBattery, 5, 50),
            GpuTemperature = new GpuTemperatureAlert
            {
                On = GpuTemperature?.On ?? false,
                Limits = new((GpuTemperature?.Limits ?? []).Where(l => !string.IsNullOrWhiteSpace(l.Key))
                    .ToDictionary(l => l.Key, l => Math.Clamp(l.Value, GpuLimitMin, GpuLimitMax), StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase),
            },
            RepeatMinutes = RepeatChoices.Contains(RepeatMinutes) ? RepeatMinutes : defaults.RepeatMinutes,
        };
    }

    private static PercentAlert Snap(PercentAlert? alert, PercentAlert fallback, int min, int max)
    {
        if (alert is null)
            return fallback;
        int snapped = (int)Math.Round(alert.Percent / (double)PercentStep) * PercentStep;
        return alert with { Percent = Math.Clamp(snapped, min, max) };
    }
}
