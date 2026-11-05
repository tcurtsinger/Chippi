// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;

namespace Chippi.Features.Debounce;

/// <summary>
/// Drops the press a worn switch sends right after a release of the same button (left, right, middle). The hub keeps
/// a dropped press's release too, so nothing is left held; the release of an accepted press always passes.
/// </summary>
internal sealed class ClickDebounceFilter : IInputFilter
{
    private static readonly long Reset = 5 * Stopwatch.Frequency;

    private readonly long[] _lastUp = new long[6];
    private readonly bool[] _hasUp = new bool[6];
    private readonly bool[] _dropping = new bool[6];
    private long _window = 25 * Stopwatch.Frequency / 1000;
    private int _ignored;

    public InputOrder Order => InputOrder.Debounce;

    public InputDevices Devices => InputDevices.Mouse;

    public bool Pauses => true;

    /// <summary>Input other programs send is left alone, except in the Sandbox probe, whose <c>SendInput</c> counts as a hand's.</summary>
    public bool IncludeInjected { get; init; }

    public int WindowMs
    {
        set => Volatile.Write(ref _window, value * Stopwatch.Frequency / 1000);
    }

    public int Ignored => Volatile.Read(ref _ignored);

    public bool Mouse(in MouseInput mouse)
    {
        if ((mouse.Injected && !IncludeInjected) || mouse.Kind is not (MouseKind.Down or MouseKind.Up)
            || mouse.Button is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle))
            return false;
        int button = (int)mouse.Button;
        if (mouse.Kind == MouseKind.Up)
        {
            if (_dropping[button])
            {
                _dropping[button] = false;
                return true;
            }
            _lastUp[button] = mouse.Timestamp;
            _hasUp[button] = true;
            return false;
        }
        if (!_hasUp[button])
            return false;
        long gap = mouse.Timestamp - _lastUp[button];
        if (gap < 0 || gap > Reset)
        {
            _hasUp[button] = false; // silence, or time going backwards: start over
            return false;
        }
        if (gap >= Volatile.Read(ref _window))
            return false;
        _dropping[button] = true;
        Interlocked.Increment(ref _ignored);
        return true;
    }

    public bool Key(in KeyInput key) => false;
}
