// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using Chippi.Core.Clipboard;
using Chippi.Features.Clipboard.History;

namespace Chippi.Features.Clipboard.Views;

/// <summary>The page's words for times, kinds and limits (design notes #8, #9, #13).</summary>
internal static class Format
{
    /// <summary>"Now", "2 min", "1 hr", "Yesterday", "Mon", "Oct 21".</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var age = now - at;
        if (age < TimeSpan.FromMinutes(1))
            return "Now";
        if (age < TimeSpan.FromHours(1))
            return $"{(int)age.TotalMinutes} min";
        if (age < TimeSpan.FromDays(1))
            return $"{(int)age.TotalHours} hr";
        var day = at.ToLocalTime().Date;
        var today = now.ToLocalTime().Date;
        if (day == today.AddDays(-1))
            return "Yesterday";
        if (day > today.AddDays(-7))
            return day.ToString("ddd", CultureInfo.CurrentCulture);
        return day.ToString("MMM d", CultureInfo.CurrentCulture);
    }

    /// <summary>The row's one line: the text's first line, the files' names, or the picture's size.</summary>
    public static string Line(HistoryEntry entry) => entry.Kind switch
    {
        EntryKind.Files => string.Join(", ", (entry.Files ?? []).Select(Path.GetFileName)),
        EntryKind.Picture => Picture(entry),
        _ => FirstLine(entry.Text ?? ""),
    };

    /// <summary>The selected row's kind and details: "Text · 4 lines", "Picture · 1920 × 1080", "3 files", "Link".</summary>
    public static string Details(HistoryEntry entry) => entry.Kind switch
    {
        EntryKind.Files => Count(entry.Files?.Count ?? 0, "file"),
        EntryKind.Picture => Picture(entry),
        EntryKind.Link => "Link",
        _ => "Text · " + Count(Lines(entry.Text ?? ""), "line"),
    };

    /// <summary>The Caution line for a copy that wasn't saved (notes #13).</summary>
    public static string? Limit(ClipLimit limit) => limit switch
    {
        ClipLimit.Image => "Picture not saved · over 16 MiB",
        ClipLimit.Text => "Text not saved · over 1 MiB",
        ClipLimit.Files => "Not saved · over 100 files",
        _ => null,
    };

    public const string Full = "History full · 128 MiB";

    /// <summary>"1 file unavailable", "2 files unavailable".</summary>
    public static string Unavailable(int missing) => Count(missing, "file") + " unavailable";

    /// <summary>A file's size, "22 KB", "2.1 MB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1000 => $"{bytes} B",
        < 1_000_000 => $"{Math.Max(1, bytes / 1000)} KB",
        < 1_000_000_000 => (bytes / 1e6).ToString("0.#", CultureInfo.CurrentCulture) + " MB",
        _ => (bytes / 1e9).ToString("0.#", CultureInfo.CurrentCulture) + " GB",
    };

    public static int Lines(string text) => text.Length == 0 ? 0 : text.TrimEnd('\r', '\n').Split('\n').Length;

    private static string Picture(HistoryEntry entry) => $"Picture · {entry.Width} × {entry.Height}";

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string FirstLine(string text)
    {
        foreach (var line in text.Split('\n'))
            if (line.Trim() is { Length: > 0 } trimmed)
                return trimmed.Replace('\t', ' ');
        return "";
    }
}
