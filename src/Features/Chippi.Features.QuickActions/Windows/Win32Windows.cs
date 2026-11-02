// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Windows.Win32;
using Windows.Win32.Foundation;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>Small window lookups the Quick actions share. None of them sends a message, so a hung window can't stall them.</summary>
internal static class Win32Windows
{
    public static bool ClassIs(HWND hwnd, string name)
    {
        Span<char> buffer = stackalloc char[64];
        int length = PInvoke.GetClassName(hwnd, buffer);
        return length > 0 && buffer[..length].SequenceEqual(name);
    }

    public static uint ProcessId(HWND hwnd)
    {
        PInvoke.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    /// <summary>Every top-level window, in z-order (the front one first).</summary>
    public static List<HWND> TopLevel()
    {
        var windows = new List<HWND>();
        PInvoke.EnumWindows((hwnd, _) =>
        {
            windows.Add(hwnd);
            return true;
        }, default);
        return windows;
    }

    /// <summary>The top-level windows of a class.</summary>
    public static List<HWND> TopLevel(string className) => [.. TopLevel().Where(w => ClassIs(w, className))];

    /// <summary>The taskbar on the main screen, or null when Explorer isn't running.</summary>
    public static HWND Taskbar => PInvoke.FindWindowEx(HWND.Null, HWND.Null, "Shell_TrayWnd", null);

    /// <summary>The desktop's folder view, under <c>Progman</c> or, after Windows moved it for the wallpaper, a <c>WorkerW</c>.</summary>
    public static HWND DesktopView()
    {
        var shell = PInvoke.GetShellWindow();
        if (!shell.IsNull)
        {
            var view = PInvoke.FindWindowEx(shell, HWND.Null, "SHELLDLL_DefView", null);
            if (!view.IsNull)
                return view;
        }
        foreach (var worker in TopLevel("WorkerW"))
        {
            var view = PInvoke.FindWindowEx(worker, HWND.Null, "SHELLDLL_DefView", null);
            if (!view.IsNull)
                return view;
        }
        return HWND.Null;
    }

    /// <summary>Runs shell work on its own STA thread, as the shell expects of its callers.</summary>
    public static Task RunSta(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }) { IsBackground = true, Name = "Chippi Quick actions" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
