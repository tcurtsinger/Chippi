// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>The Chippi menu's Lock, Sleep, Restart, Shut down and Sign out, done as Quick actions does them.</summary>
internal static class PowerActions
{
    public enum Choice { Sleep, Restart, ShutDown, SignOut }

    public static void Lock()
    {
        if (!LockWorkStation())
            throw new Win32Exception();
    }

    /// <summary>Never forced: apps with unsaved work still ask.</summary>
    public static void Power(Choice choice)
    {
        const uint Planned = 0x80000000; // SHTDN_REASON_MAJOR_OTHER, MINOR_OTHER, FLAG_PLANNED
        const uint Logoff = 0x0, Shutdown = 0x1, Reboot = 0x2, PowerOff = 0x8, Hybrid = 0x00400000;
        switch (choice)
        {
            case Choice.Sleep:
                EnableShutdownPrivilege();
                if (!SetSuspendState(false, false, false))
                    throw new Win32Exception();
                break;
            case Choice.Restart:
                EnableShutdownPrivilege();
                Exit(Reboot, Planned);
                break;
            case Choice.ShutDown:
                EnableShutdownPrivilege();
                Exit(Shutdown | PowerOff | (FastStartup ? Hybrid : 0), Planned);
                break;
            case Choice.SignOut:
                Exit(Logoff, 0);
                break;
        }
    }

    private static bool FastStartup
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            return key?.GetValue("HiberbootEnabled") is int enabled && enabled != 0;
        }
    }

    private static void Exit(uint flags, uint reason)
    {
        if (!ExitWindowsEx(flags, reason))
            throw new Win32Exception();
    }

    private static void EnableShutdownPrivilege()
    {
        using var self = Process.GetCurrentProcess();
        if (!OpenProcessToken(self.Handle, 0x0020 | 0x0008, out nint token)) // TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY
            throw new Win32Exception();
        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out long luid))
                throw new Win32Exception();
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 0x2 };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0) || Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception();
        }
        finally { CloseHandle(token); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool LockWorkStation();
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("powrprof.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool wakeupEventsDisabled);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges state, uint length, nint previous, nint returnLength);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
