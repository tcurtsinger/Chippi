// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Chippi.Features.AgentUsage.Sessions;

/// <summary>What the session lists ask of Windows. Tests pass a fake.</summary>
internal interface IProcesses
{
    /// <summary>Whether <paramref name="processId"/> runs, and is the process that started at <paramref name="startedFileTime"/> (a FILETIME), when given.</summary>
    bool IsRunning(int processId, long? startedFileTime);

    /// <summary>Whether a process holds a lock on <paramref name="path"/> (Codex's writer lock on a thread it has open). Only reads.</summary>
    bool IsLocked(string path);

    /// <summary>The process that has <paramref name="path"/> open (Codex's for a thread), or null.</summary>
    int? HolderOf(string path);

    /// <summary>The app <paramref name="processId"/> runs in: the first of it and its parents with a window.</summary>
    SessionApp? AppOf(int processId);

    /// <summary>Brings <paramref name="window"/> forward, restoring it when it's minimized; false when Windows didn't.</summary>
    bool BringForward(nint window);
}

/// <summary>
/// The Windows answers. A Codex lock is tested by reading one byte under it, which a held lock refuses; the lock is never
/// taken. Its holder comes from the Restart Manager, which only lists. An app is found by walking up the process's parents
/// (a terminal's shell, VS Code's extension host, the Claude app's helper) to one with a visible top-level window.
/// </summary>
internal sealed class WindowsProcesses : IProcesses
{
    private const int LockViolation = unchecked((int)0x80070021);
    private static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "svchost", "services", "wininit", "winlogon", "csrss", "smss", "System", "Idle", "sihost", "taskhostw", "RuntimeBroker",
    };

    public bool IsRunning(int processId, long? startedFileTime)
    {
        nint process = OpenProcess(QueryLimitedInformation, false, (uint)processId);
        if (process == 0)
            return false;
        try
        {
            if (!GetExitCodeProcess(process, out uint code) || code != StillActive)
                return false;
            if (startedFileTime is not { } expected)
                return true;
            // The same pid can be a new process; a second's slack for how the agent rounds its start.
            return GetProcessTimes(process, out long created, out _, out _, out _) && Math.Abs(created - expected) < TimeSpan.TicksPerSecond;
        }
        finally { CloseHandle(process); }
    }

    public bool IsLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            Span<byte> one = stackalloc byte[1];
            _ = stream.Read(one);
            return false;
        }
        catch (IOException ex) when (ex.HResult == LockViolation) { return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public unsafe int? HolderOf(string path)
    {
        char* key = stackalloc char[RmSessionKeyLength + 1];
        if (RmStartSession(out uint session, 0, key) != 0)
            return null;
        try
        {
            if (RmRegisterResources(session, 1, [path], 0, 0, 0, null) != 0)
                return null;
            uint needed = 0, count = 0;
            int result = RmGetList(session, out needed, ref count, null, out _);
            if (result != MoreData || needed == 0)
                return null;
            var info = new RmProcessInfo[needed];
            count = needed;
            if (RmGetList(session, out needed, ref count, info, out _) != 0)
                return null;
            return count > 0 ? (int)info[0].Process.ProcessId : null;
        }
        finally { RmEndSession(session); }
    }

    public SessionApp? AppOf(int processId)
    {
        var parents = Parents();
        var windows = TopWindows();
        int pid = processId;
        for (int depth = 0; depth < 12 && pid > 4; depth++)
        {
            if (!parents.TryGetValue(pid, out var me) || Shells.Contains(me.Name))
                return null;
            if (windows.TryGetValue(pid, out nint window))
                return new SessionApp(window, AppName(me.Name, pid));
            pid = me.Parent;
        }
        return null;
    }

    public bool BringForward(nint window)
    {
        if (window == 0 || !IsWindow(window))
            return false;
        if (IsIconic(window))
            ShowWindow(window, Restore);
        return SetForegroundWindow(window);
    }

    /// <summary>"Terminal", "VS Code", "Claude", "Codex", else the program's own description ("Rider").</summary>
    internal static string AppName(string exe, int processId)
    {
        switch (exe.ToLowerInvariant())
        {
            case "windowsterminal" or "openconsole" or "conhost" or "cmd" or "powershell" or "pwsh" or "wezterm-gui" or "alacritty" or "mintty":
                return "Terminal";
            case "code" or "code - insiders":
                return "VS Code";
            case "cursor": return "Cursor";
            case "claude": return "Claude";
            case "codex": return "Codex";
        }
        try
        {
            using var process = Process.GetProcessById(processId);
            string? description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                return description.Trim();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return exe;
    }

    // Every process's parent and program name (without ".exe"), from one snapshot.
    private static Dictionary<int, (int Parent, string Name)> Parents()
    {
        var map = new Dictionary<int, (int, string)>();
        nint snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1)
            return map;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (bool more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
                map[(int)entry.ProcessId] = ((int)entry.ParentProcessId, Path.GetFileNameWithoutExtension(entry.ExeFile));
        }
        finally { CloseHandle(snapshot); }
        return map;
    }

    // Each process's first visible, unowned, titled top-level window: the one you'd switch to.
    private static Dictionary<int, nint> TopWindows()
    {
        var map = new Dictionary<int, nint>();
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window) && GetWindow(window, Owner) == 0 && GetWindowTextLengthW(window) > 0
                && GetWindowThreadProcessId(window, out uint pid) != 0 && !map.ContainsKey((int)pid))
                map[(int)pid] = window;
            return true;
        }, 0);
        return map;
    }

    private const uint QueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;
    private const uint SnapProcess = 0x2;
    private const uint Owner = 4;
    private const int Restore = 9;
    private const int RmSessionKeyLength = 32;
    private const int MoreData = 234;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public uint ProcessId;
        public long StartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(nint snapshot, ref ProcessEntry entry);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLengthW(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern unsafe int RmStartSession(out uint session, int flags, char* key);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint session);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint files, string[] names, uint applications, nint processes, uint services, string[]? serviceNames);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] RmProcessInfo[]? info, out uint reasons);
}
