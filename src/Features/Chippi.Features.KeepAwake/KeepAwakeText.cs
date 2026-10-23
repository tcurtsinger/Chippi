// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.KeepAwake;

/// <summary>The lengths and status lines the panel and the page share (DESIGN.md §5).</summary>
public static class KeepAwakeText
{
    /// <summary>The fixed lengths, in chip order; "Until…" follows them.</summary>
    public static IReadOnlyList<(string Label, KeepAwakeSettings Length)> Lengths { get; } =
    [
        ("30 min", new() { Mode = AwakeMode.Duration, DurationMinutes = 30 }),
        ("1 hr", new() { Mode = AwakeMode.Duration, DurationMinutes = 60 }),
        ("2 hr", new() { Mode = AwakeMode.Duration, DurationMinutes = 120 }),
        ("4 hr", new() { Mode = AwakeMode.Duration, DurationMinutes = 240 }),
        ("No limit", new() { Mode = AwakeMode.Indefinitely }),
    ];

    /// <summary>Whether two settings start the same length (the screen setting aside).</summary>
    public static bool SameLength(KeepAwakeSettings a, KeepAwakeSettings b) => a.Mode == b.Mode && a.Mode switch
    {
        AwakeMode.Duration => a.DurationMinutes == b.DurationMinutes,
        AwakeMode.UntilTime => a.UntilMinute == b.UntilMinute,
        _ => true,
    };

    public static string Until(int minute) => DateTime.Today.AddMinutes(minute).ToString("t");

    /// <summary>"Off", "On · no time limit" or "On · 1 hr left".</summary>
    public static string Status(KeepAwakeModule module, DateTimeOffset now) => !module.IsOn ? "Off"
        : module.Deadline is { } end ? $"On · {Left(end - now)} left"
        : "On · no time limit";

    /// <summary>The line under the status: "PC sleeps as usual", "Until 11:42 AM" or "Until you turn it off".</summary>
    public static string Detail(KeepAwakeModule module) => !module.IsOn ? "PC sleeps as usual"
        : module.Deadline is { } end ? $"Until {end.LocalDateTime:t}"
        : "Until you turn it off";

    /// <summary>"45 min", "1 hr", "1 hr 30 min", rounded up to the minute.</summary>
    public static string Left(TimeSpan left)
    {
        int minutes = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
        int hours = minutes / 60, rest = minutes % 60;
        return hours == 0 ? $"{rest} min" : rest == 0 ? $"{hours} hr" : $"{hours} hr {rest} min";
    }
}
