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
    internal static Task<T> OnWorkerAsync<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "Chippi capture output" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return done.Task;
    }

    internal static byte[] Thumbnail(BitmapSource pixels)
    {
        double scale = Math.Min(1, 240d / pixels.PixelWidth);
        var image = new TransformedBitmap(pixels, new ScaleTransform(scale, scale));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream);
        return stream.ToArray();
    }
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    /// <summary>Opens <paramref name="folder"/> in File Explorer; false when it's missing or Windows refuses, never a guessed path.</summary>
    public static bool OpenFolder(string? folder)
    {
        try
        {
            if (folder == null || !Directory.Exists(folder)) return false;
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return false; }
    }

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
