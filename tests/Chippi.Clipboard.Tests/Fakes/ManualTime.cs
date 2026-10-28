// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Clipboard.Tests;

/// <summary>A clock that moves only when a test calls <see cref="Advance"/>; its timers fire then, in order.</summary>
public sealed class ManualTime : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<Timer> _timers = [];
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() { lock (_gate) return _ticks; }

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        long end;
        lock (_gate) end = _ticks + by.Ticks;
        while (true)
        {
            Timer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.Due <= end).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    _ticks = end;
                    return;
                }
                _ticks = Math.Max(_ticks, next.Due);
                next.Due = next.Period > 0 ? next.Due + next.Period : long.MaxValue;
                if (next.Period <= 0)
                    _timers.Remove(next);
            }
            next.Fire();
        }
    }

    private sealed class Timer(ManualTime owner, TimerCallback callback, object? state) : ITimer
    {
        public long Due { get; set; }

        public long Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                owner._timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan)
                    return true;
                Due = owner._ticks + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                owner._timers.Add(this);
            }
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose() { lock (owner._gate) owner._timers.Remove(this); }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
