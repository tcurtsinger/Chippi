// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using Chippi.Core.Notifications;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Alerts;

/// <summary>One enabled condition currently beyond its threshold. Text is also the tile's Caution status.</summary>
public sealed record MonitorAlert(string Kind, string Target, string Text, string TileText);

/// <summary>Settings order, then device order. Conditions drive Caution; Notices have passed sustain and cooldown.</summary>
public sealed record AlertEvaluation(IReadOnlyList<MonitorAlert> Conditions, IReadOnlyList<MonitorAlert> Notices)
{
    public MonitorAlert? First => Conditions.FirstOrDefault();
}

/// <summary>Deterministic alert policy. Call on one thread, or under the owner's lock.</summary>
public sealed class AlertEvaluator(TimeProvider? time = null)
{
    public const string Activation = "--system-monitor";
    public static readonly TimeSpan Sustain = TimeSpan.FromSeconds(12);
    private const NoticePlaces Both = NoticePlaces.Windows | NoticePlaces.Island;
    public static IReadOnlyList<NoticeKind> Kinds { get; } = Array.AsReadOnly<NoticeKind>([
        new("cpu-load", "CPU load", Both), new("memory", "Memory in use", Both),
        new("low-disk", "Low disk space", Both), new("low-battery", "Low battery", Both),
        new("gpu-temperature", "GPU temperature", Both),
    ]);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<(string Kind, string Target), Held> _held = [];
    private readonly Dictionary<(string Kind, string Target), long> _sent = [];

    public static SensorNeed Need(SystemMonitorSettings settings)
    {
        var s = settings.Normalized();
        return (s.CpuLoad.On ? SensorNeed.Cpu : SensorNeed.None)
            | (s.Memory.On ? SensorNeed.Memory : SensorNeed.None)
            | (s.LowDisk.On ? SensorNeed.Disks : SensorNeed.None)
            | (s.LowBattery.On ? SensorNeed.Battery : SensorNeed.None)
            | (s.GpuTemperature.On && s.GpuTemperature.Limits.Count > 0 ? SensorNeed.GpuSensors : SensorNeed.None);
    }

    /// <summary>Resets sustained stretches on resume or settings changes; repeat cooldowns survive recovery.</summary>
    public void ResetSustain() => _held.Clear();

    public AlertEvaluation Evaluate(SystemSnapshot snapshot, SystemMonitorSettings settings)
    {
        var s = settings.Normalized();
        var conditions = new List<MonitorAlert>();
        var notices = new List<MonitorAlert>();
        var seen = new HashSet<(string Kind, string Target)>();
        var now = _time.GetUtcNow();
        long stamp = _time.GetTimestamp();
        var maxGap = s.UpdateInterval * 2.5;
        bool Fresh<T>(Sample<T> sample) where T : class => sample.Status == ReadingStatus.Ok && sample.Value is not null
            && sample.At <= now && now - sample.At <= maxGap;

        void Consider(string kind, string target, string text, string tile, DateTimeOffset at, bool sustained)
        {
            var key = (kind, target.ToUpperInvariant());
            if (!seen.Add(key)) return;
            var alert = new MonitorAlert(kind, target, text, tile);
            conditions.Add(alert);
            bool freshSample = true;
            bool mature = !sustained;
            if (sustained)
            {
                if (!_held.TryGetValue(key, out var held) || at < held.LastAt || at - held.LastAt > maxGap
                    || _time.GetElapsedTime(held.LastStamp, stamp) > maxGap)
                    held = new Held(stamp, at, stamp);
                else
                    freshSample = at != held.LastAt;
                mature = freshSample && _time.GetElapsedTime(held.Since, stamp) >= Sustain;
                _held[key] = freshSample ? held with { LastAt = at, LastStamp = stamp } : held;
            }
            if (mature && (!_sent.TryGetValue(key, out long previous) || _time.GetElapsedTime(previous, stamp) >= s.RepeatInterval))
            {
                _sent[key] = stamp;
                notices.Add(alert);
            }
        }

        if (s.CpuLoad.On && Fresh(snapshot.Cpu) && snapshot.Cpu.Value is { } cpu && double.IsFinite(cpu.Utility)
            && cpu.Utility >= s.CpuLoad.Percent)
        {
            string value = Percent(cpu.Utility);
            Consider("cpu-load", "cpu", "CPU load " + value, "CPU " + value, snapshot.Cpu.At, true);
        }
        if (s.Memory.On && Fresh(snapshot.Memory) && snapshot.Memory.Value is { TotalBytes: > 0 } memory
            && memory.UsedPercent >= s.Memory.Percent)
        {
            string value = Percent(memory.UsedPercent);
            Consider("memory", "memory", "Memory in use " + value, "Memory " + value, snapshot.Memory.At, false);
        }
        if (s.LowDisk.On && Fresh(snapshot.Disks))
            foreach (var volume in snapshot.Disks.Value!.SelectMany(d => d.Volumes))
                if (volume.TotalBytes >= 10_000_000_000 && volume.FreePercent < s.LowDisk.Percent)
                {
                    string value = Percent(volume.FreePercent) + " free";
                    string name = string.IsNullOrWhiteSpace(volume.Letter) ? "Volume" : volume.Letter;
                    Consider("low-disk", volume.Id, name + " " + value, name + " " + value, snapshot.Disks.At, false);
                }
        if (s.LowBattery.On && Fresh(snapshot.Battery) && snapshot.Battery.Value is { State: BatteryState.OnBattery, Percent: { } percent }
            && percent <= s.LowBattery.Percent)
            Consider("low-battery", "battery", "Battery " + Percent(percent), "Battery " + Percent(percent), snapshot.Battery.At, false);
        if (s.GpuTemperature.On && Fresh(snapshot.Gpus))
            foreach (var gpu in snapshot.Gpus.Value!)
                if (gpu.TemperatureC is { } celsius && double.IsFinite(celsius)
                    && s.GpuTemperature.Limits.TryGetValue(gpu.Id, out int limit) && celsius >= limit)
                {
                    double value = s.Temperature == TemperatureUnit.Fahrenheit ? celsius * 9 / 5 + 32 : celsius;
                    string text = gpu.Name + " " + value.ToString("0", CultureInfo.CurrentCulture)
                        + (s.Temperature == TemperatureUnit.Fahrenheit ? " °F" : " °C");
                    Consider("gpu-temperature", gpu.Id, text, text, snapshot.Gpus.At, true);
                }
        foreach (var key in _held.Keys.Where(k => !seen.Contains(k)).ToArray())
            _held.Remove(key);
        return new(Array.AsReadOnly(conditions.ToArray()), Array.AsReadOnly(notices.ToArray()));
    }

    private static string Percent(double value) => Math.Clamp(value, 0, 100).ToString("0", CultureInfo.CurrentCulture) + "%";
    private sealed record Held(long Since, DateTimeOffset LastAt, long LastStamp);
}
