// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AgentUsage;

/// <summary>
/// How often each agent's limits are checked (the brief's "How often"): every minute while the page or its Settings page
/// shows, every 5 minutes otherwise, and not at all while nobody can see the screen (the session locked, the display off).
/// A check that fails waits longer before the next one, doubling each time: up to 30 minutes for what only you can fix (not
/// installed, signed out, too old), up to 10 for the rest.
/// </summary>
internal static class UsageSchedule
{
    public static readonly TimeSpan Seen = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Background = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MostAfterFailure = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MostForYouToFix = TimeSpan.FromMinutes(30);

    /// <summary>The first checks wait this long after Chippi starts, so they don't add to the sign-in's load.</summary>
    public static readonly TimeSpan First = TimeSpan.FromSeconds(10);

    /// <summary>The wait after a check: the interval, or after <paramref name="failures"/> in a row, twice the last wait up to its most.</summary>
    public static TimeSpan Delay(bool seen, int failures, CheckState last)
    {
        var interval = seen ? Seen : Background;
        if (failures <= 0)
            return interval;
        var most = last.OnlyYouCanFix() ? MostForYouToFix : MostAfterFailure;
        double minutes = interval.TotalMinutes * Math.Pow(2, Math.Min(failures, 10));
        return TimeSpan.FromMinutes(Math.Min(minutes, most.TotalMinutes));
    }

    /// <summary>When the next check is due, given the last one (null before the first, which is due <see cref="First"/> after <paramref name="started"/>).</summary>
    public static DateTimeOffset Due(DateTimeOffset started, DateTimeOffset? last, bool seen, int failures, CheckState state) =>
        last is { } at ? at + Delay(seen, failures, state) : started + First;
}
