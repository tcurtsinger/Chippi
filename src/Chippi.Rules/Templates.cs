// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;

namespace Chippi.Rules;

public static class Templates
{
    private static readonly string[] Tokens = ["{year}", "{month}", "{day}", "{extension}", "{filename}", "{stem}"];

    public static (string Stem, string Extension) Parts(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot <= 0 ? (name, "") : (name[..dot], name[(dot + 1)..].ToLowerInvariant());
    }

    public static string Render(string template, string name, DateOnly localDate)
    {
        var (stem, extension) = Parts(name);
        // Parse tokens from the template only, never re-expand braces in a real filename.
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '}') throw new ArgumentException("Unknown placeholder.", nameof(template));
            if (template[i] != '{') { result.Append(template[i]); continue; }
            int end = template.IndexOf('}', i + 1);
            if (end < 0) throw new ArgumentException("Unclosed placeholder.", nameof(template));
            result.Append(template[i..(end + 1)] switch
            {
                "{year}" => localDate.ToString("yyyy", CultureInfo.InvariantCulture),
                "{month}" => localDate.ToString("MM", CultureInfo.InvariantCulture),
                "{day}" => localDate.ToString("dd", CultureInfo.InvariantCulture),
                "{stem}" => stem,
                "{filename}" => name,
                "{extension}" => extension,
                _ => throw new ArgumentException("Unknown placeholder.", nameof(template)),
            });
            i = end;
        }
        return result.ToString();
    }

    public static void Validate(string template, bool renameOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        _ = Render(template, "sample.txt", new DateOnly(2000, 1, 1));
        string sample = template;
        foreach (string token in Tokens) sample = sample.Replace(token, "sample", StringComparison.Ordinal);
        if (renameOnly) WindowsPaths.ValidateComponent(sample);
        else if (WindowsPaths.IsAbsolute(sample)) _ = WindowsPaths.NormalizeAbsolute(sample);
        else
        {
            if (sample.StartsWith('\\') || sample.StartsWith('/')) throw new ArgumentException("Root-relative paths are not supported.", nameof(template));
            foreach (string part in sample.Replace('/', '\\').Split('\\')) WindowsPaths.ValidateComponent(part);
        }
    }
}
