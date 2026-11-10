// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Chippi.Media;

/// <summary>Windows' installed WIC decoders and Media Foundation codecs; exports never upload or play media.</summary>
public sealed class MediaToolsCodec : IMediaToolsCodec
{
    public static async Task<byte[]> PreviewAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(path); using var stream = input.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        double ratio = Math.Min(1, Math.Min(230d / decoder.OrientedPixelWidth, 230d / decoder.OrientedPixelHeight));
        var transform = new BitmapTransform { ScaledWidth = (uint)Math.Max(1, (int)Math.Round(decoder.PixelWidth * ratio)), ScaledHeight = (uint)Math.Max(1, (int)Math.Round(decoder.PixelHeight * ratio)), InterpolationMode = BitmapInterpolationMode.Fant };
        byte[] pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken)).DetachPixelData();
        await using var output = new MemoryStream(); using var destination = output.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, destination).AsTask(cancellationToken);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)Math.Max(1, (int)Math.Round(decoder.OrientedPixelWidth * ratio)), (uint)Math.Max(1, (int)Math.Round(decoder.OrientedPixelHeight * ratio)), 96, 96, pixels);
        await encoder.FlushAsync().AsTask(cancellationToken); return output.ToArray();
    }
    private static readonly Lazy<IReadOnlyList<string>> InstalledImageExtensions = new(() => BitmapDecoder.GetDecoderInformationEnumerator()
        .SelectMany(codec => codec.FileExtensions).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray());
    public static IReadOnlyList<string> ImageExtensions => InstalledImageExtensions.Value;

    public async Task<MediaFileInfo> InspectAsync(string path, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("File not found", path);
        if (file.Length > MediaFileInfo.MaxVideoBytes) throw new InvalidOperationException("Too large");
        cancellationToken.ThrowIfCancellationRequested();
        if (ImageExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
        {
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var stream = source.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
            int width = checked((int)decoder.OrientedPixelWidth), height = checked((int)decoder.OrientedPixelHeight);
            if ((long)width * height > MediaFileInfo.MaxImagePixels) throw new InvalidOperationException("Too large");
            bool animated = decoder.DecoderInformation.CodecId == BitmapDecoder.GifDecoderId && decoder.FrameCount > 1;
            if (animated) { var canvas = MediaGifExport.ReadCanvas(source); width = canvas.Width; height = canvas.Height; }
            return new(path, file.Length, width, height, false, TimeSpan.Zero, animated, decoder.BitmapAlphaMode != BitmapAlphaMode.Ignore);
        }
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path)).AsTask(cancellationToken);
            var format = clip.GetVideoEncodingProperties();
            if (format.Width == 0 || format.Height == 0) throw new InvalidOperationException("Unsupported format · " + file.Extension);
            return new(path, file.Length, checked((int)format.Width), checked((int)format.Height), true, clip.OriginalDuration);
        }
        catch (COMException ex) { throw new InvalidOperationException("Unsupported format · " + file.Extension.TrimStart('.').ToUpperInvariant(), ex); }
    }

    public async Task ExportAsync(MediaFileInfo file, string destination, MediaToolOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        options.Validate();
        file = await InspectAsync(file.Path, cancellationToken);
        if (Refusal(file, options) is { } refusal) throw new InvalidOperationException(refusal);
        await (options.OutputFor(file) switch
        {
            MediaOutputFormat.Png or MediaOutputFormat.Jpeg when !file.IsVideo => MediaImageExport.ExportAsync(file, destination, options, progress, cancellationToken),
            MediaOutputFormat.Mp4 when file.IsVideo => VideoAsync(file, destination, options, progress, cancellationToken),
            MediaOutputFormat.Mp4 when file.Animated => MediaGifExport.ToVideoAsync(file, destination, options, progress, cancellationToken),
            MediaOutputFormat.Gif when file.IsVideo => RecordingExports.GifAsync(file.Path, destination, TimeSpan.Zero, file.Duration, progress, cancellationToken),
            _ => throw new InvalidOperationException("Choose PNG or JPEG for images; MP4 or GIF for videos"),
        });
    }

    public static string? Refusal(MediaFileInfo file, MediaToolOptions options)
    {
        var format = options.OutputFor(file);
        if (file.IsVideo && format is MediaOutputFormat.Png or MediaOutputFormat.Jpeg) return "Choose MP4 or GIF for video";
        if (!file.IsVideo && format == MediaOutputFormat.Gif) return "Choose PNG, JPEG, or MP4 for this image";
        if (!file.IsVideo && !file.Animated && format == MediaOutputFormat.Mp4) return "MP4 needs a video or animated GIF";
        if (format == MediaOutputFormat.Gif && file.Duration > TimeSpan.FromSeconds(60)) return "GIF export supports clips up to 60 seconds";
        try
        {
            var size = options.SizeFor(file);
            if (format == MediaOutputFormat.Mp4 && (size.Width < 48 || size.Height < 48)) return "MP4 needs at least 48 × 48 pixels";
        }
        catch (ArgumentException ex) { return ex.Message; }
        return null;
    }

    private static async Task VideoAsync(MediaFileInfo file, string destination, MediaToolOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var source = await StorageFile.GetFileFromPathAsync(file.Path);
        var clip = await MediaClip.CreateFromFileAsync(source).AsTask(cancellationToken);
        var input = clip.GetVideoEncodingProperties();
        var size = options.SizeFor(file);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        if (clip.EmbeddedAudioTracks.Count == 0) profile.Audio = null;
        profile.Video.Width = (uint)Math.Max(2, size.Width & ~1); profile.Video.Height = (uint)Math.Max(2, size.Height & ~1);
        profile.Video.FrameRate.Numerator = input.FrameRate.Numerator > 0 ? input.FrameRate.Numerator : 30;
        profile.Video.FrameRate.Denominator = input.FrameRate.Denominator > 0 ? input.FrameRate.Denominator : 1;
        double fps = profile.Video.FrameRate.Numerator / (double)profile.Video.FrameRate.Denominator;
        double quality = options.Quality switch { MediaQuality.Small => .035, MediaQuality.High => .14, _ => .075 };
        long desired = (long)(profile.Video.Width * profile.Video.Height * fps * quality);
        if (options.Tool == MediaTool.Compress && input.Bitrate > 0)
            desired = Math.Min(desired, (long)(input.Bitrate * (options.Quality switch { MediaQuality.Small => .35, MediaQuality.High => .8, _ => .55 })));
        profile.Video.Bitrate = (uint)Math.Clamp(desired, 64_000, 100_000_000);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(destination)!);
        var output = await folder.CreateFileAsync(Path.GetFileName(destination), CreationCollisionOption.FailIfExists);
        try
        {
            foreach (bool hardware in new[] { true, false })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = hardware, AlwaysReencode = true };
                try
                {
                    using (var reset = await output.OpenAsync(FileAccessMode.ReadWrite).AsTask(cancellationToken)) reset.Size = 0;
                    var prepared = await transcoder.PrepareFileTranscodeAsync(source, output, profile).AsTask(cancellationToken);
                    if (!prepared.CanTranscode)
                    {
                        if (hardware) continue;
                        throw new InvalidOperationException(prepared.FailureReason == TranscodeFailureReason.InvalidProfile ? "Video size or quality is unsupported" : "Video encoding unavailable · " + prepared.FailureReason);
                    }
                    await prepared.TranscodeAsync().AsTask(cancellationToken, progress);
                    break;
                }
                catch (COMException) when (hardware && !cancellationToken.IsCancellationRequested) { }
            }
            if (options.KeepMetadata)
            {
                var metadata = await source.Properties.RetrievePropertiesAsync(MediaImageExport.MetadataNames).AsTask(cancellationToken);
                var writable = metadata.Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value);
                if (writable.Count > 0) await output.Properties.SavePropertiesAsync(writable).AsTask(cancellationToken);
            }
            else
            {
                // MediaTranscoder carries tags into the new container; explicitly remove private shell metadata.
                var metadata = await output.Properties.RetrievePropertiesAsync(MediaImageExport.MetadataNames).AsTask(cancellationToken);
                var remove = metadata.Where(p => p.Value != null).ToDictionary(p => p.Key, _ => (object)null!);
                if (remove.Count > 0) await output.Properties.SavePropertiesAsync(remove).AsTask(cancellationToken);
            }
        }
        catch (COMException ex) { throw new InvalidOperationException("Video encoding unavailable", ex); }
    }
}
