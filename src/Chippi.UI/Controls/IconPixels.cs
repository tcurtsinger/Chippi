// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.UI.Controls;

/// <summary>
/// The arithmetic on an icon's pixels (32-bit BGRA, top row first). Pure, so it's tested without Windows' icons.
/// </summary>
public static class IconPixels
{
    /// <summary>
    /// Whether the pixels carry straight alpha: a color channel above its alpha can't be premultiplied. Windows hands app
    /// icons over this way, where WPF draws premultiplied.
    /// </summary>
    public static bool IsStraight(ReadOnlySpan<byte> bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Straight alpha made premultiplied, in place. Drawn unconverted, a soft edge comes out brighter than the icon itself:
    /// jagged, with specks of color in the corners.
    /// </summary>
    public static void Premultiply(Span<byte> bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a == 255)
                continue;
            bgra[i] = (byte)((bgra[i] * a + 127) / 255);
            bgra[i + 1] = (byte)((bgra[i + 1] * a + 127) / 255);
            bgra[i + 2] = (byte)((bgra[i + 2] * a + 127) / 255);
        }
    }

    /// <summary>The smallest rectangle holding every pixel that isn't clear; empty for a clear picture.</summary>
    public static Int32Rect ArtBox(ReadOnlySpan<byte> bgra, int width, int height)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                if (bgra[row + x * 4 + 3] == 0)
                    continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                bottom = y;
            }
        }
        return right < 0 ? Int32Rect.Empty : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// The icon's artwork without its clear margins, centered in a square and scaled once to <paramref name="pixels"/> a
    /// side: icons drawn small inside their frame come out the size of the ones that fill it, and nothing is rescaled while
    /// it's drawn. Frozen. Null for a clear picture.
    /// </summary>
    public static BitmapSource? Fit(BitmapSource? source, int pixels)
    {
        if (source is null || pixels <= 0)
            return null;
        if (source.Format != PixelFormats.Pbgra32)
            source = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        int width = source.PixelWidth, height = source.PixelHeight;
        var all = new byte[width * height * 4];
        source.CopyPixels(all, width * 4, 0);
        var box = ArtBox(all, width, height);
        if (box.IsEmpty)
            return null;
        int side = Math.Max(box.Width, box.Height);
        var square = new byte[side * side * 4];
        int dx = (side - box.Width) / 2, dy = (side - box.Height) / 2;
        for (int y = 0; y < box.Height; y++)
            Array.Copy(all, ((box.Y + y) * width + box.X) * 4, square, ((dy + y) * side + dx) * 4, box.Width * 4);
        BitmapSource art = BitmapSource.Create(side, side, 96, 96, PixelFormats.Pbgra32, null, square, side * 4);
        if (side != pixels)
        {
            double scale = (double)pixels / side;
            art = new TransformedBitmap(art, new ScaleTransform(scale, scale));
        }
        // Read out once, so the scaling is done here and not each time it's drawn.
        int w = art.PixelWidth, h = art.PixelHeight;
        var baked = new byte[w * h * 4];
        art.CopyPixels(baked, w * 4, 0);
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, baked, w * 4);
        result.Freeze();
        return result;
    }
}
