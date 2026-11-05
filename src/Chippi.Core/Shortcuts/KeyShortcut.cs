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

    public override string ToString() => string.Join("+", new[]
    {
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
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F),
        _ => "0x" + key.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
    };
}
