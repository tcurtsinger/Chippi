// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Media;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// The last 120 readings as an accent line over an accent fade, filling from the right as readings
/// arrive; a missing reading is a gap. A second series draws as a dashed line (network send). On a
/// detail page a dashed line marks the scale's middle (the page writes the scale's top beside its
/// value, where the line can't run over it); a widget's graph has none, and reaches its card's edges.
/// </summary>
internal sealed class HistoryGraph : FrameworkElement
{
    private IReadOnlyList<double?> _points = [], _second = [];
    private double _max = 100;
    private bool _caution;
    // Brushes and pens, made again only when a theme brush changes: a redraw every read makes only its geometry.
    private (Brush? Line, bool Caution, Brush Fill, Pen Pen)? _strokes;
    private (Brush? Hair, Pen Solid, Pen Dashed)? _hairs;
    private (Brush? Second, Pen Pen)? _secondPen;
    private Brush? _fade;

    public static readonly DependencyProperty LineProperty = Brush(nameof(Line));
    public static readonly DependencyProperty CautionLineProperty = Brush(nameof(CautionLine));
    public static readonly DependencyProperty SecondProperty = Brush(nameof(Second));
    public static readonly DependencyProperty HairProperty = Brush(nameof(Hair));

    public HistoryGraph() : this(Look.Island) { }

    public HistoryGraph(Look look)
    {
        SetResourceReference(LineProperty, look.Accent);
        SetResourceReference(CautionLineProperty, Look.Caution);
        SetResourceReference(SecondProperty, look.Secondary);
        SetResourceReference(HairProperty, look.Hair);
        SnapsToDevicePixels = true;
    }

    /// <summary>The dashed line at the scale's middle (a detail page's graph).</summary>
    public bool Midline { get; init; } = true;

    /// <summary>The hairline along the bottom, where a strip sits under the graph.</summary>
    public bool Baseline { get; init; } = true;


    /// <summary>The oldest readings fade in over this many pixels: a graph that starts inside its card rather than at its edge.</summary>
    public double FadeIn { get; init; }

    /// <summary>Rounds the bottom corners to the card's, inside its border, where the graph reaches the card's bottom.</summary>
    public double BottomRadius { get; init; }

    /// <summary>Whether the bottom left corner is rounded too (not when the graph starts inside the card).</summary>
    public bool RoundLeft { get; init; } = true;

    public Brush? Line { get => (Brush?)GetValue(LineProperty); set => SetValue(LineProperty, value); }
    public Brush? CautionLine { get => (Brush?)GetValue(CautionLineProperty); set => SetValue(CautionLineProperty, value); }
    public Brush? Second { get => (Brush?)GetValue(SecondProperty); set => SetValue(SecondProperty, value); }
    public Brush? Hair { get => (Brush?)GetValue(HairProperty); set => SetValue(HairProperty, value); }

    /// <summary>The points drawn, oldest first (tests read them).</summary>
    internal IReadOnlyList<double?> Points => _points;

    /// <summary>The scale's top (tests read it).</summary>
    internal double Max => _max;

    /// <summary>Whether there's a line to draw: a graph of failed reads has no scale to write.</summary>
    public bool Drawn => _points.Any(IsValue) || _second.Any(IsValue);

    private static bool IsValue(double? value) => value is { } v && double.IsFinite(v);

    public void Set(IReadOnlyList<HistoryPoint> points, double max, IReadOnlyList<HistoryPoint>? second = null, bool caution = false, Func<double, double>? convert = null)
    {
        _points = [.. points.Select(p => p.Value is { } v ? convert?.Invoke(v) ?? v : (double?)null)];
        _second = second is null ? [] : [.. second.Select(p => p.Value)];
        _max = max > 0 ? max : 1;
        _caution = caution;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        double r = BottomRadius, w = sizeInfo.NewSize.Width, h = sizeInfo.NewSize.Height;
        // A rounded rectangle whose top corners sit above the graph, so only the bottom ones show.
        double left = RoundLeft ? 0 : -2 * r;
        Clip = r > 0 ? new RectangleGeometry(new Rect(left, -2 * r, w - left, h + 2 * r), r, r) : null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;
        if (Hair is { } hair && (Midline || Baseline))
        {
            if (!ReferenceEquals(_hairs?.Hair, hair))
                _hairs = (hair, Frozen(new Pen(hair, 1)), Frozen(new Pen(hair, 1) { DashStyle = new DashStyle([2, 3], 0) }));
            if (Midline)
                dc.DrawLine(_hairs!.Value.Dashed, new Point(0, Math.Round(h / 2) + 0.5), new Point(w, Math.Round(h / 2) + 0.5));
            if (Baseline)
                dc.DrawLine(_hairs!.Value.Solid, new Point(0, h - 0.5), new Point(w, h - 0.5));
        }
        if (FadeIn > 0)
            dc.PushOpacityMask(_fade ??= Frozen(new LinearGradientBrush([new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 1)], new Point(0, 0), new Point(FadeIn, 0))
            {
                MappingMode = BrushMappingMode.Absolute,
            }));
        if ((_caution ? CautionLine : Line) is SolidColorBrush line)
        {
            if (!ReferenceEquals(_strokes?.Line, line) || _strokes?.Caution != _caution)
            {
                var fill = new LinearGradientBrush(
                    [new GradientStop(WithAlpha(line.Color, _caution ? 0.16 : 0.30), 0), new GradientStop(WithAlpha(line.Color, 0.02), 1)], 90);
                _strokes = (line, _caution, Frozen(fill), Frozen(new Pen(line, 1.5) { LineJoin = PenLineJoin.Round }));
            }
            var (area, stroke) = Trace(_points, w, h, area: true);
            if (area is not null)
                dc.DrawGeometry(_strokes!.Value.Fill, null, area);
            if (stroke is not null)
                dc.DrawGeometry(null, _strokes!.Value.Pen, stroke);
        }
        if (Second is { } second && _second.Count > 0 && Trace(_second, w, h, area: false).Line is { } dashed)
        {
            if (!ReferenceEquals(_secondPen?.Second, second))
                _secondPen = (second, Frozen(new Pen(second, 1.25) { DashStyle = new DashStyle([2.4, 2.4], 0) }));
            dc.DrawGeometry(null, _secondPen!.Value.Pen, dashed);
        }
        if (FadeIn > 0)
            dc.Pop();
    }

    // Each unbroken stretch of points as a figure of one line geometry, and with area of one fill down
    // to the bottom, in one pass. Each point sits in one of 120 slots, so the newest is at the right edge;
    // a lone point is a dot.
    private (StreamGeometry? Area, StreamGeometry? Line) Trace(IReadOnlyList<double?> values, double w, double h, bool area)
    {
        double step = w / (SamplerCore.HistoryLength - 1);
        int first = SamplerCore.HistoryLength - values.Count;
        StreamGeometry? fill = null, line = null;
        StreamGeometryContext? fillContext = null, lineContext = null;
        var last = default(Point);
        int run = 0;
        for (int i = 0; i <= values.Count; i++)
        {
            if (i < values.Count && values[i] is { } v && double.IsFinite(v))
            {
                var point = new Point((first + i) * step, (h - 1) - Math.Clamp(v / _max, 0, 1) * (h - 2));
                if (run == 0)
                {
                    lineContext ??= (line = new StreamGeometry()).Open();
                    lineContext.BeginFigure(point, false, false);
                    if (area)
                    {
                        fillContext ??= (fill = new StreamGeometry()).Open();
                        fillContext.BeginFigure(new Point(point.X, h), true, true);
                    }
                }
                else
                    lineContext!.LineTo(point, true, true);
                fillContext?.LineTo(point, false, false);
                last = point;
                run++;
                continue;
            }
            if (run == 1)
                lineContext!.LineTo(last with { X = last.X + 0.01 }, true, false);
            if (run > 0)
                fillContext?.LineTo(new Point(last.X, h), false, false);
            run = 0;
        }
        fillContext?.Close();
        lineContext?.Close();
        fill?.Freeze();
        line?.Freeze();
        return (fill, line);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(color.A * alpha), color.R, color.G, color.B);

    private static DependencyProperty Brush(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(HistoryGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}

/// <summary>
/// A graph's top. CPU and GPU loads fit their history (10, 25, 50 or 100%), so 4% uses the graph
/// instead of lying on its floor; rates double from 64 KiB/s (in bits, 500 Kbps then 1, 2 and 5 Mbps
/// and on); temperatures go by 25 °C (50 °F). Memory and battery are levels and stay at 100%: a fitted
/// scale would draw 42% as nearly full.
/// </summary>
internal static class GraphScale
{
    private static readonly double[] Loads = [10, 25, 50, 100], BitSteps = [1, 2, 5];

    public static double Load(IReadOnlyList<HistoryPoint> points)
    {
        double busiest = Busiest(points);
        return Loads.FirstOrDefault(step => step >= busiest, 100);
    }

    /// <summary>
    /// The first step at or above the busiest point (bytes a second), so lines never clip: 64 KiB/s,
    /// 128, 256… in bytes, which read whole in binary units; 500 Kbps, 1, 2, 5, 10, 20 Mbps… in bits,
    /// which read whole in decimal ones ("1074 Mbps" is no scale to read).
    /// </summary>
    public static double Rate(double busiest, NetworkUnit unit = NetworkUnit.Bytes)
    {
        if (unit == NetworkUnit.Bytes)
        {
            double scale = 64 * 1024;
            while (scale < busiest) scale *= 2;
            return scale;
        }
        double bits = busiest * 8;
        if (bits <= 500_000)
            return 500_000 / 8.0;
        for (double decade = 1_000_000; ; decade *= 10)
            foreach (double step in BitSteps)
                if (step * decade >= bits)
                    return step * decade / 8;
    }

    /// <summary>The next <paramref name="step"/> at or above the hottest point: 25 for °C, 50 for °F.</summary>
    public static double Temperature(double hottest, double step = 25) => Math.Max(step, Math.Ceiling(hottest / step) * step);

    public static double Busiest(IEnumerable<HistoryPoint> points) =>
        points.Select(p => p.Value ?? 0).Where(double.IsFinite).DefaultIfEmpty(0).Max();
}

/// <summary>One bar per logical processor, filled from the bottom by its load at the graph's scale.</summary>
internal sealed class BarStrip : FrameworkElement
{
    private IReadOnlyList<double> _values = [];
    private double _max = 100;

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(BarStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(BarStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public BarStrip()
    {
        SetResourceReference(TrackProperty, "IslandTrackBrush");
        SetResourceReference(FillProperty, "IslandAccentBrush");
        SnapsToDevicePixels = true;
    }

    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    internal IReadOnlyList<double> Values => _values;

    public void Set(IReadOnlyList<double>? values, double max = 100)
    {
        _values = values ?? [];
        _max = max > 0 ? max : 100;
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
            double filled = Math.Clamp(_values[i] / _max, 0, 1) * h;
            if (filled > 0.5)
                dc.DrawRoundedRectangle(Fill, null, new Rect(rect.X, h - filled, bar, filled), radius, radius);
        }
    }
}
