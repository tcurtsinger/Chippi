// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;

namespace Chippi.Media;

/// <summary>Video and WASAPI timestamps share QPC; paused intervals are removed from both tracks.</summary>
internal sealed class RecordingClock(Func<long>? timestamp = null)
{
    private readonly Func<long> _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    private readonly object _gate = new();
    private long _start, _pausedAt, _pausedTicks, _acceptedFrom;
    private bool _running, _paused;

    public bool IsPaused { get { lock (_gate) return _paused; } }
    public TimeSpan Elapsed => At(_timestamp()) ?? Current;
    private TimeSpan Current { get { lock (_gate) return _running ? Stopwatch.GetElapsedTime(_start, (_paused ? _pausedAt : _timestamp()) - _pausedTicks) : TimeSpan.Zero; } }
    public void Start() { lock (_gate) { _acceptedFrom = _start = _timestamp(); _pausedAt = _pausedTicks = 0; _running = true; _paused = false; } }
    public void Pause() { lock (_gate) { if (_running && !_paused) { _pausedAt = _timestamp(); _paused = true; } } }
    public void Resume() { lock (_gate) { if (_paused) { _acceptedFrom = _timestamp(); _pausedTicks += _acceptedFrom - _pausedAt; _paused = false; } } }
    public TimeSpan? At(long timestamp)
    {
        lock (_gate)
        {
            if (!_running || _paused || timestamp < _acceptedFrom) return null;
            return Stopwatch.GetElapsedTime(_start, timestamp - _pausedTicks);
        }
    }
    public TimeSpan? AtSystemTime(TimeSpan qpcTime) => At((long)(qpcTime.Ticks * ((double)Stopwatch.Frequency / TimeSpan.TicksPerSecond)));
}
