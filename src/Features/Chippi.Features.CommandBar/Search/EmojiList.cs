// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;

namespace Chippi.Features.CommandBar.Search;

/// <summary>One emoji: itself, its name ("party popper") and its subgroup's words ("event").</summary>
public sealed record Emoji(string Text, string Name, string Words);

/// <summary>
/// The emoji Chippi finds by name, from Unicode's emoji-test.txt (Data/emoji.txt): fully qualified, no skin-tone variants or flags,
/// up to Emoji 15.1, in CLDR order. Read once, on first use.
/// </summary>
public static class EmojiList
{
    private static readonly Lazy<IReadOnlyList<Emoji>> Loaded = new(Load);

    public static IReadOnlyList<Emoji> All => Loaded.Value;

    private static IReadOnlyList<Emoji> Load()
    {
        using var stream = typeof(EmojiList).Assembly.GetManifestResourceStream("Chippi.Features.CommandBar.emoji.txt")
            ?? throw new InvalidOperationException("Emoji names missing");
        using var reader = new StreamReader(stream);
        var all = new List<Emoji>(1900);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var parts = line.Split('\t');
            if (parts.Length == 3)
                all.Add(new Emoji(parts[0], Capitalize(parts[1]), parts[2].Replace('-', ' ')));
        }
        return all;
    }

    // "party popper" → "Party popper", as the rows show names.
    private static string Capitalize(string name) =>
        name.Length == 0 ? name : char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];
}
