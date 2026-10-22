// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Rules;

/// <summary>Lexical Windows path handling, independent of the machine's disk, current directory or OS.</summary>
public static class WindowsPaths
{
    public static bool IsAbsolute(string path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
        || path.StartsWith("\\\\", StringComparison.Ordinal);

    public static string NormalizeAbsolute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = path.Replace('/', '\\');
        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
            throw new ArgumentException("Device paths are not supported.", nameof(path));
        string root;
        string remainder;
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
        {
            root = char.ToUpperInvariant(path[0]) + ":\\";
            remainder = path[3..];
        }
        else if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            var parts = path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) throw new ArgumentException("A UNC path needs a server and share.", nameof(path));
            ValidateComponent(parts[0]);
            ValidateComponent(parts[1]);
            root = "\\\\" + parts[0] + "\\" + parts[1] + "\\";
            remainder = string.Join('\\', parts.Skip(2));
        }
        else throw new ArgumentException("An absolute Windows path is required.", nameof(path));
        var components = remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (string component in components) ValidateComponent(component);
        return root + string.Join('\\', components);
    }

    public static void ValidateComponent(string component)
    {
        if (string.IsNullOrWhiteSpace(component) || component.Length > 255 || component is "." or ".." || component.EndsWith('.') || component.EndsWith(' ')
            || component.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)))
            throw new ArgumentException("Invalid path component.", nameof(component));
        string stem = component.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789\u00B9\u00B2\u00B3".Contains(stem[3]))
            throw new ArgumentException("Reserved Windows name.", nameof(component));
    }

    public static string Combine(string folder, string name) => NormalizeAbsolute(folder.TrimEnd('\\') + "\\" + name);
    public static string Parent(string path)
    {
        string parent = path[..(path.LastIndexOf('\\') + 1)].TrimEnd('\\');
        return parent.EndsWith(':') ? parent + "\\" : parent;
    }
    public static string Leaf(string path) => path[(path.LastIndexOf('\\') + 1)..];
}
