// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Audio;

/// <summary>One entry of a priority list: the endpoint ID and the name it had when saved.</summary>
public sealed record PriorityEntry(string Id, string Name);

/// <summary>
/// The pure rules behind Audio routing's two lists (Hopper's PriorityResolver plus the brief's
/// lead calls): who wins, how lists are seeded and grown, and how stale IDs are re-matched. No COM;
/// every method returns a new list and leaves its input alone.
/// </summary>
public static class PriorityPolicy
{
    /// <summary>
    /// The highest listed endpoint that is active and not excluded, or null. Null means leave
    /// Windows' choice alone; there is no fallback outside the list (Hopper parity).
    /// </summary>
    public static string? Resolve(IReadOnlyList<PriorityEntry> list, IReadOnlyList<AudioEndpoint> active, IReadOnlySet<string>? exclude = null)
    {
        foreach (var entry in list)
        {
            if (exclude is not null && exclude.Contains(entry.Id))
                continue;
            if (active.Any(a => a.IsActive && Eq(a.Id, entry.Id)))
                return entry.Id;
        }
        return null;
    }

    /// <summary>First-run list: every active endpoint, the current default first.</summary>
    public static IReadOnlyList<PriorityEntry> Seed(IReadOnlyList<AudioEndpoint> active, string? defaultId) =>
        active.Where(a => a.IsActive)
            .OrderByDescending(a => Eq(a.Id, defaultId))
            .Select(a => new PriorityEntry(a.Id, a.Name))
            .ToList();

    /// <summary>
    /// Appends active endpoints that are neither listed nor in <paramref name="removed"/> (the IDs
    /// the user took off the list), in Windows' order. A removed device never returns on its own.
    /// </summary>
    public static IReadOnlyList<PriorityEntry> AppendNew(IReadOnlyList<PriorityEntry> list, IReadOnlyList<AudioEndpoint> active, IReadOnlySet<string> removed)
    {
        var added = active
            .Where(a => a.IsActive && !removed.Contains(a.Id) && !list.Any(e => Eq(e.Id, a.Id)))
            .Select(a => new PriorityEntry(a.Id, a.Name))
            .ToList();
        return added.Count == 0 ? list : [.. list, .. added];
    }

    /// <summary>
    /// Re-points entries whose ID no longer exists (a Windows update can regenerate every endpoint
    /// GUID) at the one endpoint with the same name. Ambiguity is never guessed: if two endpoints
    /// share the name, or the match is already listed under its own ID, the entry stays as it is and
    /// shows as unavailable. <paramref name="known"/> is every endpoint Windows still lists, active or not.
    /// </summary>
    public static IReadOnlyList<PriorityEntry> Repair(IReadOnlyList<PriorityEntry> list, IReadOnlyList<AudioEndpoint> known)
    {
        var result = list.ToList();
        var changed = false;
        for (var i = 0; i < result.Count; i++)
        {
            var entry = result[i];
            if (known.Any(k => Eq(k.Id, entry.Id)))
                continue;

            var sameName = known.Where(k => Eq(k.Name, entry.Name)).ToList();
            if (sameName.Count != 1)
                continue;
            var match = sameName[0];
            if (result.Any(e => Eq(e.Id, match.Id)))
                continue;

            result[i] = entry with { Id = match.Id };
            changed = true;
        }
        return changed ? result : list;
    }

    internal static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
