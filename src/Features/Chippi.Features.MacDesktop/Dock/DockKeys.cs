// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>
/// Win+1 to Win+9 open the dock's apps in its order, as they open the taskbar's while it shows: the hidden taskbar would
/// otherwise still answer them in its own order. Only the plain chord is kept; with Shift, Ctrl or Alt it goes on to
/// Windows. Answers from its own key state, then hands the app's number to <paramref name="open"/> to run on the UI thread.
/// </summary>
internal sealed class DockKeys(Action<int> open) : IInputFilter
{
    private readonly object _gate = new();
    private bool _win, _shift, _control, _alt;

    /// <summary>Set once added: masks the Windows key's up, so a kept Win+digit opens no Start menu.</summary>
    public IInputRegistration? Registration { get; set; }

    public InputOrder Order => InputOrder.Triggers;
    public InputDevices Devices => InputDevices.Keyboard;
    public bool Pauses => true;

    public bool Mouse(in MouseInput mouse) => false;

    public void InputReset(InputDevices devices)
    {
        if ((devices & InputDevices.Keyboard) != 0)
            lock (_gate) _win = _shift = _control = _alt = false;
    }

    public bool Key(in KeyInput key)
    {
        lock (_gate)
        {
            switch (key.Vk)
            {
                case 0x5B or 0x5C: _win = key.Down; return false;
                case 0x10 or 0xA0 or 0xA1: _shift = key.Down; return false;
                case 0x11 or 0xA2 or 0xA3: _control = key.Down; return false;
                case 0x12 or 0xA4 or 0xA5: _alt = key.Down; return false;
            }
            if (!Matches(key.Vk, _win, _shift, _control, _alt) || !key.Down)
                return false;
        }
        if (!key.WasDown)
        {
            try { Registration?.Mask(); }
            catch (Exception ex) { Chippi.Core.Diagnostics.Log.Warn("Mac-style desktop: masking Win failed", ex); }
            open((int)(key.Vk - 0x31));
        }
        return true;
    }

    /// <summary>Whether the key is a digit 1 to 9 (top row) with Windows alone held.</summary>
    public static bool Matches(uint vk, bool win, bool shift, bool control, bool alt) =>
        vk is >= 0x31 and <= 0x39 && win && !shift && !control && !alt;
}
