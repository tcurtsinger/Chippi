// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Chippi.Media;

public sealed record RecordingInfo(TimeSpan Duration, int Width, int Height, byte[] Poster);

/// <summary>Exports use a new temporary file beside their destination; the original recording is never modified.</summary>
public static class RecordingExports
{
    public static async Task CopyAsync(string original, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string temporary = TemporaryFor(original, destination, ".mp4");
        try
        {
            await using (var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                byte[] buffer = new byte[131072]; int count; long copied = 0;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                { await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken); copied += count; progress?.Report(copied * 100d / Math.Max(1, input.Length)); }
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, Path.GetFullPath(destination), true);
        }
        finally { DeletePartial(temporary); }
    }
    public static async Task<RecordingInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path))).AsTask(cancellationToken);
        var format = clip.GetVideoEncodingProperties();
        var composition = new MediaComposition(); composition.Clips.Add(clip);
        using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.Zero, 240, 135, VideoFramePrecision.NearestFrame).AsTask(cancellationToken);
        using var input = thumbnail.AsStreamForRead();
        using var output = new MemoryStream(); await input.CopyToAsync(output, cancellationToken);
        return new(clip.OriginalDuration, (int)format.Width, (int)format.Height, output.ToArray());
    }

    public static async Task TrimAsync(string original, string destination, TimeSpan start, TimeSpan end, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string temporary = TemporaryFor(original, destination, ".mp4");
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(original))).AsTask(cancellationToken);
            ValidateRange(start, end, clip.OriginalDuration);
            var format = clip.GetVideoEncodingProperties();
            clip.TrimTimeFromStart = start; clip.TrimTimeFromEnd = clip.OriginalDuration - end;
            var composition = new MediaComposition(); composition.Clips.Add(clip);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(temporary)!);
            var file = await folder.CreateFileAsync(Path.GetFileName(temporary), CreationCollisionOption.FailIfExists);
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Width = format.Width; profile.Video.Height = format.Height;
            if (format.FrameRate.Numerator > 0 && format.FrameRate.Denominator > 0)
            { profile.Video.FrameRate.Numerator = format.FrameRate.Numerator; profile.Video.FrameRate.Denominator = format.FrameRate.Denominator; }
            profile.Video.Bitrate = format.Bitrate > 0 ? format.Bitrate : (uint)Math.Clamp((long)format.Width * format.Height * 4, 2_000_000, 50_000_000);
            var result = await composition.RenderToFileAsync(file, MediaTrimmingPreference.Precise, profile).AsTask(cancellationToken, progress);
            if (result != TranscodeFailureReason.None) throw new InvalidOperationException("Video export failed: " + result);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, Path.GetFullPath(destination), overwrite: true);
        }
        finally { DeletePartial(temporary); }
    }

    public static async Task GifAsync(string original, string destination, TimeSpan start, TimeSpan end, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string temporary = TemporaryFor(original, destination, ".gif");
        string looped = temporary + ".loop.gif";
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(original))).AsTask(cancellationToken);
            ValidateRange(start, end, clip.OriginalDuration);
            if (end - start > TimeSpan.FromSeconds(60)) throw new ArgumentException("Choose a range of 60 seconds or less for GIF export.");
            var format = clip.GetVideoEncodingProperties();
            int count = (int)Math.Ceiling((end - start).TotalSeconds * 15);
            var composition = new MediaComposition(); composition.Clips.Add(clip);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(temporary)!);
            var file = await folder.CreateFileAsync(Path.GetFileName(temporary), CreationCollisionOption.FailIfExists);
            using (var output = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.GifEncoderId, output);
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var image = await composition.GetThumbnailAsync(start + TimeSpan.FromSeconds(i / 15d), (int)format.Width, (int)format.Height, VideoFramePrecision.NearestFrame).AsTask(cancellationToken);
                    var decoder = await BitmapDecoder.CreateAsync(image).AsTask(cancellationToken);
                    var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, decoder.PixelWidth, decoder.PixelHeight, 96, 96, pixels.DetachPixelData());
                    ushort delay = (ushort)((i + 1) * 100 / 15 - i * 100 / 15);
                    await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet
                    {
                        ["/grctlext/Delay"] = new BitmapTypedValue(delay, PropertyType.UInt16),
                        ["/grctlext/Disposal"] = new BitmapTypedValue((byte)1, PropertyType.UInt8),
                    }).AsTask(cancellationToken);
                    if (i + 1 < count) await encoder.GoToNextFrameAsync().AsTask(cancellationToken);
                    else await encoder.FlushAsync().AsTask(cancellationToken);
                    progress?.Report((i + 1d) * 100 / count);
                }
            }
            using (var input = File.OpenRead(temporary))
            using (var output = new FileStream(looped, FileMode.CreateNew, FileAccess.Write))
                await AddGifLoopAsync(input, output, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(looped, Path.GetFullPath(destination), overwrite: true);
        }
        finally { DeletePartial(looped); DeletePartial(temporary); }
    }

    internal static void ValidateRange(TimeSpan start, TimeSpan end, TimeSpan duration)
    {
        if (start < TimeSpan.Zero || end > duration || end <= start) throw new ArgumentException("Choose a valid start and end time.");
    }
    private static string TemporaryFor(string original, string destination, string extension)
    {
        string output = Path.GetFullPath(destination);
        if (string.Equals(Path.GetFullPath(original), output, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a different file to preserve the original recording.");
        string folder = Path.GetDirectoryName(output) ?? throw new ArgumentException("Choose an output folder.");
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("The output folder is unavailable.");
        return Path.Combine(folder, ".chippi-" + Guid.NewGuid().ToString("N") + extension);
    }
    private static void DeletePartial(string path) { if (File.Exists(path)) File.Delete(path); }

    internal static async Task AddGifLoopAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        byte[] header = new byte[13]; await input.ReadExactlyAsync(header, cancellationToken);
        if (!header.AsSpan(0, 6).SequenceEqual("GIF89a"u8) && !header.AsSpan(0, 6).SequenceEqual("GIF87a"u8)) throw new InvalidDataException("GIF encoding failed.");
        await output.WriteAsync(header, cancellationToken);
        if ((header[10] & 0x80) != 0)
        {
            byte[] palette = new byte[3 * (1 << ((header[10] & 7) + 1))];
            await input.ReadExactlyAsync(palette, cancellationToken); await output.WriteAsync(palette, cancellationToken);
        }
        await output.WriteAsync(new byte[] { 0x21, 0xff, 11, 78, 69, 84, 83, 67, 65, 80, 69, 50, 46, 48, 3, 1, 0, 0, 0 }, cancellationToken);
        await input.CopyToAsync(output, cancellationToken);
    }
}
