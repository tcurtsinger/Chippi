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

    /// <summary>The island's display out of the box. The one place to change it.</summary>
    public const IslandDisplay DefaultIslandDisplay = IslandDisplay.FollowActiveWindow;

    public ThemePreference Theme { get; set; } = ThemePreference.Dark;

    public bool IslandEnabled { get; set; } = true;

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
}
