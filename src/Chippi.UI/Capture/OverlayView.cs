// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Chippi.Capture;
using Chippi.UI.Controls;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.UI.Capture;

/// <summary>
/// What one display's overlay draws over its frozen image: the dimming with the selection or hovered window cut out, the
/// frame and handles, a freeform outline, the size pill, the magnifier and the feature's bars (capture-layer.md). The
/// image, dimming and frame are laid out in the display's physical pixels inside a Viewbox, so no DPI math touches the
/// pixels; the pill, magnifier and bars sit in DIPs above it. The look is neutral so it reads over any screen; Lumen's
/// board 3 restyles it. Rendered offscreen for shots, or hosted by <see cref="OverlayWindow"/>.
/// </summary>
public sealed class OverlayView : Grid
{
    /// <summary>The magnifier: this many frozen pixels across, each drawn this many DIPs wide.</summary>
    internal const int MagnifierPixels = 15, MagnifierZoom = 8;

    // The frozen screen's own colours (Frozen screen v1): content, the same in both themes, so they read over any
    // screen. The feature's bars are the only part that follows the app theme.
    private static readonly Brush DimBrush = Frozen(Color.FromArgb(0x73, 0, 0, 0));
    private static readonly Brush EdgeShadowBrush = Frozen(Color.FromArgb(0x66, 0, 0, 0));
    private static readonly Brush MarkBrush = Frozen(Color.FromRgb(0x74, 0x44, 0xF0));
    private static readonly Brush PillBrush = Frozen(Color.FromArgb(0xE6, 0x14, 0x14, 0x18));
    private static readonly Brush GridBrush = Frozen(Color.FromArgb(0x38, 0, 0, 0));

    /// <summary>The size pill and the pointer's pill: 22 high, 8 in, 6 from what they label.</summary>
    private const double PillHeight = 22, PillGap = 6, PillInset = 8;

    private readonly Canvas _pixels = new();
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Path _dim = new() { Fill = DimBrush };
    private readonly Rectangle _frame = new() { Stroke = Brushes.White, Visibility = Visibility.Collapsed, SnapsToDevicePixels = true };
    private readonly Rectangle _frameShadow = new() { Stroke = EdgeShadowBrush, Visibility = Visibility.Collapsed, SnapsToDevicePixels = true };
    private readonly Rectangle _window = new() { Stroke = MarkBrush, Visibility = Visibility.Collapsed };
    private readonly Rectangle _windowRing = new() { Stroke = Brushes.White, Visibility = Visibility.Collapsed };
    private readonly Polyline _outline = new() { Stroke = Brushes.White, Visibility = Visibility.Collapsed };
    private readonly Rectangle[] _handles = [.. Enumerable.Range(0, 8).Select(_ => new Rectangle { Fill = Brushes.White, Stroke = MarkBrush, Visibility = Visibility.Collapsed })];
    private readonly Canvas _chrome = new() { IsHitTestVisible = true };
    private readonly Border _pill;
    private readonly LucideIcon _pillCopy = new() { Data = LucideIcons.Copy, Size = 12, Brush = Brushes.White, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock _pillText = Pill();
    private readonly StackPanel _magnifier;
    private readonly Image _zoom = new() { Width = MagnifierPixels * MagnifierZoom, Height = MagnifierPixels * MagnifierZoom, Stretch = Stretch.Fill };
    private readonly TextBlock _where = Pill();
    private readonly WriteableBitmap _zoomBits = new(MagnifierPixels, MagnifierPixels, 96, 96, PixelFormats.Bgra32, null);
    private readonly byte[] _zoomPixels = new byte[MagnifierPixels * MagnifierPixels * 4];
    private WriteableBitmap? _bitmap;
    private FrameworkElement? _tools, _output;

    public OverlayView()
    {
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
        _pixels.Children.Add(_image);
        _pixels.Children.Add(_dim);
        _pixels.Children.Add(Layer);
        _pixels.Children.Add(_windowRing);
        _pixels.Children.Add(_window);
        _pixels.Children.Add(_frameShadow);
        _pixels.Children.Add(_frame);
        _pixels.Children.Add(_outline);
        foreach (var h in _handles)
            _pixels.Children.Add(h);
        Children.Add(new Viewbox { Stretch = Stretch.Fill, Child = _pixels });

        _pill = PillBox(new StackPanel { Orientation = Orientation.Horizontal, Children = { _pillCopy, _pillText } });
        _pill.Visibility = Visibility.Collapsed;

        // A round lens over the frozen pixels, its grid and the centre pixel ringed black then white; the pointer's
        // position and colour in a pill under it.
        double lens = MagnifierPixels * MagnifierZoom;
        var grid = new Grid { Width = lens, Height = lens, Clip = new EllipseGeometry(new Rect(0, 0, lens, lens)) };
        grid.Children.Add(_zoom);
        grid.Children.Add(MagnifierGrid());
        var ring = new Border
        {
            Width = lens + 4,
            Height = lens + 4,
            CornerRadius = new CornerRadius(lens / 2 + 2),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Background = Brushes.Black,
            Child = grid,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black },
        };
        var where = PillBox(_where);
        where.HorizontalAlignment = HorizontalAlignment.Left;
        where.Margin = new Thickness(0, PillGap, 0, 0);
        _magnifier = new StackPanel { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Children = { ring, where } };
        _zoom.Source = _zoomBits;
        _chrome.Children.Add(_pill);
        _chrome.Children.Add(_magnifier);
        Children.Add(_chrome);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static TextBlock Pill() => new()
    {
        Foreground = Brushes.White,
        FontSize = 11,
        MaxWidth = 360,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Border PillBox(UIElement child) => new()
    {
        Background = PillBrush,
        CornerRadius = new CornerRadius(4),
        Height = PillHeight,
        Padding = new Thickness(PillInset, 0, PillInset, 0),
        Child = child,
        IsHitTestVisible = false,
    };

    /// <summary>The feature's drawing layer, in the display's physical pixels.</summary>
    public Canvas Layer { get; } = new() { IsHitTestVisible = false };

    /// <summary>The display this view covers, in physical virtual-screen pixels.</summary>
    public PixelRect Bounds { get; private set; }

    /// <summary>DIPs to physical pixels on this display.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>Sizes the view for a display: its pixels and its DPI.</summary>
    public void Place(PixelRect bounds, int dpi)
    {
        Bounds = bounds;
        Scale = dpi / 96.0;
        _pixels.Width = Layer.Width = bounds.Width;
        _pixels.Height = Layer.Height = bounds.Height;
        Width = bounds.Width / Scale;
        Height = bounds.Height / Scale;
        Line = Math.Max(1, Math.Round(Scale));
        _frame.StrokeThickness = _frameShadow.StrokeThickness = _outline.StrokeThickness = _windowRing.StrokeThickness = Line;
        _window.StrokeThickness = 2 * Line;
        _window.RadiusX = _window.RadiusY = Math.Round(8 * Scale);
        _windowRing.RadiusX = _windowRing.RadiusY = _window.RadiusX + Line;
        foreach (var h in _handles)
        {
            h.Width = h.Height = Math.Round(8 * Scale);
            h.StrokeThickness = Line;
        }
    }

    /// <summary>One DIP of line on this display, in whole physical pixels.</summary>
    private double Line { get; set; } = 1;

    /// <summary>Shows a display's frozen pixels, reusing the bitmap when the size is the same.</summary>
    public void SetImage(CapturedScreen? screen)
    {
        if (screen is null)
        {
            _image.Source = null;
            return;
        }
        var b = screen.Bounds;
        if (_bitmap is null || _bitmap.PixelWidth != b.Width || _bitmap.PixelHeight != b.Height)
            _bitmap = new WriteableBitmap(b.Width, b.Height, 96, 96, PixelFormats.Bgra32, null);
        _bitmap.WritePixels(new Int32Rect(0, 0, b.Width, b.Height), screen.Pixels, screen.Stride, 0);
        _image.Width = b.Width;
        _image.Height = b.Height;
        _image.Source = _bitmap;
    }

    /// <summary>The feature's bars; the next <see cref="Render"/> places them.</summary>
    public void SetBars(FrameworkElement? tools, FrameworkElement? output)
    {
        foreach (var old in new[] { _tools, _output })
            if (old is not null && !ReferenceEquals(old, tools) && !ReferenceEquals(old, output))
                _chrome.Children.Remove(old);
        _tools = tools;
        _output = output;
        foreach (var bar in new[] { tools, output })
            if (bar is not null && !_chrome.Children.Contains(bar))
            {
                // A bar can belong to one display's overlay at a time.
                (bar.Parent as Panel)?.Children.Remove(bar);
                bar.Visibility = Visibility.Collapsed;
                _chrome.Children.Add(bar);
            }
    }

    /// <summary>Redraws this display's part of the selector's state.</summary>
    /// <param name="screen">This display's frozen pixels, for the magnifier.</param>
    public void Render(Selector selector, CaptureRequest request, CapturedScreen? screen)
    {
        var local = new PixelRect(0, 0, Bounds.Width, Bounds.Height);
        PixelRect? hole = selector.Rect ?? (selector.Hover is { } hover ? SelectionMath.WindowRect(selector.Freeze.Displays, hover) : null);
        var cut = hole is { } h ? h.Offset(-Bounds.X, -Bounds.Y).Intersect(local) : default;
        bool picking = selector.Mode == CaptureMode.PickPoint;

        // Dimming, with the selection (or the hovered window) left clear.
        if (request.Dim && !picking)
        {
            var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
            dim.Children.Add(new RectangleGeometry(new Rect(0, 0, local.Width, local.Height)));
            if (!cut.IsEmpty && selector.Selection?.Mask is null && selector.Outline.Count == 0)
                dim.Children.Add(new RectangleGeometry(new Rect(cut.X, cut.Y, cut.Width, cut.Height)));
            else if (selector.Outline.Count > 2)
                dim.Children.Add(Outline(selector.Outline, closed: true));
            _dim.Data = dim;
            _dim.Visibility = Visibility.Visible;
        }
        else
            _dim.Visibility = Visibility.Collapsed;

        // The hovered window lights with a rounded mark; a selection gets a white frame with a dark edge outside it (2
        // wide when it fills the display, where the edge has no room); a freeform outline instead of a frame.
        bool freeform = selector.Outline.Count > 0;
        bool hovering = selector.Rect is null && !cut.IsEmpty && !picking;
        bool framed = !cut.IsEmpty && !freeform && !picking && !hovering;
        if (framed)
        {
            _frame.StrokeThickness = cut == local ? 2 * Line : Line;
            Put(_frame, cut, 0);
            Put(_frameShadow, cut, Line);
        }
        _frame.Visibility = _frameShadow.Visibility = framed ? Visibility.Visible : Visibility.Collapsed;
        if (hovering)
        {
            Put(_window, cut, 0);
            Put(_windowRing, cut, Line);
        }
        _window.Visibility = _windowRing.Visibility = hovering ? Visibility.Visible : Visibility.Collapsed;
        if (freeform)
        {
            _outline.Points = new PointCollection(selector.Outline.Select(p => new Point(p.X - Bounds.X + 0.5, p.Y - Bounds.Y + 0.5)));
            if (selector.IsReleased && selector.Outline.Count > 0)
                _outline.Points.Add(new Point(selector.Outline[0].X - Bounds.X + 0.5, selector.Outline[0].Y - Bounds.Y + 0.5));
            _outline.Visibility = Visibility.Visible;
        }
        else
            _outline.Visibility = Visibility.Collapsed;

        RenderHandles(selector, cut);
        RenderPill(selector);
        RenderMagnifier(selector, request, screen);
        RenderBars(selector);
        Layer.IsHitTestVisible = selector.IsReleased && selector.Tooling;
    }

    /// <summary>Lays a stroked rectangle over <paramref name="r"/>, grown by <paramref name="outward"/> on each side.</summary>
    private static void Put(Rectangle shape, PixelRect r, double outward)
    {
        Canvas.SetLeft(shape, r.X - outward);
        Canvas.SetTop(shape, r.Y - outward);
        shape.Width = r.Width + 2 * outward;
        shape.Height = r.Height + 2 * outward;
    }

    private void RenderHandles(Selector selector, PixelRect cut)
    {
        bool show = selector.Selection is { Mask: null } && !selector.Fixed && !cut.IsEmpty && !selector.IsDragging;
        var rect = selector.Rect?.Offset(-Bounds.X, -Bounds.Y) ?? default;
        double[] xs = [rect.X, rect.X + rect.Width / 2.0, rect.Right];
        double[] ys = [rect.Y, rect.Y + rect.Height / 2.0, rect.Bottom];
        (int, int)[] at = [(0, 0), (1, 0), (2, 0), (2, 1), (2, 2), (1, 2), (0, 2), (0, 1)];
        for (int i = 0; i < 8; i++)
        {
            var handle = _handles[i];
            var (cx, cy) = (xs[at[i].Item1], ys[at[i].Item2]);
            bool on = show && cx >= 0 && cy >= 0 && cx <= Bounds.Width && cy <= Bounds.Height;
            handle.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                Canvas.SetLeft(handle, cx - handle.Width / 2);
                Canvas.SetTop(handle, cy - handle.Height / 2);
            }
        }
    }

    /// <summary>
    /// "1040 × 800" (with a window, "Terminal · 1040 × 800") at the selection's or hovered window's top left, above it
    /// when there's room, on the display that corner is on.
    /// </summary>
    private void RenderPill(Selector selector)
    {
        var window = selector.Selection?.Window ?? (selector.Rect is null ? selector.Hover : null);
        PixelRect? shown = selector.Rect ?? (window is null ? null : SelectionMath.WindowRect(selector.Freeze.Displays, window));
        if (shown is not { IsEmpty: false } rect || selector.Mode == CaptureMode.PickPoint
            || selector.Freeze.NearestScreen(rect.TopLeft).Bounds != Bounds)
        {
            _pill.Visibility = Visibility.Collapsed;
            return;
        }
        // A reopened capture shown smaller still reads its own size.
        var of = selector.Selection?.Source is { } source ? new PixelSize(source.Width, source.Height) : rect.Size;
        string pixels = string.Create(CultureInfo.InvariantCulture, $"{of.Width} × {of.Height}");
        _pillText.Text = window is { Title.Length: > 0 } ? $"{window.Title} · {pixels}" : pixels;
        _pillCopy.Visibility = selector.CopyDrag ? Visibility.Visible : Visibility.Collapsed;
        _pill.Visibility = Visibility.Visible;
        _pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _pill.DesiredSize;
        double left = (rect.X - Bounds.X) / Scale, top = (rect.Y - Bounds.Y) / Scale;
        double x = left, y = top - size.Height - PillGap;
        if (y < 0)
        {
            // No room above: inside the corner, clear of the frame.
            x = Math.Max(left, 0) + PillInset;
            y = Math.Max(top, 0) + PillInset;
        }
        Canvas.SetLeft(_pill, Math.Clamp(x, 0, Math.Max(0, Width - size.Width)));
        Canvas.SetTop(_pill, Math.Clamp(y, 0, Math.Max(0, Height - size.Height)));
    }

    /// <summary>8× over the frozen pixels around the pointer, with the grid, its physical coordinates and the HEX, flipped to stay on the display.</summary>
    private void RenderMagnifier(Selector selector, CaptureRequest request, CapturedScreen? screen)
    {
        var p = selector.Pointer;
        // From hover on, while choosing (Lumen, Frozen screen v1: fine aim starts before the press); always for a pick.
        bool show = screen is not null && Bounds.Contains(p) && !selector.IsReleased
            && (selector.Mode == CaptureMode.PickPoint || request.Magnifier);
        if (!show || screen is null)
        {
            _magnifier.Visibility = Visibility.Collapsed;
            return;
        }
        int half = MagnifierPixels / 2;
        for (int y = 0; y < MagnifierPixels; y++)
            for (int x = 0; x < MagnifierPixels; x++)
            {
                uint c = screen.PixelAt(new PixelPoint(p.X - half + x, p.Y - half + y)) ?? 0xFF000000u;
                int i = (y * MagnifierPixels + x) * 4;
                _zoomPixels[i] = (byte)c;
                _zoomPixels[i + 1] = (byte)(c >> 8);
                _zoomPixels[i + 2] = (byte)(c >> 16);
                _zoomPixels[i + 3] = 255;
            }
        _zoomBits.WritePixels(new Int32Rect(0, 0, MagnifierPixels, MagnifierPixels), _zoomPixels, MagnifierPixels * 4, 0);
        uint center = screen.PixelAt(p) ?? 0;
        _where.Text = string.Create(CultureInfo.InvariantCulture, $"{p.X}, {p.Y} · #{(center >> 16) & 0xFF:X2}{(center >> 8) & 0xFF:X2}{center & 0xFF:X2}");
        _magnifier.Visibility = Visibility.Visible;
        _magnifier.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _magnifier.DesiredSize;
        double px = (p.X - Bounds.X) / Scale, py = (p.Y - Bounds.Y) / Scale;
        const double offset = 16;
        double mx = px + offset + size.Width > Width ? px - offset - size.Width : px + offset;
        double my = py + offset + size.Height > Height ? py - offset - size.Height : py + offset;
        Canvas.SetLeft(_magnifier, Math.Max(0, mx));
        Canvas.SetTop(_magnifier, Math.Max(0, my));
    }

    /// <summary>The feature's bars beside the released selection, on the display it was released on; hidden while dragging.</summary>
    private void RenderBars(Selector selector)
    {
        bool here = selector.Selection is { } s && s.Display.Bounds == Bounds && !selector.IsDragging;
        foreach (var bar in new[] { _tools, _output })
            if (bar is not null)
                bar.Visibility = here ? Visibility.Visible : Visibility.Collapsed;
        if (!here || selector.Selection is not { } selection)
            return;
        PixelSize SizeOf(FrameworkElement? bar)
        {
            if (bar is null)
                return default;
            bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new PixelSize((int)Math.Ceiling(bar.DesiredSize.Width * Scale), (int)Math.Ceiling(bar.DesiredSize.Height * Scale));
        }
        var (tools, output) = BarPlacement.Place(selection.Rect, Bounds, SizeOf(_tools), SizeOf(_output), (int)Math.Round(BarPlacement.Gap * Scale));
        Put(_tools, tools);
        Put(_output, output);
    }

    private void Put(FrameworkElement? bar, PixelRect at)
    {
        if (bar is null || at.IsEmpty)
            return;
        Canvas.SetLeft(bar, (at.X - Bounds.X) / Scale);
        Canvas.SetTop(bar, (at.Y - Bounds.Y) / Scale);
    }

    private StreamGeometry Outline(IReadOnlyList<PixelPoint> points, bool closed)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(points[0].X - Bounds.X + 0.5, points[0].Y - Bounds.Y + 0.5), true, closed);
            c.PolyLineTo([.. points.Skip(1).Select(p => new Point(p.X - Bounds.X + 0.5, p.Y - Bounds.Y + 0.5))], true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The magnifier's pixel grid, and its centre pixel ringed black, then white, so it reads on any colour.</summary>
    private static Canvas MagnifierGrid()
    {
        var g = new GeometryGroup();
        double size = MagnifierPixels * MagnifierZoom;
        for (int i = 1; i < MagnifierPixels; i++)
        {
            g.Children.Add(new LineGeometry(new Point(i * MagnifierZoom + 0.5, 0), new Point(i * MagnifierZoom + 0.5, size)));
            g.Children.Add(new LineGeometry(new Point(0, i * MagnifierZoom + 0.5), new Point(size, i * MagnifierZoom + 0.5)));
        }
        g.Freeze();
        int c = MagnifierPixels / 2 * MagnifierZoom;
        var canvas = new Canvas { IsHitTestVisible = false, Children = { new Path { Data = g, Stroke = GridBrush, StrokeThickness = 1 } } };
        foreach (var (inset, brush) in new[] { (-1.0, Brushes.Black), (-2.0, Brushes.White) })
        {
            var ring = new Rectangle { Width = MagnifierZoom - 2 * inset, Height = MagnifierZoom - 2 * inset, Stroke = brush, StrokeThickness = 1 };
            Canvas.SetLeft(ring, c + inset);
            Canvas.SetTop(ring, c + inset);
            canvas.Children.Add(ring);
        }
        return canvas;
    }
}
