// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Clipboard;

namespace Chippi.Features.Shelf;

/// <summary>A dropped picture as the Shelf keeps it: a PNG, and a thumbnail for its item. Any thread, frozen sources only.</summary>
internal static class ShelfPictures
{
    /// <summary>The thumbnail's width at most; the item shows it at 48 × 28.</summary>
    public const int ThumbnailWidth = 320;

    /// <summary>The PNG and its thumbnail, or null when the picture is over <see cref="ClipLimits.ImagePixels"/> or its PNG over <see cref="ClipLimits.ImageEncodedBytes"/>.</summary>
    public static (byte[] Png, byte[] Thumbnail)? Encode(BitmapSource source)
    {
        if ((long)source.PixelWidth * source.PixelHeight > ClipLimits.ImagePixels)
            return null;
        byte[] png = Png(source);
        if (png.Length > ClipLimits.ImageEncodedBytes)
            return null;
        BitmapSource thumbnail = source;
        if (source.PixelWidth > ThumbnailWidth)
        {
            double scale = (double)ThumbnailWidth / source.PixelWidth;
            thumbnail = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            thumbnail.Freeze();
        }
        return (png, Png(thumbnail));
    }

    /// <summary>The PNG back as pixels, to put on the clipboard.</summary>
    public static ClipImage Decode(byte[] png)
    {
        var bgra = new FormatConvertedBitmap(Load(png), PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return new ClipImage(bgra.PixelWidth, bgra.PixelHeight, pixels);
    }

    /// <summary>A PNG as an image the page can show.</summary>
    public static BitmapSource Load(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    private static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
