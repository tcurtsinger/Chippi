// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Features.RadialMenu.Pc;

/// <summary>The Radial menu's Win32: the hooks, the clicks it gives back, the pointer and monitors. A slice's action runs on the shell's.</summary>
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

    public const uint InputMouse = 0;
    public const uint MouseMiddleDown = 0x0020, MouseMiddleUp = 0x0040, MouseXDown = 0x0080, MouseXUp = 0x0100;

    /// <summary>Marks the input Chippi sends, so its own hook passes it on.</summary>
    public const nint Ours = Core.Input.ChippiInput.Marker;

    public const int HookKeyboard = 13, HookMouse = 14;
    public const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105, WmQuit = 0x0012;
    public const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmRButtonDown = 0x0204, WmRButtonUp = 0x0205,
        WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmXButtonDown = 0x020B, WmXButtonUp = 0x020C;
    public const uint InjectedMouse = 0x01, InjectedKey = 0x10;

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(Point at, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>The monitor's rectangle, its work area (without the taskbar), and its scale.</summary>
    public static (Rect Bounds, Rect Work, double Scale) Monitor(nint monitor)
    {
        var info = new MonitorInfo { Size = sizeof(MonitorInfo) };
        if (!GetMonitorInfo(monitor, ref info))
            return (default, default, 1);
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        return (info.Monitor, info.Work, scale);
    }

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
