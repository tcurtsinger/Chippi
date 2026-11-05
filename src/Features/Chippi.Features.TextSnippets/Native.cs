// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Core.Input;

namespace Chippi.Features.TextSnippets;

/// <summary>The Win32 Text snippets needs: typed characters, the window in front, and keys sent as Chippi's.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    // The mouse member gives INPUT its size; snippets send keys only.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    private const uint KeyUp = 0x0002, KeyUnicode = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vk);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint thread);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, [Out] char[] text, int size, uint flags, nint layout);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int kind, out int value, int size, out int returned);

    public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Win, or Ctrl or Alt alone: a shortcut, not typing. Ctrl and Alt together are AltGr, which types characters.</summary>
    public static bool CommandHeld()
    {
        if (IsDown(0x5B) || IsDown(0x5C))
            return true;
        return IsDown(0x11) != IsDown(0x12);
    }

    public static uint ProcessOf(nint window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        return pid;
    }

    /// <summary>
    /// The character the key types now in the layout of the window in front, or '\0' for a dead key, a key that types
    /// nothing or a control. The no-state-change flag (bit 2) keeps dead keys and accents working in the app.
    /// </summary>
    public static char CharOf(uint vk, uint scan)
    {
        nint front = GetForegroundWindow();
        uint thread = front == 0 ? 0 : GetWindowThreadProcessId(front, out _);
        nint layout = GetKeyboardLayout(thread);
        var state = new byte[256];
        if (IsDown(0x10)) state[0x10] = 0x80;
        if (IsDown(0x11)) state[0x11] = 0x80;
        if (IsDown(0x12)) state[0x12] = 0x80;
        if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 1;
        if (scan == 0)
            scan = MapVirtualKeyEx(vk, 0, layout);
        var text = new char[4];
        int count = ToUnicodeEx(vk, scan, state, text, text.Length, 0x4, layout);
        return count == 1 && !char.IsControl(text[0]) ? text[0] : '\0';
    }

    public static uint Send(ReadOnlySpan<Stroke> strokes)
    {
        var inputs = new Input[strokes.Length];
        for (int i = 0; i < strokes.Length; i++)
        {
            var s = strokes[i];
            inputs[i] = new Input
            {
                Type = 1,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        Vk = s.Vk,
                        Scan = s.IsUnicode ? s.Unit : (ushort)0,
                        Flags = (s.IsUnicode ? KeyUnicode : 0) | (s.Up ? KeyUp : 0),
                        ExtraInfo = ChippiInput.Marker,
                    },
                },
            };
        }
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    /// <summary>The window runs above Chippi, or Windows won't say: it gets no keys. A process Chippi can't open counts as elevated.</summary>
    public static bool IsElevated(uint processId)
    {
        nint process = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0)
            return true;
        try
        {
            if (!OpenProcessToken(process, 0x0008, out nint token)) // TOKEN_QUERY
                return true;
            try { return !GetTokenInformation(token, 20, out int elevated, sizeof(int), out _) || elevated != 0; } // TokenElevation
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    /// <summary>Brings <paramref name="window"/> to the front; true once it is there.</summary>
    public static bool Activate(nint window)
    {
        SetForegroundWindow(window);
        return GetForegroundWindow() == window;
    }
}
