// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

/// <summary>
/// The open island's one size, for the grid and every page (DESIGN.md §5 Island, revised 2026-10-28),
/// in DIPs. Inside the 12 inset and the 24 + 8 header each page has a 496 × 304 body of six 76
/// columns with 8 gaps. Pages fit their content to the body; nothing scrolls.
/// </summary>
public static class IslandPage
{
    public const double Width = 520;
    public const double Height = 360;
    public const double Inset = 12;
    public const double HeaderHeight = 24;
    public const double Gap = 8;
    public const double BodyWidth = Width - 2 * Inset; // 496
    public const double BodyHeight = Height - 2 * Inset - HeaderHeight - Gap; // 304
    public const int Columns = 6;
    public const double ColumnWidth = 76;

    /// <summary>The width of <paramref name="columns"/> body columns and the gaps between them.</summary>
    public static double Span(int columns) => columns * ColumnWidth + (columns - 1) * Gap;
}

/// <summary>
/// A tile's button on the home that acts without opening the page (Keep awake's start and stop, the
/// microphone's mute). The home draws the button split: the toggle, and an arrow that opens the page.
/// </summary>
/// <param name="IsOn">Whether it's on (Keep awake running, the microphone muted). Read each time the home refreshes; the
/// home draws it in the accent fill unless the tile needs attention (a muted mic shows in Caution instead).</param>
/// <param name="Toggle">Starts or stops it. Called on the UI thread; when it fails (throws or faults), the home says "Failed" under the button.</param>
/// <param name="Name">The button's name when it isn't the tile's (the Sound tile's mute is "Microphone").</param>
/// <param name="Icon">Lucide icon key when it isn't the tile's.</param>
/// <param name="Status">The line under the button when it isn't the tile's status (the microphone's "Live").</param>
public sealed record IslandTileToggle(Func<bool> IsOn, Func<Task> Toggle, string? Name = null, string? Icon = null, Func<string>? Status = null);

/// <summary>
/// The home's volume slider, offered by the tile that owns the system volume (Sound). Its arrow opens
/// that tile's page.
/// </summary>
/// <param name="Level">The volume, 0 to 1, or null when there is no output.</param>
/// <param name="Set">Sets the volume, 0 to 1. Called on the UI thread for every step of a drag, so it coalesces.</param>
/// <param name="Muted">Whether the output is muted; null where the tile can't tell (the home then never shows it muted).</param>
/// <param name="SetMuted">Mutes or unmutes the output; null where the tile can't (the speaker is then just a glyph).</param>
public sealed record IslandTileVolume(Func<double?> Level, Action<double> Set, Func<bool>? Muted = null, Func<bool, Task>? SetMuted = null);

/// <summary>
/// One module's part of an island tile (docs/phase-3/architecture.md §5).
/// </summary>
/// <param name="Key">Identifies the tile, such as "sound". Modules that use the same key share the tile and its page.</param>
/// <param name="Name">The tile's name, such as "Microphone". A shared tile takes the name and icon of its first section.</param>
/// <param name="Icon">Lucide icon key, as in <see cref="FeatureDescriptor.Icon"/>.</param>
/// <param name="Order">Where the tile sits in the grid, lowest first. Now Playing is always first.</param>
/// <param name="SectionOrder">Where this module's section sits on a shared page, lowest first.</param>
/// <param name="CreateSection">This module's section of the page. Must return a WPF FrameworkElement.</param>
public sealed record IslandTileSpec(string Key, string Name, string Icon, int Order, int SectionOrder, Func<object> CreateSection)
{
    /// <summary>The home's toggle for this tile; null for a button that only opens the page.</summary>
    public IslandTileToggle? Toggle { get; init; }

    /// <summary>The home's volume slider; null for every tile but the one that owns the system volume.</summary>
    public IslandTileVolume? Volume { get; init; }

    /// <summary>On a shared tile, the status of the module with the highest priority that set one shows ("Mic muted" over the output).</summary>
    public int StatusPriority { get; init; }

    /// <summary>
    /// On a page of side-by-side columns (Sound's Output and Microphone), the column this section
    /// stacks in, under that name. Columns sit in the order of their first section. Null for a page
    /// of one column.
    /// </summary>
    public string? Column { get; init; }

    /// <summary>On a page of columns, this section sits under all of them, full width, instead of in its column (Sound's Mic processing).</summary>
    public bool SpansColumns { get; init; }

    /// <summary>The page's header repeats the tile's status, for a page whose controls don't show it (Keep awake's time left).</summary>
    public bool StatusOnPage { get; init; }

    /// <summary>Optional page-owned header. The first header on a shared page replaces the shell header.</summary>
    public Func<object>? CreateHeader { get; init; }

    /// <summary>The page control to focus after taking the keyboard, never during hover or a drag.</summary>
    public Func<object?>? InitialFocus { get; init; }

    /// <summary>
    /// The page sees Esc first, from the island and the global Esc alike: true when it used it (Clipboard
    /// clears its search or leaves Clean URL), and the island stays open. Null for the usual close.
    /// </summary>
    public Func<bool>? HandleEscape { get; init; }

    /// <summary>
    /// The page sees its Back arrow first: true when it used it (System's detail page returns to its
    /// overview), and the page stays. Null for the usual return to the home.
    /// </summary>
    public Func<bool>? HandleBack { get; init; }

    /// <summary>
    /// A drag over the closed tab opens this tile's page after 250 ms, without the keyboard, and the tab
    /// shows the tile's glyph meanwhile (the Shelf, Phase 5 notes #48). The tab itself never takes the drop.
    /// </summary>
    public bool OpensOnDrag { get; init; }
}

/// <summary>A module's handle on its island tile.</summary>
public interface IIslandTile
{
    /// <summary>The tile's status line, such as "Muted" or "1 hr left"; empty for none.</summary>
    void SetStatus(string status);

    /// <summary>
    /// Text at the right of the page's header instead of the tile's status, for a page that already
    /// shows its status; empty for none.
    /// </summary>
    void SetPageNote(string note);

    /// <summary>Whether the tile is on screen: the grid is open and the tile isn't hidden. Its page doesn't count.</summary>
    bool OnScreen { get; }

    /// <summary>Asks the home to read the tile's toggle and volume again (they changed without the status changing).</summary>
    void Refresh();

    /// <summary>Raised on the UI thread when <see cref="OnScreen"/> changes, so a status that costs something to keep current runs only while seen.</summary>
    event EventHandler? OnScreenChanged;
}

/// <summary>A running module's tile part, as the island host reads it.</summary>
public sealed class IslandTileContribution(string moduleId, IslandTileSpec spec) : IIslandTile
{
    private string _status = "";
    private string _pageNote = "";
    private volatile bool _onScreen;

    public string ModuleId { get; } = moduleId;

    public IslandTileSpec Spec { get; } = spec;

    public string Status => Volatile.Read(ref _status);

    public string PageNote => Volatile.Read(ref _pageNote);

    public bool OnScreen => _onScreen;

    /// <summary>Raised on the thread that set the status or the page note.</summary>
    public event EventHandler? StatusChanged;

    public event EventHandler? OnScreenChanged;

    public void SetStatus(string status)
    {
        status ??= "";
        if (Interlocked.Exchange(ref _status, status) != status)
            StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public void SetPageNote(string note)
    {
        note ??= "";
        if (Interlocked.Exchange(ref _pageNote, note) != note)
            StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The island host reports whether the tile is on screen. Call on the UI thread.</summary>
    public void SetOnScreen(bool onScreen)
    {
        if (_onScreen == onScreen)
            return;
        _onScreen = onScreen;
        OnScreenChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Groups tile parts by key into tiles in grid order. A tile's parts are in section order, it takes
    /// its name and icon from its first part.
    /// </summary>
    public static IReadOnlyList<IslandTileGroup> Group(IEnumerable<IslandTileContribution> parts) =>
        [.. parts
            .GroupBy(p => p.Spec.Key, StringComparer.Ordinal)
            .Select(g => new IslandTileGroup(g.Key, [.. g.OrderBy(p => p.Spec.SectionOrder)]))
            .OrderBy(t => t.Order)];
}

/// <summary>One tile in the grid: the parts that share its key, in section order.</summary>
public sealed record IslandTileGroup(string Key, IReadOnlyList<IslandTileContribution> Parts)
{
    public string Name => Parts[0].Spec.Name;

    public string Icon => Parts[0].Spec.Icon;

    public int Order => Parts.Min(p => p.Spec.Order);

    /// <summary>The first part's toggle, or null.</summary>
    public IslandTileToggle? Toggle => Parts.Select(p => p.Spec.Toggle).FirstOrDefault(t => t is not null);

    /// <summary>The first part's volume slider, or null.</summary>
    public IslandTileVolume? Volume => Parts.Select(p => p.Spec.Volume).FirstOrDefault(v => v is not null);

    public bool StatusOnPage => Parts.Any(p => p.Spec.StatusOnPage);

    /// <summary>The first part's non-empty page note, or empty.</summary>
    public string PageNote => Parts.Select(p => p.PageNote).FirstOrDefault(n => n.Length > 0) ?? "";

    /// <summary>The highest-priority non-empty status, or empty.</summary>
    public string Status =>
        Parts.Where(p => p.Status.Length > 0).OrderByDescending(p => p.Spec.StatusPriority).FirstOrDefault()?.Status ?? "";
}
