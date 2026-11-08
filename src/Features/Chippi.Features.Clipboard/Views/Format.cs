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

    /// <summary>A search shows the matching line or filename. The saved content stays intact.</summary>
    public static string Line(HistoryEntry entry, string search = "")
    {
        if (entry.Kind == EntryKind.Picture) return Picture(entry);
        if (entry.Kind == EntryKind.Files)
        {
            var names = (entry.Files ?? []).Select(Path.GetFileName);
            if (search.Length != 0 && names.FirstOrDefault(name => name?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) is { } match)
                return Excerpt(match, search);
            return string.Join(", ", names);
        }
        return Excerpt(entry.Text ?? "", search);
    }

    internal static string Excerpt(string text, string search)
    {
        int hit = search.Length == 0 ? -1 : text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (hit < 0) return FirstLine(text);
        int start = MatchStart(text, hit);
        int end = text.IndexOf('\n', hit + search.Length);
        var snippet = text[start..(end < 0 ? text.Length : end)].Trim().Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return start > 0 && text[start - 1] != '\n' ? "…" + snippet : snippet;
    }

    /// <summary>Start at the matching line, with room for the match even on a long unbroken URL.</summary>
    internal static int MatchStart(string text, int hit)
    {
        if (hit == 0) return 0;
        int start = Math.Max(text.LastIndexOf('\n', hit - 1) + 1, hit - 18);
        if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1])) start--;
        return start;
    }

    /// <summary>The selected row's kind and details: "Text · 4 lines", "Picture · 1920 × 1080", "3 files", "Link".</summary>
    public static string Details(HistoryEntry entry) => entry.Kind switch
    {
        EntryKind.Files => Count(entry.Files?.Count ?? 0, "file"),
        EntryKind.Picture => Picture(entry),
        EntryKind.Link => "Link",
        _ => "Text · " + Count(Lines(entry.Text ?? ""), "line"),
    };

    /// <summary>A row or its menu, identifiable by a short excerpt while browsing saved history.</summary>
    public static string AccessibleName(HistoryEntry entry, DateTimeOffset now, string search = "")
    {
        string details = Details(entry);
        string line = entry.Kind == EntryKind.Picture ? "" : Line(entry, search);
        if (line.Length > 80)
        {
            int end = char.IsHighSurrogate(line[79]) ? 79 : 80;
            line = line[..end].TrimEnd() + "…";
        }
        return $"{(line.Length == 0 ? "" : line + ", ")}{details}, {When(entry.Recent, now)}{(entry.Pinned ? ", pinned" : "")}";
    }

    /// <summary>The Caution line for a copy that wasn't saved (notes #13).</summary>
    public static string? Limit(ClipLimit limit) => limit switch
    {
        ClipLimit.Image => "Picture not saved · over 16 MiB",
        ClipLimit.ImagePixels => "Picture not saved · over 16.6 MP",
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
        foreach (var line in text.AsSpan().EnumerateLines())
            if (line.Trim() is { Length: > 0 } trimmed)
                return trimmed.ToString().Replace('\t', ' ');
        return "";
    }
}
