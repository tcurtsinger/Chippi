// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.Features.ScreenCapture;

public enum CaptureFileType { Png, Jpeg }

/// <summary>Publishes encoded pixels only after the sibling temporary file is closed.</summary>
public static class CaptureFiles
{
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public static string Save(BitmapSource flattened, string folder, CaptureFileType type, DateTimeOffset time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("An absolute destination is required.", nameof(folder));
        Directory.CreateDirectory(folder);
        string stem = "Screenshot-" + time.ToString("yyyy-MM-dd-HHmmss-fff", CultureInfo.InvariantCulture);
        string extension = type == CaptureFileType.Png ? ".png" : ".jpg";
        string temporary = Path.Combine(folder, ".chippi-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Encode(flattened, temporary, type);
            for (int suffix = 0; ; suffix++)
            {
                string destination = Path.Combine(folder, stem + (suffix == 0 ? "" : "-" + suffix.ToString(CultureInfo.InvariantCulture)) + extension);
                try { File.Move(temporary, destination); return destination; }
                catch (IOException) when (File.Exists(destination)) { }
            }
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>The caller obtains overwrite confirmation before passing overwrite=true.</summary>
    public static void SaveAs(BitmapSource flattened, string destination, CaptureFileType type, bool overwrite)
    {
        if (!Path.IsPathFullyQualified(destination)) throw new ArgumentException("An absolute destination is required.", nameof(destination));
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".chippi-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Encode(flattened, temporary, type);
            File.Move(temporary, destination, overwrite);
        }
        finally { File.Delete(temporary); }
    }

    public static void Encode(BitmapSource pixels, string path, CaptureFileType type)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        BitmapEncoder encoder;
        if (type == CaptureFileType.Jpeg)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, pixels.PixelWidth, pixels.PixelHeight);
                dc.DrawRectangle(Brushes.White, null, bounds);
                dc.DrawImage(pixels, bounds);
            }
            var opaque = new RenderTargetBitmap(pixels.PixelWidth, pixels.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            opaque.Render(visual);
            pixels = opaque;
            encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        }
        else encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(pixels));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        stream.Flush(true);
    }
}
