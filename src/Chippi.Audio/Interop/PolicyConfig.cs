// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Audio.Interop;

/// <summary>
/// Undocumented COM interface that sets the default audio endpoint; Windows has no public API for
/// it, so every audio switcher (EarTrumpet, SoundSwitch, Hopper) uses this one. Windows 7+ vtable
/// layout, stable on Windows 10 and 11. Only <see cref="SetDefaultEndpoint"/> is called; the earlier
/// slots keep the method order.
/// </summary>
[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(string deviceId, out IntPtr format);
    [PreserveSig] int GetDeviceFormat(string deviceId, bool isDefault, out IntPtr format);
    [PreserveSig] int ResetDeviceFormat(string deviceId);
    [PreserveSig] int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod(string deviceId, bool isDefault, out IntPtr defaultPeriod, out IntPtr minimumPeriod);
    [PreserveSig] int SetProcessingPeriod(string deviceId, IntPtr period);
    [PreserveSig] int GetShareMode(string deviceId, out IntPtr mode);
    [PreserveSig] int SetShareMode(string deviceId, IntPtr mode);
    [PreserveSig] int GetPropertyValue(string deviceId, bool isFxStore, IntPtr key, out IntPtr value);
    [PreserveSig] int SetPropertyValue(string deviceId, bool isFxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, AudioRole role);
    [PreserveSig] int SetEndpointVisibility(string deviceId, bool isVisible);
}

/// <summary>COM class that implements <see cref="IPolicyConfig"/>.</summary>
[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient
{
}

internal static class ProcessPaths
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>The process's exe path, or null when it can't be read (gone, or protected).</summary>
    public static string? ExePath(int processId)
    {
        if (processId <= 0)
            return null;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

#pragma warning disable SYSLIB1054 // DllImport keeps this free of unsafe code; three calls per new session.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
#pragma warning restore SYSLIB1054
}
