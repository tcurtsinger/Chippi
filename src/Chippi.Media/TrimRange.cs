// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

/// <summary>A span of a clip, as the recording page's trim bar sets it.</summary>
public readonly record struct TrimRange(TimeSpan Start, TimeSpan End)
{
    /// <summary>The shortest span the handles leave.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromMilliseconds(500);

    public TimeSpan Length => End - Start;

    public static TrimRange Whole(TimeSpan duration) => new(TimeSpan.Zero, duration);

    /// <summary>True when the span is the whole clip, so Save needs no re-encode.</summary>
    public bool IsWhole(TimeSpan duration) => Start <= TimeSpan.Zero && End >= duration;

    /// <summary>
    /// The span with handles at fractions <paramref name="from"/> and <paramref name="to"/> of the bar, kept inside
    /// the clip and at least <see cref="Shortest"/> long (or the whole clip, when that's shorter).
    /// </summary>
    public static TrimRange FromBar(double from, double to, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return default;
        from = Math.Clamp(double.IsNaN(from) ? 0 : from, 0, 1);
        to = Math.Clamp(double.IsNaN(to) ? 1 : to, 0, 1);
        if (to < from) (from, to) = (to, from);
        var start = TimeSpan.FromTicks((long)(duration.Ticks * from));
        var end = TimeSpan.FromTicks((long)(duration.Ticks * to));
        return Clamp(start, end, duration);
    }

    /// <summary>The span kept inside the clip and at least <see cref="Shortest"/> long.</summary>
    public static TrimRange Clamp(TimeSpan start, TimeSpan end, TimeSpan duration)
    {
        if (duration <= Shortest) return Whole(duration);
        start = start < TimeSpan.Zero ? TimeSpan.Zero : start > duration ? duration : start;
        end = end < TimeSpan.Zero ? TimeSpan.Zero : end > duration ? duration : end;
        if (end < start) (start, end) = (end, start);
        if (end - start >= Shortest) return new TrimRange(start, end);
        // Too short: grow past the end first, then back past the start.
        end = start + Shortest;
        if (end > duration) (start, end) = (duration - Shortest, duration);
        return new TrimRange(start, end);
    }
}
