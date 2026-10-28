// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

/// <summary>Which round button beside the open island opens a tile directly (docs/phase-3/architecture.md §5).</summary>
public enum IslandOrbit
{
    None,
    Left,
    Right,
}

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
    /// <summary>Default page width in DIPs, the grid's.</summary>
    public const double DefaultPageWidth = 328;

    /// <summary>The page's width in DIPs. On a shared page the widest section's width wins.</summary>
    public double PageWidth { get; init; } = DefaultPageWidth;

    /// <summary>A round button beside the open island that opens this tile.</summary>
    public IslandOrbit Orbit { get; init; } = IslandOrbit.None;

    /// <summary>On a shared tile, the status of the module with the highest priority that set one shows ("Mic muted" over the output).</summary>
    public int StatusPriority { get; init; }

    /// <summary>
    /// On a page of side-by-side columns (Sound's Output and Microphone), the column this section
    /// stacks in, under that name. Columns sit in the order of their first section. Null for a page
    /// of one column.
    /// </summary>
    public string? Column { get; init; }

    /// <summary>The page's header repeats the tile's status, for a page whose controls don't show it (Keep awake's time left).</summary>
    public bool StatusOnPage { get; init; }

    /// <summary>Optional page-owned header. The first header on a shared page replaces the shell header.</summary>
    public Func<object>? CreateHeader { get; init; }

    /// <summary>The page control to focus after taking the keyboard, never during hover or a drag.</summary>
    public Func<object?>? InitialFocus { get; init; }
}

/// <summary>A module's handle on its island tile.</summary>
public interface IIslandTile
{
    /// <summary>The tile's status line, such as "Muted" or "1 hr left"; empty for none.</summary>
    void SetStatus(string status);

    /// <summary>
    /// Text at the right of the page's header instead of the tile's status, for a page that already
    /// shows its status (the System monitor's "Last 4 min"); empty for none.
    /// </summary>
    void SetPageNote(string note);

    /// <summary>Whether the tile is on screen: the grid is open and the tile isn't hidden. Its page doesn't count.</summary>
    bool OnScreen { get; }

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
    /// its name and icon from its first part, and the widest part's page width.
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

    public double PageWidth => Parts.Max(p => p.Spec.PageWidth);

    public IslandOrbit Orbit => Parts.Select(p => p.Spec.Orbit).FirstOrDefault(o => o != IslandOrbit.None);

    public bool StatusOnPage => Parts.Any(p => p.Spec.StatusOnPage);

    /// <summary>The first part's non-empty page note, or empty.</summary>
    public string PageNote => Parts.Select(p => p.PageNote).FirstOrDefault(n => n.Length > 0) ?? "";

    /// <summary>The highest-priority non-empty status, or empty.</summary>
    public string Status =>
        Parts.Where(p => p.Status.Length > 0).OrderByDescending(p => p.Spec.StatusPriority).FirstOrDefault()?.Status ?? "";
}
