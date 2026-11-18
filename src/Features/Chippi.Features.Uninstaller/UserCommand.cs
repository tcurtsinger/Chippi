// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;

namespace Chippi.Features.Uninstaller;

/// <summary>
/// Turns an HKCU app's command into the program and arguments Chippi starts as the standard user, by the rules the admin helper applies to a machine
/// app: an MSI entry runs msiexec on its product code; otherwise the quiet string, else the normal one. Nothing is left for Windows to guess.
/// </summary>
public static class UserCommand
{
    public const int MaxLength = 4096;

    public sealed record Line(string Application, string Arguments);

    /// <summary>The line to run for <paramref name="app"/>, or null when it has none that resolves to an existing .exe.</summary>
    public static Line? For(AppEntry app, Func<string, string>? expand = null, Func<string, bool>? fileExists = null)
    {
        expand ??= Environment.ExpandEnvironmentVariables;
        fileExists ??= File.Exists;
        if (app.IsMsi && app.ProductCode is { } code)
            return new Line(Path.Combine(expand("%SystemRoot%"), "System32", "msiexec.exe"), "/x " + code + " /passive /norestart");
        if (Parse(app.QuietUninstallString, expand, fileExists) is { } quiet) return quiet;
        return Parse(app.UninstallString, expand, fileExists);
    }

    /// <summary>
    /// A quoted path ends at its closing quote. An unquoted one is resolved left to right: the first ".exe" boundary (followed by a space or the
    /// end) whose file exists is the program, and the rest are its arguments. Only an ".exe" qualifies.
    /// </summary>
    public static Line? Parse(string? text, Func<string, string> expand, Func<string, bool> fileExists)
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
        return application.Contains('"') ? null : new Line(application, rest);
    }

    /// <summary>The folder an uninstaller runs from: part of the leftover scope. Null for an MSI entry (msiexec's folder is not the app's).</summary>
    public static string? FolderOf(AppEntry app, Func<string, string>? expand = null, Func<string, bool>? fileExists = null)
    {
        if (app.IsMsi) return null;
        var line = For(app, expand, fileExists);
        return line is null ? null : Path.GetDirectoryName(line.Application);
    }
}
