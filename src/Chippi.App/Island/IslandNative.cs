// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Island;

/// <summary>A monitor's rectangles in physical pixels and its scale (effective DPI / 96).</summary>
internal readonly record struct MonitorArea(HMONITOR Handle, Rect Monitor, Rect Work, double Scale);

/// <summary>
/// The Win32 side of the island's windows (DESIGN.md §9): no-activate tool windows, placed in
/// physical pixels with SetWindowPos, topmost.
/// </summary>
internal static class IslandNative
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private static readonly HWND Topmost = (HWND)(nint)(-1);

    /// <summary>
    /// Creates the window's handle with WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW (never in Alt+Tab or
    /// the taskbar), and answers WM_MOUSEACTIVATE with MA_NOACTIVATE so a click keeps focus where it was.
    /// </summary>
    public static HWND MakeNoActivate(Window window, HwndSourceHook? hook = null)
    {
        var hwnd = (HWND)new WindowInteropHelper(window).EnsureHandle();
        int ex = PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        _ = PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(NoActivate);
        if (hook is not null)
            source?.AddHook(hook);
        return hwnd;
    }

    private static nint NoActivate(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg != PInvoke.WM_MOUSEACTIVATE)
            return 0;
        handled = true;
        return (nint)PInvoke.MA_NOACTIVATE;
    }

    public static MonitorArea? Area(HMONITOR monitor)
    {
        if (monitor == default)
            return null;
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!PInvoke.GetMonitorInfo(monitor, ref info))
            return null;
        double scale = OperatingSystem.IsWindowsVersionAtLeast(8, 1)
            && PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpi, out _).Succeeded ? dpi / 96.0 : 1.0;
        return new MonitorArea(monitor, ToRect(info.rcMonitor), ToRect(info.rcWork), scale);
    }

    /// <summary>The pointer in physical pixels.</summary>
    public static Point Cursor() => PInvoke.GetCursorPos(out var point) ? new Point(point.X, point.Y) : default;

    public static HMONITOR MonitorAt(Point physical) =>
        PInvoke.MonitorFromPoint(new System.Drawing.Point((int)physical.X, (int)physical.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);

    /// <summary>Moves and sizes the window (physical pixels, rounded out) and keeps it topmost, without activating it.</summary>
    public static void Move(HWND hwnd, Rect rect)
    {
        if (hwnd == default)
            return;
        int left = (int)Math.Floor(rect.Left), top = (int)Math.Floor(rect.Top);
        PInvoke.SetWindowPos(hwnd, Topmost, left, top, (int)Math.Ceiling(rect.Right) - left, (int)Math.Ceiling(rect.Bottom) - top,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Moves the window's top-left corner (physical pixels, rounded down), keeping its size and place in the
    /// z-order: a drag's move every frame. <see cref="Move"/> rounds the size out, so a fractional position can
    /// resize it by a pixel, and WPF redraws the whole layered window.
    /// </summary>
    public static void MoveTo(HWND hwnd, Point topLeft)
    {
        if (hwnd != default)
            PInvoke.SetWindowPos(hwnd, HWND.Null, (int)Math.Floor(topLeft.X), (int)Math.Floor(topLeft.Y), 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    /// <summary>Brings the window to the top of the topmost windows, where it is, without activating it.</summary>
    public static void Raise(HWND hwnd)
    {
        if (hwnd != default)
            PInvoke.SetWindowPos(hwnd, Topmost, 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Makes the window the foreground window. Only after the user clicked into it: the click is the
    /// last input, so Windows allows it.
    /// </summary>
    public static void Activate(HWND hwnd)
    {
        if (hwnd != default)
            PInvoke.SetForegroundWindow(hwnd);
    }

    public static Rect WindowRect(HWND hwnd) =>
        hwnd != default && PInvoke.GetWindowRect(hwnd, out var r) ? ToRect(r) : Rect.Empty;

    private static Rect ToRect(RECT r) => new(r.left, r.top, r.right - r.left, r.bottom - r.top);
}
