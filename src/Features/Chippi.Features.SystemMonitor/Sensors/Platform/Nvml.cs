// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// NVIDIA's management library, loaded by full path only (System32, then the driver's NVSMI folder),
/// never through the DLL search order. Gives the fan speed NVIDIA intends, as a percentage.
/// </summary>
internal sealed unsafe class Nvml : IDisposable
{
    private const int Success = 0;
    private const int TemperatureGpu = 0;

    private readonly nint _library;
    private readonly delegate* unmanaged<int> _shutdown;
    private readonly delegate* unmanaged<uint*, int> _count;
    private readonly delegate* unmanaged<uint, nint*, int> _handle;
    private readonly delegate* unmanaged<nint, PciInfo*, int> _pci;
    private readonly delegate* unmanaged<nint, uint*, int> _fan;
    private readonly delegate* unmanaged<nint, int, uint*, int> _temperature;

    private Nvml(nint library)
    {
        _library = library;
        _shutdown = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlShutdown");
        _count = (delegate* unmanaged<uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2");
        _handle = (delegate* unmanaged<uint, nint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2");
        _pci = (delegate* unmanaged<nint, PciInfo*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetPciInfo_v3");
        _fan = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetFanSpeed");
        _temperature = (delegate* unmanaged<nint, int, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetTemperature");
    }

    /// <summary>Null when no NVIDIA driver is installed or NVML won't start.</summary>
    public static Nvml? TryOpen()
    {
        string? path = Candidates().FirstOrDefault(File.Exists);
        if (path is null || !NativeLibrary.TryLoad(path, out nint library))
            return null;
        try
        {
            var init = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlInit_v2");
            var nvml = new Nvml(library);
            if (init() == Success)
                return nvml;
        }
        catch (EntryPointNotFoundException)
        {
        }
        NativeLibrary.Free(library);
        return null;
    }

    /// <summary>Each NVIDIA device with its PCI bus and device numbers.</summary>
    public List<(nint Device, uint Bus, uint Slot)> Devices()
    {
        uint count;
        if (_count(&count) != Success)
            return [];
        var devices = new List<(nint, uint, uint)>();
        for (uint i = 0; i < count; i++)
        {
            nint device;
            var pci = new PciInfo();
            if (_handle(i, &device) == Success && _pci(device, &pci) == Success)
                devices.Add((device, pci.Bus, pci.Device));
        }
        return devices;
    }

    public uint? FanPercent(nint device)
    {
        uint percent;
        return _fan(device, &percent) == Success ? percent : null;
    }

    public double? TemperatureC(nint device)
    {
        uint celsius;
        return _temperature(device, TemperatureGpu, &celsius) == Success && celsius > 0 ? celsius : null;
    }

    public void Dispose()
    {
        _ = _shutdown();
        NativeLibrary.Free(_library);
    }

    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(Environment.SystemDirectory, "nvml.dll");
        string? programs = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programs) && Path.IsPathFullyQualified(programs))
            yield return Path.Combine(programs, "NVIDIA Corporation", "NVSMI", "nvml.dll");
    }

    // nvmlPciInfo_t (v3): busIdLegacy[16], domain, bus, device, pciDeviceId, pciSubSystemId, busId[32].
    [StructLayout(LayoutKind.Sequential)]
    private struct PciInfo
    {
        public fixed byte BusIdLegacy[16];
        public uint Domain, Bus, Device, PciDeviceId, PciSubSystemId;
        public fixed byte BusId[32];
    }
}
