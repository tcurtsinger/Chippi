// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace Chippi.Media;

internal static class MediaImageExport
{
    internal static readonly string[] MetadataNames = ["System.Title", "System.Author", "System.Comment", "System.Copyright", "System.Keywords", "System.Subject", "System.Media.SubTitle", "System.Media.EncodedBy", "System.Media.Publisher", "System.Media.Writer", "System.Photo.DateTaken", "System.Photo.CameraManufacturer", "System.Photo.CameraModel", "System.Photo.CameraSerialNumber", "System.GPS.Latitude", "System.GPS.Longitude", "System.GPS.Altitude"];

    public static async Task ExportAsync(MediaFileInfo file, string destination, MediaToolOptions options, IProgress<double>? progress, CancellationToken cancellationToken, BitmapInterpolationMode interpolation = BitmapInterpolationMode.Fant)
    {
        await using var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var source = input.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(source).AsTask(cancellationToken);
        int width = checked((int)decoder.OrientedPixelWidth), height = checked((int)decoder.OrientedPixelHeight);
        if ((long)width * height > MediaFileInfo.MaxImagePixels) throw new InvalidOperationException("Too large");
        // Normalize orientation before any resize or crop; exported pixels no longer need an EXIF rotation tag.
        byte[] pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken)).DetachPixelData();
        bool jpeg = options.OutputFor(file) == MediaOutputFormat.Jpeg;
        if (jpeg) Flatten(pixels, options.Background);
        progress?.Report(30);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var target = output.AsRandomAccessStream();
        var properties = new BitmapPropertySet();
        if (jpeg) properties["ImageQuality"] = new BitmapTypedValue(options.Quality switch { MediaQuality.Small => .55f, MediaQuality.High => .92f, _ => .78f }, PropertyType.Single);
        var encoder = await BitmapEncoder.CreateAsync(jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId, target, properties).AsTask(cancellationToken);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, jpeg ? BitmapAlphaMode.Ignore : BitmapAlphaMode.Straight, (uint)width, (uint)height, 96, 96, pixels);
        var size = options.SizeFor(width, height);
        encoder.BitmapTransform.ScaledWidth = (uint)size.ScaledWidth; encoder.BitmapTransform.ScaledHeight = (uint)size.ScaledHeight;
        encoder.BitmapTransform.InterpolationMode = interpolation;
        if (size.Width != size.ScaledWidth || size.Height != size.ScaledHeight)
            encoder.BitmapTransform.Bounds = new BitmapBounds { X = (uint)size.CropX, Y = (uint)size.CropY, Width = (uint)size.Width, Height = (uint)size.Height };
        if (options.KeepMetadata)
        {
            var metadata = await decoder.BitmapProperties.GetPropertiesAsync(MetadataNames).AsTask(cancellationToken);
            if (metadata.Count > 0)
            {
                var writable = new BitmapPropertySet(); foreach (var pair in metadata) writable[pair.Key] = pair.Value;
                await encoder.BitmapProperties.SetPropertiesAsync(writable).AsTask(cancellationToken);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        await encoder.FlushAsync().AsTask(cancellationToken);
        await output.FlushAsync(cancellationToken); progress?.Report(100);
    }

    internal static void Flatten(byte[] pixels, uint background)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int alpha = pixels[i + 3];
            pixels[i] = (byte)((pixels[i] * alpha + (background & 255) * (255 - alpha) + 127) / 255);
            pixels[i + 1] = (byte)((pixels[i + 1] * alpha + ((background >> 8) & 255) * (255 - alpha) + 127) / 255);
            pixels[i + 2] = (byte)((pixels[i + 2] * alpha + ((background >> 16) & 255) * (255 - alpha) + 127) / 255);
            pixels[i + 3] = 255;
        }
    }
}
