// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// Physical memory from GlobalMemoryStatusEx; system commit from GetPerformanceInfo, because
/// MEMORYSTATUSEX's page-file fields can reflect a process or job limit.
/// </summary>
internal sealed class MemorySource : IMemorySource
{
    public MemoryReading Read()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        var performance = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!K32GetPerformanceInfo(ref performance, performance.Size))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        ulong page = performance.PageSize;
        return new MemoryReading(
            status.TotalPhys - status.AvailPhys,
            status.TotalPhys,
            performance.CommitTotal * page,
            performance.CommitLimit * page);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetPerformanceInfo(ref PerformanceInformation info, uint size);
}
