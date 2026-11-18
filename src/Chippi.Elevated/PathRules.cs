// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Elevated;

/// <summary>
/// The helper's path rules (admin-helper.md): a path must be absolute, local and already canonical before any check looks at it. Refused:
/// relative paths, "." and ".." segments, \\?\ and \\.\ prefixes, UNC and network paths, alternate data streams and 8.3 short names.
/// </summary>
public static class PathRules
{
    /// <summary>Null when the path passes, else a short reason (never the path itself).</summary>
    public static string? Refusal(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.Any(char.IsControl)) return "Unsafe path";
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return "Unsafe path";   // UNC, \\?\ and \\.\
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\') return "Unsafe path";
        if (path.IndexOf(':', 2) >= 0) return "Unsafe path";                           // an alternate data stream
        foreach (var segment in path[3..].TrimEnd('\\').Split('\\'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment[^1] is '.' or ' ') return "Unsafe path";
            if (segment.IndexOfAny(['<', '>', '"', '|', '?', '*', '/']) >= 0) return "Unsafe path";
            if (IsShortName(segment)) return "Unsafe path";
        }
        try { if (!string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase)) return "Unsafe path"; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return "Unsafe path"; }
        return null;
    }

    /// <summary>True when a segment holds "~" and a digit, the shape of an 8.3 short name (PROGRA~1).</summary>
    private static bool IsShortName(string segment)
    {
        for (int i = segment.IndexOf('~'); i >= 0; i = segment.IndexOf('~', i + 1))
            if (i + 1 < segment.Length && char.IsAsciiDigit(segment[i + 1])) return true;
        return false;
    }

    /// <summary>Whole segments, case-insensitive: C:\Program Files\App contains C:\Program Files\App\x but not C:\Program Files\AppData. A folder doesn't contain itself.</summary>
    public static bool Contains(string parent, string child)
    {
        parent = parent.TrimEnd('\\'); child = child.TrimEnd('\\');
        return child.Length > parent.Length && child.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && child[parent.Length] == '\\';
    }
}
