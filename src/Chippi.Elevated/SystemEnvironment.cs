// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Text;

namespace Chippi.Elevated;

/// <summary>
/// The only environment the helper trusts: a fixed set of Windows folders for expanding an Uninstall entry's path, and a system-only, scrubbed block
/// for the uninstallers it starts. Neither reads the variables Chippi, and so the owner's profile, handed the helper.
/// </summary>
public static class SystemEnvironment
{
    private static readonly string[] BlockedPrefixes = ["DOTNET_", "COR_", "CORECLR_", "COMPLUS_", "JAVA", "_JAVA", "JDK_", "NODE_", "NPM_", "PYTHON", "PERL", "RUBY"];

    /// <summary>The variables an Uninstall path may use, each from Windows' own folder list.</summary>
    public static IReadOnlyDictionary<string, string> FixedVariables()
    {
        string Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f).TrimEnd('\\');
        string windows = Folder(Environment.SpecialFolder.Windows);
        string programData = Folder(Environment.SpecialFolder.CommonApplicationData);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = windows, ["windir"] = windows, ["SystemDrive"] = (Path.GetPathRoot(windows) ?? @"C:\").TrimEnd('\\'),
            ["ProgramFiles"] = Folder(Environment.SpecialFolder.ProgramFiles), ["ProgramW6432"] = Folder(Environment.SpecialFolder.ProgramFiles),
            ["ProgramFiles(x86)"] = Folder(Environment.SpecialFolder.ProgramFilesX86),
            ["CommonProgramFiles"] = Folder(Environment.SpecialFolder.CommonProgramFiles), ["CommonProgramW6432"] = Folder(Environment.SpecialFolder.CommonProgramFiles),
            ["CommonProgramFiles(x86)"] = Folder(Environment.SpecialFolder.CommonProgramFilesX86),
            ["ProgramData"] = programData, ["ALLUSERSPROFILE"] = programData,
        };
        foreach (var name in map.Where(p => p.Value.Length == 0).Select(p => p.Key).ToArray()) map.Remove(name);
        return map;
    }

    /// <summary>Replaces each %NAME% that names a variable in the map; any other stays as written (and the caller refuses a path that still holds one).</summary>
    public static string Expand(string text, IReadOnlyDictionary<string, string> variables)
    {
        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '%')
            {
                int close = text.IndexOf('%', i + 1);
                if (close > i + 1 && variables.TryGetValue(text[(i + 1)..close], out var value)) { result.Append(value); i = close; continue; }
            }
            result.Append(text[i]);
        }
        return result.ToString();
    }

    /// <summary>
    /// The system variables an uninstaller may start with: the toolchain, runtime-options and compatibility variables are dropped (a profiler,
    /// a startup hook or a Java option would load code into an elevated process) and PATH is pinned to the Windows folders.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Scrub(IEnumerable<KeyValuePair<string, string>> system, string systemDirectory)
    {
        string windows = Path.GetDirectoryName(systemDirectory.TrimEnd('\\')) ?? systemDirectory;
        var kept = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in system)
        {
            if (name.Length == 0 || name.Contains('=') || name.Equals("PATH", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals("__COMPAT_LAYER", StringComparison.OrdinalIgnoreCase) || BlockedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            kept[name] = value;
        }
        kept["PATH"] = $@"{systemDirectory};{windows};{Path.Combine(systemDirectory, "Wbem")}";
        return [.. kept];
    }

    /// <summary>A CreateProcess environment block: each "name=value" ended by a null, and the list by a second one.</summary>
    public static char[] Block(IEnumerable<KeyValuePair<string, string>> variables)
    {
        var text = new StringBuilder();
        foreach (var (name, value) in variables) text.Append(name).Append('=').Append(value).Append('\0');
        return (text.Append('\0').ToString()).ToCharArray();
    }
}
