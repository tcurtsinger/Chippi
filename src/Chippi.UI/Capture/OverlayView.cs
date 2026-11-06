// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Chippi.Capture;
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

    private readonly Canvas _pixels = new();
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)) };
    private readonly Rectangle _frame = new() { Stroke = Brushes.White, Visibility = Visibility.Collapsed, SnapsToDevicePixels = true };
    private readonly Polyline _outline = new() { Stroke = Brushes.White, Visibility = Visibility.Collapsed };
    private readonly Rectangle[] _handles = [.. Enumerable.Range(0, 8).Select(_ => new Rectangle { Fill = Brushes.White, Stroke = Brushes.Black, Visibility = Visibility.Collapsed })];
    private readonly Canvas _chrome = new() { IsHitTestVisible = true };
    private readonly Border _pill;
    private readonly TextBlock _pillText = new() { Foreground = Brushes.White, FontSize = 12, MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _magnifier;
    private readonly Image _zoom = new() { Width = MagnifierPixels * MagnifierZoom, Height = MagnifierPixels * MagnifierZoom, Stretch = Stretch.Fill };
    private readonly TextBlock _where = new() { Foreground = Brushes.White, FontSize = 11 };
    private readonly TextBlock _hex = new() { Foreground = Brushes.White, FontSize = 11 };
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
        _pixels.Children.Add(_frame);
        _pixels.Children.Add(_outline);
        foreach (var h in _handles)
            _pixels.Children.Add(h);
        Children.Add(new Viewbox { Stretch = Stretch.Fill, Child = _pixels });

        _pill = new Border { Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Child = _pillText, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        var grid = new Grid();
        grid.Children.Add(_zoom);
        grid.Children.Add(MagnifierGrid());
        _magnifier = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = new StackPanel { Children = { grid, _where, _hex } },
        };
        _zoom.Source = _zoomBits;
        _chrome.Children.Add(_pill);
        _chrome.Children.Add(_magnifier);
        Children.Add(_chrome);
    }

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
        double line = Math.Max(1, Math.Round(Scale));
        _frame.StrokeThickness = _outline.StrokeThickness = line;
        foreach (var h in _handles)
        {
            h.Width = h.Height = Math.Round(8 * Scale);
            h.StrokeThickness = line;
        }
    }

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

        // The frame around the selection or hovered window; a freeform outline instead of a frame.
        bool freeform = selector.Outline.Count > 0;
        if (!cut.IsEmpty && !freeform && !picking)
        {
            Canvas.SetLeft(_frame, cut.X);
            Canvas.SetTop(_frame, cut.Y);
            _frame.Width = cut.Width;
            _frame.Height = cut.Height;
            _frame.Visibility = Visibility.Visible;
        }
        else
            _frame.Visibility = Visibility.Collapsed;
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

    private void RenderHandles(Selector selector, PixelRect cut)
    {
        bool show = selector.Selection is { Mask: null } && !cut.IsEmpty && !selector.IsDragging;
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
        string pixels = string.Create(CultureInfo.InvariantCulture, $"{rect.Width} × {rect.Height}");
        _pillText.Text = window is { Title.Length: > 0 } ? $"{window.Title} · {pixels}" : pixels;
        _pill.Visibility = Visibility.Visible;
        _pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _pill.DesiredSize;
        double x = Math.Max(rect.X - Bounds.X, 0) / Scale, y = (rect.Y - Bounds.Y) / Scale - size.Height - 4;
        if (y < 4)
            y = Math.Max(rect.Y - Bounds.Y, 0) / Scale + 4; // no room above: inside
        Canvas.SetLeft(_pill, Math.Clamp(x, 4, Math.Max(4, Width - size.Width - 4)));
        Canvas.SetTop(_pill, Math.Clamp(y, 4, Math.Max(4, Height - size.Height - 4)));
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
        _where.Text = string.Create(CultureInfo.InvariantCulture, $"{p.X}, {p.Y}");
        _hex.Text = string.Create(CultureInfo.InvariantCulture, $"#{(center >> 16) & 0xFF:X2}{(center >> 8) & 0xFF:X2}{center & 0xFF:X2}");
        _magnifier.Visibility = Visibility.Visible;
        _magnifier.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _magnifier.DesiredSize;
        double px = (p.X - Bounds.X) / Scale, py = (p.Y - Bounds.Y) / Scale;
        const double offset = 20;
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

    /// <summary>The magnifier's pixel grid and its centre pixel's frame.</summary>
    private static Path MagnifierGrid()
    {
        var g = new GeometryGroup();
        double size = MagnifierPixels * MagnifierZoom;
        for (int i = 1; i < MagnifierPixels; i++)
        {
            g.Children.Add(new LineGeometry(new Point(i * MagnifierZoom, 0), new Point(i * MagnifierZoom, size)));
            g.Children.Add(new LineGeometry(new Point(0, i * MagnifierZoom), new Point(size, i * MagnifierZoom)));
        }
        int c = MagnifierPixels / 2 * MagnifierZoom;
        g.Children.Add(new RectangleGeometry(new Rect(c, c, MagnifierZoom, MagnifierZoom)));
        g.Freeze();
        return new Path { Data = g, Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, IsHitTestVisible = false };
    }
}
