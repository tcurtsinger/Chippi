// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Media;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// A card's history graph (design notes #8, #9): 36 DIP tall, the last 120 samples placed by their
/// time, newest at the right. A 1.5 DIP accent line over a flat 14% accent fill and a 1 DIP baseline.
/// A null point is a gap. An optional second series is dashed (3 2) in the secondary text color, unfilled.
/// </summary>
internal sealed class Graph : FrameworkElement
{
    public const double GraphHeight = 36;
    private const double LineWidth = 1.5;

    private IReadOnlyList<HistoryPoint> _main = [];
    private IReadOnlyList<HistoryPoint> _second = [];
    private DateTimeOffset _end;
    private TimeSpan _span = TimeSpan.FromMinutes(4);
    private double _max = 100;

    public Graph()
    {
        Height = GraphHeight;
        SnapsToDevicePixels = true;
    }

    /// <summary>Draws <paramref name="main"/> (and <paramref name="second"/>) from 0 to <paramref name="max"/>, ending at <paramref name="end"/>.</summary>
    public void Show(IReadOnlyList<HistoryPoint> main, DateTimeOffset end, TimeSpan span, double max, IReadOnlyList<HistoryPoint>? second = null)
    {
        _main = main;
        _second = second ?? [];
        _end = end;
        _span = span;
        _max = max > 0 ? max : 1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;
        var accent = TryFindResource("AccentBrush") as SolidColorBrush ?? Brushes.DodgerBlue;
        var secondary = TryFindResource("IslandTextTertiaryBrush") as Brush ?? Brushes.Gray;
        var baseline = TryFindResource("IslandBorderBrush") as Brush ?? Brushes.DimGray;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        var fill = new SolidColorBrush(accent.Color) { Opacity = 0.14 };
        fill.Freeze();
        foreach (var run in Runs(_main, width, height))
        {
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(run[0].X, height), true, true);
                g.PolyLineTo(run, false, false);
                g.LineTo(new Point(run[^1].X, height), false, false);
            }
            area.Freeze();
            dc.DrawGeometry(fill, null, area);
        }
        var line = new Pen(accent, LineWidth) { LineJoin = PenLineJoin.Round };
        line.Freeze();
        foreach (var run in Runs(_main, width, height))
            dc.DrawGeometry(null, line, Line(run));
        var dashed = new Pen(secondary, LineWidth) { DashStyle = new DashStyle([3 / LineWidth, 2 / LineWidth], 0) };
        dashed.Freeze();
        foreach (var run in Runs(_second, width, height))
            dc.DrawGeometry(null, dashed, Line(run));
        dc.Pop();
        dc.DrawRectangle(baseline, null, new Rect(0, height - 1, width, 1));
    }

    private static StreamGeometry Line(IList<Point> run)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(run[0], false, false);
            if (run.Count == 1)
                g.LineTo(new Point(run[0].X + 1, run[0].Y), true, false); // a lone sample still shows
            else
                g.PolyLineTo(run.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    // Unbroken stretches of points, split at gaps; x by time, newest at the right edge.
    private List<List<Point>> Runs(IReadOnlyList<HistoryPoint> points, double width, double height)
    {
        var runs = new List<List<Point>>();
        List<Point>? run = null;
        double top = LineWidth / 2, usable = height - 1 - LineWidth;
        foreach (var point in points)
        {
            if (point.Value is not { } value)
            {
                run = null;
                continue;
            }
            double x = width - (_end - point.At).TotalMilliseconds / _span.TotalMilliseconds * width;
            double y = top + usable * (1 - Math.Clamp(value / _max, 0, 1));
            if (run is null)
                runs.Add(run = []);
            run.Add(new Point(x, y));
        }
        return runs;
    }
}
