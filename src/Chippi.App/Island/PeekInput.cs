// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;
using Chippi.Core.Shortcuts;

namespace Chippi.App.Island;

/// <summary>Observes release through the shared hub; Windows' shortcut service owns the actual chord.</summary>
internal sealed class PeekInput(Action<long> released) : IInputFilter
{
    private readonly Lock _gate = new();
    private KeyShortcut? _shortcut;
    private bool _held;
    private uint _modifiers;
    private long _press;
    public InputOrder Order => InputOrder.Triggers;
    public InputDevices Devices => InputDevices.Keyboard;
    public bool Pauses => false;
    public (long Press, bool Held) Current { get { lock (_gate) return (_press, _held); } }

    public void Configure(KeyShortcut? shortcut)
    {
        lock (_gate) { _shortcut = shortcut; _held = false; _modifiers = 0; _press++; }
    }

    public bool Key(in KeyInput key)
    {
        long? release = null;
        lock (_gate)
        {
            if (_shortcut is not { } shortcut) return false;
            uint bit = ModifierBit(key.Vk);
            _modifiers = key.Down ? _modifiers | bit : _modifiers & ~bit;
            if (key.Vk == shortcut.Key && key.Down && !key.WasDown) { _held = true; _press++; }
            else if (!key.Down && _held && (key.Vk == shortcut.Key || RequiredModifierReleased(shortcut.Modifiers, bit)))
            { _held = false; release = _press; }
        }
        if (release is { } press) released(press);
        return false;
    }

    private bool RequiredModifierReleased(ShortcutModifiers required, uint bit)
    {
        var (modifier, mask) = bit switch
        {
            1 or 2 => (ShortcutModifiers.Shift, 3u), 4 or 8 => (ShortcutModifiers.Control, 12u),
            16 or 32 => (ShortcutModifiers.Alt, 48u), 64 or 128 => (ShortcutModifiers.Windows, 192u), _ => ((ShortcutModifiers)0, 0u),
        };
        return mask != 0 && (required & modifier) != 0 && (_modifiers & mask) == 0;
    }

    private static uint ModifierBit(uint key) => key switch
    { 0x10 or 0xA0 => 1, 0xA1 => 2, 0x11 or 0xA2 => 4, 0xA3 => 8, 0x12 or 0xA4 => 16, 0xA5 => 32, 0x5B => 64, 0x5C => 128, _ => 0 };

    public bool Mouse(in MouseInput mouse) => false;
    public void InputReset(InputDevices devices)
    {
        if (!devices.HasFlag(InputDevices.Keyboard)) return;
        long? release = null;
        lock (_gate) { if (_held) release = _press; _held = false; _modifiers = 0; }
        if (release is { } press) released(press);
    }
}
