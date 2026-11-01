// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Extensions.Logging;

namespace Chippi.Features.SystemMonitor.Sensors;

/// <summary>
/// One tick of the sampler, without the thread: reads what's needed, turns failures into
/// Unavailable, and keeps the history with its gaps. Not thread-safe for ticks (the sampler thread
/// is the only caller); <see cref="History"/> and <see cref="Latest"/> may be read from any thread.
/// </summary>
internal sealed class SamplerCore(SensorSources sources, ILogger log) : IDisposable
{
    public const int HistoryLength = 120;

    /// <summary>A source that failed is tried again only after this long; its reading stays Unavailable meanwhile.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(10);

    /// <summary>Points further apart than this many intervals have a gap between them (sleep, a pause in watching).</summary>
    public const double GapFactor = 2.5;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<HistoryPoint>> _history = [];
    private readonly Dictionary<string, DateTimeOffset> _lastPoint = [];
    private readonly Dictionary<string, DateTimeOffset> _retryAt = [];
    private readonly HashSet<string> _failing = [];
    private SystemSnapshot _latest = SystemSnapshot.Empty(DateTimeOffset.MinValue);
    private bool _resumed;

    public SystemSnapshot Latest
    {
        get { lock (_gate) return _latest; }
    }

    public void Resumed()
    {
        lock (_gate) _resumed = true;
    }

    public SystemSnapshot Tick(DateTimeOffset now, SensorNeed need, TimeSpan interval)
    {
        bool gapAll;
        lock (_gate)
        {
            gapAll = _resumed;
            _resumed = false;
        }

        var cpu = Read("cpu", now, need.HasFlag(SensorNeed.Cpu) || need.HasFlag(SensorNeed.PerProcessor),
            () => sources.Cpu.Read(need.HasFlag(SensorNeed.PerProcessor)));
        var memory = Read("memory", now, need.HasFlag(SensorNeed.Memory), sources.Memory.Read);
        bool gpu = need.HasFlag(SensorNeed.GpuSensors) || need.HasFlag(SensorNeed.GpuLoad);
        var gpus = ReadList("gpu", now, gpu, () => sources.Gpu.Read(need.HasFlag(SensorNeed.GpuLoad)));
        var network = ReadList("network", now, need.HasFlag(SensorNeed.Network), sources.Network.Read);
        var disks = ReadList("disks", now, need.HasFlag(SensorNeed.Disks), sources.Disks.Read);
        var battery = Read("battery", now, need.HasFlag(SensorNeed.Battery), sources.Battery.Read, nullIsNotSupported: true);
        var zones = ReadList("thermal", now, need.HasFlag(SensorNeed.ThermalZones), sources.Temperature.Read);
        var processes = ReadList("processes", now, need.HasFlag(SensorNeed.Processes), sources.Processes.Read);
        var snapshot = new SystemSnapshot(now, cpu, memory, gpus, network, disks, battery, zones) { Processes = processes };

        lock (_gate)
        {
            if (gapAll)
                foreach (var key in _history.Keys)
                    Append(key, now, null, interval, force: true);
            AppendOne(Series.Cpu, cpu, c => Math.Min(c.Utility, 100), now, interval);
            AppendOne(Series.Memory, memory, m => m.UsedPercent, now, interval);
            if (need.HasFlag(SensorNeed.GpuLoad))
                AppendEach(gpus, g => Series.GpuLoad(g.Id), g => g.Load is { } l ? Math.Min(l, 100) : null, now, interval, "gpu.load:");
            AppendEach(network, n => Series.NetworkReceive(n.Id), n => n.ReceiveBytesPerSecond, now, interval, "net.rx:");
            AppendEach(network, n => Series.NetworkSend(n.Id), n => n.SendBytesPerSecond, now, interval, "net.tx:");
            AppendOne(Series.Battery, battery, b => b.Percent, now, interval);
            AppendEach(zones, z => Series.ThermalZone(z.Id), z => z.TemperatureC, now, interval, "thermal:");
            _latest = snapshot;
        }
        return snapshot;
    }

    public IReadOnlyList<HistoryPoint> History(string key)
    {
        lock (_gate) return _history.TryGetValue(key, out var points) ? [.. points] : [];
    }

    public void Dispose() => sources.Dispose();

    private Sample<T> Read<T>(string name, DateTimeOffset now, bool wanted, Func<T?> read, bool nullIsNotSupported = false) where T : class
    {
        if (!wanted)
            return Sample<T>.NotRead(now);
        if (_retryAt.TryGetValue(name, out var retry) && now < retry)
            return new(ReadingStatus.Unavailable, null, now);
        try
        {
            var value = read();
            _retryAt.Remove(name);
            if (_failing.Remove(name))
                log.LogInformation("System: {Source} reads again", name);
            return value is null
                ? new(nullIsNotSupported ? ReadingStatus.NotSupported : ReadingStatus.Pending, null, now)
                : new(ReadingStatus.Ok, value, now);
        }
        catch (Exception ex)
        {
            _retryAt[name] = now + RetryAfterFailure;
            if (_failing.Add(name))
                log.LogWarning(ex, "System: reading {Source} failed", name);
            return new(ReadingStatus.Unavailable, null, now);
        }
    }

    // A list source that returns nothing means the PC has none (no GPU, no thermal zone): NotSupported.
    private Sample<IReadOnlyList<T>> ReadList<T>(string name, DateTimeOffset now, bool wanted, Func<IReadOnlyList<T>?> read)
    {
        var sample = Read<IReadOnlyList<T>>(name, now, wanted, read);
        return sample is { Status: ReadingStatus.Ok, Value.Count: 0 } ? sample with { Status = ReadingStatus.NotSupported } : sample;
    }

    // Pending (a rate starting over) adds no point of its own but ends a line already drawn: a series
    // never starts with a gap, and never bridges a sample it didn't get.
    private void AppendOne<T>(string key, Sample<T> sample, Func<T, double?> value, DateTimeOffset now, TimeSpan interval) where T : class
    {
        switch (sample.Status)
        {
            case ReadingStatus.NotRead or ReadingStatus.NotSupported:
                return;
            case ReadingStatus.Pending:
                Append(key, now, null, interval, force: true);
                return;
            default:
                Append(key, now, sample.Value is { } v ? value(v) : null, interval);
                return;
        }
    }

    // Each device's series gets a point; a device that was in the history but missing now gets a gap.
    private void AppendEach<T>(Sample<IReadOnlyList<T>> sample, Func<T, string> key, Func<T, double?> value, DateTimeOffset now, TimeSpan interval, string prefix)
    {
        if (sample.Status == ReadingStatus.NotRead)
            return;
        var seen = new HashSet<string>();
        foreach (var item in sample.Value ?? [])
        {
            var k = key(item);
            seen.Add(k);
            Append(k, now, value(item), interval);
        }
        foreach (var k in _history.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !seen.Contains(k)).ToList())
            Append(k, now, null, interval);
    }

    private void Append(string key, DateTimeOffset now, double? value, TimeSpan interval, bool force = false)
    {
        if (!_history.TryGetValue(key, out var points))
            _history[key] = points = new Queue<HistoryPoint>(HistoryLength + 1);
        if (!force && _lastPoint.TryGetValue(key, out var last) && now - last > interval * GapFactor
            && points.Count > 0 && points.Last().Value is not null)
            Add(points, new HistoryPoint(last + interval, null));
        if (force)
        {
            if (points.Count > 0 && points.Last().Value is not null)
                Add(points, new HistoryPoint(now, null));
            return;
        }
        Add(points, new HistoryPoint(now, value));
        _lastPoint[key] = now;
    }

    private static void Add(Queue<HistoryPoint> points, HistoryPoint point)
    {
        points.Enqueue(point);
        while (points.Count > HistoryLength)
            points.Dequeue();
    }
}
