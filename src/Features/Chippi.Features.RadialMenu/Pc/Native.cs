// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Features.RadialMenu.Pc;

/// <summary>The Radial menu's Win32: the hooks, input it sends, monitors, and the windows a slice acts on.</summary>
internal static unsafe class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CursorInfo
    {
        public int Size;
        public uint Flags;
        public nint Cursor;
        public Point At;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseHookInfo
    {
        public Point At;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyHookInfo
    {
        public uint Vk, Scan, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public Point At;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    public const uint InputMouse = 0, InputKeyboard = 1;
    public const uint KeyUp = 0x0002, KeyExtended = 0x0001, KeyUnicode = 0x0004;
    public const uint MouseMiddleDown = 0x0020, MouseMiddleUp = 0x0040, MouseXDown = 0x0080, MouseXUp = 0x0100;

    /// <summary>Marks the input Chippi sends, so its own hook passes it on.</summary>
    public const nint Ours = Core.Input.ChippiInput.Marker;

    public const int HookKeyboard = 13, HookMouse = 14;
    public const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105, WmQuit = 0x0012;
    public const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmRButtonDown = 0x0204, WmRButtonUp = 0x0205,
        WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmXButtonDown = 0x020B, WmXButtonUp = 0x020C;
    public const uint InjectedMouse = 0x01, InjectedKey = 0x10;

    public const uint WmSysCommand = 0x0112;
    public const nint ScMinimize = 0xF020, ScMaximize = 0xF030, ScClose = 0xF060, ScRestore = 0xF120;

    public delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    public static extern int GetMessage(out Msg message, nint window, uint min, uint max);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, Input* inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point at);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(Point at);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern nint GetShellWindow();

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint window, char* name, int size);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(Point at, uint flags);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    public delegate bool MonitorEnum(nint monitor, nint dc, ref Rect bounds, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnum callback, nint data);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect value, int size);

    /// <summary>The window's class name ("CASCADIA_HOSTING_WINDOW_CLASS").</summary>
    public static string ClassOf(nint window)
    {
        char* name = stackalloc char[256];
        int length = GetClassName(window, name, 256);
        return length > 0 ? new string(name, 0, length) : "";
    }

    /// <summary>The monitor's rectangle, its work area (without the taskbar), and its scale.</summary>
    public static (Rect Bounds, Rect Work, double Scale) Monitor(nint monitor)
    {
        var info = new MonitorInfo { Size = sizeof(MonitorInfo) };
        if (!GetMonitorInfo(monitor, ref info))
            return (default, default, 1);
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        return (info.Monitor, info.Work, scale);
    }

    /// <summary>Every monitor's handle, left to right.</summary>
    public static List<nint> Monitors()
    {
        var all = new List<(nint Handle, int Left, int Top)>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref Rect bounds, nint _) =>
        {
            all.Add((monitor, bounds.Left, bounds.Top));
            return true;
        }, 0);
        return [.. all.OrderBy(m => m.Left).ThenBy(m => m.Top).Select(m => m.Handle)];
    }

    /// <summary>A key going down or up, marked as Chippi's.</summary>
    public static Input Key(ushort vk, bool up, uint flags = 0) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Flags = flags | (up ? KeyUp : 0), ExtraInfo = Ours } },
    };

    /// <summary>A UTF-16 unit typed as itself, going down or up.</summary>
    public static Input Unit(char unit, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { Scan = unit, Flags = KeyUnicode | (up ? KeyUp : 0), ExtraInfo = Ours } },
    };

    /// <summary>A mouse button going down or up where the pointer is, marked as Chippi's.</summary>
    public static Input Button(uint flags, uint data = 0) => new()
    {
        Type = InputMouse,
        Data = new InputUnion { Mouse = new MouseInput { Flags = flags, MouseData = data, ExtraInfo = Ours } },
    };

    public static uint Send(ReadOnlySpan<Input> inputs)
    {
        fixed (Input* first = inputs)
            return SendInput((uint)inputs.Length, first, sizeof(Input));
    }
}
