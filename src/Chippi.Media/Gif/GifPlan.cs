// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media.Gif;

/// <summary>
/// The arithmetic of a GIF export (screen-recording.md, GIF): 15 fps, at most 60 seconds, from the start of the
/// clip or its trim.
/// </summary>
public static class GifPlan
{
    public const int Fps = 15;

    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    /// <summary>The span to export, and whether the limit cut it ("GIF is limited to 60 seconds").</summary>
    public static (TrimRange Span, bool Cut) Span(TrimRange clip)
    {
        if (clip.Length <= Limit) return (clip, false);
        return (new TrimRange(clip.Start, clip.Start + Limit), true);
    }

    /// <summary>How many frames <paramref name="length"/> takes at <see cref="Fps"/>; a partial last slot counts.</summary>
    public static int FrameCount(TimeSpan length)
    {
        if (length <= TimeSpan.Zero) return 0;
        return (int)Math.Ceiling(length.Ticks * (double)Fps / TimeSpan.TicksPerSecond - 1e-9);
    }

    /// <summary>Where frame <paramref name="index"/> is read from, measured from the span's start.</summary>
    public static TimeSpan FrameTime(int index) => TimeSpan.FromTicks(index * TimeSpan.TicksPerSecond / Fps);

    /// <summary>
    /// Frame <paramref name="index"/>'s delay in hundredths of a second (GIF's unit). 15 fps is 6.67, so the delays
    /// run 7, 7, 6 and so on, and any frame ends where the clock says.
    /// </summary>
    public static int Delay(int index) => Hundredths(index + 1) - Hundredths(index);

    private static int Hundredths(int frames) => (int)Math.Round(frames * 100.0 / Fps, MidpointRounding.AwayFromZero);

    /// <summary>A delay of several held frames joined into one (frame differencing drops unchanged frames).</summary>
    public static int Delay(int first, int count) => Hundredths(first + count) - Hundredths(first);
}
