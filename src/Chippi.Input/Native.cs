// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using Chippi.Core.Input;

namespace Chippi.Input;

/// <summary>The hub's Win32: the hooks, its message-only window and timer, the session and desktop events, and the keys it sends.</summary>
internal static unsafe class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyHookInfo
    {
        public uint Vk, Scan, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseHookInfo
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
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
    public struct LastInputInfo
    {
        public uint Size, Time;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Proc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? Menu;
        public string Name;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public nint ExtraInfo;
    }

    // The union's largest member is MOUSEINPUT; keyboard input fits inside it.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    public const int HookKeyboard = 13, HookMouse = 14;

    public delegate nint HookProc(int code, nint wParam, nint lParam);

    public delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    public delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    public static extern int GetMessage(out Msg message, nint window, uint min, uint max);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static extern nint DispatchMessage(in Msg message);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int code);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string? name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    public static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint proc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool KillTimer(nint window, nuint id);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSUnRegisterSessionNotification(nint window);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point at);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input* inputs, int size);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint process);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    /// <summary>The exe behind a window, or null when it can't be read (an elevated or protected process).</summary>
    public static string? AppPath(nint window)
    {
        if (window == 0)
            return null;
        GetWindowThreadProcessId(window, out uint id);
        nint process = id == 0 ? 0 : OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, id);
        if (process == 0)
            return null;
        try
        {
            char* name = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(process, 0, name, ref size) ? new string(name, 0, (int)size) : null;
        }
        finally { CloseHandle(process); }
    }

    private static readonly string ClassName = "Chippi.InputHub." + Environment.ProcessId;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, WndProc> Windows = new();
    private static readonly WndProc Dispatch = (window, message, wParam, lParam) =>
        Windows.TryGetValue(window, out var proc) ? proc(window, message, wParam, lParam) : DefWindowProc(window, message, wParam, lParam);
    private static readonly Lock Registering = new();
    private static bool _registered;

    /// <summary>A message-only window on the calling thread, for the hub's work, timer and session messages.</summary>
    public static nint CreateMessageWindow(WndProc proc)
    {
        lock (Registering)
        {
            if (!_registered)
            {
                var windowClass = new WindowClass
                {
                    Size = (uint)Marshal.SizeOf<WindowClass>(),
                    Proc = Marshal.GetFunctionPointerForDelegate(Dispatch),
                    Instance = GetModuleHandle(null),
                    Name = ClassName,
                };
                if (RegisterClassEx(ref windowClass) == 0)
                    throw new Win32Exception();
                _registered = true;
            }
        }
        nint window = CreateWindowEx(0, ClassName, null, 0, 0, 0, 0, 0, -3 /* HWND_MESSAGE */, 0, GetModuleHandle(null), 0);
        if (window == 0)
            throw new Win32Exception();
        Windows[window] = proc;
        return window;
    }

    public static void DestroyMessageWindow(nint window)
    {
        DestroyWindow(window);
        Windows.TryRemove(window, out _);
    }

    /// <summary>Right Ctrl, Right Alt and the Windows keys are extended keys; sent without the flag they arrive as the left ones.</summary>
    private static uint Extended(ushort vk) => vk is 0xA3 or 0xA5 or 0x5B or 0x5C ? 0x0001u : 0;

    /// <summary>Keys going down or up, marked as Chippi's, in one batch.</summary>
    public static void SendKeys(ReadOnlySpan<SentKey> keys)
    {
        Span<Input> inputs = keys.Length <= 16 ? stackalloc Input[keys.Length] : new Input[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            inputs[i] = new Input
            {
                Type = 1, // INPUT_KEYBOARD
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput { Vk = keys[i].Vk, Flags = (keys[i].Up ? 0x0002u : 0) | Extended(keys[i].Vk), ExtraInfo = ChippiInput.Marker },
                },
            };
        fixed (Input* first = inputs)
            SendInput((uint)inputs.Length, first, sizeof(Input));
    }
}
