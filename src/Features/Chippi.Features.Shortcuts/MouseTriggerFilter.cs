// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;

namespace Chippi.Features.Shortcuts;

/// <summary>
/// Middle, Back, Forward and the side wheel's two directions, as shortcuts' triggers. A bound button's press is kept from
/// the app (the hub keeps its release too), so it no longer does its old job; the press fires once, on its down. A tilt of
/// the wheel fires once per notch. A button the Radial menu holds, and any button in an app on the list, passes untouched.
/// </summary>
internal sealed class MouseTriggerFilter : IInputFilter
{
    private const int Notch = 120;
    private static readonly long TiltIdle = Stopwatch.Frequency * 3 / 10; // a pause this long starts a new notch count

    private sealed record Bound(bool[] Triggers, HashSet<string> IgnoredNames);

    private Bound _bound = new(new bool[5], []);
    private int _tilt;
    private long _lastTilt;

    public InputOrder Order => InputOrder.Triggers;

    public InputDevices Devices => InputDevices.Mouse;

    public bool Pauses => true;

    /// <summary>Called on the hub's thread with the trigger that fired: hand it off before doing anything slow.</summary>
    public Action<MouseTrigger> Fired { get; init; } = _ => { };

    /// <summary>Whether the Radial menu opens from this trigger now (Middle, Back, Forward only). A memory read.</summary>
    public Func<MouseTrigger, bool> HeldByRadial { get; init; } = _ => false;

    /// <summary>The exe path of the window in front, or null. A memory read.</summary>
    public Func<string?> ForegroundApp { get; init; } = () => null;

    /// <summary>The triggers that have a binding, and the apps whose windows keep their buttons. Safe from any thread.</summary>
    public void Set(IEnumerable<MouseTrigger> triggers, IEnumerable<string> ignoredApps)
    {
        var on = new bool[5];
        foreach (var t in triggers)
            on[(int)t] = true;
        Volatile.Write(ref _bound, new Bound(on, [.. ignoredApps.Select(Path.GetFileName).OfType<string>().Select(n => n.ToLowerInvariant())]));
    }

    public bool Mouse(in MouseInput mouse)
    {
        if (mouse.Kind == MouseKind.Down && Trigger(mouse.Button) is { } trigger)
            return Take(trigger);
        if (mouse.Kind == MouseKind.HorizontalWheel)
            return Tilt(mouse);
        return false;
    }

    public bool Key(in KeyInput key) => false;

    public void InputReset(InputDevices devices) => _tilt = 0;

    private static MouseTrigger? Trigger(MouseButton button) => button switch
    {
        MouseButton.Middle => MouseTrigger.Middle,
        MouseButton.Back => MouseTrigger.Back,
        MouseButton.Forward => MouseTrigger.Forward,
        _ => null,
    };

    private bool Take(MouseTrigger trigger)
    {
        var bound = Volatile.Read(ref _bound);
        if (!bound.Triggers[(int)trigger] || HeldByRadial(trigger) || Ignored(bound))
            return false;
        Fired(trigger);
        return true;
    }

    private bool Tilt(in MouseInput mouse)
    {
        var bound = Volatile.Read(ref _bound);
        var trigger = mouse.Delta > 0 ? MouseTrigger.WheelRight : MouseTrigger.WheelLeft;
        if (mouse.Delta == 0 || !bound.Triggers[(int)trigger] || Ignored(bound))
        {
            _tilt = 0;
            return false;
        }
        long gap = mouse.Timestamp - _lastTilt;
        _lastTilt = mouse.Timestamp;
        // A notch is 120; a finer wheel sends less each time and counts up to one. A turn the other way starts again.
        if (gap < 0 || gap > TiltIdle || Math.Sign(_tilt) != Math.Sign(mouse.Delta))
            _tilt = 0;
        _tilt += mouse.Delta;
        while (Math.Abs(_tilt) >= Notch)
        {
            _tilt -= Math.Sign(_tilt) * Notch;
            Fired(trigger);
        }
        return true;
    }

    private bool Ignored(Bound bound)
    {
        if (bound.IgnoredNames.Count == 0)
            return false;
        string? app = ForegroundApp();
        return app is not null && Path.GetFileName(app) is { } name && bound.IgnoredNames.Contains(name.ToLowerInvariant());
    }
}
