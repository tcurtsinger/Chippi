// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>The shell's explorer.exe (the taskbar, Start and the desktop), and restarting it as Task Manager's Restart does.</summary>
internal static class ExplorerShell
{
    /// <summary>
    /// What the taskbar's own Exit Explorer (Ctrl+Shift+right-click) posts to it: the shell saves its state and exits
    /// cleanly, in a second or two. Task Manager's Restart asks the same way.
    /// </summary>
    private const uint ExitExplorer = 0x05B4;

    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(5);

    /// <summary>The shell's process, or 0 when it isn't running.</summary>
    public static uint ProcessId()
    {
        var shell = PInvoke.GetShellWindow();
        if (shell.IsNull)
            shell = Win32Windows.Taskbar;
        return shell.IsNull ? 0 : Win32Windows.ProcessId(shell);
    }

    /// <summary>
    /// Asks the shell to exit and starts it again. One that doesn't exit within 5 seconds is ended, as Task Manager's End
    /// task would, and Windows starts it again itself. (Windows' Restart Manager, used before 2026-11-02, took 30 seconds
    /// to give up on the shell and ended it anyway, which Windows took for a crash: a black screen, then a half-drawn
    /// taskbar.)
    /// </summary>
    public static void Restart()
    {
        uint pid = ProcessId();
        if (pid == 0)
        {
            EnsureRunning();
            return;
        }
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE | PROCESS_ACCESS_RIGHTS.PROCESS_SYNCHRONIZE, false, pid);
        if (process.IsInvalid)
            return;
        var taskbar = Win32Windows.Taskbar;
        bool exited = !taskbar.IsNull && PInvoke.PostMessage(taskbar, ExitExplorer, default, default)
            && PInvoke.WaitForSingleObject(process, (uint)ExitWait.TotalMilliseconds) == WAIT_EVENT.WAIT_OBJECT_0;
        if (exited)
        {
            // Windows doesn't restart a shell that exited cleanly: it's Chippi's to start.
            Start();
            return;
        }
        if (PInvoke.TerminateProcess(process, 1))
            PInvoke.WaitForSingleObject(process, (uint)ExitWait.TotalMilliseconds);
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
        Start();
    }

    private static void Start()
    {
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = false })?.Dispose();
    }
}
