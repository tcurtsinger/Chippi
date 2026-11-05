// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Core.Input;

/// <summary>
/// Whether a window is a fullscreen app or game (docs/phase-6/architecture.md §5): D3D fullscreen or presentation mode, or a
/// window covering its whole monitor that isn't merely maximized (a borderless game). The island hides on the same test,
/// for its own display; the input hub's pause applies it to the window in front, wherever it is.
/// </summary>
public static class FullscreenCheck
{
    private const int GwlStyle = -16, WsCaption = 0x00C00000;
    private const int QunsRunningD3DFullScreen = 3, QunsPresentationMode = 4;

    /// <summary>The window in front counts as fullscreen on its own monitor. The desktop and the taskbar never do.</summary>
    public static bool IsFullscreen(nint foreground)
    {
        if (foreground == 0 || IsShell(foreground))
            return false;
        nint monitor = MonitorFromWindow(foreground, 2 /* MONITOR_DEFAULTTONEAREST */);
        int state = NotificationState();
        // Presentation mode is system-wide. D3D fullscreen is too, so it counts only for a window without a caption: a
        // browser clicked on the other monitor while a game holds its own is in front, and nothing should pause.
        return CoversMonitor(foreground, monitor) || state == QunsPresentationMode
            || (state == QunsRunningD3DFullScreen && (GetWindowLong(foreground, GwlStyle) & WsCaption) != WsCaption);
    }

    /// <summary>
    /// SHQueryUserNotificationState, or 0 when it fails. Only D3D fullscreen (3) and presentation mode (4) count: QUNS_BUSY
    /// alone isn't trusted, because some systems report it while an ordinary maximized window is in front.
    /// </summary>
    public static int NotificationState() => SHQueryUserNotificationState(out int state) == 0 ? state : 0;

    /// <summary>
    /// The window covers its whole monitor and isn't merely maximized: a maximized window with a caption covers the monitor
    /// only when the taskbar auto-hides, and isn't fullscreen.
    /// </summary>
    public static bool CoversMonitor(nint window, nint monitor)
    {
        if (monitor == 0 || !IsWindowVisible(window) || IsIconic(window))
            return false;
        if (IsZoomed(window) && (GetWindowLong(window, GwlStyle) & WsCaption) == WsCaption)
            return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(window, out var rect))
            return false;
        var m = info.Monitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }

    /// <summary>The desktop (Progman, WorkerW) and the taskbars.</summary>
    public static bool IsShell(nint window) => ClassOf(window) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static string ClassOf(nint window)
    {
        var buffer = new System.Text.StringBuilder(256);
        return GetClassName(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder name, int size);
}
