// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class CaptureClipboardTests
{
    [Fact]
    public void Clipboard_pixels_are_straight_Bgra_with_transparency_preserved()
    {
        var pixels = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Pbgra32, null,
            new byte[] { 0, 0, 64, 128, 0, 0, 0, 0 }, 8);
        pixels.Freeze();
        var image = CaptureClipboard.Content(pixels).Image!;
        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.InRange(image.Bgra[2], (byte)127, (byte)128);
        Assert.Equal(128, image.Bgra[3]);
        Assert.Equal(new byte[4], image.Bgra[4..]);
    }
}
