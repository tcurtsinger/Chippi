// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// The last 120 readings as an accent line over an accent fade, filling from the right as readings
/// arrive; a missing reading is a gap. A second series draws as a dashed line (network send). The
/// scale's top is written at the top right, and a dashed line marks its middle.
/// </summary>
internal sealed class HistoryGraph : FrameworkElement
{
    private IReadOnlyList<double?> _points = [], _second = [];
    private double _max = 100;
    private string _scale = "";
    private bool _caution;

    public static readonly DependencyProperty LineProperty = Brush(nameof(Line));
    public static readonly DependencyProperty CautionLineProperty = Brush(nameof(CautionLine));
    public static readonly DependencyProperty SecondProperty = Brush(nameof(Second));
    public static readonly DependencyProperty HairProperty = Brush(nameof(Hair));
    public static readonly DependencyProperty LabelProperty = Brush(nameof(Label));

    public HistoryGraph()
    {
        SetResourceReference(LineProperty, "IslandAccentBrush");
        SetResourceReference(CautionLineProperty, "WarningTextBrush");
        SetResourceReference(SecondProperty, "IslandTextSecondaryBrush");
        SetResourceReference(HairProperty, "IslandHairBrush");
        SetResourceReference(LabelProperty, "IslandTextTertiaryBrush");
        SnapsToDevicePixels = true;
    }

    public Brush? Line { get => (Brush?)GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public Brush? CautionLine { get => (Brush?)GetValue(CautionLineProperty); set => SetValue(CautionLineProperty, value); }
    public Brush? Second { get => (Brush?)GetValue(SecondProperty); set => SetValue(SecondProperty, value); }
    public Brush? Hair { get => (Brush?)GetValue(HairProperty); set => SetValue(HairProperty, value); }
    public Brush? Label { get => (Brush?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>The points drawn, oldest first (tests read them).</summary>
    internal IReadOnlyList<double?> Points => _points;

    internal string Scale => _scale;

    public void Set(IReadOnlyList<HistoryPoint> points, double max, string scale, IReadOnlyList<HistoryPoint>? second = null, bool caution = false, Func<double, double>? convert = null)
    {
        _points = [.. points.Select(p => p.Value is { } v ? convert?.Invoke(v) ?? v : (double?)null)];
        _second = second is null ? [] : [.. second.Select(p => p.Value)];
        _max = max > 0 ? max : 1;
        _scale = scale;
        _caution = caution;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        if (Hair is { } hair)
        {
            var dashed = new Pen(hair, 1) { DashStyle = new DashStyle([2, 3], 0) };
            dc.DrawLine(dashed, new Point(0, Math.Round(h / 2) + 0.5), new Point(w, Math.Round(h / 2) + 0.5));
            dc.DrawLine(new Pen(hair, 1), new Point(0, h - 0.5), new Point(w, h - 0.5));
        }
        var line = (_caution ? CautionLine : Line) as SolidColorBrush;
        if (line is not null)
        {
            var fill = new LinearGradientBrush(
                [new GradientStop(WithAlpha(line.Color, _caution ? 0.16 : 0.30), 0), new GradientStop(WithAlpha(line.Color, 0.02), 1)], 90);
            fill.Freeze();
            foreach (var run in Runs(_points, w, h))
                dc.DrawGeometry(fill, null, Area(run, h));
            var pen = new Pen(line, 1.5) { LineJoin = PenLineJoin.Round };
            foreach (var run in Runs(_points, w, h))
                dc.DrawGeometry(null, pen, Polyline(run));
        }
        if (Second is { } second && _second.Count > 0)
        {
            var pen = new Pen(second, 1.25) { DashStyle = new DashStyle([2.4, 2.4], 0) };
            foreach (var run in Runs(_second, w, h))
                dc.DrawGeometry(null, pen, Polyline(run));
        }
        if (_scale.Length > 0 && Label is { } label)
        {
            var text = new FormattedText(_scale, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Variable Text, Segoe UI"), 10, label, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(w - text.Width, -1));
        }
    }

    // Unbroken stretches of points, each placed in one of 120 slots so the newest is at the right edge.
    private IEnumerable<List<Point>> Runs(IReadOnlyList<double?> values, double w, double h)
    {
        double step = w / (SamplerCore.HistoryLength - 1);
        int first = SamplerCore.HistoryLength - values.Count;
        var run = new List<Point>();
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } v || !double.IsFinite(v))
            {
                if (run.Count > 0) yield return run;
                run = [];
                continue;
            }
            double y = (h - 1) - Math.Clamp(v / _max, 0, 1) * (h - 2);
            run.Add(new Point((first + i) * step, y));
        }
        if (run.Count > 0) yield return run;
    }

    private static StreamGeometry Polyline(List<Point> run)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(run[0], false, false);
            if (run.Count == 1)
                ctx.LineTo(run[0] with { X = run[0].X + 0.01 }, true, false);
            else
                ctx.PolyLineTo(run.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry Area(List<Point> run, double h)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(run[0].X, h), true, true);
            ctx.PolyLineTo([.. run, new Point(run[^1].X, h)], false, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(color.A * alpha), color.R, color.G, color.B);

    private static DependencyProperty Brush(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(HistoryGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}

/// <summary>One bar per logical processor, filled from the bottom by its load.</summary>
internal sealed class BarStrip : FrameworkElement
{
    private IReadOnlyList<double> _values = [];

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(BarStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(BarStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public BarStrip()
    {
        SetResourceReference(TrackProperty, "IslandTrackBrush");
        SetResourceReference(FillProperty, "IslandTextSecondaryBrush");
        SnapsToDevicePixels = true;
    }

    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    internal IReadOnlyList<double> Values => _values;

    public void Set(IReadOnlyList<double>? values)
    {
        _values = values ?? [];
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        int n = _values.Count;
        double w = ActualWidth, h = ActualHeight;
        if (n == 0 || w <= 0 || h <= 0)
            return;
        // 4 apart while the bars stay wider than 8; then 2, then 1 for very many processors.
        double gap = (w - 3 * (n - 1)) / n >= 8 ? 4 : (w - (n - 1)) / n >= 4 ? 2 : 1;
        double bar = Math.Max(1, (w - gap * (n - 1)) / n);
        double radius = bar >= 6 ? 2 : 0;
        for (int i = 0; i < n; i++)
        {
            var rect = new Rect(i * (bar + gap), 0, bar, h);
            dc.DrawRoundedRectangle(Track, null, rect, radius, radius);
            double filled = Math.Clamp(_values[i] / 100, 0, 1) * h;
            if (filled > 0.5)
                dc.DrawRoundedRectangle(Fill, null, new Rect(rect.X, h - filled, bar, filled), radius, radius);
        }
    }
}
