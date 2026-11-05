// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;

namespace Chippi.Features.CloseProtection;

[Flags]
public enum CloseModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

// Process creation identity protects against a recycled HWND and process id.
public sealed record CloseTarget(nint Window, uint ProcessId, long ProcessStarted, string Path, string Name, bool Eligible);
public enum CloseChord { AltF4, ControlW }
public sealed record PendingClose(CloseTarget Target, CloseChord Chord, long Expires);

/// <summary>
/// Reads only cached target data and the current modifier bits. Feedback callbacks must only queue work,
/// never invoke a dispatcher synchronously, query processes, or perform I/O on the hook thread.
/// The module supplies focus/reset notifications even if no key arrives between focus changes.
/// </summary>
public sealed class CloseProtectionFilter(Func<CloseTarget?> target, Func<CloseModifiers> modifiers) : IInputFilter
{
    public static readonly long IntervalTicks = checked(Stopwatch.Frequency * 6);
    private readonly object _gate = new();
    private readonly bool[] _swallowed = new bool[256];
    private readonly PendingClose?[] _pending = new PendingClose?[2];
    private readonly bool[] _released = new bool[2];
    private HashSet<string> _controlW = new(StringComparer.OrdinalIgnoreCase);
    private bool _enabled, _paused;
    private CloseTarget? _lastTarget;
    private long _lastTime;

    public InputOrder Order => InputOrder.CloseProtection;
    public InputDevices Devices => Volatile.Read(ref _enabled) ? InputDevices.Keyboard : InputDevices.None;
    public bool Pauses => true;
    public event Action<PendingClose?>? Feedback;

    public void Configure(bool enabled, IEnumerable<string> controlW)
    {
        var paths = controlW.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            _controlW = paths;
            _enabled = enabled;
            Clear();
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_gate) { _paused = paused; Clear(); }
    }

    // Focus loss, target destruction, session lock, disable and removal all clear pending presses.
    // The hub still owns a swallowed down's repeats and up; never release those into the app.
    public void Reset(bool forgetKeys = false)
    {
        lock (_gate) { Clear(); _lastTarget = null; if (forgetKeys) Array.Clear(_swallowed); }
    }

    public void Advance(long now)
    {
        lock (_gate) Expire(now);
    }

    public bool Mouse(in MouseInput mouse) => false;

    public bool Key(in KeyInput key)
    {
        if (key.Vk >= _swallowed.Length) return false;
        lock (_gate)
        {
            int vk = (int)key.Vk;
            Expire(key.Timestamp);
            var current = target();
            if (!SameWindow(current, _lastTarget)) { Clear(); _lastTarget = current; }
            if (!key.Down)
            {
                if (!_swallowed[vk]) return false;
                _swallowed[vk] = false;
                int releasedChord = vk == 0x73 ? 0 : 1;
                if (_pending[releasedChord] is not null) _released[releasedChord] = true;
                return true;
            }
            if (_swallowed[vk]) return true;
            if (!_enabled || _paused || key.WasDown || current is not { Eligible: true }) return false;
            var held = modifiers();
            CloseChord? chord = vk == 0x73 && held == CloseModifiers.Alt ? CloseChord.AltF4
                : vk == 0x57 && held == CloseModifiers.Control && _controlW.Contains(current.Path) ? CloseChord.ControlW : null;
            if (chord is null) return false;
            int index = (int)chord.Value;
            if (_pending[index] is { } prior && _released[index] && SameWindow(prior.Target, current))
            {
                _pending[index] = null;
                _released[index] = false;
                Notify();
                return false;
            }
            var pending = new PendingClose(current, chord.Value, checked(key.Timestamp + IntervalTicks));
            _pending[index] = pending;
            _released[index] = false;
            _swallowed[vk] = true;
            Feedback?.Invoke(pending);
            return true;
        }
    }

    private void Expire(long now)
    {
        now = Math.Max(now, _lastTime);
        _lastTime = now;
        bool expired = false;
        for (int i = 0; i < _pending.Length; i++)
            if (_pending[i] is { } pending && now >= pending.Expires)
            {
                _pending[i] = null;
                _released[i] = false;
                expired = true;
            }
        if (expired) Notify();
    }

    private void Notify() => Feedback?.Invoke(_pending.Where(p => p is not null).MaxBy(p => p!.Expires));

    private void Clear()
    {
        bool hadPending = _pending.Any(p => p is not null);
        Array.Clear(_pending);
        Array.Clear(_released);
        if (hadPending) Feedback?.Invoke(null);
    }

    private static bool SameWindow(CloseTarget? a, CloseTarget? b) =>
        a?.Window == b?.Window && a?.ProcessId == b?.ProcessId && a?.ProcessStarted == b?.ProcessStarted;
}
