// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Features.ScreenCapture;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class ExportTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static BitmapSource Source(int width = 64, int height = 64)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = 255; }
        return BitmapSource.Create(width, height, 192, 192, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    [Fact]
    public void Redaction_crop_and_reopen_export_only_flattened_pixels() => Sta(() =>
    {
        var draft = new CaptureDraft(Source());
        draft.Add(new(MarkupTool.Redact, [new Point(0, 0), new Point(32, 32)], Colors.Red));
        draft.SetCrop(new Int32Rect(0, 0, 32, 32));
        var flattened = draft.Flatten();
        byte[] pixels = new byte[32 * 32 * 4]; flattened.CopyPixels(pixels, 128, 0);
        for (int i = 0; i < pixels.Length; i += 4) { Assert.Equal(0, pixels[i + 2]); Assert.Equal(255, pixels[i + 3]); }
        var reopened = new CaptureDraft(flattened);
        Assert.False(reopened.Undo());
        Assert.Equal(32, reopened.Flatten().PixelWidth);
        Assert.True(draft.Undo()); Assert.Equal(64, draft.Flatten().PixelWidth);
        Assert.True(draft.Redo());
        draft.Add(new(MarkupTool.Line, [new Point(0, 0), new Point(2, 2)], Colors.Blue));
        Assert.False(draft.Redo());
    });

    [Fact]
    public void Save_collisions_and_failed_overwrite_preserve_existing_files() => Sta(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var pixels = new CaptureDraft(Source(3440, 1440)).Flatten();
            var now = DateTimeOffset.Now;
            string first = CaptureFiles.Save(pixels, folder, CaptureFileType.Png, now);
            string second = CaptureFiles.Save(pixels, folder, CaptureFileType.Png, now);
            Assert.NotEqual(first, second);
            byte[] original = File.ReadAllBytes(first);
            Assert.Throws<IOException>(() => CaptureFiles.SaveAs(pixels, first, CaptureFileType.Jpeg, false));
            Assert.Equal(original, File.ReadAllBytes(first));
            Assert.Equal(2, Directory.GetFiles(folder).Length);
            var decoded = new BitmapImage();
            decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad; decoded.UriSource = new Uri(first); decoded.EndInit();
            Assert.Equal(3440, decoded.PixelWidth); Assert.Equal(1440, decoded.PixelHeight);
        }
        finally { Directory.Delete(folder, true); }
    });

    [Fact]
    public void Jpeg_white_background_and_all_tools_encode() => Sta(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var transparent = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 64 * 4], 256);
            var draft = new CaptureDraft(transparent);
            foreach (var tool in Enum.GetValues<MarkupTool>())
                draft.Add(new(tool, [new Point(10, 10), new Point(30, 30), new Point(15, 20)], Colors.Blue, 3, text: "Test"));
            string path = CaptureFiles.Save(draft.Flatten(), folder, CaptureFileType.Jpeg, DateTimeOffset.Now);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit();
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            byte[] pixel = new byte[4]; converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Assert.InRange(pixel[0], (byte)245, (byte)255);
            Assert.InRange(pixel[1], (byte)245, (byte)255);
            Assert.InRange(pixel[2], (byte)245, (byte)255);
        }
        finally { Directory.Delete(folder, true); }
    });
}
