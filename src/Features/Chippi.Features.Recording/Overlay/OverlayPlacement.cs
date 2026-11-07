// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Capture;

namespace Chippi.Features.Recording.Overlay;

/// <summary>
/// Where the red frame and the pill go around what's recorded (screen-recording.md, Frame and pill), in physical
/// virtual-screen pixels. Both stay on the recorded display.
/// </summary>
public static class OverlayPlacement
{
    /// <summary>The frame's window: the recorded rectangle grown by the frame's width, cut to the display, so the frame
    /// sits just outside the recording and falls inside it only where the recording meets the display's edge.</summary>
    public static PixelRect Frame(PixelRect recorded, PixelRect display, int thickness) =>
        PixelRect.FromEdges(recorded.Left - thickness, recorded.Top - thickness, recorded.Right + thickness, recorded.Bottom + thickness)
            .Intersect(display);

    /// <summary>
    /// The pill: under the recording, its right edge on the recording's right edge, <paramref name="gap"/> clear of the
    /// frame; above when there's no room under; inside the bottom right corner when there's room for neither (a whole
    /// display). Kept on the display.
    /// </summary>
    public static PixelPoint Pill(PixelRect recorded, PixelRect display, PixelSize pill, int gap)
    {
        int x = Math.Clamp(recorded.Right - pill.Width, display.Left + gap, Math.Max(display.Left + gap, display.Right - gap - pill.Width));
        int below = recorded.Bottom + gap;
        if (below + pill.Height <= display.Bottom)
            return new PixelPoint(x, below);
        int above = recorded.Top - gap - pill.Height;
        if (above >= display.Top)
            return new PixelPoint(x, above);
        int inside = Math.Min(recorded.Bottom, display.Bottom) - gap - pill.Height;
        int right = Math.Min(recorded.Right, display.Right) - gap - pill.Width;
        return new PixelPoint(Math.Max(display.Left, right), Math.Max(display.Top, inside));
    }
}
