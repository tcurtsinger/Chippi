// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>
/// Where a feature's bars go beside a selection (capture-layer.md, The bars' placement). The layer places them, so every
/// capture feature's bars behave the same. All in physical pixels.
/// </summary>
public static class BarPlacement
{
    /// <summary>Space between the selection and a bar, and between a bar and the display's edge, in pixels at 100%.</summary>
    public const int Gap = 8;

    /// <summary>
    /// The tool column and the output bar for <paramref name="selection"/>, both on <paramref name="display"/> (the one the
    /// drag was released on), never across two.
    /// <list type="bullet">
    /// <item>Tool column: outside the right edge; else outside the left; else inside the right.</item>
    /// <item>Output bar: under the selection, right-aligned with it; else above; else inside the bottom edge.</item>
    /// <item>A selection that fills its display has both inside.</item>
    /// </list>
    /// An empty size leaves that bar out (empty rectangle back).
    /// </summary>
    public static (PixelRect Tools, PixelRect Output) Place(PixelRect selection, PixelRect display, PixelSize tools, PixelSize output, int gap = Gap)
    {
        // Bars follow the part of the selection on this display.
        var sel = selection.Intersect(display);
        if (sel.IsEmpty)
            sel = new PixelRect(Math.Clamp(selection.X, display.Left, display.Right - 1), Math.Clamp(selection.Y, display.Top, display.Bottom - 1), 1, 1);
        bool fills = SelectionMath.FillsDisplay(selection, display);

        PixelRect toolRect = default;
        if (tools.Width > 0 && tools.Height > 0)
        {
            int outsideRight = sel.Right + gap, outsideLeft = sel.Left - gap - tools.Width;
            int x = !fills && outsideRight + tools.Width <= display.Right - gap ? outsideRight
                : !fills && outsideLeft >= display.Left + gap ? outsideLeft
                : sel.Right - gap - tools.Width;
            int y = fills ? sel.Top + gap : sel.Top;
            toolRect = Clamp(new PixelRect(x, y, tools.Width, tools.Height), display, gap);
        }

        PixelRect outputRect = default;
        if (output.Width > 0 && output.Height > 0)
        {
            int under = sel.Bottom + gap, above = sel.Top - gap - output.Height;
            int y = !fills && under + output.Height <= display.Bottom - gap ? under
                : !fills && above >= display.Top + gap ? above
                : sel.Bottom - gap - output.Height;
            // Right-aligned with the selection (a gap in from its edge when inside it), and clear of the tool column.
            int right = fills ? sel.Right - gap : sel.Right;
            var rect = Clamp(new PixelRect(right - output.Width, y, output.Width, output.Height), display, gap);
            if (!toolRect.Intersect(rect).IsEmpty)
                rect = Clamp(rect with { X = toolRect.Left - gap - output.Width }, display, gap);
            outputRect = rect;
        }
        return (toolRect, outputRect);
    }

    /// <summary>Keeps a bar on the display, a gap from its edges where it fits.</summary>
    private static PixelRect Clamp(PixelRect r, PixelRect display, int gap)
    {
        int x = Math.Clamp(r.X, display.Left + gap, Math.Max(display.Left + gap, display.Right - gap - r.Width));
        int y = Math.Clamp(r.Y, display.Top + gap, Math.Max(display.Top + gap, display.Bottom - gap - r.Height));
        return r with { X = x, Y = y };
    }
}
