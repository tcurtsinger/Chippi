// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Elevated;

/// <summary>The real Windows behind IPortSystem. Tests never run its actions against a process or service they didn't start.</summary>
public sealed unsafe class NativePortSystem : IPortSystem
{
    private const int InsufficientBuffer = 122, ErrorMoreData = 234, InvalidParameter = 87, StillActive = 259;
    private const uint QueryLimited = 0x1000, TerminateRight = 0x1;
    private const uint TcpOwnerModuleAll = 8, UdpOwnerModule = 2, TableHeader = 8;
    private const uint WmClose = 0x0010;
    private const int ProcessProtectionLevelInfo = 7;
    private const uint ProtectionLevelNone = 0xFFFFFFFE;
    /// <summary>Row sizes and field offsets of the OWNER_MODULE tables (the 16-entry module array makes each row 128 bytes longer).</summary>
    private const int Tcp4Row = 160, Tcp6Row = 192, Udp4Row = 160, Udp6Row = 176;

    public IReadOnlyList<PortEntry> ReadEndpoints(PortProtocol protocol, AddressFamily family)
    {
        bool v6 = family == AddressFamily.InterNetworkV6;
        uint af = v6 ? 23u : 2u;
        int stride = protocol == PortProtocol.Tcp ? (v6 ? Tcp6Row : Tcp4Row) : (v6 ? Udp6Row : Udp4Row);
        uint size = 0;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            byte* buffer = (byte*)NativeMemory.Alloc(size == 0 ? 8 : size);
            try
            {
                int result = protocol == PortProtocol.Tcp
                    ? GetExtendedTcpTable(buffer, ref size, 0, af, TcpOwnerModuleAll, 0)
                    : GetExtendedUdpTable(buffer, ref size, 0, af, UdpOwnerModule, 0);
                if (result == InsufficientBuffer) { size += 4096 + size / 8; continue; }
                if (result != 0) throw new Win32Exception(result);
                uint count = *(uint*)buffer;
                if (TableHeader + (ulong)count * (uint)stride > size) throw new InvalidDataException("Invalid port table.");
                var entries = new List<PortEntry>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    var row = new ReadOnlySpan<byte>(buffer + TableHeader + i * (uint)stride, stride);
                    entries.Add(Parse(protocol, v6, row));
                }
                return entries;
            }
            finally { NativeMemory.Free(buffer); }
        }
        throw new InvalidOperationException("The port table kept changing.");
    }

    private static PortEntry Parse(PortProtocol protocol, bool v6, ReadOnlySpan<byte> row)
    {
        // Ports are stored in network byte order in the low 16 bits of a DWORD.
        static int Port(ReadOnlySpan<byte> row, int offset) => (row[offset] << 8) | row[offset + 1];
        static int Dword(ReadOnlySpan<byte> row, int offset) => BitConverter.ToInt32(row.Slice(offset, 4));
        static long Scope(ReadOnlySpan<byte> row, int offset) => BitConverter.ToUInt32(row.Slice(offset, 4));   // unsigned: a high scope id is not negative
        IPAddress address = v6 ? new IPAddress(row[..16], Scope(row, 16)) : new IPAddress(row.Slice(protocol == PortProtocol.Tcp ? 4 : 0, 4));
        int portOffset = v6 ? 20 : protocol == PortProtocol.Tcp ? 8 : 4;   // a TCP v4 row starts with dwState and dwLocalAddr
        int pidOffset = (protocol, v6) switch { (PortProtocol.Tcp, false) => 20, (PortProtocol.Tcp, true) => 52, (PortProtocol.Udp, false) => 8, _ => 24 };
        return new PortEntry(protocol, address, Port(row, portOffset), Dword(row, pidOffset), row.ToArray());
    }

    public string? OwnerModule(PortEntry entry)
    {
        if (entry.Raw is null) return null;
        bool v6 = entry.Address.AddressFamily == AddressFamily.InterNetworkV6;
        uint size = 0;
        fixed (byte* row = entry.Raw)
        {
            int result = Query(entry.Protocol, v6, row, null, ref size);
            if (result != InsufficientBuffer || size is 0 or > 65536) return null;
            byte* buffer = (byte*)NativeMemory.Alloc(size);
            try
            {
                if (Query(entry.Protocol, v6, row, buffer, ref size) != 0) return null;
                // TCPIP_OWNER_MODULE_BASIC_INFO: two string pointers into this buffer, name first.
                char* name = *(char**)buffer;
                return name == null ? null : new string(name);
            }
            finally { NativeMemory.Free(buffer); }
        }
    }

    private static int Query(PortProtocol protocol, bool v6, byte* row, byte* buffer, ref uint size) => (protocol, v6) switch
    {
        (PortProtocol.Tcp, false) => GetOwnerModuleFromTcpEntry(row, 0, buffer, ref size),
        (PortProtocol.Tcp, true) => GetOwnerModuleFromTcp6Entry(row, 0, buffer, ref size),
        (PortProtocol.Udp, false) => GetOwnerModuleFromUdpEntry(row, 0, buffer, ref size),
        _ => GetOwnerModuleFromUdp6Entry(row, 0, buffer, ref size),
    };

    public ProcessIdentity? Identify(int pid)
    {
        using var process = OpenProcess(QueryLimited, false, (uint)pid);
        if (process.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            return error == InvalidParameter ? null : new ProcessIdentity(0, null, null, Unreadable: true);
        }
        if (!GetExitCodeProcess(process, out uint code) || code != StillActive) return null;
        if (!GetProcessTimes(process, out long created, out _, out _, out _)) return new ProcessIdentity(0, null, null, Unreadable: true);
        string? path = ImagePath(process);
        return new ProcessIdentity(created, path is null ? null : Path.GetFileNameWithoutExtension(path), path, Protected: IsProtected(process));
    }

    /// <summary>A protected (PPL) process opens for limited access too, so its protection level is read: anything but none is protected. A failed read counts as protected.</summary>
    private static bool IsProtected(SafeProcessHandle process) =>
        !GetProcessInformation(process, ProcessProtectionLevelInfo, out uint level, sizeof(uint)) || level != ProtectionLevelNone;

    private static string? ImagePath(SafeProcessHandle process)
    {
        char* buffer = stackalloc char[1024];
        uint length = 1024;
        return QueryFullProcessImageName(process, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : null;
    }

    /// <summary>Opens the process and checks the start time on that same handle, so a reused PID is never touched.</summary>
    private static (SafeProcessHandle? Handle, NativeResult Failure) OpenSame(int pid, long startTime, uint access)
    {
        var process = OpenProcess(access | QueryLimited, false, (uint)pid);
        if (process.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            process.Dispose();
            return (null, new(error == InvalidParameter ? NativeOutcome.Gone : NativeOutcome.Failed, error));
        }
        if (!GetExitCodeProcess(process, out uint code) || code != StillActive) { process.Dispose(); return (null, new(NativeOutcome.Gone)); }
        if (!GetProcessTimes(process, out long created, out _, out _, out _)) { int error = Marshal.GetLastPInvokeError(); process.Dispose(); return (null, new(NativeOutcome.Failed, error)); }
        if (created != startTime) { process.Dispose(); return (null, new(NativeOutcome.Changed)); }
        return (process, default);
    }

    public NativeResult Terminate(int pid, long startTime)
    {
        var (process, failure) = OpenSame(pid, startTime, TerminateRight);
        if (process is null) return failure;
        using (process)
            return TerminateProcess(process, 1) ? new(NativeOutcome.Done) : new(NativeOutcome.Failed, Marshal.GetLastPInvokeError());
    }

    private sealed class WindowPoster(uint pid) { public uint Pid = pid; public int Posted; }

    public NativeResult PostClose(int pid, long startTime)
    {
        var (process, failure) = OpenSame(pid, startTime, 0);
        if (process is null) return failure;
        using (process)
        {
            var state = new WindowPoster((uint)pid);
            var handle = GCHandle.Alloc(state);
            try { EnumWindows(&EnumProc, GCHandle.ToIntPtr(handle)); }
            finally { handle.Free(); }
            return state.Posted > 0 ? new(NativeOutcome.Done) : new(NativeOutcome.Failed, 1400);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int EnumProc(nint window, nint state)
    {
        var poster = (WindowPoster)GCHandle.FromIntPtr(state).Target!;
        GetWindowThreadProcessId(window, out uint owner);
        if (owner == poster.Pid && IsWindowVisible(window) && PostMessage(window, WmClose, 0, 0)) poster.Posted++;
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public nint Name, Display;
        public uint Type, State, Controls, Win32Exit, ServiceExit, CheckPoint, WaitHint, ProcessId, Flags;
    }

    public IReadOnlyList<ServiceEntry> RunningServices()
    {
        nint manager = OpenSCManager(null, null, 0x4);
        if (manager == 0) throw new Win32Exception();
        try
        {
            uint needed = 0, count = 0, resume = 0;
            EnumServicesStatusEx(manager, 0, 0x30, 1, null, 0, out needed, out count, ref resume, null);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                uint size = needed + 8192;
                byte* buffer = (byte*)NativeMemory.Alloc(size);
                try
                {
                    resume = 0;
                    if (!EnumServicesStatusEx(manager, 0, 0x30, 1, buffer, size, out needed, out count, ref resume, null))
                    {
                        int error = Marshal.GetLastPInvokeError();
                        if (error == ErrorMoreData) continue;
                        throw new Win32Exception(error);
                    }
                    var list = new List<ServiceEntry>((int)count);
                    var rows = (EnumServiceStatusProcess*)buffer;
                    for (uint i = 0; i < count; i++)
                        list.Add(new(new string((char*)rows[i].Name), rows[i].Type, rows[i].State, rows[i].Controls, (int)rows[i].ProcessId));
                    return list;
                }
                finally { NativeMemory.Free(buffer); }
            }
            throw new InvalidOperationException("The service list kept changing.");
        }
        finally { CloseServiceHandle(manager); }
    }

    public NativeResult StopService(string name)
    {
        nint manager = OpenSCManager(null, null, 0x1);
        if (manager == 0) return new(NativeOutcome.Failed, Marshal.GetLastPInvokeError());
        try
        {
            nint service = OpenService(manager, name, 0x20 | 0x4);
            if (service == 0) return new(NativeOutcome.Failed, Marshal.GetLastPInvokeError());
            try
            {
                if (!ControlService(service, 1, out var status)) return new(NativeOutcome.Failed, Marshal.GetLastPInvokeError());
                // Give it a short while to finish stopping, so the page's next read sees the port free.
                for (int i = 0; i < 50 && status.CurrentState != 1; i++)
                {
                    Thread.Sleep(200);
                    if (!QueryServiceStatus(service, out status)) break;
                }
                return status.CurrentState == 1 ? new(NativeOutcome.Done) : new(NativeOutcome.Failed, 1053);
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }

    [DllImport("iphlpapi.dll")] private static extern int GetExtendedTcpTable(byte* table, ref uint size, int order, uint family, uint tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern int GetExtendedUdpTable(byte* table, ref uint size, int order, uint family, uint tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern int GetOwnerModuleFromTcpEntry(byte* entry, int infoClass, byte* buffer, ref uint size);
    [DllImport("iphlpapi.dll")] private static extern int GetOwnerModuleFromTcp6Entry(byte* entry, int infoClass, byte* buffer, ref uint size);
    [DllImport("iphlpapi.dll")] private static extern int GetOwnerModuleFromUdpEntry(byte* entry, int infoClass, byte* buffer, ref uint size);
    [DllImport("iphlpapi.dll")] private static extern int GetOwnerModuleFromUdp6Entry(byte* entry, int infoClass, byte* buffer, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* buffer, ref uint size);
    [DllImport("user32.dll")] private static extern int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessInformation(SafeProcessHandle process, int informationClass, out uint information, uint size);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenService(nint manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ControlService(nint service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatus(nint service, out ServiceStatus status);
    [DllImport("advapi32.dll", EntryPoint = "EnumServicesStatusExW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusEx(nint manager, int infoLevel, uint serviceType, uint state, byte* services, uint size, out uint needed, out uint count, ref uint resume, string? group);
}
