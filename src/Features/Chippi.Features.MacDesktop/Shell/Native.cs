// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>The Win32 calls the Mac-style desktop makes. Physical pixels throughout.</summary>
internal static partial class Native
{
    internal const int GwlExStyle = -20;
    internal const long WsExLayered = 0x00080000, WsExTransparent = 0x00000020, WsExToolWindow = 0x00000080,
        WsExNoActivate = 0x08000000, WsExAppWindow = 0x00040000, WsExTopmost = 0x00000008;
    internal const uint LwaAlpha = 0x2;
    internal const uint WmCommand = 0x0111, WmInitMenu = 0x0116, WmInitMenuPopup = 0x0117, WmClose = 0x0010,
        WmSettingChange = 0x001A, WmDisplayChange = 0x007E, WmMouseActivate = 0x0021, WmNcHitTest = 0x0084,
        WmDpiChanged = 0x02E0, WmSysCommand = 0x0112;
    internal const int MaNoActivate = 3, HtTransparent = -1;
    internal const int SmShuttingDown = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public Rect(int left, int top, int right, int bottom) => (Left, Top, Right, Bottom) = (left, top, right, bottom);
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly System.Windows.Rect ToWpf() => new(Left, Top, Math.Max(0, Width), Math.Max(0, Height));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AppBarData
    {
        public int Size;
        public nint Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Bounds;
        public nint Param;
    }

    internal const uint AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3, AbmGetState = 4, AbmSetState = 10;
    internal const uint AbnPosChanged = 1, AbnFullscreenApp = 2;
    internal const uint AbeLeft = 0, AbeTop = 1, AbeRight = 2, AbeBottom = 3;
    internal const int AbsAutoHide = 1;

    [DllImport("shell32.dll")] internal static extern nuint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")][return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint window, char[] buffer, int length);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    /// <summary>The window is there to be seen: visible, and not cloaked (Windows keeps its own flyouts cloaked while closed).</summary>
    internal static bool IsShown(nint window) =>
        window != 0 && IsWindow(window) && IsWindowVisible(window) && (DwmGetWindowAttribute(window, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) != 0 || cloaked == 0);

    // Menus
    [DllImport("user32.dll")] internal static extern nint GetMenu(nint window);
    [DllImport("user32.dll")] internal static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] internal static extern nint GetSubMenu(nint menu, int position);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsMenu(nint menu);
    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMenuItemInfo(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItemInfo info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MenuItemInfo
    {
        public int Size;
        public uint Mask, Type, State, Id;
        public nint SubMenu, Checked, Unchecked;
        public nuint Data;
        public nint TypeData;
        public uint Length;
        public nint Bitmap;
    }

    internal const uint MiimState = 0x1, MiimId = 0x2, MiimSubmenu = 0x4, MiimString = 0x40, MiimFType = 0x100;
    internal const uint MftSeparator = 0x800, MftOwnerDraw = 0x100, MfsDisabled = 0x3, MfsChecked = 0x8;

    // Keyboard layouts
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] internal static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);

    // Power
    [StructLayout(LayoutKind.Sequential)]
    internal struct PowerStatus
    {
        public byte AcLine, BatteryFlag, BatteryPercent, SystemStatus;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemPowerStatus(out PowerStatus status);

    /// <summary>Since Windows started, in 100-nanosecond units; kernel time includes idle time.</summary>
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    // WinEvents
    internal delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);
    internal const uint EventSystemForeground = 0x0003, EventObjectNameChange = 0x800C, EventObjectDestroy = 0x8001,
        EventObjectShow = 0x8002, EventObjectHide = 0x8003, EventSystemMinimizeStart = 0x0016, EventSystemMinimizeEnd = 0x0017,
        EventObjectLocationChange = 0x800B;
    internal const uint WinEventOutOfContext = 0, WinEventSkipOwnProcess = 2;
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWinEvent(nint hook);

    internal const uint MonitorDefaultToNearest = 2, MonitorDefaultToPrimary = 1;
    internal static readonly nint HwndTopmost = -1;
    internal const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpShowWindow = 0x40,
        SwpNoOwnerZOrder = 0x200, SwpHideWindow = 0x80;

    internal static string ClassOf(nint window)
    {
        var buffer = new char[256];
        int length = GetClassName(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    /// <summary>A monitor's rectangles in physical pixels and its scale (effective DPI / 96); null when it's gone.</summary>
    internal static (System.Windows.Rect Monitor, System.Windows.Rect Work, double Scale)? Area(nint monitor)
    {
        if (monitor == 0)
            return null;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return null;
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        return (info.Monitor.ToWpf(), info.Work.ToWpf(), scale);
    }

    internal static System.Windows.Point Cursor() => GetCursorPos(out var p) ? new System.Windows.Point(p.X, p.Y) : default;

    /// <summary>Moves and sizes the window in physical pixels, topmost, without activating it.</summary>
    internal static void Place(nint window, System.Windows.Rect rect, bool topmost = true)
    {
        if (window == 0)
            return;
        int left = (int)Math.Floor(rect.Left), top = (int)Math.Floor(rect.Top);
        SetWindowPos(window, topmost ? HwndTopmost : 0, left, top, (int)Math.Ceiling(rect.Right) - left,
            (int)Math.Ceiling(rect.Bottom) - top, SwpNoActivate | SwpNoOwnerZOrder | (topmost ? 0 : SwpNoZOrder));
    }

    /// <summary>
    /// Makes a WPF window a no-activate tool window (never in Alt+Tab or the taskbar; a click leaves the keyboard where it
    /// was), as the island's windows are.
    /// </summary>
    internal static nint MakeNoActivate(Window window, System.Windows.Interop.HwndSourceHook? hook = null)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        long ex = GetWindowLongPtr(handle, GwlExStyle);
        SetWindowLongPtr(handle, GwlExStyle, (nint)((ex | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow));
        var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
        source?.AddHook(NoActivate);
        if (hook is not null)
            source?.AddHook(hook);
        return handle;
    }

    private static nint NoActivate(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg != WmMouseActivate)
            return 0;
        handled = true;
        return MaNoActivate;
    }
}
