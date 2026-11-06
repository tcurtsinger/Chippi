// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>A point in physical virtual-screen pixels: the main display's top left is 0, 0; a display left of it is negative.</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public PixelPoint Offset(int dx, int dy) => new(X + dx, Y + dy);
}

/// <summary>A size in physical pixels.</summary>
public readonly record struct PixelSize(int Width, int Height);

/// <summary>A rectangle in physical virtual-screen pixels. <see cref="Right"/> and <see cref="Bottom"/> are exclusive.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;
    public PixelSize Size => new(Width, Height);

    public static PixelRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    /// <summary>The rectangle two corner pixels span, both included, whichever way the drag went.</summary>
    public static PixelRect Spanning(PixelPoint a, PixelPoint b) =>
        FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);

    public bool Contains(PixelPoint p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    public bool Contains(PixelRect r) => !r.IsEmpty && r.X >= X && r.Y >= Y && r.Right <= Right && r.Bottom <= Bottom;

    /// <summary>The overlap; empty (0 × 0 at the origin) when there is none.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        int l = Math.Max(X, other.X), t = Math.Max(Y, other.Y), r = Math.Min(Right, other.Right), b = Math.Min(Bottom, other.Bottom);
        return r > l && b > t ? FromEdges(l, t, r, b) : default;
    }

    /// <summary>The smallest rectangle holding both; an empty one adds nothing.</summary>
    public PixelRect Union(PixelRect other) =>
        IsEmpty ? other : other.IsEmpty ? this
            : FromEdges(Math.Min(X, other.X), Math.Min(Y, other.Y), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public PixelPoint TopLeft => new(X, Y);

    public override string ToString() => $"{X},{Y} {Width}×{Height}";
}
