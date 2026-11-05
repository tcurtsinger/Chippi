// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.SmoothScrolling;

/// <summary>
/// The glide: wheel amounts go in, small whole deltas come out frame by frame. Pure and clocked by the caller (stopwatch
/// ticks), so tests hold the clock. What goes in comes out: the sum of the deltas sent equals the sum queued, rounded once,
/// unless the glide is cancelled. A reversal drops the tail the other way; a new target drops everything.
/// </summary>
internal sealed class ScrollEngine(long ticksPerSecond)
{
    private readonly double[] _remaining = new double[2];
    private readonly double[] _carry = new double[2];
    private nint _target;
    private long _last;
    private double _tauMs = 1;

    /// <summary>The window the queued motion belongs to, or 0 when nothing is queued.</summary>
    public nint Target => Idle ? 0 : _target;

    public bool Idle => _remaining[0] == 0 && _remaining[1] == 0;

    /// <summary>
    /// Queues <paramref name="amount"/> wheel units on an axis (0 vertical, 1 horizontal). <paramref name="glideMs"/> is how
    /// long the glide takes to settle; 0 sends it all on the next step.
    /// </summary>
    public void Add(int axis, double amount, nint target, int glideMs, long now)
    {
        if (!Idle && target != _target)
            Cancel();
        if (Idle)
            _last = now;
        _target = target;
        _tauMs = Math.Max(glideMs, 0) / 4.0; // about 98% of the way after glideMs
        if (_remaining[axis] * amount < 0)
        {
            _remaining[axis] = 0; // reversal: no tail the old way
            _carry[axis] = 0;
        }
        _remaining[axis] += amount;
    }

    public void Cancel()
    {
        Array.Clear(_remaining);
        Array.Clear(_carry);
        _target = 0;
    }

    /// <summary>The whole deltas to send now, per axis.</summary>
    public (int Vertical, int Horizontal) Step(long now)
    {
        double dtMs = Math.Max(now - _last, 0) * 1000.0 / ticksPerSecond;
        _last = now;
        return (StepAxis(0, dtMs), StepAxis(1, dtMs));
    }

    private int StepAxis(int axis, double dtMs)
    {
        double left = _remaining[axis];
        if (left == 0)
            return 0;
        double share = _tauMs <= 0 ? 1 : 1 - Math.Exp(-dtMs / _tauMs);
        double out_ = left * share;
        double owed;
        if (Math.Abs(left - out_) < 1)
        {
            _remaining[axis] = 0;
            owed = Math.Round(_carry[axis] + left, MidpointRounding.AwayFromZero);
            _carry[axis] = 0;
            return (int)owed;
        }
        _remaining[axis] = left - out_;
        owed = _carry[axis] + out_;
        double whole = Math.Truncate(owed);
        _carry[axis] = owed - whole;
        return (int)whole;
    }
}
