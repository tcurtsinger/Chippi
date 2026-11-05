// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

/// <summary>Captured before a menu opens. Identity is rechecked before the window is changed.</summary>
public sealed record ActionWindow(nint Handle, uint ProcessId, uint ThreadId, long ProcessStarted,
    string ClassName, string Executable, string App, string Title, bool Elevated, long Lifetime = 0);

/// <summary>Where a window lands on another display (Snap's Next monitor, the App switcher). Physical pixels.</summary>
public static class DisplayMove
{
    /// <summary>
    /// Keeps the window's place relative to the work area, scaled to the other one's size, never grown, and fitted inside it.
    /// <paramref name="window"/> is the visible frame; the work areas are (Left, Top, Width, Height).
    /// </summary>
    public static (int Left, int Top, int Width, int Height) Fit((int Left, int Top, int Width, int Height) window,
        (int Left, int Top, int Width, int Height) from, (int Left, int Top, int Width, int Height) to)
    {
        if (from.Width <= 0 || from.Height <= 0 || to.Width <= 0 || to.Height <= 0)
            throw new ArgumentException("Display removed");
        double sx = (double)to.Width / from.Width, sy = (double)to.Height / from.Height;
        double fit = Math.Min(1, Math.Min(sx, sy));
        int width = Math.Max(1, Math.Min(to.Width, (int)Math.Round(window.Width * fit)));
        int height = Math.Max(1, Math.Min(to.Height, (int)Math.Round(window.Height * fit)));
        int x = Math.Clamp(to.Left + (int)Math.Round((window.Left - from.Left) * sx), to.Left, to.Left + to.Width - width);
        int y = Math.Clamp(to.Top + (int)Math.Round((window.Top - from.Top) * sy), to.Top, to.Top + to.Height - height);
        return (x, y, width, height);
    }
}

/// <summary>A named zone's fractions of a monitor work area. Gaps are physical pixels.</summary>
public sealed record WindowZone(double X, double Y, double Width, double Height, int WindowGap, int ScreenEdgeGap)
{
    public (int Left, int Top, int Right, int Bottom) Bounds(int width, int height)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(X) || !double.IsFinite(Y)
            || !double.IsFinite(Width) || !double.IsFinite(Height) || X < 0 || Y < 0 || X >= 1 || Y >= 1 || Width <= 0 || Height <= 0
            || X + Width > 1.000000001 || Y + Height > 1.000000001
            || WindowGap is < 0 or > 24 || ScreenEdgeGap is < 0 or > 24)
            throw new ArgumentException("Invalid zone");
        int left = Math.Min(width - 1, (int)Math.Round(X * width));
        int top = Math.Min(height - 1, (int)Math.Round(Y * height));
        int right = Math.Max(left + 1, Math.Min(width, (int)Math.Round((X + Width) * width)));
        int bottom = Math.Max(top + 1, Math.Min(height, (int)Math.Round((Y + Height) * height)));
        int l = X == 0 ? ScreenEdgeGap : WindowGap / 2;
        int t = Y == 0 ? ScreenEdgeGap : WindowGap / 2;
        int r = X + Width >= 1 - 1e-9 ? ScreenEdgeGap : (WindowGap + 1) / 2;
        int b = Y + Height >= 1 - 1e-9 ? ScreenEdgeGap : (WindowGap + 1) / 2;
        // A deeply split zone can be smaller than the gaps. Reduce its insets before it becomes empty.
        int available = right - left - 1;
        if (l + r > available) { l = Math.Min(l, available / 2); r = Math.Min(r, available - l); }
        available = bottom - top - 1;
        if (t + b > available) { t = Math.Min(t, available / 2); b = Math.Min(b, available - t); }
        left += l; top += t; right -= r; bottom -= b;
        return (left, top, right, bottom);
    }
}
