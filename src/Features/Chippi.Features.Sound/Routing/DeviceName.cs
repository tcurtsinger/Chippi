// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.Audio;
using Chippi.UI.Controls;

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

    /// <summary>Shown names for these outputs, as for the Sound page's rows.</summary>
    public static IReadOnlyDictionary<string, string> Shown(IEnumerable<AudioEndpoint> outputs) =>
        Shown(outputs.Select(e => new PriorityEntry(e.Id, e.Name)).ToList());

    // Display audio: a monitor or TV over HDMI or DisplayPort. A board's own "High Definition Audio Device" isn't one.
    private static readonly string[] Displays = ["NVIDIA High Definition Audio", "AMD High Definition Audio", "Display Audio"];

    /// <summary>
    /// The kind of device as App volume's glyph: earbuds, headphones (a headset, earphones), a monitor
    /// for display audio, else a speaker. Read from Windows' names, so a renamed endpoint may read as a speaker.
    /// </summary>
    public static Geometry Glyph(AudioEndpoint? output)
    {
        if (output is null)
            return LucideIcons.Speaker;
        var (kind, device) = Split(output.Name) ?? ("", output.Name);
        bool Has(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);
        if (Has(output.Name, "buds") || Has(output.Name, "AirPods"))
            return LucideIcons.Earbuds;
        if (Has(kind, "Head") || Has(kind, "Earphone") || Has(device, "Headset") || Has(device, "Headphone"))
            return LucideIcons.Headphones;
        if (Displays.Any(d => Has(device, d) || Has(output.AdapterName, d)))
            return LucideIcons.Monitor;
        return LucideIcons.Speaker;
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
