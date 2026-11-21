// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>
/// Where the scrolling capture's bar and preview go around the area (docs/phase-7/scrolling-capture.md, By hand), in
/// physical pixels. The bar sits as the finish bar does; the preview sits beside the area and flips to its other side at a
/// display's edge, as the tools do.
/// </summary>
public static class ScrollingPlacement
{
    /// <summary>The bar: under the area, right-aligned with it; else above; else inside its bottom edge (<see cref="BarPlacement"/>).</summary>
    public static PixelRect Bar(PixelRect area, PixelRect display, PixelSize size, int gap = BarPlacement.Gap) =>
        BarPlacement.Place(area, display, default, size, gap).Output;

    /// <summary>
    /// The preview strip: outside the area's right edge, top-aligned with it; else outside its left; else inside its right
    /// edge. Always on the display, a gap from its edges where it fits.
    /// </summary>
    public static PixelRect Preview(PixelRect area, PixelRect display, PixelSize size, int gap = BarPlacement.Gap)
    {
        var sel = area.Intersect(display);
        if (sel.IsEmpty)
            sel = new PixelRect(Math.Clamp(area.X, display.Left, display.Right - 1), Math.Clamp(area.Y, display.Top, display.Bottom - 1), 1, 1);
        int x;
        if (sel.Right + gap + size.Width <= display.Right - gap)
            x = sel.Right + gap;
        else if (sel.Left - gap - size.Width >= display.Left + gap)
            x = sel.Left - gap - size.Width;
        else
            x = sel.Right - gap - size.Width;
        x = Math.Clamp(x, display.Left + gap, Math.Max(display.Left + gap, display.Right - gap - size.Width));
        int y = Math.Clamp(sel.Top, display.Top + gap, Math.Max(display.Top + gap, display.Bottom - gap - size.Height));
        return new PixelRect(x, y, size.Width, size.Height);
    }
}

/// <summary>What the preview strip draws, in DIPs (<see cref="Compute"/>).</summary>
/// <param name="Width">The strip's width.</param>
/// <param name="Height">The visible height: the whole strip, or the area's height once it's taller.</param>
/// <param name="StripHeight">The whole strip's height.</param>
/// <param name="Offset">How far the strip is scrolled up inside the visible part (newest in view).</param>
/// <param name="Seams">Each seam's y inside the visible part.</param>
/// <param name="Gap">The gap's y inside the visible part, or null.</param>
/// <param name="ViewportTop">The framed part's top inside the visible part (may be negative or past the bottom when scrolled out of view).</param>
/// <param name="ViewportHeight">The framed part's height: what the area shows now.</param>
public sealed record ScrollingPreviewLayout(
    double Width, double Height, double StripHeight, double Offset, IReadOnlyList<double> Seams, double? Gap, double ViewportTop, double ViewportHeight)
{
    /// <summary>The strip's fixed width in DIPs.</summary>
    public const double StripWidth = 96;

    /// <summary>
    /// The strip for a result of <paramref name="resultWidth"/> by <paramref name="resultHeight"/> physical pixels, drawn
    /// <see cref="StripWidth"/> DIPs wide and growing downward. Taller than <paramref name="maxHeight"/> (the area's height in
    /// DIPs), it shows the newest part, unless the framed part would leave it: then it holds the frame in view.
    /// </summary>
    /// <param name="areaHeight">The area's height in physical pixels (the frame's).</param>
    /// <param name="seams">Result rows where each appended strip starts.</param>
    /// <param name="viewportTop">The result row of the area's top.</param>
    /// <param name="gapRow">The result row of a gap, drawn only when <paramref name="gap"/>.</param>
    public static ScrollingPreviewLayout Compute(
        int resultWidth, int resultHeight, int areaHeight, IReadOnlyList<int> seams, int viewportTop, int gapRow, bool gap, double maxHeight)
    {
        double scale = StripWidth / Math.Max(1, resultWidth);
        double strip = Math.Max(1, resultHeight * scale);
        double visible = Math.Min(strip, Math.Max(1, maxHeight));
        double frame = areaHeight * scale;
        double offset = strip - visible;
        double top = viewportTop * scale;
        // Scrolled back up past what's shown: hold the frame in view rather than the newest rows.
        if (top < offset)
            offset = Math.Max(0, top);
        var lines = new List<double>();
        foreach (int seam in seams)
        {
            double y = seam * scale - offset;
            if (y > 0 && y < visible)
                lines.Add(y);
        }
        double? gapY = null;
        if (gap)
        {
            double y = gapRow * scale - offset;
            if (y >= 0 && y <= visible)
                gapY = y;
        }
        return new ScrollingPreviewLayout(StripWidth, visible, strip, offset, lines, gapY, top - offset, frame);
    }
}
