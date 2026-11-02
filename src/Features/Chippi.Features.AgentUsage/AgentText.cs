// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;

namespace Chippi.Features.AgentUsage;

/// <summary>The lines the page, the bar and Settings share, in the user's local time.</summary>
public static class AgentText
{
    /// <summary>"12 min", "1 hr 5 min", rounded down to the minute; under a minute is "1 min".</summary>
    public static string Minutes(TimeSpan span)
    {
        int minutes = Math.Max(1, (int)span.TotalMinutes);
        int hours = minutes / 60, rest = minutes % 60;
        return hours == 0 ? $"{rest} min" : rest == 0 ? $"{hours} hr" : $"{hours} hr {rest} min";
    }

    /// <summary>"42%", or "Reset" for a window past its reset time.</summary>
    public static string Value(UsageWindow window, DateTimeOffset now) => window.HasReset(now) ? "Reset" : $"{window.Percent}%";

    /// <summary>The island's reset line: "Resets 6:10 PM" today, "Resets Thu 9:00 AM" in the week ahead, "Resets Nov 12" later; empty when the source doesn't say or it has reset.</summary>
    public static string Resets(UsageWindow window, DateTimeOffset now) =>
        window.ResetsAt is not { } at || window.HasReset(now) ? "" : "Resets " + When(at, now, comma: false);

    /// <summary>Settings' reset line: "Resets today, 6:10 PM", "Resets Thu, 9:00 AM".</summary>
    public static string ResetsLong(UsageWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt is not { } at || window.HasReset(now))
            return "";
        var local = at.ToLocalTime();
        return local.Date == now.ToLocalTime().Date ? $"Resets today, {Time(local)}" : "Resets " + When(at, now, comma: true);
    }

    /// <summary>A row's tooltip: "5-hour limit: resets today at 6:10 PM · 42% used".</summary>
    public static string Tip(UsageWindow window, DateTimeOffset now)
    {
        string used = window.HasReset(now) ? "reset" : $"{window.Percent}% used";
        if (window.ResetsAt is not { } at || window.HasReset(now))
            return $"{window.Label} limit · {used}";
        var local = at.ToLocalTime();
        var today = now.ToLocalTime().Date;
        string day = local.Date == today ? "today" : local.Date == today.AddDays(1) ? "tomorrow"
            : (local.Date - today).TotalDays < 7 ? local.ToString("dddd", CultureInfo.CurrentCulture)
            : local.ToString("MMMM d", CultureInfo.CurrentCulture);
        return $"{window.Label} limit: resets {day} at {Time(local)} · {used}";
    }

    /// <summary>A stale reading's note: "As of 2:10 PM" today, "As of Fri 9:14 AM" this week, "As of Oct 30" before.</summary>
    public static string AsOf(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var today = now.ToLocalTime().Date;
        return local.Date == today ? $"As of {Time(local)}"
            : (today - local.Date).TotalDays < 7 ? $"As of {local.ToString("ddd", CultureInfo.CurrentCulture)} {Time(local)}"
            : $"As of {local.ToString("MMM d", CultureInfo.CurrentCulture)}";
    }

    /// <summary>"Max", "Pro", "Plus": a plan's name as the agent writes it ("max", "pro"), capitalized; null for none.</summary>
    public static string? Plan(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return null;
        string p = plan.Trim().Replace('_', ' ');
        return char.ToUpper(p[0], CultureInfo.InvariantCulture) + p[1..];
    }

    /// <summary>A window's name from its length: "5-hour", "Weekly", "Daily", "8-hour", "3-day".</summary>
    public static string WindowName(int minutes) => minutes switch
    {
        10080 => "Weekly",
        1440 => "Daily",
        < 1440 when minutes % 60 == 0 => $"{minutes / 60}-hour",
        < 1440 => $"{minutes}-minute",
        _ when minutes % 1440 == 0 => $"{minutes / 1440}-day",
        _ => $"{minutes / 60}-hour",
    };

    private static string When(DateTimeOffset at, DateTimeOffset now, bool comma)
    {
        var local = at.ToLocalTime();
        var today = now.ToLocalTime().Date;
        string sep = comma ? ", " : " ";
        return local.Date == today ? Time(local)
            : (local.Date - today).TotalDays < 7 ? local.ToString("ddd", CultureInfo.CurrentCulture) + sep + Time(local)
            : local.ToString("MMM d", CultureInfo.CurrentCulture);
    }

    private static string Time(DateTimeOffset local) => local.ToString("t", CultureInfo.CurrentCulture);
}
