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
    /// A quoted path ends at its closing quote. An unquoted one is resolved left to right: the first ".exe" boundary (followed by a space or the
    /// end) whose file exists is the application, and the rest are its arguments. Environment variables are expanded first. Only an ".exe"
    /// qualifies. The command line passed on always names the application in quotes, so it can't be read another way.
    /// </summary>
    public static (string Application, string CommandLine)? Parse(string? text, Func<string, string> expand, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string s = expand(text).Trim();
        if (s.Length is 0 or > MaxLength || s.Any(char.IsControl)) return null;
        string application, rest;
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            if (end < 0) return null;
            application = s[1..end];
            rest = s[(end + 1)..].Trim();
            if (!application.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        }
        else
        {
            string? found = null;
            int boundary = -1;
            for (int i = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); i >= 0; i = s.IndexOf(".exe", i + 1, StringComparison.OrdinalIgnoreCase))
            {
                int end = i + 4;
                if (end != s.Length && !char.IsWhiteSpace(s[end])) continue;
                if (!fileExists(s[..end])) continue;
                found = s[..end]; boundary = end;
                break;
            }
            if (found is null) return null;
            application = found;
            rest = s[boundary..].Trim();
        }
        if (application.Contains('"')) return null;
        return (application, rest.Length == 0 ? $"\"{application}\"" : $"\"{application}\" {rest}");
    }

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex ProductCodePattern();
}
