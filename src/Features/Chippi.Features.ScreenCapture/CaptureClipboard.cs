// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Clipboard;

namespace Chippi.Features.ScreenCapture;

public static class CaptureClipboard
{
    /// <summary>The shell owns retries and clipboard access. Only composed, flattened pixels leave the draft.</summary>
    public static async Task<bool> CopyAsync(IClipboardHub clipboard, BitmapSource flattened, CancellationToken cancel = default)
        => await clipboard.WriteAsync(Content(flattened), cancel) == ClipStatus.Ok;

    internal static ClipContent Content(BitmapSource flattened)
    {
        // RenderTargetBitmap is premultiplied; the clipboard hub takes straight BGRA.
        var pixels = flattened.Format == PixelFormats.Bgra32 ? flattened
            : new FormatConvertedBitmap(flattened, PixelFormats.Bgra32, null, 0);
        int stride = checked(pixels.PixelWidth * 4);
        byte[] bytes = new byte[checked(stride * pixels.PixelHeight)];
        pixels.CopyPixels(bytes, stride, 0);
        return ClipContent.FromImage(new ClipImage(pixels.PixelWidth, pixels.PixelHeight, bytes));
    }
}
