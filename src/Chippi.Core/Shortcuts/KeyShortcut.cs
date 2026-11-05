// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Shortcuts;

[Flags]
public enum ShortcutModifiers : uint { Alt = 1, Control = 2, Shift = 4, Windows = 8 }

/// <summary>A global key shortcut: modifiers and a virtual key, as <c>RegisterHotKey</c> takes them.</summary>
public sealed record KeyShortcut(ShortcutModifiers Modifiers = ShortcutModifiers.Control | ShortcutModifiers.Shift, uint Key = 0x4D)
{
    // Require Ctrl or Alt so Shift-only shortcuts cannot consume normal typing. Windows combinations and F12 stay reserved.
    // Space is allowed for the Command bar's Ctrl+Alt+Space (Travis, 2026-11-01).
    public bool IsValid => (Modifiers & ShortcutModifiers.Windows) == 0
        && (Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt)) != 0 && ((uint)Modifiers & ~7u) == 0
        && (Key is 0x20 or >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A);

    /// <summary>
    /// Valid as a Shortcuts binding (docs/phase-6/shortcuts.md): the Windows key may be held, and the keys include the
    /// navigation and punctuation keys. Still needs Ctrl, Alt or Windows so a bare Shift never eats typing.
    /// </summary>
    public bool IsBindable => ((uint)Modifiers & ~15u) == 0
        && (Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Windows)) != 0
        && (Key is 0x08 or 0x09 or 0x0D or 0x20 or (>= 0x21 and <= 0x28) or 0x2D or 0x2E or (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A)
            or (>= 0x70 and <= 0x87) or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDE));

    public override string ToString() => string.Join("+", new[]
    {
        Modifiers.HasFlag(ShortcutModifiers.Windows) ? "Win" : null,
        Modifiers.HasFlag(ShortcutModifiers.Control) ? "Ctrl" : null,
        Modifiers.HasFlag(ShortcutModifiers.Alt) ? "Alt" : null,
        Modifiers.HasFlag(ShortcutModifiers.Shift) ? "Shift" : null,
        KeyName(Key),
    }.Where(x => x is not null));

    /// <summary>The key as Windows names it: "M", "1" (not "D1"), "Space", "F5".</summary>
    private static string KeyName(uint key) => key switch
    {
        >= 0x30 and <= 0x5A => ((char)key).ToString(),
        0x20 => "Space",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x21 => "Page up",
        0x22 => "Page down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F),
        _ => "0x" + key.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
    };
}
