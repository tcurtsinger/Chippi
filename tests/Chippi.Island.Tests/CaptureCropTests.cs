// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Chippi.Capture;
using Chippi.UI.Capture;
using Xunit;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.Island.Tests;

public sealed class CaptureCropTests
{
    [Fact]
    public void Crop_changes_the_visible_frame_dimming_and_size_and_undo_restores_them() => WpfTestThread.Run(() =>
    {
        var bytes = new byte[500 * 300 * 4];
        for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = 224; bytes[i + 1] = 192; bytes[i + 2] = 128; bytes[i + 3] = 255; }
        var screen = new CapturedScreen("fixture", 0, new(0, 0, 500, 300), 96, bytes);
        var freeze = new Freeze([screen], [], DateTimeOffset.UnixEpoch);
        var selector = new Selector(freeze, CaptureMode.Region, null, new(50, 20), new(4, 4), 6);
        selector.Down(new(50, 20), false); selector.Up(new(449, 269));
        var view = new OverlayView(); view.Place(screen.Bounds, 96); view.SetImage(screen);
        var request = new CaptureRequest(CaptureMode.Region);
        view.Render(selector, request, screen, new CapturePreview(new(100, 80, 200, 100), new(200, 100)));
        var cropped = Render(view);
        var frame = Assert.Single(Descendants<Rectangle>(view), r => r.Visibility == Visibility.Visible && r.ActualWidth > 0 && r.Stroke == Brushes.White);
        Assert.Equal(200, frame.Width); Assert.Equal(100, frame.Height);
        Assert.Equal(100, Canvas.GetLeft(frame)); Assert.Equal(80, Canvas.GetTop(frame));
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "200 × 100");
        byte[] pixel = new byte[4];
        cropped.CopyPixels(new Int32Rect(75, 150, 1, 1), pixel, 4, 0); Assert.InRange(pixel[2], (byte)60, (byte)80);
        cropped.CopyPixels(new Int32Rect(150, 130, 1, 1), pixel, 4, 0); Assert.Equal(128, pixel[2]);
        view.Render(selector, request, screen);
        var restored = Render(view);
        Assert.Equal(400, frame.Width); Assert.Equal(250, frame.Height);
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "400 × 250");
        restored.CopyPixels(new Int32Rect(75, 150, 1, 1), pixel, 4, 0); Assert.Equal(128, pixel[2]);
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            Save(cropped, System.IO.Path.Combine(folder, "capture-crop-applied.png"));
            Save(restored, System.IO.Path.Combine(folder, "capture-crop-undone.png"));
        }
    });
    private static RenderTargetBitmap Render(FrameworkElement view)
    {
        view.Measure(new Size(500, 300)); view.Arrange(new Rect(0, 0, 500, 300)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(500, 300, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view); return bitmap;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
