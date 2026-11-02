// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RestartManager;
using Windows.Win32.System.Threading;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>The shell's explorer.exe (the taskbar, Start and the desktop), and restarting it as Task Manager's Restart does.</summary>
internal static class ExplorerShell
{
    /// <summary>The shell's process, or 0 when it isn't running.</summary>
    public static uint ProcessId()
    {
        var shell = PInvoke.GetShellWindow();
        if (shell.IsNull)
            shell = Win32Windows.Taskbar;
        return shell.IsNull ? 0 : Win32Windows.ProcessId(shell);
    }

    public static void Restart()
    {
        uint pid = ProcessId();
        if (pid != 0 && !RestartWithRestartManager(pid))
        {
            using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE | PROCESS_ACCESS_RIGHTS.PROCESS_SYNCHRONIZE, false, pid);
            if (!process.IsInvalid && PInvoke.TerminateProcess(process, 1))
                PInvoke.WaitForSingleObject(process, 5000);
        }
        EnsureRunning();
    }

    /// <summary>Waits a few seconds for the shell to come back by itself, and starts it when it doesn't.</summary>
    public static void EnsureRunning()
    {
        for (int i = 0; i < 100; i++)
        {
            if (!PInvoke.GetShellWindow().IsNull)
                return;
            Thread.Sleep(100);
        }
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = false })?.Dispose();
    }

    private static unsafe bool RestartWithRestartManager(uint pid)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid || !PInvoke.GetProcessTimes(process, out var started, out _, out _, out _))
            return false;
        Span<char> key = stackalloc char[(int)PInvoke.CCH_RM_SESSION_KEY + 1];
        if (PInvoke.RmStartSession(out uint session, key) != WIN32_ERROR.ERROR_SUCCESS)
            return false;
        try
        {
            RM_UNIQUE_PROCESS[] shell = [new() { dwProcessId = pid, ProcessStartTime = started }];
            return PInvoke.RmRegisterResources(session, [], shell, []) == WIN32_ERROR.ERROR_SUCCESS
                && PInvoke.RmShutdown(session, (uint)RM_SHUTDOWN_TYPE.RmForceShutdown, null) == WIN32_ERROR.ERROR_SUCCESS
                && PInvoke.RmRestart(session, null) == WIN32_ERROR.ERROR_SUCCESS;
        }
        finally
        {
            PInvoke.RmEndSession(session);
        }
    }
}
