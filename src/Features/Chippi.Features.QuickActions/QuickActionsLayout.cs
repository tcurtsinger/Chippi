// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.QuickActions;

/// <summary>Stable gallery positions, independent of visibility and what hardware is currently available.</summary>
internal static class QuickActionsLayout
{
    internal const int Slots = 20;

    /// <summary>The tiles that moved into Power and Quit apps. An older saved layout may still hold them.</summary>
    private static readonly HashSet<string> Moved = ["lock", "displays", "screensaver", "frozen"];

    /// <param name="added">Tiles other features added that already have a position (<see cref="QuickActionsSettings.Added"/>).</param>
    internal static List<string?> Normalize(IEnumerable<string?>? saved, IReadOnlyCollection<string>? shown, IReadOnlyCollection<string>? added = null)
    {
        string[] keys = [.. QuickAction.All.Where(a => a.InGallery).Select(a => a.Key), .. (added ?? []).Where(k => QuickAction.FromKey(k) is null).Distinct()];
        // Existing users keep the packed catalog order they already see. Hidden actions follow their selected ones.
        if (saved is null)
            return [.. keys.Where(k => shown?.Contains(k) == true).Concat(keys.Where(k => shown?.Contains(k) != true)).Take(Slots), .. Enumerable.Repeat<string?>(null, Math.Max(0, Slots - keys.Length))];

        var known = keys.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var slots = new string?[Slots];
        int index = 0;
        foreach (var key in saved.Take(Slots))
        {
            if (key is not null && Moved.Contains(key))
                continue; // a tile that left the gallery: its slot closes, so every later tile moves up one
            if (key is not null && known.Contains(key) && seen.Add(key))
                slots[index] = key;
            index++;
        }
        // Repair duplicates/obsolete keys without moving any valid saved position.
        foreach (var key in keys.Where(k => !seen.Contains(k)))
        {
            int free = Array.IndexOf(slots, null);
            if (free < 0) break;
            slots[free] = key;
        }
        return [.. slots];
    }

    /// <summary>A newly added tile's position: after the last tile, or the first gap once the last slot is taken.</summary>
    internal static List<string?> Append(IReadOnlyList<string?> layout, string key)
    {
        var slots = layout.ToList();
        int at = slots.FindLastIndex(k => k is not null) + 1;
        if (at >= slots.Count)
            at = slots.IndexOf(null);
        if (at >= 0)
            slots[at] = key;
        return slots;
    }
}
