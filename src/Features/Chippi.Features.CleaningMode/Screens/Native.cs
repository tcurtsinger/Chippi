// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Features.CleaningMode.Screens;

/// <summary>The screens' Win32: every monitor, a window covering one, and the session and desktop notices.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    private const uint MonitorPrimary = 1;

    /// <summary>Every monitor's rectangle in physical pixels, and whether it's the main one.</summary>
    public static List<(Rect Bounds, bool Primary)> Monitors()
    {
        var monitors = new List<(Rect, bool)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref Rect _, IntPtr _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                monitors.Add((info.Monitor, (info.Flags & MonitorPrimary) != 0));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    /// <summary>Puts the window over <paramref name="bounds"/>, above other topmost windows, without taking the keyboard.</summary>
    public static void Cover(IntPtr window, Rect bounds) =>
        SetWindowPos(window, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x0010 | 0x0200);

    /// <summary>
    /// Keeps it out of Alt+Tab and the taskbar; <paramref name="noActivate"/>, a click on it never makes it the window in
    /// front (the main screen's stays in front, so nothing on another screen, the island included, comes up over it).
    /// </summary>
    public static void MakeToolWindow(IntPtr window, bool noActivate) =>
        SetWindowLongPtr(window, -20, new IntPtr(GetWindowLongPtr(window, -20).ToInt64() | 0x80L | (noActivate ? 0x08000000L : 0L)));

    public static void Foreground(IntPtr window) => SetForegroundWindow(window);

    public const int DisplayChange = 0x007E;
    public const int SessionChange = 0x02B1, SessionLock = 0x7;
    public const int PowerBroadcast = 0x0218, Suspend = 0x4;
    private const uint DesktopSwitch = 0x0020;

    public delegate void WinEventProc(IntPtr hook, uint @event, IntPtr window, int objectId, int childId, uint thread, uint time);

    public static bool WatchSession(IntPtr window) => WTSRegisterSessionNotification(window, 0);

    public static void UnwatchSession(IntPtr window) => WTSUnRegisterSessionNotification(window);

    /// <summary>Hears Windows switch desktops (Ctrl+Alt+Del's secure screen) on this thread.</summary>
    public static IntPtr WatchDesktopSwitch(WinEventProc proc) => SetWinEventHook(DesktopSwitch, DesktopSwitch, IntPtr.Zero, proc, 0, 0, 0);

    public static void Unwatch(IntPtr hook) => UnhookWinEvent(hook);

    private delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, ref Rect bounds, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
