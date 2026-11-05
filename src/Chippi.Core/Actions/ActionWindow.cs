// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

/// <summary>Captured before a menu opens. Identity is rechecked before the window is changed.</summary>
public sealed record ActionWindow(nint Handle, uint ProcessId, uint ThreadId, long ProcessStarted,
    string ClassName, string Executable, string App, string Title, bool Elevated, long Lifetime = 0);

/// <summary>A named zone's fractions of a monitor work area. Gaps are physical pixels.</summary>
public sealed record WindowZone(double X, double Y, double Width, double Height, int WindowGap, int ScreenEdgeGap)
{
    public (int Left, int Top, int Right, int Bottom) Bounds(int width, int height)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(X) || !double.IsFinite(Y)
            || !double.IsFinite(Width) || !double.IsFinite(Height) || X < 0 || Y < 0 || Width <= 0 || Height <= 0
            || X + Width > 1.000000001 || Y + Height > 1.000000001
            || WindowGap is < 0 or > 24 || ScreenEdgeGap is < 0 or > 24)
            throw new ArgumentException("Invalid zone");
        int left = (int)Math.Round(X * width), top = (int)Math.Round(Y * height);
        int right = (int)Math.Round((X + Width) * width), bottom = (int)Math.Round((Y + Height) * height);
        left += X == 0 ? ScreenEdgeGap : WindowGap / 2;
        top += Y == 0 ? ScreenEdgeGap : WindowGap / 2;
        right -= X + Width >= 1 - 1e-9 ? ScreenEdgeGap : (WindowGap + 1) / 2;
        bottom -= Y + Height >= 1 - 1e-9 ? ScreenEdgeGap : (WindowGap + 1) / 2;
        if (right <= left || bottom <= top) throw new ArgumentException("Zone too small");
        return (left, top, right, bottom);
    }
}
