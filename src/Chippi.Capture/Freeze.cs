// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>
/// One display read at the freeze: its pixels as BGRA, top row first, 4 bytes a pixel with no padding, alpha 255.
/// </summary>
/// <param name="DisplayId">DisplayCatalog's id (<c>\\?\DISPLAY#…</c>), or "" when the catalog didn't know the monitor.</param>
/// <param name="Monitor">The HMONITOR at the freeze.</param>
/// <param name="Bounds">The display in physical virtual-screen pixels.</param>
/// <param name="Dpi">Its effective DPI (96 at 100%).</param>
public sealed class CapturedScreen(string displayId, nint monitor, PixelRect bounds, int dpi, byte[] pixels)
{
    public string DisplayId { get; } = displayId;
    public nint Monitor { get; } = monitor;
    public PixelRect Bounds { get; } = bounds;
    public int Dpi { get; } = dpi;
    public byte[] Pixels { get; } = pixels.Length == (long)bounds.Width * bounds.Height * 4
        ? pixels : throw new ArgumentException("The pixels don't match the display's size.", nameof(pixels));

    public int Stride => Bounds.Width * 4;

    /// <summary>The pixel at a virtual-screen point as 0xAARRGGBB, or null off this display.</summary>
    public uint? PixelAt(PixelPoint p)
    {
        if (!Bounds.Contains(p))
            return null;
        int i = ((p.Y - Bounds.Y) * Bounds.Width + (p.X - Bounds.X)) * 4;
        return (uint)(Pixels[i] | Pixels[i + 1] << 8 | Pixels[i + 2] << 16 | Pixels[i + 3] << 24);
    }
}

/// <summary>A top-level window as it was at the freeze, front first in <see cref="Freeze.Windows"/>.</summary>
/// <param name="Frame">Its visible frame (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>) in physical virtual-screen pixels.</param>
public sealed record FrozenWindow(nint Handle, string Title, string ProcessPath, PixelRect Frame);

/// <summary>An image cut from the frozen screen: BGRA, top row first, <see cref="Stride"/> = width × 4.</summary>
public sealed record CapturedImage(int Width, int Height, byte[] Pixels)
{
    public int Stride => Width * 4;
}

/// <summary>
/// Every display read once at the press, and the windows on them at that moment. Everything after the press (the drag,
/// window snapping, the magnifier, the cut) reads this, never the live screen. Released with the overlay.
/// </summary>
public sealed class Freeze(IReadOnlyList<CapturedScreen> screens, IReadOnlyList<FrozenWindow> windows, DateTimeOffset time)
{
    public IReadOnlyList<CapturedScreen> Screens { get; } = screens;

    /// <summary>Front first: visible, not cloaked, not minimized, Chippi's own overlays and puck left out.</summary>
    public IReadOnlyList<FrozenWindow> Windows { get; } = windows;

    public DateTimeOffset Time { get; } = time;

    /// <summary>The rectangle around every display; gaps between displays of different sizes are inside it.</summary>
    public PixelRect VirtualBounds => Screens.Aggregate(default(PixelRect), (r, s) => r.Union(s.Bounds));

    public IReadOnlyList<PixelRect> Displays => [.. Screens.Select(s => s.Bounds)];

    public CapturedScreen? ScreenAt(PixelPoint p) => Screens.FirstOrDefault(s => s.Bounds.Contains(p));

    /// <summary>The display a point is on, or the nearest one for a point in a gap or off every display.</summary>
    public CapturedScreen NearestScreen(PixelPoint p) =>
        ScreenAt(p) ?? Screens.MinBy(s => SelectionMath.DistanceSquared(s.Bounds, p))
        ?? throw new InvalidOperationException("A freeze has at least one display.");

    public uint? PixelAt(PixelPoint p) => ScreenAt(p)?.PixelAt(p);

    /// <summary>The frontmost window whose frame holds the point, clipped to the displays it's on; null over the desktop.</summary>
    public FrozenWindow? WindowAt(PixelPoint p) => Windows.FirstOrDefault(w => w.Frame.Contains(p));

    /// <summary>
    /// The pixels inside <paramref name="rect"/>: each display's own, gaps between displays and pixels outside the mask
    /// transparent (alpha 0).
    /// </summary>
    public CapturedImage Cut(PixelRect rect, SelectionMask? mask = null)
    {
        if (rect.IsEmpty)
            return new CapturedImage(0, 0, []);
        var pixels = new byte[(long)rect.Width * rect.Height * 4];
        foreach (var screen in Screens)
        {
            var part = rect.Intersect(screen.Bounds);
            if (part.IsEmpty)
                continue;
            int rowBytes = part.Width * 4;
            for (int y = part.Y; y < part.Bottom; y++)
            {
                int from = ((y - screen.Bounds.Y) * screen.Bounds.Width + (part.X - screen.Bounds.X)) * 4;
                int to = ((y - rect.Y) * rect.Width + (part.X - rect.X)) * 4;
                Buffer.BlockCopy(screen.Pixels, from, pixels, to, rowBytes);
            }
        }
        if (mask is not null)
            mask.Apply(rect, pixels);
        return new CapturedImage(rect.Width, rect.Height, pixels);
    }
}

/// <summary>Which pixels of a freeform selection's bounding rectangle are inside its outline: one byte a pixel, 0 or 255.</summary>
public sealed class SelectionMask(PixelRect bounds, byte[] inside)
{
    public PixelRect Bounds { get; } = bounds;

    public byte[] Inside { get; } = inside.Length == bounds.Area
        ? inside : throw new ArgumentException("The mask doesn't match its bounds.", nameof(inside));

    public bool Contains(PixelPoint p) => Bounds.Contains(p) && Inside[(p.Y - Bounds.Y) * Bounds.Width + p.X - Bounds.X] != 0;

    /// <summary>Clears the alpha of every pixel of <paramref name="pixels"/> (laid out over <paramref name="rect"/>) outside the mask.</summary>
    internal void Apply(PixelRect rect, byte[] pixels)
    {
        for (int y = rect.Y; y < rect.Bottom; y++)
            for (int x = rect.X; x < rect.Right; x++)
                if (!Contains(new PixelPoint(x, y)))
                {
                    int i = ((y - rect.Y) * rect.Width + x - rect.X) * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
                }
    }
}

/// <summary>How the selector selects (capture-layer.md, Modes).</summary>
public enum CaptureMode
{
    /// <summary>Drag a rectangle; a click takes the window under the pointer.</summary>
    Region,

    /// <summary>Hover and click a window; a drag does nothing.</summary>
    Window,

    /// <summary>The display under the pointer, selected at once.</summary>
    Screen,

    /// <summary>Every display, selected at once.</summary>
    AllDisplays,

    /// <summary>Drag a closed outline.</summary>
    Freeform,

    /// <summary>The previous rectangle on a fresh freeze; Region when there's none.</summary>
    LastRegion,

    /// <summary>Click a pixel (Color picker): no selection, the magnifier always on, the screen not dimmed.</summary>
    PickPoint,
}

/// <summary>
/// What the selector hands the feature that asked: the selected rectangle and the frozen screen it was cut from.
/// </summary>
/// <param name="Rect">In physical virtual-screen pixels; may span displays.</param>
/// <param name="Freeze">The frozen displays and windows; <see cref="Image"/> cuts from it.</param>
/// <param name="Mask">For freeform, which pixels of <see cref="Rect"/> are inside the outline.</param>
/// <param name="Window">The window clicked, for a window selection.</param>
/// <param name="FastPath">Ctrl was held when the drag started (Screen capture copies on release).</param>
/// <param name="Display">The display the drag was released on, where the bars go.</param>
public sealed record Selection(PixelRect Rect, Freeze Freeze, CaptureMode Mode, SelectionMask? Mask, FrozenWindow? Window, bool FastPath, CapturedScreen Display)
{
    /// <summary>The displays the selection touches, with their pixels, DPI and ids.</summary>
    public IReadOnlyList<CapturedScreen> Screens => [.. Freeze.Screens.Where(s => !s.Bounds.Intersect(Rect).IsEmpty)];

    public DateTimeOffset Time => Freeze.Time;

    /// <summary>The selected pixels, cut from each display's own frozen image.</summary>
    public CapturedImage Image() => Freeze.Cut(Rect, Mask);
}
