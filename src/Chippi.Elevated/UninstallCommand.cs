// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Text.RegularExpressions;

namespace Chippi.Elevated;

/// <summary>Turns an Uninstall entry's command into the exact application and command line the helper starts. Nothing is left for Windows to guess.</summary>
public static partial class UninstallCommand
{
    public const int MaxLength = 4096;

    /// <summary>A Windows Installer product code: braces around a GUID, as the key names of MSI entries are.</summary>
    public static bool IsProductCode(string? name) => name is not null && ProductCodePattern().IsMatch(name);

    /// <summary>
    /// The text is split first, on its own characters, and only then is the application's path expanded (the arguments are passed as written). A
    /// quoted path ends at its closing quote. An unquoted one is resolved left to right: the first ".exe" boundary (followed by a space or the end)
    /// whose expanded path passes the path rules and exists is the application. An expansion that adds a quote or a control character, or leaves a
    /// "%" behind, refuses the path, so no variable can move where the application ends. Only an ".exe" qualifies. The command line passed on
    /// always names the application in quotes, so it can't be read another way.
    /// </summary>
    public static (string Application, string CommandLine)? Parse(string? text, Func<string, string> expand, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string s = text.Trim();
        if (s.Length is 0 or > MaxLength || s.Any(char.IsControl)) return null;
        string application, rest;
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            if (end < 0) return null;
            string? expanded = Expanded(s[1..end], expand);
            if (expanded is null || !expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            application = expanded;
            rest = s[(end + 1)..].Trim();
        }
        else
        {
            string? found = null;
            int boundary = -1;
            for (int i = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); i >= 0; i = s.IndexOf(".exe", i + 1, StringComparison.OrdinalIgnoreCase))
            {
                int end = i + 4;
                if (end != s.Length && !char.IsWhiteSpace(s[end])) continue;
                string? candidate = Expanded(s[..end], expand);
                // The path rules come before any file is touched: a UNC candidate must never be probed from the elevated helper.
                if (candidate is null || PathRules.Refusal(candidate) is not null) continue;
                if (!fileExists(candidate)) continue;
                found = candidate; boundary = end;
                break;
            }
            if (found is null) return null;
            application = found;
            rest = s[boundary..].Trim();
        }
        if (application.Contains('"') || application.Length > MaxLength) return null;
        return (application, rest.Length == 0 ? $"\"{application}\"" : $"\"{application}\" {rest}");
    }

    /// <summary>The path with its variables expanded; null when the expansion brought in a quote or a control character, or left a "%" behind.</summary>
    private static string? Expanded(string path, Func<string, string> expand)
    {
        string result = expand(path);
        return result.Contains('"') || result.Contains('%') || result.Any(char.IsControl) ? null : result;
    }

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex ProductCodePattern();
}
