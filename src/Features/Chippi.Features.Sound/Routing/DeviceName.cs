// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;

namespace Chippi.Features.Sound.Routing;

/// <summary>
/// The Sound page's row names (DESIGN.md §5 Island): Windows' "Speakers (INZONE Buds - Game)" leads with
/// the device, "INZONE Buds - Game", when what comes before it only says the kind of endpoint, so a cut
/// name loses the kind and not the device. Rows that would then read the same keep their full names.
/// </summary>
internal static class DeviceName
{
    // Windows' own endpoint kinds, in English; any other name, a monitor's say, stays whole.
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Speakers", "Speaker", "Headphones", "Headset", "Headset Earphone", "Headset Microphone", "Earphones",
        "Microphone", "Microphone Array", "External Microphone", "Line", "Line In", "Line Out", "Stereo Mix",
        "Digital Audio (S/PDIF)", "Digital Output", "SPDIF Interface", "Speakers/Headphones",
    };

    /// <summary>Each row's shown name, by endpoint ID.</summary>
    public static IReadOnlyDictionary<string, string> Shown(IReadOnlyList<PriorityEntry> rows)
    {
        var shown = rows.ToDictionary(e => e.Id, e => Short(e.Name), StringComparer.OrdinalIgnoreCase);
        var twice = shown.Values.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(r => twice.Contains(shown[r.Id])))
            shown[row.Id] = row.Name;
        return shown;
    }

    /// <summary>"INZONE Buds - Game" for "Speakers (INZONE Buds - Game)"; the name itself when it doesn't start with a kind.</summary>
    public static string Short(string name) =>
        Split(name) is ({ } kind, { Length: > 0 } device) && Kinds.Contains(kind) ? device : name;

    /// <summary>The name before its last bracketed part, and that part: "Digital Audio (S/PDIF)" and "Realtek(R) Audio".</summary>
    internal static (string Kind, string Device)? Split(string name)
    {
        if (!name.EndsWith(')'))
            return null;
        int depth = 0;
        for (int i = name.Length - 1; i > 0; i--)
        {
            if (name[i] == ')')
                depth++;
            else if (name[i] == '(' && --depth == 0)
                return name[i - 1] == ' ' ? (name[..(i - 1)].Trim(), name[(i + 1)..^1].Trim()) : null;
        }
        return null;
    }
}
