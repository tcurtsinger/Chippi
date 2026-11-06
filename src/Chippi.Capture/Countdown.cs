// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>
/// A capture's delay (capture-layer.md, The freeze, step 4): "Capturing in 3", then 2, then 1, then the freeze. A
/// click on the bar line or the same starter again cancels it; Esc doesn't. Its timer runs on the
/// <see cref="TimeProvider"/>'s thread: callers marshal to their own.
/// </summary>
public sealed class Countdown : IDisposable
{
    /// <summary>The delays a starter can pick, in seconds.</summary>
    public static IReadOnlyList<int> Delays { get; } = [3, 5, 10];

    private readonly object _gate = new();
    private readonly ITimer _timer;
    private int _left;
    private bool _ended;

    /// <param name="seconds">How long; above 0.</param>
    /// <param name="tick">Each second left, from <paramref name="seconds"/> down to 1, the first at once.</param>
    /// <param name="done">At 0, once, unless cancelled first.</param>
    public Countdown(int seconds, TimeProvider time, Action<int> tick, Action done)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 1);
        _left = seconds;
        Tick = tick;
        Done = done;
        _timer = time.CreateTimer(_ => Step(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        tick(seconds);
        _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private Action<int> Tick { get; }
    private Action Done { get; }

    public int SecondsLeft { get { lock (_gate) return _left; } }

    public bool IsRunning { get { lock (_gate) return !_ended; } }

    private void Step()
    {
        int left;
        lock (_gate)
        {
            if (_ended)
                return;
            left = --_left;
            if (left <= 0)
                _ended = true;
        }
        if (left > 0)
            Tick(left);
        else
        {
            _timer.Dispose();
            Done();
        }
    }

    /// <summary>Stops it; <c>done</c> never runs. False when it had already ended.</summary>
    public bool Cancel()
    {
        lock (_gate)
        {
            if (_ended)
                return false;
            _ended = true;
        }
        _timer.Dispose();
        return true;
    }

    public void Dispose() => Cancel();
}
