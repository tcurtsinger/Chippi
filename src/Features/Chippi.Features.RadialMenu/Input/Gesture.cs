// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.RadialMenu.Pc;

namespace Chippi.Features.RadialMenu.Input;

public enum MouseButton { Left, Right, Middle, Back, Forward }

/// <summary>What the hook does after an event, besides passing it on or keeping it.</summary>
public enum GestureEffect
{
    None,

    /// <summary>Starts the hold's 250ms; <see cref="Gesture.Elapsed"/> with the press's number when it's up.</summary>
    StartHold,

    /// <summary>The press moved first: its kept down goes to the app now, so the move stays the app's drag.</summary>
    ReplayDown,

    /// <summary>That drag's release, kept and sent after its replayed down, so the app never sees the up first.</summary>
    ReplayUp,

    /// <summary>A quick click: its kept down and up go to the app as a click.</summary>
    ReplayClick,

    /// <summary>Held without moving: the wheel opens where the press was, the button still held.</summary>
    Open,

    /// <summary>The pointer moved while the wheel is open.</summary>
    Move,

    /// <summary>The button that opened the wheel was let go.</summary>
    Release,

    /// <summary>A left click while the wheel is open (on a slice, the hub, a crumb or off the wheel).</summary>
    Click,

    /// <summary>Esc, a right click or another button: the wheel closes without running anything.</summary>
    Dismiss,
}

/// <summary>What the hook decided for one event: keep it from the apps, or pass it on, and what follows.</summary>
public readonly record struct GestureStep(bool Keep, GestureEffect Effect = GestureEffect.None, MouseButton Button = MouseButton.Left, PixelPoint At = default);

/// <summary>
/// The press, hold and flick, decided per mouse event on the hook's thread (docs/phase-5.5/radial-menu.md, Opening it).
/// A trigger's press is kept from the app until it's clear what it is: held 250ms without moving, the wheel opens; moved
/// first, it goes to the app as the start of its drag; let go first, it goes to the app as a click. While the wheel is
/// open Chippi keeps the clicks and Esc. Not thread-safe: the hook calls it under its lock.
/// </summary>
public sealed class Gesture(Func<MouseButton, bool> watched, int slop)
{
    private enum Phase { Idle, Pressed, Held, Open }

    private readonly HashSet<MouseButton> _kept = [];
    private readonly HashSet<MouseButton> _dragging = []; // replayed downs whose ups follow them out
    private Phase _phase;
    private MouseButton _button;
    private PixelPoint _at;
    private bool _escKept;

    /// <summary>Counts presses, so a hold's timer that fires late for an earlier press does nothing.</summary>
    public int Press { get; private set; }

    /// <summary>The wheel is showing: its clicks and Esc are Chippi's.</summary>
    public bool IsOpen => _phase is Phase.Held or Phase.Open;

    /// <summary>Nothing pressed, held or open: the hook can stop if no button opens the wheel.</summary>
    public bool IsIdle => _phase == Phase.Idle && _kept.Count == 0 && _dragging.Count == 0 && !_escKept;

    public GestureStep Down(MouseButton button, PixelPoint at)
    {
        switch (_phase)
        {
            case Phase.Idle when button is not (MouseButton.Left or MouseButton.Right) && watched(button):
                _phase = Phase.Pressed;
                _button = button;
                _at = at;
                Press++;
                _kept.Add(button);
                return new(true, GestureEffect.StartHold, button, at);
            case Phase.Held or Phase.Open:
                _kept.Add(button);
                // A left click picks while the wheel waits; holding the trigger, only letting it go does.
                if (button == MouseButton.Left)
                    return new(true, _phase == Phase.Open ? GestureEffect.Click : GestureEffect.None, button, at);
                return new(true, GestureEffect.Dismiss, button, at);
            default:
                return new(false);
        }
    }

    public GestureStep Up(MouseButton button, PixelPoint at)
    {
        if (_phase == Phase.Pressed && button == _button)
        {
            _phase = Phase.Idle;
            _kept.Remove(button);
            return new(true, GestureEffect.ReplayClick, button, at);
        }
        if (_phase == Phase.Held && button == _button)
        {
            _phase = Phase.Open;
            _kept.Remove(button);
            return new(true, GestureEffect.Release, button, at);
        }
        // A quick drag can let go before its replayed down has gone out: the up goes out after it, not before.
        if (_dragging.Remove(button))
            return new(true, GestureEffect.ReplayUp, button, at);
        // The up of a down kept earlier stays Chippi's too, even once the wheel has gone.
        return _kept.Remove(button) ? new(true) : new(false);
    }

    public GestureStep Move(PixelPoint at)
    {
        if (_phase == Phase.Pressed)
        {
            if (Math.Abs(at.X - _at.X) <= slop && Math.Abs(at.Y - _at.Y) <= slop)
                return new(false);
            _phase = Phase.Idle;
            _kept.Remove(_button);
            _dragging.Add(_button);
            return new(false, GestureEffect.ReplayDown, _button, at);
        }
        return _phase is Phase.Held or Phase.Open ? new(false, GestureEffect.Move, _button, at) : new(false);
    }

    /// <summary>The hold's 250ms are up for press <paramref name="press"/>.</summary>
    public GestureStep Elapsed(int press)
    {
        if (_phase != Phase.Pressed || press != Press)
            return new(false);
        _phase = Phase.Held;
        return new(true, GestureEffect.Open, _button, _at);
    }

    /// <summary>A key going down or up: while the wheel shows, Esc closes it and is Chippi's, down and up.</summary>
    public GestureStep Key(uint vk, bool down)
    {
        if (vk != 0x1B)
            return new(false);
        if (down && IsOpen)
        {
            _escKept = true;
            return new(true, GestureEffect.Dismiss);
        }
        if (!down && _escKept)
        {
            _escKept = false;
            return new(true);
        }
        return new(_escKept);
    }

    /// <summary>The wheel opened from its keyboard shortcut: clicks and Esc are its own until it closes.</summary>
    public void OpenedByShortcut()
    {
        if (_phase == Phase.Pressed)
            _kept.Remove(_button);
        _phase = Phase.Open;
    }

    /// <summary>The wheel has closed (it ran a slice, or was dismissed).</summary>
    public void Closed()
    {
        if (_phase is Phase.Held or Phase.Open)
            _phase = Phase.Idle;
    }
}
