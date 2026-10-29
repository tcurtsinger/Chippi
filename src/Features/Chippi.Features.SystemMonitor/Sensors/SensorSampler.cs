// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.SystemMonitor.Sensors;

/// <summary>What a consumer needs read. The sampler reads the union of every live <see cref="SensorSampler.Watch"/>.</summary>
[Flags]
public enum SensorNeed
{
    None = 0,
    Cpu = 1,
    /// <summary>Each logical processor too (the page's strip while it's open).</summary>
    PerProcessor = 2,
    Memory = 4,
    /// <summary>GPU temperature and fan only (D3DKMT, NVML): cheap.</summary>
    GpuSensors = 8,
    /// <summary>GPU load and memory too (the GPU Engine counters): the costly read.</summary>
    GpuLoad = 16,
    Network = 32,
    Disks = 64,
    Battery = 128,
    ThermalZones = 256,
}

/// <summary>A history point; a null value is a gap (sleep, a failed read, a pause in sampling).</summary>
public readonly record struct HistoryPoint(DateTimeOffset At, double? Value);

/// <summary>History series keys. Per-device series carry the device's stable ID.</summary>
public static class Series
{
    public const string Cpu = "cpu";
    public const string Memory = "memory";
    public static string GpuLoad(string gpuId) => "gpu.load:" + gpuId;
    public static string NetworkReceive(string adapterId) => "net.rx:" + adapterId;
    public static string NetworkSend(string adapterId) => "net.tx:" + adapterId;
}

/// <summary>
/// Reads the sources on one background thread, only while something watches (the page, the tile,
/// an alert), at the update interval. Each tick publishes an immutable <see cref="SystemSnapshot"/>
/// and appends to a 120-point history per series. Nothing watching means no timer and no reads.
/// </summary>
public sealed class SensorSampler : IDisposable
{
    /// <summary>Between the unpublished first sample of a new need and the one that's published.</summary>
    internal static readonly TimeSpan PrimeDelay = TimeSpan.FromMilliseconds(100);

    private readonly SamplerCore _core;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private readonly List<Lease> _leases = [];
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private TimeSpan _interval = TimeSpan.FromSeconds(2);
    private volatile bool _stopping;
    private bool _disposed;

    public SensorSampler(SensorSources sources, TimeProvider? time = null, ILogger? log = null)
    {
        _time = time ?? TimeProvider.System;
        _log = log ?? NullLogger.Instance;
        _core = new SamplerCore(sources, _log);
        _thread = new Thread(Loop) { IsBackground = true, Name = "Chippi.SystemMonitor" };
        _thread.Start();
    }

    /// <summary>Raised on the sampler thread after every tick. Handlers must be quick and marshal to their own thread.</summary>
    public event Action<SystemSnapshot>? Updated;

    public SystemSnapshot Latest => _core.Latest;

    public TimeSpan Interval
    {
        get { lock (_gate) return _interval; }
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value));
            lock (_gate) _interval = value;
            Changed();
        }
    }

    /// <summary>The union of what every live watch needs.</summary>
    public SensorNeed Need
    {
        get
        {
            lock (_gate) return _leases.Aggregate(SensorNeed.None, (all, l) => all | l.Need);
        }
    }

    /// <summary>
    /// Starts reading <paramref name="need"/> until the returned handle is disposed. The first tick
    /// runs right away. Change what a live watch needs with <see cref="SensorWatch.Need"/>.
    /// </summary>
    public SensorWatch Watch(SensorNeed need)
    {
        var lease = new Lease(this, need);
        lock (_gate) _leases.Add(lease);
        Changed();
        return lease;
    }

    /// <summary>The last 120 points of <paramref name="key"/> (see <see cref="Series"/>), oldest first.</summary>
    public IReadOnlyList<HistoryPoint> History(string key) => _core.History(key);

    /// <summary>The PC woke from sleep: the next point of every series starts after a gap.</summary>
    public void Resumed()
    {
        _core.Resumed();
        Changed();
    }

    /// <summary>
    /// Stops the thread and disposes the sources. Watches still held (a page unloading after its
    /// module stopped) can still change or be disposed afterwards; they no longer do anything.
    /// </summary>
    public void Dispose()
    {
        if (_stopping)
            return;
        _stopping = true;
        _wake.Set();
        _thread.Join();
        lock (_gate)
        {
            _disposed = true;
            _wake.Dispose();
        }
    }

    private void Changed()
    {
        lock (_gate)
        {
            if (!_disposed)
                _wake.Set();
        }
    }

    private void Loop()
    {
        var next = DateTimeOffset.MinValue;
        var last = SensorNeed.None;
        try
        {
            while (!_stopping)
            {
                var need = Need;
                var interval = Interval;
                var now = _time.GetUtcNow();
                // A newly added need is read at once rather than at the next tick.
                bool added = (need & ~last) != 0;
                if (need != SensorNeed.None && (now >= next || added))
                {
                    try
                    {
                        if (added)
                        {
                            // Rates (CPU, GPU load, network, and the disk list itself) need two samples, and a
                            // pause of over 10s starts them over: a short unpublished first sample means a page
                            // that just opened shows every reading at once, not 2s later.
                            _core.Tick(now, need, interval);
                            // The whole delay: opening a page also lets go of the tile's watch, and that wake
                            // must not cut it short (rates over a fraction of a millisecond come back empty).
                            long primed = Stopwatch.GetTimestamp();
                            for (var left = PrimeDelay; left > TimeSpan.Zero && !_stopping; left = PrimeDelay - Stopwatch.GetElapsedTime(primed))
                                _wake.WaitOne(left);
                            if (_stopping)
                                break;
                            need = Need;
                            now = _time.GetUtcNow();
                            if (need == SensorNeed.None)
                            {
                                last = SensorNeed.None;
                                continue; // every watch let go meanwhile
                            }
                        }
                        var snapshot = _core.Tick(now, need, interval);
                        Updated?.Invoke(snapshot);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "System monitor tick failed");
                    }
                    last = need;
                    next = now + interval;
                }
                if (need == SensorNeed.None)
                    last = SensorNeed.None;
                var wait = need == SensorNeed.None ? Timeout.InfiniteTimeSpan : Max(next - _time.GetUtcNow(), TimeSpan.Zero);
                _wake.WaitOne(wait);
            }
        }
        finally
        {
            _core.Dispose();
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private sealed class Lease(SensorSampler owner, SensorNeed need) : SensorWatch
    {
        private SensorNeed _need = need;
        private bool _disposed;

        public override SensorNeed Need
        {
            get => _need;
            set
            {
                if (_disposed || value == _need)
                    return;
                _need = value;
                owner.Changed();
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            lock (owner._gate) owner._leases.Remove(this);
            owner.Changed();
        }
    }
}

/// <summary>A live watch on the sampler. Dispose it when the consumer is gone.</summary>
public abstract class SensorWatch : IDisposable
{
    public abstract SensorNeed Need { get; set; }

    public abstract void Dispose();
}
