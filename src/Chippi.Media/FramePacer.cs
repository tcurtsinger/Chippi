// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

/// <summary>
/// Thins frames to the file's rate. WGC hands over a frame for every change, up to the display's refresh (165 Hz
/// here), and none while nothing moves. A frame is kept when it opens a new slot of 1/fps, and is stamped at its
/// slot's start, so the file's timestamps sit on an even grid. Slots with no frame stay empty: the last frame holds.
/// </summary>
public sealed class FramePacer(int fps)
{
    private long _last = -1;

    public int Fps { get; } = fps > 0 ? fps : throw new ArgumentOutOfRangeException(nameof(fps));

    /// <summary>Frames kept so far.</summary>
    public long Kept { get; private set; }

    /// <summary>The time a frame at <paramref name="at"/> is written at, or null when its slot is taken.</summary>
    public TimeSpan? Take(TimeSpan at)
    {
        if (at < TimeSpan.Zero) return null;
        var slot = at.Ticks * Fps / TimeSpan.TicksPerSecond;
        if (slot <= _last) return null;
        _last = slot;
        Kept++;
        return SlotTime(slot);
    }

    public TimeSpan SlotTime(long slot) => TimeSpan.FromTicks(slot * TimeSpan.TicksPerSecond / Fps);

    /// <summary>One slot's length, rounded down to a tick.</summary>
    public TimeSpan Interval => TimeSpan.FromTicks(TimeSpan.TicksPerSecond / Fps);
}
