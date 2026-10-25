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

/// <summary>The round buttons around the open island (DESIGN.md §5 Island).</summary>
public enum OrbitSlot
{
    /// <summary>All features on a page; Back to the remembered page on the grid.</summary>
    Back,

    /// <summary>The tile with <c>IslandOrbit.Left</c> (Keep awake).</summary>
    Left,

    Settings,

    /// <summary>The tile with <c>IslandOrbit.Right</c> (Volume mixer).</summary>
    Right,

    /// <summary>The live activity's tile, below the island.</summary>
    Live,
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
    /// The tab's size in DIPs on the top edge, turned on the sides: 96×28 at rest and 120×32 when
    /// live. Swollen (hovered, or the island open) it is 16px longer and 4px deeper: 112×32 and 136×36.
    /// </summary>
    public static Size TabSize(IslandEdge edge, bool live, bool swollen = false)
    {
        var (along, across) = (live, swollen) switch
        {
            (false, false) => (96.0, 28.0),
            (false, true) => (112.0, 32.0),
            (true, false) => (120.0, 32.0),
            (true, true) => (136.0, 36.0),
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
    /// Where a drop at <paramref name="pointer"/> docks: the nearest edge of the work area and the
    /// place along it, or null when the drop is refused: nearest to the bottom or to the taskbar's
    /// edge, or inside an edge's end margin (a corner).
    /// </summary>
    public static (IslandEdge Edge, double Place)? Snap(Point pointer, Rect monitor, Rect work, double scale)
    {
        var distances = new (ScreenEdge Edge, double Distance)[]
        {
            (ScreenEdge.Top, Math.Abs(pointer.Y - work.Top)),
            (ScreenEdge.Left, Math.Abs(pointer.X - work.Left)),
            (ScreenEdge.Right, Math.Abs(work.Right - pointer.X)),
            (ScreenEdge.Bottom, Math.Abs(work.Bottom - pointer.Y)),
        };
        var nearest = distances.MinBy(d => d.Distance).Edge;
        if (nearest == ScreenEdge.Bottom || nearest == TaskbarEdge(monitor, work))
            return null;
        double place = nearest == ScreenEdge.Top
            ? Fraction(pointer.X, work.Left, work.Width, TopEndMargin * scale)
            : Fraction(pointer.Y, work.Top, work.Height, SideEndMargin * scale);
        if (place is < 0 or > 1)
            return null;
        return (nearest switch { ScreenEdge.Left => IslandEdge.Left, ScreenEdge.Right => IslandEdge.Right, _ => IslandEdge.Top }, place);
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

    /// <summary>An orbit button's diameter (DIPs).</summary>
    public const double OrbitSize = 40;

    /// <summary>The gap between the shape and its orbit buttons, and from the shape's top to the first one (DIPs).</summary>
    public const double OrbitGap = 12;

    /// <summary>From one orbit button's top to the next one's: 40 plus the 8px gap (DIPs).</summary>
    public const double OrbitStep = 48;

    /// <summary>
    /// The orbit buttons around an open shape (DIPs, any origin), DESIGN.md §5 Island. On the top edge
    /// <see cref="OrbitSlot.Back"/> sits 12px from the shape's top on the left with
    /// <see cref="OrbitSlot.Left"/> under it, and Settings and <see cref="OrbitSlot.Right"/> likewise on
    /// the right. On a side edge all four stack on the open side in that order; a missing feature
    /// closes up its column. Back's place is always kept, so nothing moves when it appears on a page.
    /// <see cref="OrbitSlot.Live"/> is centered below.
    /// </summary>
    public static IReadOnlyList<(OrbitSlot Slot, Rect Rect)> OrbitLayout(IslandEdge edge, Rect shape, IReadOnlyCollection<OrbitSlot> slots)
    {
        var placed = new List<(OrbitSlot, Rect)>();
        Rect At(double x, double row) => new(x, shape.Top + OrbitGap + row * OrbitStep, OrbitSize, OrbitSize);
        void Column(double x, OrbitSlot[] order)
        {
            int row = 0;
            foreach (var slot in order)
            {
                if (slots.Contains(slot))
                    placed.Add((slot, At(x, row)));
                if (slots.Contains(slot) || slot == OrbitSlot.Back)
                    row++;
            }
        }
        if (edge == IslandEdge.Top)
        {
            Column(shape.Left - OrbitGap - OrbitSize, [OrbitSlot.Back, OrbitSlot.Left]);
            Column(shape.Right + OrbitGap, [OrbitSlot.Settings, OrbitSlot.Right]);
        }
        else
        {
            Column(edge == IslandEdge.Left ? shape.Right + OrbitGap : shape.Left - OrbitGap - OrbitSize,
                [OrbitSlot.Back, OrbitSlot.Left, OrbitSlot.Settings, OrbitSlot.Right]);
        }
        if (slots.Contains(OrbitSlot.Live))
            placed.Add((OrbitSlot.Live, new Rect(shape.Left + shape.Width / 2 - OrbitSize / 2, shape.Bottom + OrbitGap, OrbitSize, OrbitSize)));
        return placed;
    }

    /// <summary>How far the orbit buttons reach beyond a shape of <paramref name="shape"/> size on each side (DIPs).</summary>
    public static Thickness OrbitReach(IslandEdge edge, Size shape, IReadOnlyCollection<OrbitSlot> slots)
    {
        var box = new Rect(shape);
        foreach (var (_, rect) in OrbitLayout(edge, new Rect(shape), slots))
            box.Union(rect);
        return new Thickness(-box.Left, -box.Top, box.Right - shape.Width, box.Bottom - shape.Height);
    }

    /// <summary>
    /// <see cref="PanelOrigin"/> for a shape whose orbit buttons reach <paramref name="reach"/> (DIPs)
    /// beyond it: the whole interactive footprint stays 8px inside the work area along the edge.
    /// </summary>
    public static Point PanelOrigin(IslandEdge edge, Rect tab, Size panel, Rect work, double scale, Thickness reach)
    {
        var inner = new Rect(work.Left + reach.Left * scale, work.Top + reach.Top * scale,
            Math.Max(0, work.Width - (reach.Left + reach.Right) * scale), Math.Max(0, work.Height - (reach.Top + reach.Bottom) * scale));
        var origin = PanelOrigin(edge, tab, panel, inner, scale);
        // The flat side stays on the edge whatever the buttons need.
        return edge switch
        {
            IslandEdge.Left => new Point(tab.Left, origin.Y),
            IslandEdge.Right => new Point(tab.Right - panel.Width, origin.Y),
            _ => new Point(origin.X, tab.Top),
        };
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
