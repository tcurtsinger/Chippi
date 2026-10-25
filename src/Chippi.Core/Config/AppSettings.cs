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
}
