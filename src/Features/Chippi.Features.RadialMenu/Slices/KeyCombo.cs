// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;

namespace Chippi.Features.RadialMenu.Slices;

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// <summary>
/// A Key shortcut slice's keys: modifiers and one key, written "Ctrl+Shift+T" or "Win+Shift+S" in settings.json and on the
/// key caps Settings shows. Unlike a Chippi shortcut, any key and the Windows key are allowed: they're pressed, not taken.
/// </summary>
public readonly record struct KeyCombo(KeyModifiers Modifiers, uint Key)
{
    private static readonly (KeyModifiers Flag, string Name, uint Vk)[] Mods =
        [(KeyModifiers.Win, "Win", 0x5B), (KeyModifiers.Ctrl, "Ctrl", 0x11), (KeyModifiers.Alt, "Alt", 0x12), (KeyModifiers.Shift, "Shift", 0x10)];

    private static readonly Dictionary<uint, string> Names = BuildNames();
    private static readonly Dictionary<string, uint> ByName = Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The modifiers' virtual keys, in the order they go down.</summary>
    public IEnumerable<uint> ModifierKeys
    {
        get
        {
            var held = Modifiers;
            return Mods.Where(m => held.HasFlag(m.Flag)).Select(m => m.Vk);
        }
    }

    /// <summary>The caps, in order: "Ctrl", "Shift", "T".</summary>
    public IReadOnlyList<string> Caps
    {
        get
        {
            var held = Modifiers;
            return [.. Mods.Where(m => held.HasFlag(m.Flag)).Select(m => m.Name), NameOf(Key)];
        }
    }

    public override string ToString() => string.Join("+", Caps);

    /// <summary>A key's cap ("T", "F5", "Page Up", ","), or its number for one without a name.</summary>
    public static string NameOf(uint vk) => Names.TryGetValue(vk, out var name) ? name : "Key " + vk.ToString(CultureInfo.InvariantCulture);

    /// <summary>A modifier key (either Ctrl, Alt, Shift or Windows key) as the flag it sets, or None for any other key.</summary>
    public static KeyModifiers ModifierOf(uint vk) => vk switch
    {
        0x11 or 0xA2 or 0xA3 => KeyModifiers.Ctrl,
        0x12 or 0xA4 or 0xA5 => KeyModifiers.Alt,
        0x10 or 0xA0 or 0xA1 => KeyModifiers.Shift,
        0x5B or 0x5C => KeyModifiers.Win,
        _ => KeyModifiers.None,
    };

    /// <summary>"Ctrl+Shift+T" back into keys; null for text that isn't one.</summary>
    public static KeyCombo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var mod = Mods.FirstOrDefault(m => string.Equals(m.Name, part, StringComparison.OrdinalIgnoreCase));
            if (mod.Name is null)
                return null;
            modifiers |= mod.Flag;
        }
        string last = parts[^1];
        if (ByName.TryGetValue(last, out uint vk))
            return new KeyCombo(modifiers, vk);
        if (last.StartsWith("Key ", StringComparison.Ordinal) && uint.TryParse(last[4..], NumberStyles.None, CultureInfo.InvariantCulture, out vk) && vk is > 0 and < 0xFF)
            return new KeyCombo(modifiers, vk);
        return null;
    }

    /// <summary>Keys Windows wants flagged as extended when they're sent (the arrows and the block above them).</summary>
    public static bool IsExtended(uint vk) => vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x6F or 0x90 or 0xA3 or 0xA5;

    private static Dictionary<uint, string> BuildNames()
    {
        var names = new Dictionary<uint, string>
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x1B] = "Esc", [0x20] = "Space",
            [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2C] = "Print Screen", [0x2D] = "Insert", [0x2E] = "Delete", [0x5D] = "Menu",
            [0x6A] = "Num *", [0x6B] = "Num Plus", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
            [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
        };
        for (uint vk = 0x30; vk <= 0x39; vk++)
            names[vk] = ((char)vk).ToString();
        for (uint vk = 0x41; vk <= 0x5A; vk++)
            names[vk] = ((char)vk).ToString();
        for (uint vk = 0x60; vk <= 0x69; vk++)
            names[vk] = "Num " + (vk - 0x60).ToString(CultureInfo.InvariantCulture);
        for (uint vk = 0x70; vk <= 0x87; vk++)
            names[vk] = "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);
        return names;
    }
}
