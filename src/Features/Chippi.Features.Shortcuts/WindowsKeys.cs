// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;

namespace Chippi.Features.Shortcuts;

/// <summary>
/// Combinations Windows keeps for itself. A binding on one saves and shows "In use by Windows", and doesn't act. From the
/// shortcuts Windows 11 documents; Windows has no call that lists them, so a new one it adds isn't known here.
/// </summary>
internal static class WindowsKeys
{
    public const string Text = "In use by Windows";

    private const uint Backspace = 0x08, Tab = 0x09, Enter = 0x0D, Pause = 0x13, Esc = 0x1B, Space = 0x20, PageUp = 0x21, PageDown = 0x22,
        End = 0x23, Home = 0x24, Left = 0x25, Down = 0x28, PrintScreen = 0x2C, Delete = 0x2E, F4 = 0x73;

    // Win + these: the letters and digits Windows 11 uses, its navigation, and OEM keys (; = , - . and a few more).
    private static readonly HashSet<uint> WinKeys =
    [
        .. "ABCDEFGHIKLMNOPQRSTUVWXZ".Select(c => (uint)c),
        .. Enumerable.Range('0', 10).Select(c => (uint)c),
        Tab, Enter, Pause, Esc, Space, Home, End, PageUp, PageDown, PrintScreen,
        0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, // ; = , - . /
    ];

    public static bool IsReserved(KeyShortcut keys)
    {
        var m = keys.Modifiers;
        uint k = keys.Key;
        bool win = m.HasFlag(ShortcutModifiers.Windows);
        if (win && m == ShortcutModifiers.Windows)
            return WinKeys.Contains(k) || k is >= Left and <= Down; // the arrows snap windows
        if (win && m == (ShortcutModifiers.Windows | ShortcutModifiers.Shift))
            return k is 'S' or 'M' or 'C' or 'V' or Left or Down or 0x26 or 0x27 or Tab; // snip, undo minimize, Narrator, ...
        if (win && m == (ShortcutModifiers.Windows | ShortcutModifiers.Control))
            return k is 'D' or 'F' or 'C' or 'Q' or 'N' or 'O' or 'B' or F4 or Left or 0x27; // desktops, close desktop, Quick Assist
        if (win)
            return k is Tab or Delete or Space && m.HasFlag(ShortcutModifiers.Control); // Ctrl+Win+Space and the like
        if (m == ShortcutModifiers.Alt)
            return k is Tab or F4 or Esc or Space;
        if (m == ShortcutModifiers.Control)
            return k is Esc;
        if (m == (ShortcutModifiers.Control | ShortcutModifiers.Alt))
            return k is Delete or Tab;
        if (m == (ShortcutModifiers.Control | ShortcutModifiers.Shift))
            return k is Esc; // Task Manager
        if (m == (ShortcutModifiers.Alt | ShortcutModifiers.Shift))
            return k is Tab;
        return false;
    }
}
