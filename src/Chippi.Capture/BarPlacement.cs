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
    /// drag was released on), never across two. The two meet at one corner of the selection, as an L: the column grows
    /// along the selection's side from that corner, the bar along its top or bottom.
    /// <list type="bullet">
    /// <item>Tool column: outside the right edge; else outside the left; else inside the right.</item>
    /// <item>Output bar: under the selection; else above; else inside the bottom edge.</item>
    /// <item>A selection that fills its display has both inside, still meeting at the corner.</item>
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
        bool hasTools = tools.Width > 0 && tools.Height > 0, hasOutput = output.Width > 0 && output.Height > 0;

        // The column's side picks the corner's x, the output bar's side its y.
        bool right = fills || sel.Right + gap + tools.Width <= display.Right - gap || sel.Left - gap - tools.Width < display.Left + gap;
        bool toolsInside = fills || right && sel.Right + gap + tools.Width > display.Right - gap;
        bool under = fills || sel.Bottom + gap + output.Height <= display.Bottom - gap || sel.Top - gap - output.Height < display.Top + gap;
        bool outputInside = fills || under && sel.Bottom + gap + output.Height > display.Bottom - gap;

        // The corner: the selection's own, or moved in past a bar that sits inside, so the bars still meet there. The
        // column is a gap to one side of it and the output bar a gap below or above it.
        int cornerX = !right ? sel.Left : !toolsInside ? sel.Right : hasTools ? sel.Right - gap - tools.Width - gap : sel.Right - gap;
        int cornerY = !under ? sel.Top : !outputInside ? sel.Bottom : hasOutput ? sel.Bottom - gap - output.Height - gap : sel.Bottom - gap;

        PixelRect toolRect = default;
        if (hasTools)
        {
            int x = right ? cornerX + gap : cornerX - gap - tools.Width;
            int y = under ? cornerY - tools.Height : cornerY;
            toolRect = Clamp(new PixelRect(x, y, tools.Width, tools.Height), display, gap);
        }

        PixelRect outputRect = default;
        if (hasOutput)
        {
            int x = right ? cornerX - output.Width : cornerX;
            int y = under ? cornerY + gap : cornerY - gap - output.Height;
            outputRect = Clamp(new PixelRect(x, y, output.Width, output.Height), display, gap);
            // Clamped onto the column (a small selection in a display's corner): the bar goes past the column's end.
            if (!toolRect.Intersect(outputRect).IsEmpty)
                outputRect = Clamp(outputRect with { Y = under ? toolRect.Bottom + gap : toolRect.Top - gap - output.Height }, display, gap);
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
