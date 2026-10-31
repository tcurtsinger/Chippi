// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Rules;

public static class IgnoreFile
{
    public static IgnoreParseResult Parse(string text)
    {
        var result = new List<string>();
        var errors = new List<IgnoreParseError>();
        int lineNumber = 0;
        foreach (string line in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            lineNumber++;
            var pattern = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] == '#') { pattern.Append('#'); i++; }
                else if (line[i] == '#') break;
                else pattern.Append(line[i]);
            }
            string value = pattern.ToString().Trim();
            if (value.Length == 0) continue;
            if (value.StartsWith('!') || value.Contains("**", StringComparison.Ordinal) || value.IndexOfAny(['?', '/', '\\']) >= 0)
            {
                errors.Add(new(lineNumber, value, "Unsupported pattern"));
                continue;
            }
            result.Add(value);
        }
        return new(result.ToArray(), errors.ToArray());
    }

    public static bool Matches(string name, string pattern)
    {
        // A small wildcard matcher; patterns are not regular expressions.
        int n = 0, p = 0, star = -1, retry = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && pattern[p] != '*' && char.ToUpperInvariant(name[n]) == char.ToUpperInvariant(pattern[p])) { n++; p++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = n; }
            else if (star >= 0) { p = star + 1; n = ++retry; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}

public static class FileSafety
{
    private static readonly string[] Partials = [".part", ".partial", ".crdownload", ".download", ".tmp", ".opdownload", ".!ut"];
    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini", "thumbs.db", "ntuser.dat", "ntuser.ini", "pagefile.sys", "hiberfil.sys", "swapfile.sys", "autorun.inf", "bootmgr", "boot.ini",
    };
    private static readonly HashSet<string> Executables = new("exe msi bat cmd com scr pif ps1 vbs vbe js jse wsf hta lnk".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Disguises = new("pdf doc docx xls xlsx ppt pptx txt rtf odt jpg jpeg png gif webp bmp svg mp3 mp4 wav avi mov zip rar 7z tgz gz tar".Split(' '), StringComparer.OrdinalIgnoreCase);

    public static bool IsMisleadingName(string name)
    {
        var parts = name.Split('.');
        return parts.Length >= 2 && Executables.Contains(parts[^1])
            && (parts.Length >= 3 && Disguises.Contains(parts[^2].Trim()) || name.IndexOfAny(['\u202D', '\u202E']) >= 0 || parts[^2].EndsWith("  ", StringComparison.Ordinal));
    }

    /// <summary>
    /// What a misleading name hides, for people: "Looks like a .pdf \u00B7 is an app" when a second extension
    /// is the disguise, else "Disguised app" (a reversed name, padding before the real extension).
    /// </summary>
    public static string Disguise(string name)
    {
        var parts = name.Split('.');
        return parts.Length >= 3 && Disguises.Contains(parts[^2].Trim()) && name.IndexOfAny(['\u202D', '\u202E']) < 0
            ? $"Looks like a .{parts[^2].Trim().ToLowerInvariant()} \u00B7 is an app"
            : "Disguised app";
    }

    public static PlanWarning? Exclusion(FileFacts file)
    {
        const FileAttributes recallOnDataAccess = (FileAttributes)0x00400000;
        if ((file.Attributes & (FileAttributes.Offline | recallOnDataAccess)) != 0)
            return new(WarningKind.CloudPlaceholder, "Cloud placeholder");
        if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            || file.Name.StartsWith('.') || file.Name.StartsWith("~$", StringComparison.Ordinal) || SystemNames.Contains(file.Name)
            || Partials.Any(s => file.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            return new(WarningKind.BuiltInExclusion, "Excluded file");
        return file.Bytes == 0 ? new(WarningKind.EmptyFile, "Empty file") : null;
    }
}
