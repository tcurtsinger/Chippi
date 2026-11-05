// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;

namespace Chippi.Features.Shortcuts;

/// <summary>
/// Settings › Shortcuts › "Press a mouse button": takes the next Middle, Back or Forward press, or side wheel tilt, so it
/// reaches no app, and reports it once. Dispose it to give the buttons back.
/// </summary>
public sealed class MouseButtonRecorder : IInputFilter, IDisposable
{
    private readonly IInputRegistration _registration;
    private readonly Action<MouseTrigger> _recorded;
    private int _done;

    /// <param name="recorded">Called once, on the hub's thread.</param>
    public MouseButtonRecorder(IInputHub hub, Action<MouseTrigger> recorded)
    {
        _recorded = recorded;
        _registration = hub.Add(this);
    }

    /// <summary>Before every filter, so a button another shortcut or the Radial menu holds is heard too, and refused with its reason.</summary>
    public InputOrder Order => (InputOrder)1;

    public InputDevices Devices => InputDevices.Mouse;

    public bool Pauses => false;

    public bool Mouse(in MouseInput mouse)
    {
        MouseTrigger? trigger = mouse.Kind switch
        {
            MouseKind.Down when mouse.Button == MouseButton.Middle => MouseTrigger.Middle,
            MouseKind.Down when mouse.Button == MouseButton.Back => MouseTrigger.Back,
            MouseKind.Down when mouse.Button == MouseButton.Forward => MouseTrigger.Forward,
            MouseKind.HorizontalWheel when mouse.Delta > 0 => MouseTrigger.WheelRight,
            MouseKind.HorizontalWheel when mouse.Delta < 0 => MouseTrigger.WheelLeft,
            _ => null,
        };
        if (trigger is not { } found)
            return false;
        if (Interlocked.Exchange(ref _done, 1) == 0)
            _recorded(found);
        return true;
    }

    public bool Key(in KeyInput key) => false;

    public void InputReset(InputDevices devices) { }

    public void Dispose() => _registration.Dispose();
}
