// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

/// <summary>
/// Maps capture times to the file's timeline (screen-recording.md, Pause and Resume). Capture times are the
/// performance counter as a <see cref="TimeSpan"/>, which WGC frames (<c>SystemRelativeTime</c>) and WASAPI packets
/// (their QPC position) share. A pause cuts its span out, so the video has no gap: the first frame after Resume
/// follows the last one before Pause. A frame captured before Pause but delivered after it is still placed.
/// Not thread-safe: the recorder locks around it.
/// </summary>
public sealed class RecordingTimeline
{
    private readonly List<(TimeSpan From, TimeSpan To)> _pauses = [];
    private TimeSpan? _start;
    private TimeSpan? _pausedAt;

    public bool IsStarted => _start is not null;

    public bool IsPaused => _pausedAt is not null;

    /// <summary>The capture time the file starts at.</summary>
    public TimeSpan Start => _start ?? throw new InvalidOperationException("Not started.");

    public void Begin(TimeSpan at)
    {
        if (_start is not null) throw new InvalidOperationException("Already started.");
        _start = at;
    }

    /// <returns>False when already paused or not started.</returns>
    public bool Pause(TimeSpan at)
    {
        if (_start is not { } start || _pausedAt is not null) return false;
        _pausedAt = at < start ? start : at;
        return true;
    }

    /// <returns>False when not paused.</returns>
    public bool Resume(TimeSpan at)
    {
        if (_pausedAt is not { } from) return false;
        _pauses.Add((from, at < from ? from : at));
        _pausedAt = null;
        return true;
    }

    /// <summary>Where a capture time lands in the file, or null before the start or inside a pause.</summary>
    public TimeSpan? Map(TimeSpan capture)
    {
        if (_start is not { } start || capture < start) return null;
        if (_pausedAt is { } open && capture >= open) return null;
        var cut = TimeSpan.Zero;
        foreach (var (from, to) in _pauses)
        {
            if (capture < from) break;
            if (capture < to) return null;
            cut += to - from;
        }
        return capture - start - cut;
    }

    /// <summary>The file's length so far at capture time <paramref name="now"/>; it stands still while paused.</summary>
    public TimeSpan Elapsed(TimeSpan now)
    {
        if (_start is not { } start || now <= start) return TimeSpan.Zero;
        var cut = TimeSpan.Zero;
        foreach (var (from, to) in _pauses)
        {
            if (now <= from) break;
            cut += (now < to ? now : to) - from;
        }
        if (_pausedAt is { } open && now > open) cut += now - open;
        return now - start - cut;
    }
}
