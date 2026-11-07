// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.Features.ScreenCapture;

public enum MarkupTool { Pen, Highlighter, Line, Arrow, Rectangle, Ellipse, Text, Redact }

/// <summary>Immutable geometry in source pixels. Copy caller-owned point collections at construction.</summary>
public sealed class MarkupObject
{
    public MarkupTool Tool { get; }
    public IReadOnlyList<Point> Points { get; }
    public Color Color { get; }
    public double Width { get; }
    public bool Fill { get; }
    public string Text { get; }
    public bool Bold { get; }

    public MarkupObject(MarkupTool tool, IEnumerable<Point> points, Color color, double width = 3, bool fill = false, string text = "", bool bold = false)
    {
        if (!Enum.IsDefined(tool)) throw new ArgumentOutOfRangeException(nameof(tool));
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var copy = points.ToArray();
        if (copy.Length < 2 || copy.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new ArgumentException("Two finite points are required.", nameof(points));
        Tool = tool; Points = Array.AsReadOnly(copy); Color = color; Width = width; Fill = fill; Text = text; Bold = bold;
    }

    internal Rect Bounds
    {
        get { var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) Draw(dc); return visual.Drawing.Bounds; }
    }
    internal bool Hit(Point point)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc);
        return VisualTreeHelper.HitTest(visual, point) != null;
    }
    internal MarkupObject Move(Vector by) => new(Tool, Points.Select(p => p + by), Color, Width, Fill, Text, Bold);
    internal MarkupObject Resize(Rect from, Rect to) => new(Tool,
        Points.Select(p => new Point(to.Left + (p.X - from.Left) * to.Width / Math.Max(1, from.Width),
            to.Top + (p.Y - from.Top) * to.Height / Math.Max(1, from.Height))), Color, Width, Fill, Text, Bold);

    internal void Draw(DrawingContext dc)
    {
        Color color = Color;
        color.A = 255;
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var bounds = new Rect(Points[0], Points[1]);
        switch (Tool)
        {
            case MarkupTool.Redact:
                // Snap outwards to cover every touched source pixel, with no translucent edges.
                bounds = new Rect(Math.Floor(bounds.Left), Math.Floor(bounds.Top), Math.Ceiling(bounds.Right) - Math.Floor(bounds.Left), Math.Ceiling(bounds.Bottom) - Math.Floor(bounds.Top));
                dc.DrawRectangle(Brushes.Black, null, bounds);
                break;
            case MarkupTool.Rectangle: dc.DrawRectangle(Fill ? brush : null, pen, bounds); break;
            case MarkupTool.Ellipse: dc.DrawEllipse(Fill ? brush : null, pen, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), bounds.Width / 2, bounds.Height / 2); break;
            case MarkupTool.Text:
                dc.DrawText(new FormattedText(Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), Width, brush, 1), Points[0]);
                break;
            case MarkupTool.Line:
            case MarkupTool.Arrow:
                dc.DrawLine(pen, Points[0], Points[1]);
                if (Tool == MarkupTool.Arrow && Points[0] != Points[1])
                {
                    Vector direction = Points[0] - Points[1]; direction.Normalize();
                    Vector normal = new(-direction.Y, direction.X);
                    double size = Width * 4;
                    dc.DrawLine(pen, Points[1], Points[1] + direction * size + normal * size / 2);
                    dc.DrawLine(pen, Points[1], Points[1] + direction * size - normal * size / 2);
                }
                break;
            default:
                var path = new StreamGeometry();
                using (var geometry = path.Open())
                {
                    geometry.BeginFigure(Points[0], false, false);
                    geometry.PolyLineTo(Points.Skip(1).ToArray(), true, false);
                }
                // Opacity applies once to the stroke's combined geometry, including self overlap.
                if (Tool == MarkupTool.Highlighter) dc.PushOpacity(0.35);
                dc.DrawGeometry(null, pen, path);
                if (Tool == MarkupTool.Highlighter) dc.Pop();
                break;
        }
    }
}

/// <summary>An unsaved, memory-only draft. Finished results must reopen from Flatten's pixels.</summary>
public sealed class CaptureDraft
{
    private sealed record State(Int32Rect Crop, MarkupObject[] Objects);
    private readonly BitmapSource _source;
    private readonly Stack<State> _undo = new(), _redo = new();
    private State _state;
    public Int32Rect Crop => _state.Crop;
    internal bool IsCropped => Crop != new Int32Rect(0, 0, _source.PixelWidth, _source.PixelHeight);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public IReadOnlyList<MarkupObject> Objects => Array.AsReadOnly(_state.Objects);

    public CaptureDraft(BitmapSource source)
    {
        _source = source.Clone(); _source.Freeze();
        _state = new(new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight), []);
    }

    private void Commit(State state) { _undo.Push(_state); _state = state; _redo.Clear(); }
    public void Add(MarkupObject item) => Commit(_state with { Objects = [.. _state.Objects, item] });
    public void Replace(int index, MarkupObject item)
    {
        var objects = (MarkupObject[])_state.Objects.Clone(); objects[index] = item;
        Commit(_state with { Objects = objects });
    }
    public void Remove(int index) => Commit(_state with { Objects = _state.Objects.Where((_, i) => i != index).ToArray() });
    public void SetCrop(Int32Rect crop)
    {
        if (crop.Width <= 0 || crop.Height <= 0 || crop.X < 0 || crop.Y < 0 || (long)crop.X + crop.Width > _source.PixelWidth || (long)crop.Y + crop.Height > _source.PixelHeight) throw new ArgumentOutOfRangeException(nameof(crop));
        Commit(_state with { Crop = crop });
    }
    public bool Undo() { if (!_undo.TryPop(out var state)) return false; _redo.Push(_state); _state = state; return true; }
    public bool Redo() { if (!_redo.TryPop(out var state)) return false; _undo.Push(_state); _state = state; return true; }

    internal void Paint(DrawingContext dc, int skip = -1)
    {
        dc.PushClip(new RectangleGeometry(new Rect(Crop.X, Crop.Y, Crop.Width, Crop.Height)));
        dc.DrawImage(_source, new Rect(0, 0, _source.PixelWidth, _source.PixelHeight));
        for (int i = 0; i < _state.Objects.Length; i++) if (i != skip) _state.Objects[i].Draw(dc);
        dc.Pop();
    }

    public BitmapSource Flatten()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-Crop.X, -Crop.Y));
            dc.DrawImage(_source, new Rect(0, 0, _source.PixelWidth, _source.PixelHeight));
            foreach (var item in _state.Objects) item.Draw(dc);
            dc.Pop();
        }
        var result = new RenderTargetBitmap(Crop.Width, Crop.Height, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
}
