// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;

namespace Chippi.App.Island;

/// <summary>
/// Where the widget and its panel go (DESIGN.md §5), in a monitor's physical pixels. Pure arithmetic,
/// so the rules are unit-tested: the puck sits anywhere inside the work area, its place saved as
/// fractions of the room it has there (so a resolution change doesn't move it off screen), and the
/// panel opens beside it toward the work area's middle, kept 8px inside.
/// </summary>
public static class IslandGeometry
{
    /// <summary>The puck's diameter in DIPs.</summary>
    public const double Puck = 50;

    /// <summary>Where the puck starts: this far (DIPs) in from the work area's bottom-right corner.</summary>
    public const double CornerInset = 24;

    /// <summary>The open panel stays this far (DIPs) inside the work area.</summary>
    public const double Gap = 8;

    /// <summary>The panel's edge nearest the puck sits this far (DIPs) in from the puck's far side, so the puck sits on its corner.</summary>
    public const double PanelOverlap = 20;

    /// <summary>
    /// The puck's rectangle for a saved place: <paramref name="x"/> and <paramref name="y"/> are 0–1
    /// across the room the puck has in the work area (0 is flush left or top). Null, or a bad value,
    /// is the start: 24 in from the bottom-right corner.
    /// </summary>
    public static Rect PuckRect(double? x, double? y, Rect work, double scale)
    {
        double size = Puck * scale;
        if (x is not { } fx || y is not { } fy || !double.IsFinite(fx) || !double.IsFinite(fy))
            return Start(work, scale);
        double roomX = Math.Max(0, work.Width - size), roomY = Math.Max(0, work.Height - size);
        return new Rect(work.Left + Math.Clamp(fx, 0, 1) * roomX, work.Top + Math.Clamp(fy, 0, 1) * roomY, size, size);
    }

    /// <summary>The puck where it starts: 24 in from the work area's bottom-right corner.</summary>
    public static Rect Start(Rect work, double scale)
    {
        double size = Puck * scale, inset = CornerInset * scale;
        return Inside(new Rect(work.Right - inset - size, work.Bottom - inset - size, size, size), work);
    }

    /// <summary>The fractions to save for a puck at <paramref name="puck"/> (inside the work area).</summary>
    public static (double X, double Y) Fractions(Rect puck, Rect work)
    {
        double roomX = work.Width - puck.Width, roomY = work.Height - puck.Height;
        return (roomX <= 0 ? 0 : Math.Clamp((puck.Left - work.Left) / roomX, 0, 1),
                roomY <= 0 ? 0 : Math.Clamp((puck.Top - work.Top) / roomY, 0, 1));
    }

    /// <summary>The puck moved the least distance that puts all of it inside the work area.</summary>
    public static Rect Inside(Rect puck, Rect work) =>
        new(Math.Clamp(puck.Left, work.Left, Math.Max(work.Left, work.Right - puck.Width)),
            Math.Clamp(puck.Top, work.Top, Math.Max(work.Top, work.Bottom - puck.Height)), puck.Width, puck.Height);

    /// <summary>Whether the panel and the chips go left of the puck: it sits right of the work area's middle.</summary>
    public static bool OpensLeft(Rect puck, Rect work) => puck.Left + puck.Width / 2 > work.Left + work.Width / 2;

    /// <summary>Whether the panel goes above the puck: it sits below the work area's middle.</summary>
    public static bool OpensUp(Rect puck, Rect work) => puck.Top + puck.Height / 2 > work.Top + work.Height / 2;

    /// <summary>
    /// The open panel's top-left: beside the puck toward the work area's middle, its near edge
    /// level with the puck's side and 20 in from the puck's far side, so the puck sits on its corner,
    /// then kept 8px inside the work area.
    /// </summary>
    public static Point PanelOrigin(Rect puck, Size panel, Rect work, double scale)
    {
        double gap = Gap * scale, overlap = PanelOverlap * scale;
        double x = OpensLeft(puck, work) ? puck.Left - panel.Width : puck.Right;
        double y = OpensUp(puck, work) ? puck.Bottom - overlap - panel.Height : puck.Top + overlap;
        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - gap - panel.Width));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - gap - panel.Height));
        return new Point(x, y);
    }
}
