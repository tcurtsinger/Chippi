// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chippi.App.Island;

/// <summary>The pet and its status tab share a fill, rim and hit contour. The bar and open panel are ordinary rounded rectangles.</summary>
public sealed class PuckSurface : Border
{
    private bool _opensLeft;
    private Geometry? _outer;
    private Size _size;
    private double _radius;

    internal double TabExtension { get; private set; }

    internal void SetTab(double extension, bool opensLeft)
    {
        if (TabExtension == extension && _opensLeft == opensLeft)
            return;
        TabExtension = extension;
        _opensLeft = opensLeft;
        _outer = null;
        InvalidateVisual();
    }

    internal bool ContainsLocal(Point point)
    {
        if (_outer is null || _size != RenderSize || _radius != CornerRadius.TopLeft)
        {
            _size = RenderSize;
            _radius = CornerRadius.TopLeft;
            _outer = Outline(0);
        }
        return _outer.FillContains(point);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        ContainsLocal(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    protected override void OnRender(DrawingContext dc)
    {
        if (TabExtension <= 0 || Reach() <= 0)
        {
            base.OnRender(dc);
            return;
        }

        double stroke = BorderBrush is null ? 0 : BorderThickness.Left;
        var pen = stroke > 0 ? new Pen(BorderBrush, stroke) : null;
        dc.DrawGeometry(Background, pen, Outline(stroke / 2));
    }

    // The fillet center is on a circle of radius (body radius + fillet radius).
    // Moving it sideways lowers the tab's roof, while keeping the two arcs exactly tangent.
    private double Reach()
    {
        if (TabExtension <= 0) return 0;
        var (w, h) = (RenderSize.Width, RenderSize.Height);
        double puck = Math.Min(h, IslandGeometry.Puck);
        double extra = w - puck;
        if (puck <= 0 || extra <= 0) return 0;

        // A pressed handoff scales the initial tab with the puck. As the window unfolds,
        // both shoulders ease out until only the panel's own rounded corners remain.
        double extension = TabExtension * puck / IslandGeometry.Puck;
        double growth = Math.Clamp(Math.Max(extra - extension, h - puck) / puck, 0, 1);
        double compact = 1 - growth * growth * (3 - 2 * growth);
        double radius = Corner();
        double fillet = radius * 6 / 25;
        double halfTab = radius * 13 / 25;
        double reach = Math.Sqrt((radius + fillet) * (radius + fillet) - (halfTab + fillet) * (halfTab + fillet));
        return compact * Math.Min(extra, reach);
    }

    private double Corner() => Math.Min(CornerRadius.TopLeft, Math.Min(RenderSize.Width, RenderSize.Height) / 2);

    private Geometry Outline(double inset)
    {
        double w = RenderSize.Width, h = RenderSize.Height, r = Corner();
        if (w <= 2 * inset || h <= 2 * inset) return Geometry.Empty;
        double reach = Reach();
        if (reach <= 0 || r <= inset)
            return new RectangleGeometry(new Rect(inset, inset, w - 2 * inset, h - 2 * inset),
                Math.Max(0, r - inset), Math.Max(0, r - inset));

        double fillet = r * 6 / 25;
        double centers = r + fillet;
        double rise = Math.Sqrt(centers * centers - reach * reach);
        double bodyRadius = r - inset, capRadius = rise - fillet - inset;
        double tangentX = r + bodyRadius * reach / centers;
        double tangentY = r - bodyRadius * rise / centers;
        double shoulderX = r + reach, roof = r - capRadius, capX = w - inset - capRadius;
        var body = new Size(bodyRadius, bodyRadius);
        var shoulder = new Size(fillet + inset, fillet + inset);
        var cap = new Size(capRadius, capRadius);
        const SweepDirection convex = SweepDirection.Clockwise;
        const SweepDirection concave = SweepDirection.Counterclockwise;

        var path = new StreamGeometry();
        using (var g = path.Open())
        {
            g.BeginFigure(new Point(inset, r), isFilled: true, isClosed: true);
            g.ArcTo(new Point(tangentX, tangentY), body, 0, false, convex, true, true);
            g.ArcTo(new Point(shoulderX, roof), shoulder, 0, false, concave, true, true);
            g.LineTo(new Point(capX, roof), true, true);
            g.ArcTo(new Point(w - inset, r), cap, 0, false, convex, true, true);
            g.LineTo(new Point(w - inset, h - r), true, true);
            g.ArcTo(new Point(capX, h - roof), cap, 0, false, convex, true, true);
            g.LineTo(new Point(shoulderX, h - roof), true, true);
            g.ArcTo(new Point(tangentX, h - tangentY), shoulder, 0, false, concave, true, true);
            g.ArcTo(new Point(inset, h - r), body, 0, false, convex, true, true);
        }
        if (_opensLeft) path.Transform = new MatrixTransform(-1, 0, 0, 1, w, 0);
        path.Freeze();
        return path;
    }
}
