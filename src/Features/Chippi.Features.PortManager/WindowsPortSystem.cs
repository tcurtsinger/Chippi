// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Features.PortManager;

/// <summary>
/// The real Windows behind <see cref="IPortSystem"/>. Every read works for a standard user (limited process access only). Close and
/// Terminate are only called by the engine on the owner's own processes, and tests never call them on a process they didn't start.
/// </summary>
public sealed unsafe class WindowsPortSystem : IPortSystem
{
    private const int ProcessProtectionLevelInfo = 7;
    private const uint ProtectionLevelNone = 0xFFFFFFFE;
    private const int InvalidParameter = 87, StillActive = 259, ErrorMoreData = 234;
    private const uint QueryLimited = 0x1000, TerminateRight = 0x1, TcpOwnerModuleAll = 8, UdpOwnerModule = 2, WmClose = 0x0010;
    private static readonly HashSet<string> NamesWithoutExtension = new(StringComparer.OrdinalIgnoreCase)
        { "System", "Idle", "Registry", "Memory Compression", "Secure System" };

    public int CurrentPid { get; } = Environment.ProcessId;
    public string? CurrentPath { get; } = Environment.ProcessPath;

    public IReadOnlyList<PortEndpoint> ReadEndpoints()
    {
        var all = new List<PortEndpoint>();
        foreach (var protocol in new[] { PortProtocol.Tcp, PortProtocol.Udp })
            foreach (bool v6 in new[] { false, true })
                all.AddRange(PortTables.Read(protocol, v6, (byte[] buffer, ref uint size) => Table(protocol, v6, buffer, ref size)));
        return all;
    }

    private static int Table(PortProtocol protocol, bool v6, byte[] buffer, ref uint size)
    {
        uint af = v6 ? 23u : 2u;
        fixed (byte* table = buffer)
            return protocol == PortProtocol.Tcp
                ? GetExtendedTcpTable(table, ref size, 0, af, TcpOwnerModuleAll, 0)
                : GetExtendedUdpTable(table, ref size, 0, af, UdpOwnerModule, 0);
    }

    public string? OwnerModule(PortEndpoint endpoint)
    {
        if (endpoint.Raw is null) return null;
        bool v6 = endpoint.IsIPv6;
        uint size = 0;
        fixed (byte* row = endpoint.Raw)
        {
            int result = Query(endpoint.Protocol, v6, row, null, ref size);
            if (result != PortTables.InsufficientBuffer || size is 0 or > 65536) return null;
            byte* buffer = (byte*)NativeMemory.Alloc(size);
            try
            {
                if (Query(endpoint.Protocol, v6, row, buffer, ref size) != 0) return null;
                // TCPIP_OWNER_MODULE_BASIC_INFO: two string pointers into this buffer, the module name first.
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

    public ProcessInfo? ReadProcess(int pid, ProcessNames? names = null)
    {
        names ??= new ProcessNames();
        using var process = OpenProcess(QueryLimited, false, (uint)pid);
        if (process.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            return error == InvalidParameter ? null : new ProcessInfo(pid, names.StartOf(pid), NameFromList(names, pid), null, Unreadable: true);
        }
        if (!GetExitCodeProcess(process, out uint code) || code != StillActive) return null;
        if (!GetProcessTimes(process, out long created, out _, out _, out _)) return new ProcessInfo(pid, names.StartOf(pid), NameFromList(names, pid), null, Unreadable: true);
        string? path = ImagePath(process);
        string name = path is null ? NameFromList(names, pid) : Path.GetFileName(path);
        return new ProcessInfo(pid, created, name, path, Unreadable: false, CanTerminate: CanTerminate(pid), Protected: IsProtected(process));
    }

    /// <summary>Whether this user may end the process (its own), found by opening it for that and closing it again.</summary>
    private static bool CanTerminate(int pid)
    {
        using var process = OpenProcess(TerminateRight | QueryLimited, false, (uint)pid);
        return !process.IsInvalid;
    }

    private static string? ImagePath(SafeProcessHandle process)
    {
        char* buffer = stackalloc char[1024];
        uint length = 1024;
        return QueryFullProcessImageName(process, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : null;
    }

    /// <summary>A protected process refuses even limited access, so its name comes from the process list, read once per scan.</summary>
    private static string NameFromList(ProcessNames names, int pid)
    {
        string? name = names.Get(pid);
        if (name is null) return "";
        return NamesWithoutExtension.Contains(name) ? name : name + ".exe";
    }

    /// <summary>A protected (PPL) process opens for limited access too: any protection level but none is protected, and a failed read counts as protected.</summary>
    private static bool IsProtected(SafeProcessHandle process) =>
        !GetProcessInformation(process, ProcessProtectionLevelInfo, out uint level, sizeof(uint)) || level != ProtectionLevelNone;

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public nint Name, Display;
        public uint Type, State, Controls, Win32Exit, ServiceExit, CheckPoint, WaitHint, ProcessId, Flags;
    }

    public IReadOnlyList<ServiceInfo> RunningServices()
    {
        nint manager = OpenSCManager(null, null, 0x4);   // SC_MANAGER_ENUMERATE_SERVICE
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
                    var list = new List<ServiceInfo>((int)count);
                    var rows = (EnumServiceStatusProcess*)buffer;
                    for (uint i = 0; i < count; i++)
                        if (rows[i].ProcessId != 0)
                            list.Add(new(new string((char*)rows[i].Name), new string((char*)rows[i].Display), (int)rows[i].ProcessId));
                    return list;
                }
                finally { NativeMemory.Free(buffer); }
            }
            throw new InvalidOperationException("The service list kept changing.");
        }
        finally { CloseServiceHandle(manager); }
    }

    private sealed class WindowWalk(uint pid, bool post)
    {
        public readonly uint Pid = pid;
        public readonly bool Post = post;
        public int Found;
    }

    public bool HasWindow(int pid) => Walk((uint)pid, post: false) > 0;

    private static int Walk(uint pid, bool post)
    {
        var state = new WindowWalk(pid, post);
        var handle = GCHandle.Alloc(state);
        try { EnumWindows(&EnumProc, GCHandle.ToIntPtr(handle)); }
        finally { handle.Free(); }
        return state.Found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int EnumProc(nint window, nint state)
    {
        var walk = (WindowWalk)GCHandle.FromIntPtr(state).Target!;
        GetWindowThreadProcessId(window, out uint owner);
        if (owner != walk.Pid || !IsWindowVisible(window)) return 1;
        if (walk.Post) { if (PostMessage(window, WmClose, 0, 0)) walk.Found++; }
        else walk.Found++;
        return 1;
    }

    /// <summary>Opens the process and checks the start time on that same handle, so a reused PID is never touched.</summary>
    private static (SafeProcessHandle? Handle, EndResult Failure) OpenSame(int pid, long startTime, uint access)
    {
        var process = OpenProcess(access | QueryLimited, false, (uint)pid);
        if (process.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            process.Dispose();
            return (null, new(error == InvalidParameter ? EndOutcome.Gone : EndOutcome.Failed, error));
        }
        if (!GetExitCodeProcess(process, out uint code) || code != StillActive) { process.Dispose(); return (null, new(EndOutcome.Gone)); }
        if (!GetProcessTimes(process, out long created, out _, out _, out _)) { int error = Marshal.GetLastPInvokeError(); process.Dispose(); return (null, new(EndOutcome.Failed, error)); }
        if (created != startTime) { process.Dispose(); return (null, new(EndOutcome.Changed)); }
        return (process, default);
    }

    public EndResult PostClose(int pid, long startTime)
    {
        var (process, failure) = OpenSame(pid, startTime, 0);
        if (process is null) return failure;
        using (process)
            return Walk((uint)pid, post: true) > 0 ? new(EndOutcome.Done) : new(EndOutcome.Failed, 1400);
    }

    public EndResult Terminate(int pid, long startTime)
    {
        var (process, failure) = OpenSame(pid, startTime, TerminateRight);
        if (process is null) return failure;
        using (process)
            return TerminateProcess(process, 1) ? new(EndOutcome.Done) : new(EndOutcome.Failed, Marshal.GetLastPInvokeError());
    }

    [DllImport("iphlpapi.dll")] private static extern int GetExtendedTcpTable(byte* table, ref uint size, int order, uint family, uint tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern int GetExtendedUdpTable(byte* table, ref uint size, int order, uint family, uint tableClass, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern int GetOwnerModuleFromTcpEntry(byte* entry, int infoClass, byte* buffer, ref uint size);
    [DllImport("iphlpapi.dll")] private static extern int GetOwnerModuleFromTcp6Entry(byte* entry, int infoClass, byte* buffer, ref uint size);
    [DllImport("iphlpapi.dll")] private static extern int GetOwnerModuleFromUdpEntry(byte* entry, int infoClass, byte* buffer, ref uint size);
    [DllImport("iphlpapi.dll")] private static extern int GetOwnerModuleFromUdp6Entry(byte* entry, int infoClass, byte* buffer, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessInformation(SafeProcessHandle process, int informationClass, out uint information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* buffer, ref uint size);
    [DllImport("user32.dll")] private static extern int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(nint handle);
    [DllImport("advapi32.dll", EntryPoint = "EnumServicesStatusExW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusEx(nint manager, int infoLevel, uint serviceType, uint serviceState, byte* services, uint size, out uint needed, out uint count, ref uint resume, string? group);
}

/// <summary>
/// Every process's id, image name and creation time from NtQuerySystemInformation(SystemProcessInformation): what Task Manager reads, with
/// no handle to any process, so it works for processes a standard user can't open (services running as LocalSystem or NetworkService).
/// </summary>
internal static unsafe class SystemProcesses
{
    public sealed record Snapshot(IReadOnlyDictionary<int, string> Names, IReadOnlyDictionary<int, long> Starts);

    private const int SystemProcessInformation = 5;
    private const uint InfoLengthMismatch = 0xC0000004;

    public static Snapshot Read()
    {
        var names = new Dictionary<int, string>();
        var starts = new Dictionary<int, long>();
        if (IntPtr.Size != 8) return new(names, starts);   // the offsets below are the 64-bit layout; Chippi ships x64 and ARM64 only
        uint size = 512 * 1024;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            byte* buffer = (byte*)NativeMemory.Alloc(size);
            try
            {
                uint status = NtQuerySystemInformation(SystemProcessInformation, buffer, size, out uint needed);
                if (status == InfoLengthMismatch) { size = Math.Max(size * 2, needed + 64 * 1024); continue; }
                if (status != 0) break;
                for (byte* entry = buffer; ; )
                {
                    long created = *(long*)(entry + 32);
                    ushort length = *(ushort*)(entry + 56);
                    char* text = *(char**)(entry + 64);
                    int pid = (int)*(nint*)(entry + 80);
                    string name = pid == 0 ? "Idle" : text is null ? "" : new string(text, 0, length / 2);
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                    names[pid] = name;
                    starts[pid] = created;
                    uint next = *(uint*)entry;
                    if (next == 0) break;
                    entry += next;
                }
                break;
            }
            finally { NativeMemory.Free(buffer); }
        }
        return new(names, starts);
    }

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int infoClass, void* buffer, uint length, out uint returned);
}
