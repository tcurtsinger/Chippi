// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.QuickActions;

/// <summary>Stable gallery positions, independent of visibility and what hardware is currently available.</summary>
internal static class QuickActionsLayout
{
    internal const int Slots = 20;

    /// <summary>The tiles that moved into Power and Quit apps. An older saved layout may still hold them.</summary>
    private static readonly HashSet<string> Moved = ["lock", "displays", "screensaver", "frozen"];

    internal static List<string?> Normalize(IEnumerable<string?>? saved, IReadOnlyCollection<string>? shown)
    {
        var keys = QuickAction.All.Where(a => a.InGallery).Select(a => a.Key).ToArray();
        // Existing users keep the packed catalog order they already see. Hidden actions follow their selected ones.
        if (saved is null)
            return [.. keys.Where(k => shown?.Contains(k) == true), .. keys.Where(k => shown?.Contains(k) != true), .. Enumerable.Repeat<string?>(null, Slots - keys.Length)];

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
            slots[Array.IndexOf(slots, null)] = key;
        return [.. slots];
    }
}
