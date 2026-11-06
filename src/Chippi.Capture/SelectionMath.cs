// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>Which of a selection's eight handles is held, or its inside (a move).</summary>
public enum SelectionHandle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Inside }

/// <summary>
/// The selector's geometry, all in physical virtual-screen pixels (capture-layer.md, Modes and After release). Displays
/// may have gaps between them and negative origins; nothing here assumes the main display is at 0, 0.
/// </summary>
public static class SelectionMath
{
    /// <summary>A press that moved this far is a drag, not a click: Windows' drag distance (<c>SM_CXDRAG</c>, <c>SM_CYDRAG</c>).</summary>
    public static bool IsDrag(PixelPoint start, PixelPoint now, int dragWidth, int dragHeight) =>
        Math.Abs(now.X - start.X) >= dragWidth || Math.Abs(now.Y - start.Y) >= dragHeight;

    internal static long DistanceSquared(PixelRect r, PixelPoint p)
    {
        long dx = p.X < r.Left ? r.Left - p.X : p.X >= r.Right ? p.X - (r.Right - 1) : 0;
        long dy = p.Y < r.Top ? r.Top - p.Y : p.Y >= r.Bottom ? p.Y - (r.Bottom - 1) : 0;
        return dx * dx + dy * dy;
    }

    /// <summary>The display a point is on, or the nearest one.</summary>
    public static PixelRect DisplayAt(IReadOnlyList<PixelRect> displays, PixelPoint p) =>
        displays.FirstOrDefault(d => d.Contains(p)) is { IsEmpty: false } on ? on : displays.MinBy(d => DistanceSquared(d, p));

    /// <summary>A point kept on the displays: one in a gap or off every display moves to the nearest pixel of the nearest one.</summary>
    public static PixelPoint OnDisplays(IReadOnlyList<PixelRect> displays, PixelPoint p)
    {
        var d = DisplayAt(displays, p);
        return new PixelPoint(Math.Clamp(p.X, d.Left, d.Right - 1), Math.Clamp(p.Y, d.Top, d.Bottom - 1));
    }

    /// <summary>A window's selection: its frame, clipped to the rectangle around the displays.</summary>
    public static PixelRect WindowRect(IReadOnlyList<PixelRect> displays, FrozenWindow window) =>
        window.Frame.Intersect(Around(displays));

    public static PixelRect Around(IReadOnlyList<PixelRect> displays) => displays.Aggregate(default(PixelRect), (r, d) => r.Union(d));

    /// <summary>Whether every pixel of <paramref name="rect"/> lies on a display (displays never overlap).</summary>
    public static bool OnScreen(IReadOnlyList<PixelRect> displays, PixelRect rect) =>
        !rect.IsEmpty && displays.Sum(d => d.Intersect(rect).Area) == rect.Area;

    /// <summary>
    /// The last region on a fresh freeze (capture-layer.md, Last region): the rectangle as it was when it lies on the
    /// displays; when the displays changed so it no longer does, the part still on screen; null when none of it is.
    /// </summary>
    public static PixelRect? LastRegion(IReadOnlyList<PixelRect> displays, PixelRect? last)
    {
        if (last is not { IsEmpty: false } rect)
            return null;
        if (OnScreen(displays, rect))
            return rect;
        var visible = displays.Aggregate(default(PixelRect), (r, d) => r.Union(d.Intersect(rect)));
        return visible.IsEmpty ? null : visible;
    }

    /// <summary>Moves a selection by a pixel step (arrows), stopping at the edge of the rectangle around the displays.</summary>
    public static PixelRect Move(IReadOnlyList<PixelRect> displays, PixelRect rect, int dx, int dy)
    {
        var around = Around(displays);
        int x = Math.Clamp(rect.X + dx, around.Left, Math.Max(around.Left, around.Right - rect.Width));
        int y = Math.Clamp(rect.Y + dy, around.Top, Math.Max(around.Top, around.Bottom - rect.Height));
        return rect with { X = x, Y = y };
    }

    /// <summary>Resizes from the right and bottom edges (Shift+arrows): at least a pixel, never past the displays' edge.</summary>
    public static PixelRect Grow(IReadOnlyList<PixelRect> displays, PixelRect rect, int dw, int dh)
    {
        var around = Around(displays);
        int w = Math.Clamp(rect.Width + dw, 1, Math.Max(1, around.Right - rect.X));
        int h = Math.Clamp(rect.Height + dh, 1, Math.Max(1, around.Bottom - rect.Y));
        return rect with { Width = w, Height = h };
    }

    /// <summary>
    /// A handle dragged from <paramref name="start"/> by <paramref name="dx"/>, <paramref name="dy"/>: the held edges
    /// follow, crossing over the opposite ones flips the selection, and it stays at least a pixel and on the rectangle
    /// around the displays. <see cref="SelectionHandle.Inside"/> moves it whole.
    /// </summary>
    public static PixelRect Drag(IReadOnlyList<PixelRect> displays, PixelRect start, SelectionHandle handle, int dx, int dy)
    {
        if (handle == SelectionHandle.Inside)
            return Move(displays, start, dx, dy);
        var around = Around(displays);
        int l = start.Left, t = start.Top, r = start.Right, b = start.Bottom;
        if (handle is SelectionHandle.Left or SelectionHandle.TopLeft or SelectionHandle.BottomLeft) l += dx;
        if (handle is SelectionHandle.Right or SelectionHandle.TopRight or SelectionHandle.BottomRight) r += dx;
        if (handle is SelectionHandle.Top or SelectionHandle.TopLeft or SelectionHandle.TopRight) t += dy;
        if (handle is SelectionHandle.Bottom or SelectionHandle.BottomLeft or SelectionHandle.BottomRight) b += dy;
        if (l > r) (l, r) = (r, l);
        if (t > b) (t, b) = (b, t);
        l = Math.Clamp(l, around.Left, around.Right - 1);
        t = Math.Clamp(t, around.Top, around.Bottom - 1);
        r = Math.Clamp(Math.Max(r, l + 1), l + 1, around.Right);
        b = Math.Clamp(Math.Max(b, t + 1), t + 1, around.Bottom);
        return PixelRect.FromEdges(l, t, r, b);
    }

    /// <summary>
    /// The handle under a point: the eight handles within <paramref name="reach"/> pixels of a corner or edge's middle
    /// (corners first), else the inside, else none.
    /// </summary>
    public static SelectionHandle HandleAt(PixelRect rect, PixelPoint p, int reach)
    {
        int cx = rect.X + rect.Width / 2, cy = rect.Y + rect.Height / 2, r = rect.Right - 1, b = rect.Bottom - 1;
        bool Near(int x, int y) => Math.Abs(p.X - x) <= reach && Math.Abs(p.Y - y) <= reach;
        if (Near(rect.X, rect.Y)) return SelectionHandle.TopLeft;
        if (Near(r, rect.Y)) return SelectionHandle.TopRight;
        if (Near(r, b)) return SelectionHandle.BottomRight;
        if (Near(rect.X, b)) return SelectionHandle.BottomLeft;
        if (Near(cx, rect.Y)) return SelectionHandle.Top;
        if (Near(r, cy)) return SelectionHandle.Right;
        if (Near(cx, b)) return SelectionHandle.Bottom;
        if (Near(rect.X, cy)) return SelectionHandle.Left;
        return rect.Contains(p) ? SelectionHandle.Inside : SelectionHandle.None;
    }

    /// <summary>Whether a selection fills the display it's on, edge to edge (Screen mode, Ctrl+A): then the bars go inside.</summary>
    public static bool FillsDisplay(PixelRect selection, PixelRect display) => selection.Contains(display);
}
