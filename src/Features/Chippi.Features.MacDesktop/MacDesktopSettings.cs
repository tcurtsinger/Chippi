// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.MacDesktop;

/// <summary>Settings › Mac-style desktop › Desktop style.</summary>
public enum DesktopStyle { Windows, MacStyle }

/// <summary>When the dock hides: never (it keeps its space), when the pointer leaves the bottom, or when a window touches it.</summary>
public enum DockHiding { AlwaysShown, AutoHide, HideWhenTouched }

/// <summary>The edge of the main display the dock sits on.</summary>
public enum DockEdge { Bottom, Left, Right }

/// <summary>The feature's section in settings.json (<c>modules.mac-desktop</c>). See docs/mac-style/brief.md.</summary>
public sealed record MacDesktopSettings
{
    /// <summary>Windows until the user picks Mac-style: installing the feature (Everything, Power user) changes nothing by itself.</summary>
    public DesktopStyle Style { get; init; } = DesktopStyle.Windows;

    public bool MenuBar { get; init; } = true;
    public bool Dock { get; init; } = true;

    /// <summary>The island in the middle of the main display's menu bar. It needs the menu bar.</summary>
    public bool Island { get; init; } = true;

    public DockHiding DockHide { get; init; } = DockHiding.AlwaysShown;
    public DockEdge DockPosition { get; init; } = DockEdge.Bottom;
    public bool Magnify { get; init; } = true;

    /// <summary>The size icons near the pointer grow to, in DIPs (48 at rest).</summary>
    public int MagnifiedSize { get; init; } = 72;

    /// <summary>
    /// The menu bar's items, left to right (<see cref="Chippi.Features.MacDesktop.MenuBar.BarItems"/> ids); the clock comes after them. Null until
    /// the user changes them: the start set then.
    /// </summary>
    public IReadOnlyList<string>? BarItems { get; init; }

    /// <summary>
    /// The dock's pinned apps, in order (a program's path or a Start menu parsing name). Null until the first start, which
    /// brings the taskbar's pins over.
    /// </summary>
    public IReadOnlyList<string>? Pins { get; init; }

    public const int MinMagnified = 56, MaxMagnified = 96;

    public void Validate()
    {
        if (!Enum.IsDefined(Style) || !Enum.IsDefined(DockHide) || !Enum.IsDefined(DockPosition)
            || MagnifiedSize is < MinMagnified or > MaxMagnified)
            throw new ArgumentException("Invalid Mac-style desktop settings");
    }

    public bool IsMac => Style == DesktopStyle.MacStyle;

    /// <summary>The taskbar hides while the menu bar or the dock stands in for it.</summary>
    public bool HidesTaskbar => IsMac && (MenuBar || Dock);

    /// <summary>The island takes the widget's place: the mode is on with its menu bar and its Island switch.</summary>
    public bool ShowsIsland => IsMac && MenuBar && Island;

    public bool Equals(MacDesktopSettings? other) => other is not null
        && Style == other.Style && MenuBar == other.MenuBar && Dock == other.Dock && Island == other.Island
        && DockHide == other.DockHide && DockPosition == other.DockPosition && Magnify == other.Magnify
        && MagnifiedSize == other.MagnifiedSize
        && (BarItems ?? []).SequenceEqual(other.BarItems ?? []) && (BarItems is null) == (other.BarItems is null)
        && (Pins ?? []).SequenceEqual(other.Pins ?? []) && (Pins is null) == (other.Pins is null);

    public override int GetHashCode() => HashCode.Combine(Style, MenuBar, Dock, Island, DockHide, DockPosition, MagnifiedSize, Pins?.Count);
}
