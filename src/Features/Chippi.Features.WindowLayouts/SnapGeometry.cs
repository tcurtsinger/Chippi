// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.WindowLayouts;

/// <summary>Physical screen pixels, including negative monitor origins.</summary>
public readonly record struct SnapRect(int X, int Y, int Width, int Height);

/// <summary>A fraction of the monitor work area. Shared edges round identically, leaving no seams.</summary>
public sealed record SnapZone(string Id, string Name, double X, double Y, double Width, double Height)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name)
        && double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height)
        && X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1 && Y + Height <= 1;
}

public static class SnapGeometry
{
    public static IReadOnlyList<SnapZone> BuiltIns { get; } = Array.AsReadOnly<SnapZone>(
    [
        new("left", "Left half", 0, 0, .5, 1),
        new("right", "Right half", .5, 0, .5, 1),
        new("left-third", "Left third", 0, 0, 1d / 3, 1),
        new("middle-third", "Middle third", 1d / 3, 0, 1d / 3, 1),
        new("right-third", "Right third", 2d / 3, 0, 1d / 3, 1),
        new("left-two-thirds", "Left two-thirds", 0, 0, 2d / 3, 1),
        new("right-two-thirds", "Right two-thirds", 1d / 3, 0, 2d / 3, 1),
        new("top-left", "Top left", 0, 0, .5, .5),
        new("top-right", "Top right", .5, 0, .5, .5),
        new("bottom-left", "Bottom left", 0, .5, .5, .5),
        new("bottom-right", "Bottom right", .5, .5, .5, .5),
    ]);

    public static SnapRect Place(SnapRect work, SnapZone zone, int gap = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(work.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(work.Height);
        ArgumentOutOfRangeException.ThrowIfNegative(gap);
        if (!zone.IsValid)
            throw new ArgumentException("Invalid zone", nameof(zone));
        int left = (int)Math.Round(work.Width * zone.X);
        int top = (int)Math.Round(work.Height * zone.Y);
        int right = (int)Math.Round(work.Width * (zone.X + zone.Width));
        int bottom = (int)Math.Round(work.Height * (zone.Y + zone.Height));
        // A gap must not erase a small zone.
        int gx = Math.Min(gap, Math.Max(0, (right - left - 1) / 2));
        int gy = Math.Min(gap, Math.Max(0, (bottom - top - 1) / 2));
        return new(work.X + left + gx, work.Y + top + gy,
            Math.Max(1, right - left - 2 * gx), Math.Max(1, bottom - top - 2 * gy));
    }

    /// <summary>The exact bounds used by the shared native action service, including separate internal and screen-edge gaps.</summary>
    public static SnapRect PlaceWithGaps(SnapRect work, SnapZone zone, int windowGap, int screenEdgeGap)
    {
        var bounds = new Chippi.Core.Actions.WindowZone(zone.X, zone.Y, zone.Width, zone.Height, windowGap, screenEdgeGap)
            .Bounds(work.Width, work.Height);
        return new(work.X + bounds.Left, work.Y + bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    /// <summary>Keep the top edge reachable when an app's minimum size exceeds its destination.</summary>
    public static SnapRect Reachable(SnapRect requested, SnapRect work, int minimumWidth, int minimumHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(work.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(work.Height);
        int width = Math.Max(1, Math.Max(requested.Width, minimumWidth));
        int height = Math.Max(1, Math.Max(requested.Height, minimumHeight));
        return new(Math.Clamp(requested.X, work.X, work.X + Math.Max(0, work.Width - width)),
            Math.Clamp(requested.Y, work.Y, work.Y + Math.Max(0, work.Height - height)), width, height);
    }
}
