// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Clipboard;

namespace Chippi.Features.Clipboard.History;

/// <summary>
/// A row's picture for a copy that is one image file, read from the file on disk (task 48). It stays a
/// file row: only the glyph changes. Attributes come first, so a cloud placeholder or a missing file is
/// never opened. Any thread.
/// </summary>
internal sealed class FileThumbnails(Func<string, FileAttributes?> attributes, Func<string, Stream> open)
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000, RecallOnDataAccess = (FileAttributes)0x400000;

    private static readonly HashSet<string> Extensions = new([".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"], StringComparer.OrdinalIgnoreCase);

    public static readonly FileThumbnails Disk = new(
        path =>
        {
            try { return File.GetAttributes(path); }
            catch (Exception) { return null; }
        },
        path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));

    /// <summary>The file to draw for <paramref name="entry"/>: exactly one file with a picture extension, else null.</summary>
    public static string? Picture(HistoryEntry entry) =>
        entry.Kind == EntryKind.Files && entry.Files is [var path] && Extensions.Contains(Path.GetExtension(path)) ? path : null;

    /// <summary>The picture at most <see cref="Pictures.ThumbnailWidth"/> wide, or null when it's online-only, gone, over the history limits or can't be decoded.</summary>
    public BitmapSource? Read(string path)
    {
        if (attributes(path) is not { } found || (found & (FileAttributes.Directory | FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0)
            return null;
        try
        {
            using var stream = open(path);
            if (stream.Length > ClipLimits.ImageEncodedBytes)
                return null;
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None).Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > ClipLimits.ImagePixels)
                return null;
            BitmapSource image = frame;
            if (frame.PixelWidth > Pictures.ThumbnailWidth)
            {
                double scale = (double)Pictures.ThumbnailWidth / frame.PixelWidth;
                image = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            }
            // Decoded now, while the file is open.
            var thumbnail = new CachedBitmap(image, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            thumbnail.Freeze();
            return thumbnail;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
