// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Shutdown;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>Lock, displays off, the screen saver, the Recycle Bin, and sleep, restart, shut down and sign out.</summary>
internal static class SystemActions
{
    public static void Lock()
    {
        if (!PInvoke.LockWorkStation())
            throw new Win32Exception();
    }

    /// <summary>Turns every screen off and leaves the PC awake. Posted, so no hung window holds it up.</summary>
    public static async Task TurnOffDisplaysAsync()
    {
        // The hand still on the mouse after the click would wake the screens at once.
        await Task.Delay(500);
        if (!PInvoke.PostMessage(HWND.HWND_BROADCAST, PInvoke.WM_SYSCOMMAND, PInvoke.SC_MONITORPOWER, 2))
            throw new Win32Exception();
    }

    /// <summary>
    /// Starts the screen saver now. The one the user picked starts as Windows starts it after the wait, so "On resume,
    /// display logon screen" still locks the PC; with none picked (or screen saving off), the file runs by itself.
    /// </summary>
    public static unsafe void StartScreenSaver()
    {
        string? chosen = ChosenScreenSaver();
        BOOL active = false;
        if (chosen is null
            || !PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETSCREENSAVEACTIVE, 0, &active, 0) || !active)
        {
            string file = chosen ?? Path.Combine(Environment.SystemDirectory, "scrnsave.scr");
            Process.Start(new ProcessStartInfo(file, "/s") { UseShellExecute = false })?.Dispose();
            return;
        }
        // Windows' own way (WM_SYSCOMMAND SC_SCREENSAVE through DefWindowProc) needs a window of this thread; any will do.
        var window = PInvoke.CreateWindowEx(0, "Static", null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, null, null, null);
        if (window.IsNull)
            throw new Win32Exception();
        try
        {
            PInvoke.DefWindowProc(window, PInvoke.WM_SYSCOMMAND, PInvoke.SC_SCREENSAVE, 0);
        }
        finally
        {
            PInvoke.DestroyWindow(window);
        }
    }

    public static Task EmptyRecycleBinAsync() => Win32Windows.RunSta(() =>
    {
        var result = PInvoke.SHEmptyRecycleBin(HWND.Null, null, PInvoke.SHERB_NOCONFIRMATION | PInvoke.SHERB_NOPROGRESSUI | PInvoke.SHERB_NOSOUND);
        if (result.Failed && RecycleBinItems() > 0) // Windows reports an error for a bin that's already empty
            Marshal.ThrowExceptionForHR(result);
    });

    /// <summary>The items in the Recycle Bin on every drive.</summary>
    public static unsafe long RecycleBinItems()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)sizeof(SHQUERYRBINFO) };
        PInvoke.SHQueryRecycleBin(null, ref info).ThrowOnFailure();
        return info.i64NumItems;
    }

    public static void Power(PowerChoice choice)
    {
        const SHUTDOWN_REASON Planned = SHUTDOWN_REASON.SHTDN_REASON_MAJOR_OTHER | SHUTDOWN_REASON.SHTDN_REASON_MINOR_OTHER
            | SHUTDOWN_REASON.SHTDN_REASON_FLAG_PLANNED;
        // Never EWX_FORCE or EWX_FORCEIFHUNG: apps with unsaved work still ask.
        switch (choice)
        {
            case PowerChoice.Sleep:
                EnableShutdownPrivilege();
                if (!PInvoke.SetSuspendState(false, false, false))
                    throw new Win32Exception();
                break;
            case PowerChoice.Restart:
                EnableShutdownPrivilege();
                Exit(EXIT_WINDOWS_FLAGS.EWX_REBOOT, Planned);
                break;
            case PowerChoice.ShutDown:
                EnableShutdownPrivilege();
                Exit(EXIT_WINDOWS_FLAGS.EWX_SHUTDOWN | EXIT_WINDOWS_FLAGS.EWX_POWEROFF
                    | (FastStartup ? EXIT_WINDOWS_FLAGS.EWX_HYBRID_SHUTDOWN : 0), Planned);
                break;
            case PowerChoice.SignOut:
                Exit(EXIT_WINDOWS_FLAGS.EWX_LOGOFF, 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(choice), choice, null);
        }
    }

    /// <summary>The screen saver the user picked, or null when there's none.</summary>
    private static string? ChosenScreenSaver()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        if (key?.GetValue("SCRNSAVE.EXE") is not string chosen || chosen.Trim().Length == 0)
            return null;
        string path = Environment.ExpandEnvironmentVariables(chosen.Trim());
        if (!Path.IsPathRooted(path))
            path = Path.Combine(Environment.SystemDirectory, path);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Fast startup is on: shut down the way the Start menu does, as a hybrid shutdown.</summary>
    private static bool FastStartup
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            return key?.GetValue("HiberbootEnabled") is int enabled && enabled != 0
                && PInvoke.GetPwrCapabilities(out var capabilities) && capabilities.HiberFilePresent;
        }
    }

    private static void Exit(EXIT_WINDOWS_FLAGS flags, SHUTDOWN_REASON reason)
    {
        if (!PInvoke.ExitWindowsEx(flags, reason))
            throw new Win32Exception();
    }

    private static unsafe void EnableShutdownPrivilege()
    {
        using var self = Process.GetCurrentProcess();
        if (!PInvoke.OpenProcessToken(self.SafeHandle, TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
            throw new Win32Exception();
        using (token)
        {
            if (!PInvoke.LookupPrivilegeValue(null, PInvoke.SE_SHUTDOWN_NAME, out var luid))
                throw new Win32Exception();
            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
            privileges.Privileges[0] = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED };
            if (!PInvoke.AdjustTokenPrivileges(token, false, &privileges, default) || Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception();
        }
    }
}
