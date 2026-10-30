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

/// <summary>Chippi's body color (DESIGN.md §7). Synth is the default.</summary>
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
    /// <summary>Retired 2026-10-29 (a widget you place yourself stays put); a saved one reads as <see cref="MainDisplay"/>.</summary>
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

    /// <summary>
    /// Where the widget sits on its display, 0 to 1 across the room it has in the work area (0 is flush
    /// left), so a resolution change never moves it off screen. Null until it is dragged: then it
    /// sits 24 in from the bottom-right corner.
    /// </summary>
    public double? IslandX { get; set; }

    /// <summary>Like <see cref="IslandX"/>, top to bottom.</summary>
    public double? IslandY { get; set; }

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

    /// <summary>Tile keys left off the grid. Their pages stay.</summary>
    public List<string>? IslandHiddenTiles { get; set; }

    /// <summary>Where the Settings window was and the page it showed when it last closed, which the next one reopens at; null until then.</summary>
    public WindowPlacement? SettingsWindow { get; set; }
}

/// <summary>A window's restored bounds in DIPs, whether it was maximized, and the page it showed.</summary>
public sealed class WindowPlacement
{
    /// <summary>The page id (a Settings route such as "about" or "module:shelf"); empty or null for none.</summary>
    public string? Page { get; set; }

    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }
}
