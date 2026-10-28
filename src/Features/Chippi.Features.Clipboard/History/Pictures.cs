// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Clipboard;

namespace Chippi.Features.Clipboard.History;

/// <summary>A copied picture as History keeps it: a PNG, and a thumbnail for the page (architecture §4). Any thread.</summary>
internal static class Pictures
{
    /// <summary>The thumbnail's width at most; the page shows it at 208 and in a 32 × 20 row.</summary>
    public const int ThumbnailWidth = 320;

    /// <summary>The PNG and its thumbnail, or null when the PNG is over <see cref="ClipLimits.ImageEncodedBytes"/>.</summary>
    public static (byte[] Png, byte[] Thumbnail)? Encode(ClipImage image)
    {
        var source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra, image.Width * 4);
        source.Freeze();
        byte[] png = Png(source);
        if (png.Length > ClipLimits.ImageEncodedBytes)
            return null;
        BitmapSource thumbnail = source;
        if (image.Width > ThumbnailWidth)
        {
            double scale = (double)ThumbnailWidth / image.Width;
            thumbnail = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            thumbnail.Freeze();
        }
        return (png, Png(thumbnail));
    }

    /// <summary>The PNG back as pixels, to put on the clipboard.</summary>
    public static ClipImage Decode(byte[] png)
    {
        var frame = Load(png);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
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
