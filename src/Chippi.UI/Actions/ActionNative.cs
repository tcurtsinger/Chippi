// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.UI.Actions;

/// <summary>The actions' Win32: the windows they act on, the monitors those sit on, and the input they send.</summary>
internal static class ActionNative
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

    // The mouse member gives INPUT its size; actions send keys only.
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

    public const uint InputKeyboard = 1;
    public const uint KeyUp = 0x0002, KeyExtended = 0x0001, KeyUnicode = 0x0004;

    public const uint WmSysCommand = 0x0112;
    public const nint ScMinimize = 0xF020, ScMaximize = 0xF030, ScClose = 0xF060, ScRestore = 0xF120;

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point at);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(Point at);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern nint GetShellWindow();

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, [Out] char[] name, int size);

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

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private delegate bool MonitorEnum(nint monitor, nint dc, ref Rect bounds, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnum callback, nint data);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect value, int size);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? item);

    /// <summary>Whether the shell parses the name (<c>shell:AppsFolder\…</c>); Windows says "not found" for an app that's gone.</summary>
    public static bool Parses(string name)
    {
        int result = SHCreateItemFromParsingName(name, 0, typeof(IShellItem).GUID, out object? item);
        if (item is not null)
            Marshal.ReleaseComObject(item);
        return result == 0;
    }

    /// <summary>The window's class name ("CASCADIA_HOSTING_WINDOW_CLASS").</summary>
    public static string ClassOf(nint window)
    {
        var name = new char[256];
        int length = GetClassName(window, name, name.Length);
        return length > 0 ? new string(name, 0, length) : "";
    }

    /// <summary>The monitor's rectangle and its work area (without the taskbar).</summary>
    public static (Rect Bounds, Rect Work) Monitor(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info) ? (info.Monitor, info.Work) : (default, default);
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

    /// <summary>A key going down or up, marked as Chippi's so its own hook passes it on.</summary>
    public static Input Key(ushort vk, bool up, uint flags = 0) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Flags = flags | (up ? KeyUp : 0), ExtraInfo = Core.Input.ChippiInput.Marker } },
    };

    /// <summary>A UTF-16 unit typed as itself, going down or up.</summary>
    public static Input Unit(char unit, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { Scan = unit, Flags = KeyUnicode | (up ? KeyUp : 0), ExtraInfo = Core.Input.ChippiInput.Marker } },
    };

    public static uint Send(Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
}
