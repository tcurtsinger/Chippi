// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.Core.Config;

namespace Chippi.App.Island;

/// <summary>A screen edge, including the bottom (where the island never docks, but the taskbar may be).</summary>
public enum ScreenEdge
{
    Top,
    Left,
    Right,
    Bottom,
}

/// <summary>
/// Where the island's tab and panel go (DESIGN.md §5 Island), in a monitor's physical pixels. Pure
/// arithmetic, so the snapping rules are unit-tested: the tab docks on the work area's Top, Left or
/// Right edge, never on the taskbar's edge and never in a corner, and its place along the edge is
/// stored as 0–1 so a resolution change doesn't move it.
/// </summary>
public static class IslandGeometry
{
    /// <summary>The tab stays this far (DIPs) from the ends of the top edge, clear of window buttons.</summary>
    public const double TopEndMargin = 160;

    /// <summary>The tab's center stays this far (DIPs) from the ends of a side edge: half the swollen live tab (68) plus 8, so all of it stays on screen.</summary>
    public const double SideEndMargin = 76;

    /// <summary>The open island stays this far (DIPs) inside the ends of its edge.</summary>
    public const double Gap = 8;

    /// <summary>
    /// The tab's size in DIPs on the top edge, turned on the sides: 96×28 at rest, 120×32 when live,
    /// and 220×32 while it names the song playing. Swollen (hovered, or the island open) it is 16px
    /// longer and 4px deeper: 112×32, 136×36 and 236×36.
    /// </summary>
    public static Size TabSize(IslandEdge edge, bool live, bool swollen = false, bool song = false)
    {
        var (along, across) = (song ? 2 : live ? 1 : 0, swollen) switch
        {
            (0, false) => (96.0, 28.0),
            (0, true) => (112.0, 32.0),
            (1, false) => (120.0, 32.0),
            (1, true) => (136.0, 36.0),
            (_, false) => (220.0, 32.0),
            (_, true) => (236.0, 36.0),
        };
        return edge == IslandEdge.Top ? new Size(along, across) : new Size(across, along);
    }

    /// <summary>The edge the taskbar takes from the monitor (where the work area is inset), or null when none is (auto-hide).</summary>
    public static ScreenEdge? TaskbarEdge(Rect monitor, Rect work)
    {
        if (work.Bottom < monitor.Bottom)
            return ScreenEdge.Bottom;
        if (work.Top > monitor.Top)
            return ScreenEdge.Top;
        if (work.Left > monitor.Left)
            return ScreenEdge.Left;
        if (work.Right < monitor.Right)
            return ScreenEdge.Right;
        return null;
    }

    /// <summary>
    /// The point on the work area's edge where the middle of the tab's flat side sits. A taskbar on
    /// the saved edge moves the tab to the top edge, so it never sits on the taskbar.
    /// </summary>
    public static Point Anchor(IslandEdge edge, double place, Rect monitor, Rect work, double scale)
    {
        if (Edge(edge) == TaskbarEdge(monitor, work))
            edge = IslandEdge.Top;
        place = Math.Clamp(double.IsFinite(place) ? place : 0.5, 0, 1);
        return edge switch
        {
            IslandEdge.Left => new Point(work.Left, Along(work.Top, work.Height, SideEndMargin * scale, place)),
            IslandEdge.Right => new Point(work.Right, Along(work.Top, work.Height, SideEndMargin * scale, place)),
            _ => new Point(Along(work.Left, work.Width, TopEndMargin * scale, place), work.Top),
        };
    }

    /// <summary>The edge the tab actually docks on: the saved one, unless the taskbar has taken it.</summary>
    public static IslandEdge Docked(IslandEdge edge, Rect monitor, Rect work) =>
        Edge(edge) == TaskbarEdge(monitor, work) ? IslandEdge.Top : edge;

    /// <summary>The tab's rectangle for an anchor: its flat side on the edge, centered along it.</summary>
    public static Rect TabRect(IslandEdge edge, Point anchor, Size size) => edge switch
    {
        IslandEdge.Left => new Rect(anchor.X, anchor.Y - size.Height / 2, size.Width, size.Height),
        IslandEdge.Right => new Rect(anchor.X - size.Width, anchor.Y - size.Height / 2, size.Width, size.Height),
        _ => new Rect(anchor.X - size.Width / 2, anchor.Y, size.Width, size.Height),
    };

    /// <summary>
    /// Where a drop at <paramref name="pointer"/> docks: only the top edge for now (side edges retired
    /// 2026-10-29), at the pointer's place along it. Null when the drop is refused: in the work area's
    /// lower half, or inside the top edge's end margins (a corner). A taskbar on top leaves the tab
    /// just under it.
    /// </summary>
    public static (IslandEdge Edge, double Place)? Snap(Point pointer, Rect work, double scale)
    {
        if (pointer.Y > work.Top + work.Height / 2)
            return null;
        double place = Fraction(pointer.X, work.Left, work.Width, TopEndMargin * scale);
        return place is < 0 or > 1 ? null : (IslandEdge.Top, place);
    }

    /// <summary>
    /// The open island's top-left: grown out of the tab, so its flat side is on the tab's edge,
    /// centered on the tab along that edge and kept 8px inside the edge's ends.
    /// </summary>
    public static Point PanelOrigin(IslandEdge edge, Rect tab, Size panel, Rect work, double scale)
    {
        double gap = Gap * scale;
        double x, y;
        switch (edge)
        {
            case IslandEdge.Left:
                x = tab.Left;
                y = tab.Top + tab.Height / 2 - panel.Height / 2;
                break;
            case IslandEdge.Right:
                x = tab.Right - panel.Width;
                y = tab.Top + tab.Height / 2 - panel.Height / 2;
                break;
            default:
                x = tab.Left + tab.Width / 2 - panel.Width / 2;
                y = tab.Top;
                break;
        }
        if (edge == IslandEdge.Top)
            x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - gap - panel.Width));
        else
            y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - gap - panel.Height));
        return new Point(x, y);
    }

    private static ScreenEdge Edge(IslandEdge edge) => edge switch
    {
        IslandEdge.Left => ScreenEdge.Left,
        IslandEdge.Right => ScreenEdge.Right,
        _ => ScreenEdge.Top,
    };

    private static double Along(double start, double length, double margin, double place)
    {
        double span = Math.Max(0, length - 2 * margin);
        return span == 0 ? start + length / 2 : start + margin + place * span;
    }

    private static double Fraction(double value, double start, double length, double margin)
    {
        double span = length - 2 * margin;
        return span <= 0 ? 0.5 : (value - start - margin) / span;
    }
}
