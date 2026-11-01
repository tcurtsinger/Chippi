// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Runtime.InteropServices;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// Graphics adapters, named by PnP device instance ID. Temperature and measured fan come from D3DKMT
/// (any vendor); NVIDIA's intended fan percentage from NVML, matched by PCI bus and device. Load and
/// memory come from the GPU counters, only when asked for: GPU Engine is the costliest read, hundreds
/// of instances (822 on the owner's PC), so they're summed in place without a string apiece.
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
    // Each engine's load summed over its processes, by adapter; and by process, for a GPU page's top apps.
    private readonly Dictionary<(int Adapter, int Phys, int Engine), double> _engines = [];
    private readonly Dictionary<(int Adapter, int Pid, int Phys, int Engine), double> _processEngines = [];

    public IReadOnlyList<GpuReading> Read(bool load, bool sensors = true, bool processes = true)
    {
        Refresh();
        var counters = load ? ReadLoad(processes) : null;
        var readings = new List<GpuReading>(_adapters.Count);
        for (int i = 0; i < _adapters.Count; i++)
        {
            var adapter = _adapters[i];
            double? temperature = null;
            uint? fanRpm = null, fanPercent = null;
            if (sensors)
            {
                (temperature, fanRpm) = GpuNative.PerfData(adapter.Handle);
                if (_nvml is not null && _nvidia.TryGetValue(adapter.Id, out nint device))
                {
                    fanPercent = _nvml.FanPercent(device);
                    temperature ??= _nvml.TemperatureC(device);
                }
            }
            var c = counters?[i] ?? default;
            readings.Add(new GpuReading(
                adapter.Id,
                adapter.Name,
                c.Load,
                c.Dedicated,
                adapter.DedicatedTotalBytes,
                c.Shared,
                temperature,
                fanRpm,
                fanPercent,
                c.Processes));
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

    /// <summary>One adapter's counters: its load, memory and, when asked for, each process's load.</summary>
    private struct Counters
    {
        public double? Load;
        public ulong? Dedicated, Shared;
        public Dictionary<int, double>? Processes;
    }

    private Counters[] ReadLoad(bool processes)
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
        var result = new Counters[_adapters.Count];

        // Engine instances are "pid_N_luid_0x…_0x…_phys_P_eng_E_engtype_T". Each engine is summed over
        // processes; an adapter's load is its busiest engine, as Task Manager shows it. An instance
        // without a value (a process that just started) adds nothing; an adapter with no valid
        // instance at all has no load reading, not 0. A process's load is its own busiest engine, as
        // Task Manager's GPU column shows it.
        _engines.Clear();
        _processEngines.Clear();
        if (rates)
        {
            foreach (var instance in _load.Instances(_engine))
            {
                if (instance.Value is not { } value || !Engine(instance.Name, out var luid, out int pid, out int phys, out int engine))
                    continue;
                int adapter = AdapterOf(luid);
                if (adapter < 0)
                    continue;
                CollectionsMarshal.GetValueRefOrAddDefault(_engines, (adapter, phys, engine), out _) += value;
                if (processes && pid > 0)
                    CollectionsMarshal.GetValueRefOrAddDefault(_processEngines, (adapter, pid, phys, engine), out _) += value;
            }
            for (int a = 0; a < result.Length; a++)
                result[a].Processes = processes ? [] : null;
        }
        foreach (var ((adapter, _, _), sum) in _engines)
            result[adapter].Load = Math.Min(Math.Max(result[adapter].Load ?? 0, sum), 100);
        foreach (var ((adapter, pid, _, _), sum) in _processEngines)
        {
            var loads = result[adapter].Processes!;
            loads[pid] = Math.Min(Math.Max(loads.GetValueOrDefault(pid), sum), 100);
        }

        var dedicated = Memory(_dedicated);
        var shared = Memory(_shared);
        for (int a = 0; a < result.Length; a++)
            (result[a].Dedicated, result[a].Shared) = (dedicated[a], shared[a]);
        return result;
    }

    // Adapter memory instances are "luid_0x…_0x…_phys_P"; an adapter's figure is the sum over its physical parts.
    private ulong?[] Memory(nint counter)
    {
        var sums = new ulong?[_adapters.Count];
        foreach (var instance in _load!.Instances(counter))
        {
            if (instance.Value is not { } value || !Between(instance.Name, "luid_", "_phys", out var luid))
                continue;
            int adapter = AdapterOf(luid);
            if (adapter >= 0)
                sums[adapter] = (sums[adapter] ?? 0) + (ulong)value;
        }
        return sums;
    }

    private int AdapterOf(ReadOnlySpan<char> luid)
    {
        for (int i = 0; i < _adapters.Count; i++)
            if (luid.Equals(_adapters[i].Luid, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // "pid_N_luid_0x…_0x…_phys_P_eng_E_engtype_T": the adapter, the process (-1 without one) and the engine.
    private static bool Engine(ReadOnlySpan<char> name, out ReadOnlySpan<char> luid, out int pid, out int phys, out int engine)
    {
        pid = Between(name, "pid_", "_luid", out var p) && Number(p, out int n) ? n : -1;
        phys = engine = 0;
        if (!Between(name, "luid_", "_phys", out luid) || !Between(name, "_phys_", "_engtype", out var part))
            return false;
        int at = part.IndexOf("_eng_", StringComparison.Ordinal);
        return at > 0 && Number(part[..at], out phys) && Number(part[(at + 5)..], out engine);
    }

    private static bool Number(ReadOnlySpan<char> text, out int value) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool Between(ReadOnlySpan<char> s, string start, string end, out ReadOnlySpan<char> value)
    {
        value = default;
        int i = s.IndexOf(start, StringComparison.Ordinal);
        if (i < 0)
            return false;
        s = s[(i + start.Length)..];
        int j = s.IndexOf(end, StringComparison.Ordinal);
        if (j < 0)
            return false;
        value = s[..j];
        return true;
    }
}
