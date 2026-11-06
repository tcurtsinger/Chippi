// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Core.Input;

namespace Chippi.UI.Actions;

/// <summary>
/// Giving one of Chippi's windows the keyboard (AGENTS.md, Foreground). A shortcut (<c>WM_HOTKEY</c>), an island click or
/// Search leaves Chippi the right to the foreground; the input hub's doors (the Radial menu, mouse buttons, Caps Lock
/// combos) don't, as the hub swallowed the press. Then one tap of an unassigned key, marked as Chippi's so the hub passes
/// it, earns the right, and it tries once more. Never a loop, and never Alt, which can open an app's menu bar. Shared by
/// the App switcher and the capture overlay.
/// </summary>
public static partial class ForegroundRight
{
    /// <summary>VK 0x07 is unassigned: no app acts on it.</summary>
    private const ushort Unassigned = 0x07;

    /// <summary>Activates <paramref name="hwnd"/>; true when it holds the foreground.</summary>
    public static bool Take(nint hwnd)
    {
        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd)
            return true;
        Tap(Unassigned);
        return SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd;
    }

    private static void Tap(ushort vk)
    {
        Input[] inputs =
        [
            new() { Type = 1, Keyboard = new KeyboardInput { Vk = vk, ExtraInfo = ChippiInput.Marker } },
            new() { Type = 1, Keyboard = new KeyboardInput { Vk = vk, Flags = 0x0002, ExtraInfo = ChippiInput.Marker } },
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public nint ExtraInfo; }

    // INPUT with the keyboard member; the union's size is the mouse member's, hence the padding.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }

    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] private static partial uint SendInput(uint count, [In] Input[] inputs, int size);
}
