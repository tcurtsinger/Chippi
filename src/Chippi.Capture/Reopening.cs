// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>
/// Shows a finished capture on a frozen screen (capture-layer.md, Reopening a capture): centred on the display under the
/// pointer at its own size, or shrunk to fit inside a margin when it's bigger than the display. Any thread.
/// </summary>
public static class Reopening
{
    /// <summary>
    /// The freeze with the image drawn into its display's pixels, the rectangle it covers, and the scale. Transparent
    /// pixels (a freeform outline's outside, gaps between displays) show the screen under them.
    /// </summary>
    /// <param name="margin">Physical pixels kept clear on each side of the display when the image has to shrink.</param>
    public static (Freeze Freeze, PixelRect Rect, double Scale, CapturedScreen Display) Place(Freeze live, CapturedImage image, PixelPoint pointer, int margin)
    {
        if (image.Width <= 0 || image.Height <= 0)
            throw new ArgumentException("The image is empty.", nameof(image));
        var display = live.NearestScreen(pointer);
        var b = display.Bounds;
        double scale = 1;
        if (image.Width > b.Width || image.Height > b.Height)
        {
            int roomW = Math.Max(1, b.Width - 2 * margin), roomH = Math.Max(1, b.Height - 2 * margin);
            scale = Math.Min((double)roomW / image.Width, (double)roomH / image.Height);
        }
        int w = Math.Clamp((int)Math.Round(image.Width * scale), 1, b.Width);
        int h = Math.Clamp((int)Math.Round(image.Height * scale), 1, b.Height);
        var rect = new PixelRect(b.X + (b.Width - w) / 2, b.Y + (b.Height - h) / 2, w, h);
        var shown = scale == 1 ? image.Pixels : Shrink(image, w, h);
        var pixels = (byte[])display.Pixels.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 4, d = ((rect.Y - b.Y + y) * b.Width + rect.X - b.X + x) * 4;
                int a = shown[s + 3];
                for (int c = 0; c < 3; c++)
                    pixels[d + c] = (byte)((shown[s + c] * a + pixels[d + c] * (255 - a) + 127) / 255);
                pixels[d + 3] = 255;
            }
        var screen = new CapturedScreen(display.DisplayId, display.Monitor, b, display.Dpi, pixels);
        // No windows: nothing on a reopened capture snaps.
        var freeze = new Freeze([.. live.Screens.Select(s => ReferenceEquals(s, display) ? screen : s)], [], live.Time);
        return (freeze, rect, scale, screen);
    }

    /// <summary>An area average down to <paramref name="w"/> by <paramref name="h"/>, weighted by alpha.</summary>
    internal static byte[] Shrink(CapturedImage image, int w, int h)
    {
        var result = new byte[(long)w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int y0 = (int)((long)y * image.Height / h), y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * image.Height / h));
            for (int x = 0; x < w; x++)
            {
                int x0 = (int)((long)x * image.Width / w), x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * image.Width / w));
                long blue = 0, green = 0, red = 0, alpha = 0;
                for (int sy = y0; sy < y1; sy++)
                    for (int sx = x0; sx < x1; sx++)
                    {
                        int i = (sy * image.Width + sx) * 4;
                        int a = image.Pixels[i + 3];
                        blue += image.Pixels[i] * a;
                        green += image.Pixels[i + 1] * a;
                        red += image.Pixels[i + 2] * a;
                        alpha += a;
                    }
                int o = (y * w + x) * 4;
                if (alpha > 0)
                {
                    result[o] = (byte)(blue / alpha);
                    result[o + 1] = (byte)(green / alpha);
                    result[o + 2] = (byte)(red / alpha);
                }
                result[o + 3] = (byte)(alpha / ((y1 - y0) * (x1 - x0)));
            }
        }
        return result;
    }
}
