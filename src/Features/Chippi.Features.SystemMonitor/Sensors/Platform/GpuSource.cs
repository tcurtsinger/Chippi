// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// Graphics adapters, named by PnP device instance ID. Temperature and measured fan come from D3DKMT
/// (any vendor); NVIDIA's intended fan percentage from NVML, matched by PCI bus and device. Load and
/// memory come from the GPU counters, only when asked for: GPU Engine is the costliest read.
/// </summary>
internal sealed class GpuSource : IGpuSource
{
    /// <summary>Adapters are found again this often (a driver update or an external GPU changes them).</summary>
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromSeconds(30);

    private List<DisplayAdapter> _adapters = [];
    private Dictionary<string, nint> _nvidia = [];
    private long _refreshed = long.MinValue;
    private Nvml? _nvml;
    private bool _nvmlTried;
    private PdhQuery? _load;
    private nint _engine, _dedicated, _shared;
    private readonly RateClock _loadClock = new();

    public IReadOnlyList<GpuReading> Read(bool load)
    {
        Refresh();
        var engines = load ? ReadLoad() : null;
        var readings = new List<GpuReading>(_adapters.Count);
        foreach (var adapter in _adapters)
        {
            var (temperature, fanRpm) = GpuNative.PerfData(adapter.Handle);
            uint? fanPercent = null;
            if (_nvml is not null && _nvidia.TryGetValue(adapter.Id, out nint device))
            {
                fanPercent = _nvml.FanPercent(device);
                temperature ??= _nvml.TemperatureC(device);
            }
            var counters = engines?.GetValueOrDefault(adapter.Luid);
            readings.Add(new GpuReading(
                adapter.Id,
                adapter.Name,
                counters?.Load,
                counters?.Dedicated,
                adapter.DedicatedTotalBytes,
                counters?.Shared,
                temperature,
                fanRpm,
                fanPercent));
        }
        return readings;
    }

    public void Dispose()
    {
        foreach (var adapter in _adapters)
            GpuNative.Close(adapter);
        _adapters = [];
        _nvml?.Dispose();
        _nvml = null;
        _load?.Dispose();
        _load = null;
    }

    private void Refresh()
    {
        long now = Environment.TickCount64;
        if (_refreshed != long.MinValue && now - _refreshed < RefreshAfter.TotalMilliseconds)
            return;
        foreach (var adapter in _adapters)
            GpuNative.Close(adapter);
        _adapters = GpuNative.Open();
        _refreshed = now;

        var nvidia = _adapters.Where(a => a.IsNvidia).ToList();
        if (nvidia.Count > 0 && !_nvmlTried)
        {
            _nvmlTried = true;
            _nvml = Nvml.TryOpen();
        }
        _nvidia = _nvml is null ? [] : Match(nvidia, _nvml.Devices());
    }

    // By PCI bus and device; one NVIDIA adapter and one NVML device match without them.
    private static Dictionary<string, nint> Match(List<DisplayAdapter> adapters, List<(nint Device, uint Bus, uint Slot)> devices)
    {
        var matched = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
            foreach (var device in devices)
                if (adapter.Bus == device.Bus && adapter.Device == device.Slot)
                    matched[adapter.Id] = device.Device;
        if (matched.Count == 0 && adapters.Count == 1 && devices.Count == 1)
            matched[adapters[0].Id] = devices[0].Device;
        return matched;
    }

    private Dictionary<string, (double? Load, ulong? Dedicated, ulong? Shared)> ReadLoad()
    {
        if (_load is null)
        {
            _load = new PdhQuery();
            _engine = _load.Add(@"\GPU Engine(*)\Utilization Percentage");
            _dedicated = _load.Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
            _shared = _load.Add(@"\GPU Adapter Memory(*)\Shared Usage");
        }
        _load.Collect();
        bool rates = _loadClock.Collected();

        // Engine instances are "pid_N_luid_0x…_0x…_phys_P_eng_E_engtype_T". Each engine is summed over
        // processes; an adapter's load is its busiest engine, as Task Manager shows it. An instance
        // without a value (a process that just started) adds nothing; an adapter with no valid
        // instance at all has no load reading, not 0.
        var loads = rates
            ? _load.Values(_engine)
                .Select(e => (Luid: Between(e.Name, "luid_", "_phys"), Engine: Between(e.Name, "_phys_", "_engtype"), e.Value))
                .Where(e => e.Luid is not null && e.Engine is not null && e.Value is not null)
                .GroupBy(e => (e.Luid, e.Engine))
                .Select(g => (g.Key.Luid, Sum: g.Sum(e => e.Value!.Value)))
                .GroupBy(e => e.Luid!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Math.Min(g.Max(e => e.Sum), 100), StringComparer.OrdinalIgnoreCase)
            : [];
        var dedicated = Memory(_dedicated);
        var shared = Memory(_shared);

        var result = new Dictionary<string, (double?, ulong?, ulong?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in _adapters)
        {
            double? load = loads.TryGetValue(adapter.Luid, out double l) ? l : null;
            result[adapter.Luid] = (load, dedicated.TryGetValue(adapter.Luid, out var d) ? d : null, shared.TryGetValue(adapter.Luid, out var s) ? s : null);
        }
        return result;
    }

    // Adapter memory instances are "luid_0x…_0x…_phys_P"; an adapter's figure is the sum over its physical parts.
    private Dictionary<string, ulong> Memory(nint counter) =>
        _load!.Values(counter)
            .Select(m => (Luid: Between(m.Name, "luid_", "_phys"), m.Value))
            .Where(m => m.Luid is not null && m.Value is not null)
            .GroupBy(m => m.Luid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (ulong)g.Sum(m => m.Value!.Value), StringComparer.OrdinalIgnoreCase);

    private static string? Between(string s, string start, string end)
    {
        int i = s.IndexOf(start, StringComparison.Ordinal);
        if (i < 0)
            return null;
        i += start.Length;
        int j = s.IndexOf(end, i, StringComparison.Ordinal);
        return j < 0 ? null : s[i..j];
    }
}
