// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Media;

namespace Chippi.Features.RadialMenu.Wheel;

/// <summary>
/// The wheel's sizes (docs/phase-5.5/radial-menu.md, The wheel) and its angles: degrees clockwise from straight up, so 0
/// is the top slice and 90 the right one.
/// </summary>
public static class WheelGeometry
{
    /// <summary>The disc, 248 across, and its middle.</summary>
    public const double Diameter = 248, Middle = 124;

    /// <summary>The hub's 96 across; the slices are the ring from 54 to 124 out; glyphs sit 89 out.</summary>
    public const double HubRadius = 48, RingInner = 54, RingOuter = 124, GlyphRadius = 89;

    /// <summary>A submenu's chevron sits 11 inside the ring's edge.</summary>
    public const double ChevronRadius = RingOuter - 11;

    /// <summary>A submenu's wheel opens with its middle this far out in its slice's direction: 32 past the ring.</summary>
    public const double SubmenuReach = 156;

    /// <summary>A wheel left behind for a submenu shrinks to a hub this size.</summary>
    public const double CrumbSize = 40;

    /// <summary>Kept this far inside its screen's edges.</summary>
    public const double EdgeMargin = 8;

    /// <summary>The pointed slice's tint sits this far inside its hairlines and the ring's edges, with corners this round.</summary>
    public const double TintInset = 3;

    public static double Normalize(double degrees) => ((degrees % 360) + 360) % 360;

    /// <summary>The direction of an angle, on screen (y down): 0 is (0, -1).</summary>
    public static Vector Direction(double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Vector(Math.Sin(radians), -Math.Cos(radians));
    }

    public static Point At(Point middle, double radius, double degrees) => middle + radius * Direction(degrees);

    /// <summary>The angle from the middle toward <paramref name="offset"/>.</summary>
    public static double AngleOf(Vector offset) => Normalize(Math.Atan2(offset.X, -offset.Y) * 180 / Math.PI);

    /// <summary>
    /// Which of <paramref name="count"/> slices, spread evenly clockwise from <paramref name="first"/>, lies toward
    /// <paramref name="offset"/>.
    /// </summary>
    public static int SliceToward(Vector offset, int count, double first) =>
        (int)Math.Round(Normalize(AngleOf(offset) - first) / (360.0 / count)) % count;

    /// <summary>
    /// A slice's tint: its sector drawn 6 inside the hairlines and the ring's edges, grown by 3 all round with round joins,
    /// so it sits 3 inside with 3 corners. One shape to fill, so a see-through tint has no darker edge. A lone slice's tint
    /// is the whole ring.
    /// </summary>
    public static Geometry Tint(Point middle, double degrees, double span)
    {
        const double inset = 2 * TintInset;
        double outer = RingOuter - inset, inner = RingInner + inset;
        if (span >= 359)
        {
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { new EllipseGeometry(middle, outer, outer), new EllipseGeometry(middle, inner, inner) } };
            ring.Freeze();
            return ring;
        }
        double outerTrim = inset / outer * 180 / Math.PI, innerTrim = inset / inner * 180 / Math.PI;
        double from = degrees - span / 2, to = degrees + span / 2;
        var figure = new PathFigure { StartPoint = At(middle, outer, from + outerTrim), IsClosed = true };
        figure.Segments.Add(new ArcSegment(At(middle, outer, to - outerTrim), new Size(outer, outer), 0, span - 2 * outerTrim > 180, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(middle, inner, to - innerTrim), true));
        figure.Segments.Add(new ArcSegment(At(middle, inner, from + innerTrim), new Size(inner, inner), 0, span - 2 * innerTrim > 180, SweepDirection.Counterclockwise, true));
        var sector = new PathGeometry { Figures = { figure } };
        var rounded = Geometry.Combine(sector, sector.GetWidenedPathGeometry(new Pen(Brushes.Black, inset) { LineJoin = PenLineJoin.Round }), GeometryCombineMode.Union, null);
        rounded.Freeze();
        return rounded;
    }
}
