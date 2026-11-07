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

    /// <summary>
    /// A coding agent's sessions (AI agent usage): above everything but media while one waits on you
    /// (<see cref="IslandActivity.NeedsAttention"/>), below everything while they only work.
    /// </summary>
    Agent,

    /// <summary>Background state such as Keep awake. A timed session shows its countdown.</summary>
    Status,

    /// <summary>A screen recording running: above everything, media included (screen-recording.md, Bar).</summary>
    Recording,
}

public static class IslandActivityKindExtensions
{
    /// <summary>
    /// Lower wins: a recording, then media, then an agent waiting on you, then timer, then download or stash (equal), then
    /// status, then an agent working (DESIGN.md §5; Travis, 2026-11-01; screen-recording.md).
    /// </summary>
    public static int Priority(this IslandActivity activity) => activity.Kind switch
    {
        IslandActivityKind.Recording => -1,
        IslandActivityKind.Media => 0,
        IslandActivityKind.Agent when activity.NeedsAttention => 1,
        IslandActivityKind.Timer => 2,
        IslandActivityKind.Download or IslandActivityKind.Stash => 3,
        IslandActivityKind.Status => 4,
        IslandActivityKind.Agent => 5,
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
    /// <summary>0..1 for the gradient progress bar, or null for none. On feedback, the ring that counts down ("Capturing in 3").</summary>
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

    /// <summary>
    /// The tile page a click on the bar opens when the activity doesn't mark the tile with <see cref="TileKey"/>: an agent's
    /// session opens Agents, but one waiting on you doesn't turn the home's button Caution. The bar then shows the
    /// activity's own lines, <see cref="Title"/> over <see cref="Source"/>.
    /// </summary>
    public string? OpensTile { get; init; }

    /// <summary>The provider this activity represents, if any. The shell uses its mark and accessible name.</summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// A short-lived line (6 seconds) that shows over a song, as Plain paste's feedback does: an agent's "80% used". The
    /// publisher clears it.
    /// </summary>
    public bool IsFeedback { get; init; }

    /// <summary>Optional Lucide glyph for feedback without an island tile, or for a <see cref="IslandActivityKind.Status"/> in place of its dot.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// What a click on the bar does in place of opening a page, while this activity holds the bar ("Capturing in 3" cancels
    /// the capture). Runs on the UI thread.
    /// </summary>
    public Action? Clicked { get; init; }

    /// <summary>
    /// Feedback only: an encoded image (PNG or JPEG) that fills the bar's circle in place of <see cref="Icon"/>, as a
    /// song's art does: a capture's thumbnail on "Copied" and "Saved". Keep it small; the bar decodes it at 34.
    /// </summary>
    public byte[]? Picture { get; init; }

    /// <summary>Feedback only: a color, <c>#RRGGBB</c>, that fills the bar's circle in place of <see cref="Icon"/> ("#7C5CFF · Copied as HEX").</summary>
    public string? Swatch { get; init; }

    /// <summary>Set by the hub to the publishing module's id.</summary>
    public string ModuleId { get; init; } = "";
}

/// <summary>A module reported that it finished something for the user; the shell makes Chippi celebrate.</summary>
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

    /// <summary>Signals "done" (a timer finished, files stashed). Chippi celebrates for about 2.5s.</summary>
    void ReportSuccess(string? message = null);
}
