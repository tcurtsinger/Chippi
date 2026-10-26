// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Config;

public enum ThemePreference
{
    Dark,
    Light,

    /// <summary>"Follow Windows": tracks the system app theme.</summary>
    System,
}

/// <summary>The accent color (DESIGN.md §2). Each named choice has a dark and a light shade.</summary>
public enum AccentChoice
{
    /// <summary>The Windows accent: its AccentLight2 shade in dark, AccentDark1 in light.</summary>
    FollowWindows,
    Blue,
    Violet,
    Green,
    Pink,
}

/// <summary>The screen edge the island tab docks on (DESIGN.md §5 Island).</summary>
public enum IslandEdge
{
    Top,
    Left,
    Right,
}

/// <summary>Gumdrop's body color (DESIGN.md §7). Synth is the gradient and the default.</summary>
public enum PetColor
{
    Synth,
    Iris,
    Azure,
    Signal,
    Ember,
    Frost,
}

/// <summary>Which display the island sits on.</summary>
public enum IslandDisplay
{
    /// <summary>The display with the active window; the island moves as focus moves (DESIGN.md §5).</summary>
    FollowActiveWindow,

    /// <summary>Always the Windows main display.</summary>
    MainDisplay,

    /// <summary>One chosen monitor (<see cref="AppSettings.IslandMonitorId"/>); the main display while it is disconnected.</summary>
    SpecificMonitor,
}

/// <summary>The page the island opens on (DESIGN.md §5 Island).</summary>
public enum IslandOpensOn
{
    /// <summary>The page last used, while its tile exists; otherwise the grid.</summary>
    LastPage,

    /// <summary>Always the grid of all features.</summary>
    AllFeatures,
}

/// <summary>Shell settings, the "app" section of settings.json.</summary>
public sealed class AppSettings
{
    public const string SectionName = "app";

    /// <summary>The island's display out of the box (Travis chose Main display, 2026-10-22). The one place to change it.</summary>
    public const IslandDisplay DefaultIslandDisplay = IslandDisplay.MainDisplay;

    /// <summary>Follow Windows out of the box (2026-10-23). A saved choice is kept.</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public AccentChoice Accent { get; set; } = AccentChoice.FollowWindows;

    public bool IslandEnabled { get; set; } = true;

    /// <summary>Settings › Island &amp; pet: no tray icon while the island shows. It comes back while the island is off or hidden for a fullscreen app.</summary>
    public bool HideTrayIcon { get; set; }

    public IslandEdge IslandEdge { get; set; } = IslandEdge.Top;

    /// <summary>Where the tab sits along <see cref="IslandEdge"/>, 0 to 1 (0.5 is centered), so resolution changes don't move it.</summary>
    public double IslandPlace { get; set; } = 0.5;

    /// <summary>Settings › Notifications master switch: off silences every Windows notification; each event's choice is kept.</summary>
    public bool WindowsNotices { get; set; } = true;

    /// <summary>Settings › Notifications master switch: off silences island notices only (the island and its live activities stay).</summary>
    public bool IslandNotices { get; set; } = true;

    public PetColor PetColor { get; set; } = PetColor.Synth;

    /// <summary>Null follows the Windows "Animation effects" setting; true or false overrides it.</summary>
    public bool? ReduceMotion { get; set; }

    public IslandDisplay IslandDisplay { get; set; } = DefaultIslandDisplay;

    /// <summary>
    /// For <see cref="IslandDisplay.SpecificMonitor"/>: the monitor's device path from Windows
    /// display configuration (e.g. <c>\\?\DISPLAY#DEL41A8#…</c>). Unlike <c>\\.\DISPLAYn</c> it does
    /// not renumber when monitors are added, removed or rearranged. Kept when another mode is
    /// chosen, so switching back remembers the monitor.
    /// </summary>
    public string? IslandMonitorId { get; set; }

    /// <summary>The island page last opened (a tile key), which the next open restores while that tile exists; null for the grid.</summary>
    public string? IslandLastPage { get; set; }

    /// <summary>Settings › Island &amp; pet › Opens on.</summary>
    public IslandOpensOn IslandOpensOn { get; set; } = IslandOpensOn.LastPage;

    /// <summary>Settings › Island &amp; pet › Tiles: tile keys in the grid's order. Tiles not listed follow in their own order.</summary>
    public List<string>? IslandTileOrder { get; set; }

    /// <summary>Tile keys left off the grid. Their pages stay, for a round button.</summary>
    public List<string>? IslandHiddenTiles { get; set; }

    /// <summary>The round button beside the island's left (top edge) or upper (side edge) pair: a tile key, "" for none, or null for its feature's own (Keep awake).</summary>
    public string? IslandLeftButton { get; set; }

    /// <summary>Likewise for the right or lower pair; null is its feature's own (App volume).</summary>
    public string? IslandRightButton { get; set; }

    /// <summary>Settings › Island &amp; pet › Live button: the round button for music or a muted mic.</summary>
    public bool IslandLiveButton { get; set; } = true;
}
