// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>A freeform selection: the outline dragged, closed on release (capture-layer.md, Freeform).</summary>
public static class Freeform
{
    /// <summary>
    /// The outline's bounding rectangle and which of its pixels are inside, judged at each pixel's centre with the
    /// even-odd rule. Null for an outline that encloses nothing (fewer than three points, or a line).
    /// </summary>
    public static (PixelRect Bounds, SelectionMask Mask)? Close(IReadOnlyList<PixelPoint> outline)
    {
        if (outline.Count < 3)
            return null;
        // Twice the outline's area (shoelace): under a pixel's worth, it's a line or a dot.
        long twiceArea = 0;
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            twiceArea += (long)a.X * b.Y - (long)b.X * a.Y;
        }
        if (Math.Abs(twiceArea) < 2)
            return null;
        var bounds = outline.Aggregate(default(PixelRect), (r, p) => r.Union(new PixelRect(p.X, p.Y, 1, 1)));
        var inside = new byte[bounds.Area];
        var crossings = new List<double>();
        bool any = false;
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            double cy = y + 0.5;
            crossings.Clear();
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                double ay = a.Y + 0.5, by = b.Y + 0.5;
                if ((ay <= cy) == (by <= cy))
                    continue;
                crossings.Add(a.X + 0.5 + (cy - ay) / (by - ay) * (b.X - a.X));
            }
            crossings.Sort();
            for (int k = 0; k + 1 < crossings.Count; k += 2)
            {
                // Pixel centres x + 0.5 between the two crossings.
                int from = Math.Max(bounds.X, (int)Math.Ceiling(crossings[k] - 0.5));
                int to = Math.Min(bounds.Right - 1, (int)Math.Floor(crossings[k + 1] - 0.5));
                for (int x = from; x <= to; x++)
                {
                    inside[(y - bounds.Y) * bounds.Width + x - bounds.X] = 255;
                    any = true;
                }
            }
        }
        return any ? (bounds, new SelectionMask(bounds, inside)) : null;
    }
}
