// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.Shortcuts;

/// <summary>
/// Key shortcuts with the Windows key that <c>RegisterHotKey</c> refused, taken in the hub instead (architecture §4). The
/// chord's last key and its repeats are kept; when Win or Alt was part of it, one masking key goes out before their release,
/// so neither opens Start or a menu bar. Modifiers are tracked from the keys the filter hears.
/// </summary>
internal sealed class ChordFilter : IInputFilter
{
    private volatile Dictionary<(ShortcutModifiers, uint), string> _bound = [];
    private readonly HashSet<uint> _downKeys = [];
    private ShortcutModifiers _down;
    private bool _masking;

    public InputOrder Order => InputOrder.Triggers;

    public InputDevices Devices => InputDevices.Keyboard;

    public bool Pauses => true;

    /// <summary>Called on the hub's thread with the binding's id: hand it off before doing anything slow.</summary>
    public Action<string> Fired { get; init; } = _ => { };

    /// <summary>Sends the masking key (<see cref="IInputRegistration.Mask"/>); set once the filter is in the hub.</summary>
    public Action Mask { get; set; } = () => { };

    public void Set(IEnumerable<(KeyShortcut Keys, string Id)> chords) =>
        _bound = chords.GroupBy(c => (c.Keys.Modifiers, c.Keys.Key)).ToDictionary(g => g.Key, g => g.First().Id);

    public bool Mouse(in MouseInput mouse) => false;

    public void InputReset(InputDevices devices)
    {
        _downKeys.Clear();
        _down = 0;
        _masking = false;
    }

    public bool Key(in KeyInput key)
    {
        var modifier = ModifierOf(key.Vk);
        if (modifier != 0)
        {
            // Left and right are tracked apart: letting go of one Ctrl while the other is held keeps Ctrl down.
            if (key.Down)
                _downKeys.Add(key.Vk);
            else
                _downKeys.Remove(key.Vk);
            _down = 0;
            foreach (uint vk in _downKeys)
                _down |= ModifierOf(vk);
            if (!key.Down && _masking && (modifier & (ShortcutModifiers.Windows | ShortcutModifiers.Alt)) != 0 && (_down & modifier) == 0)
            {
                _masking = false;
                Mask();
            }
            return false;
        }
        if (!key.Down || !_bound.TryGetValue((_down, key.Vk), out string? id))
            return false;
        if (key.WasDown)
            return true; // a repeat of a kept chord: still the chord's
        if ((_down & (ShortcutModifiers.Windows | ShortcutModifiers.Alt)) != 0)
            _masking = true;
        Fired(id);
        return true;
    }

    private static ShortcutModifiers ModifierOf(uint vk) => vk switch
    {
        0x5B or 0x5C => ShortcutModifiers.Windows,
        0xA2 or 0xA3 or 0x11 => ShortcutModifiers.Control,
        0xA4 or 0xA5 or 0x12 => ShortcutModifiers.Alt,
        0xA0 or 0xA1 or 0x10 => ShortcutModifiers.Shift,
        _ => 0,
    };
}
