// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Text;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>A display adapter found through its device interface, with a D3DKMT handle open on it.</summary>
/// <param name="Luid">"0x0000HIGH_0x0000LOW", as the GPU counters name it. Changes every boot.</param>
internal sealed record DisplayAdapter(string Id, string Name, string Luid, uint Handle, ulong? DedicatedTotalBytes, uint? Bus, uint? Device)
{
    public bool IsNvidia => Id.StartsWith(@"PCI\VEN_10DE", StringComparison.OrdinalIgnoreCase);
}

/// <summary>D3DKMT and Configuration Manager calls for the GPU source. Query calls only.</summary>
internal static unsafe class GpuNative
{
    private static readonly Guid DisplayDeviceArrival = new("1CA05180-A699-450A-9A0C-DE4FBE3DDD89");
    private static readonly DevPropKey InstanceIdKey = new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);
    private static readonly DevPropKey BusNumberKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 23);
    private static readonly DevPropKey AddressKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 30);

    private const int QueryAdapterRegistryInfo = 8;
    private const int QuerySegmentSize = 3;
    private const int QueryAdapterPerfData = 62;
    private const int MaxPath = 260;

    /// <summary>
    /// Every adapter with a display device interface, except the Microsoft Basic Render Driver. The
    /// caller closes each handle with <see cref="Close"/>.
    /// </summary>
    public static List<DisplayAdapter> Open()
    {
        var adapters = new List<DisplayAdapter>();
        foreach (var path in Interfaces())
        {
            string? id = InterfaceString(path, InstanceIdKey);
            if (id is null || id.StartsWith(@"ROOT\BasicRender", StringComparison.OrdinalIgnoreCase))
                continue;
            var open = new OpenAdapterFromDeviceName();
            fixed (char* name = path)
            {
                open.DeviceName = name;
                if (D3DKMTOpenAdapterFromDeviceName(ref open) != 0)
                    continue;
            }
            uint? bus = DeviceUInt(id, BusNumberKey);
            uint? address = DeviceUInt(id, AddressKey);
            adapters.Add(new DisplayAdapter(
                id,
                RegistryName(open.Handle) ?? "GPU",
                $"0x{open.LuidHigh:X8}_0x{open.LuidLow:X8}",
                open.Handle,
                DedicatedTotal(open.Handle),
                bus,
                address >> 16));
        }
        return adapters;
    }

    public static void Close(DisplayAdapter adapter)
    {
        var close = new CloseAdapter { Handle = adapter.Handle };
        _ = D3DKMTCloseAdapter(ref close);
    }

    /// <summary>Temperature (°C) and measured fan speed from the driver's performance data; null when not reported.</summary>
    public static (double? TemperatureC, uint? FanRpm) PerfData(uint handle)
    {
        var data = new AdapterPerfData();
        if (Query(handle, QueryAdapterPerfData, &data, sizeof(AdapterPerfData)) != 0)
            return (null, null);
        // A driver that doesn't report a value leaves 0. A fan at 0 rpm (zero-fan idle) looks the
        // same as no fan at all, so 0 is not a reading.
        return (data.Temperature > 0 ? data.Temperature / 10.0 : null, data.FanRpm > 0 ? data.FanRpm : null);
    }

    private static string? RegistryName(uint handle)
    {
        var buffer = stackalloc char[MaxPath * 4];
        if (Query(handle, QueryAdapterRegistryInfo, buffer, MaxPath * 4 * sizeof(char)) != 0)
            return null;
        // ADAPTERREGISTRYINFO: the adapter string is the first of four MAX_PATH strings.
        string name = new string(buffer, 0, MaxPath);
        int end = name.IndexOf('\0');
        name = (end < 0 ? name : name[..end]).Trim();
        return name.Length > 0 ? name : null;
    }

    private static ulong? DedicatedTotal(uint handle)
    {
        var sizes = new SegmentSizeInfo();
        return Query(handle, QuerySegmentSize, &sizes, sizeof(SegmentSizeInfo)) == 0 && sizes.DedicatedVideoMemory > 0
            ? sizes.DedicatedVideoMemory
            : null;
    }

    private static int Query(uint handle, int type, void* data, int size)
    {
        var query = new QueryAdapterInfo { Handle = handle, Type = type, Data = data, Size = (uint)size };
        return D3DKMTQueryAdapterInfo(ref query);
    }

    private static List<string> Interfaces()
    {
        var cls = DisplayDeviceArrival;
        // The list can grow between the two calls; try again when it has.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out uint length, ref cls, null, 0) != 0)
                return [];
            var buffer = new char[length];
            int result = CM_Get_Device_Interface_ListW(ref cls, null, buffer, length, 0);
            if (result == 0)
                return [.. new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)];
            if (result != CrBufferSmall)
                return [];
        }
        return [];
    }

    private const int CrBufferSmall = 0x1A;

    private static string? InterfaceString(string path, DevPropKey key)
    {
        var buffer = new byte[512];
        uint size = (uint)buffer.Length;
        return CM_Get_Device_Interface_PropertyW(path, ref key, out _, buffer, ref size, 0) == 0
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0')
            : null;
    }

    private static uint? DeviceUInt(string instanceId, DevPropKey key)
    {
        if (CM_Locate_DevNodeW(out uint node, instanceId, 0) != 0)
            return null;
        uint value = 0;
        uint size = sizeof(uint);
        return CM_Get_DevNode_PropertyW(node, ref key, out _, &value, ref size, 0) == 0 && size == sizeof(uint) ? value : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromDeviceName
    {
        public char* DeviceName;
        public uint Handle;
        public uint LuidLow;
        public int LuidHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint Handle;
        public int Type;
        public void* Data;
        public uint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SegmentSizeInfo
    {
        public ulong DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
    }

    // D3DKMT_ADAPTER_PERFDATA (WDDM 2.4). Temperature is in tenths of a degree Celsius.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct AdapterPerfData
    {
        [FieldOffset(0)] public uint PhysicalAdapterIndex;
        [FieldOffset(48)] public uint FanRpm;
        [FieldOffset(52)] public uint Power;
        [FieldOffset(56)] public uint Temperature;
    }

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3DKMTOpenAdapterFromDeviceName(ref OpenAdapterFromDeviceName open);

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter close);

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo query);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid cls, string? device, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid cls, string? device, char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_Device_Interface_PropertyW(string path, ref DevPropKey key, out uint type, byte[] buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Locate_DevNodeW(out uint node, string instanceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_DevNode_PropertyW(uint node, ref DevPropKey key, out uint type, void* buffer, ref uint size, uint flags);
}
