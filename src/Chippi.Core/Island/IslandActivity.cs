// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Island;

/// <summary>What an activity is. The live island shows one at a time, by <see cref="IslandActivityKindExtensions.Priority"/>.</summary>
public enum IslandActivityKind
{
    Media,
    Timer,
    Download,
    Stash,
    Agent,

    /// <summary>Background state such as Keep awake. Lowest priority; a timed session shows its countdown.</summary>
    Status,
}

public static class IslandActivityKindExtensions
{
    /// <summary>Lower wins: media, then timer, then download or stash (equal), then agent, then status (DESIGN.md §5).</summary>
    public static int Priority(this IslandActivityKind kind) => kind switch
    {
        IslandActivityKind.Media => 0,
        IslandActivityKind.Timer => 1,
        IslandActivityKind.Download or IslandActivityKind.Stash => 2,
        IslandActivityKind.Agent => 3,
        IslandActivityKind.Status => 4,
        _ => int.MaxValue,
    };
}

/// <summary>One thing the island can show.</summary>
/// <param name="Key">Identifies the activity within its module; publishing the same key again updates it in place.</param>
/// <param name="Kind">Decides priority and the expanded layout.</param>
/// <param name="Title">Body-weight-700 line, e.g. "Weekend Mix".</param>
/// <param name="Source">Tertiary line, e.g. "Spotify".</param>
public sealed record IslandActivity(string Key, IslandActivityKind Kind, string Title, string Source)
{
    /// <summary>0..1 for the gradient progress bar, or null for none.</summary>
    public double? Progress { get; init; }

    /// <summary>Held or failed: the shell shows the source in amber with an amber dot (DESIGN.md §5).</summary>
    public bool NeedsAttention { get; init; }

    /// <summary>
    /// The island tile this activity opens (<see cref="Modules.IslandTileSpec.Key"/>), or null for none.
    /// The tile shows a dot while the activity is live (Caution with <see cref="NeedsAttention"/>).
    /// Media takes the live orbit button below the island; without media, the first activity with a
    /// tile and <see cref="NeedsAttention"/> (a muted mic) does.
    /// </summary>
    public string? TileKey { get; init; }

    /// <summary>Set by the hub to the publishing module's id.</summary>
    public string ModuleId { get; init; } = "";
}

/// <summary>A module reported that it finished something for the user; the shell makes Gumdrop celebrate.</summary>
public sealed class ModuleSucceededEventArgs(string moduleId, string? message) : EventArgs
{
    public string ModuleId { get; } = moduleId;

    public string? Message { get; } = message;
}

/// <summary>A module's handle on the island. Obtained from <c>ModuleContext.Island</c>.</summary>
public interface IIslandPublisher
{
    /// <summary>Adds the activity, or replaces the one with the same <see cref="IslandActivity.Key"/>.</summary>
    void Publish(IslandActivity activity);

    void Clear(string key);

    /// <summary>Signals "done" (a timer finished, files stashed). Gumdrop celebrates for about 2.5s.</summary>
    void ReportSuccess(string? message = null);
}
