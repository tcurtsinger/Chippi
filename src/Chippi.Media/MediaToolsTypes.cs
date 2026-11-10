// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

public enum MediaTool { Convert, Resize, Compress }
public enum MediaOutputFormat { Png, Jpeg, Mp4, Gif }
public enum MediaQuality { Small, Balanced, High }
public enum MediaFit { Fit, Fill, Stretch }

public sealed record MediaToolOptions
{
    public MediaTool Tool { get; init; }
    public MediaOutputFormat Format { get; init; } = MediaOutputFormat.Jpeg;
    public MediaQuality Quality { get; init; } = MediaQuality.Balanced;
    public MediaFit Fit { get; init; }
    public int Percent { get; init; } = 50;
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public uint Background { get; init; } = 0xffffff;
    public bool KeepMetadata { get; init; }
    public string OutputFolder { get; init; } = "";

    public MediaOutputFormat OutputFor(MediaFileInfo file) => Tool == MediaTool.Convert ? Format
        : file.IsVideo || (file.Animated && Tool == MediaTool.Resize) ? MediaOutputFormat.Mp4
        : Tool == MediaTool.Compress ? MediaOutputFormat.Jpeg : MediaOutputFormat.Png;

    public void Validate()
    {
        if (!Enum.IsDefined(Tool) || !Enum.IsDefined(Format) || !Enum.IsDefined(Quality) || !Enum.IsDefined(Fit))
            throw new ArgumentException("Unsupported option");
        if (Percent is < 0 or > 400 || Width is < 1 or > 16384 || Height is < 1 or > 16384)
            throw new ArgumentException("Size must be 1–16384 pixels or 1–400%");
        if (Background > 0xffffff) throw new ArgumentException("Invalid JPEG background");
    }

    public MediaResize SizeFor(int width, int height)
    {
        Validate();
        if (width < 1 || height < 1) throw new ArgumentException("Invalid image dimensions");
        if (Tool != MediaTool.Resize) return new(width, height, width, height, 0, 0);
        int targetWidth = Percent > 0 ? Math.Max(1, (int)Math.Round(width * Percent / 100d)) : Width;
        int targetHeight = Percent > 0 ? Math.Max(1, (int)Math.Round(height * Percent / 100d)) : Height;
        if ((long)targetWidth * targetHeight > MediaFileInfo.MaxImagePixels) throw new ArgumentException("Too large");
        if (Fit == MediaFit.Stretch) return new(targetWidth, targetHeight, targetWidth, targetHeight, 0, 0);
        double ratio = Fit == MediaFit.Fill ? Math.Max(targetWidth / (double)width, targetHeight / (double)height)
            : Math.Min(targetWidth / (double)width, targetHeight / (double)height);
        int scaledWidth = Math.Max(1, (int)Math.Round(width * ratio)), scaledHeight = Math.Max(1, (int)Math.Round(height * ratio));
        if ((long)scaledWidth * scaledHeight > MediaFileInfo.MaxImagePixels) throw new ArgumentException("Too large");
        return Fit == MediaFit.Fill
            ? new(targetWidth, targetHeight, Math.Max(targetWidth, scaledWidth), Math.Max(targetHeight, scaledHeight), Math.Max(0, (scaledWidth - targetWidth) / 2), Math.Max(0, (scaledHeight - targetHeight) / 2))
            : new(scaledWidth, scaledHeight, scaledWidth, scaledHeight, 0, 0);
    }

    public MediaResize SizeFor(MediaFileInfo file)
    {
        if (Tool != MediaTool.Resize || !(file.IsVideo || file.Animated && OutputFor(file) == MediaOutputFormat.Mp4)) return SizeFor(file.Width, file.Height);
        Validate();
        int height = Percent > 0 ? Math.Max(1, (int)Math.Round(file.Height * Percent / 100d)) : Height;
        int width = Math.Max(1, (int)Math.Round(file.Width * (height / (double)file.Height)));
        if ((long)width * height > MediaFileInfo.MaxImagePixels) throw new ArgumentException("Too large");
        return new(width, height, width, height, 0, 0);
    }
}

public sealed record MediaResize(int Width, int Height, int ScaledWidth, int ScaledHeight, int CropX, int CropY);
public sealed record MediaFileInfo(string Path, long Bytes, int Width, int Height, bool IsVideo, TimeSpan Duration, bool Animated = false, bool HasAlpha = false)
{
    public const long MaxImagePixels = 100_000_000;
    public const long MaxVideoBytes = 4L * 1024 * 1024 * 1024;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
}

public interface IMediaToolsCodec
{
    Task<MediaFileInfo> InspectAsync(string path, CancellationToken cancellationToken);
    Task ExportAsync(MediaFileInfo file, string destination, MediaToolOptions options, IProgress<double>? progress, CancellationToken cancellationToken);
}

public sealed record MediaExportResult(string Path, long Bytes);

/// <summary>Every job writes a private partial file, then publishes it with an atomic, non-overwriting move.</summary>
public static class MediaToolExport
{
    public static async Task<MediaExportResult> RunAsync(IMediaToolsCodec codec, MediaFileInfo source, MediaToolOptions options, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        options.Validate();
        string folder = string.IsNullOrWhiteSpace(options.OutputFolder) ? Path.GetDirectoryName(source.Path)! : Path.GetFullPath(options.OutputFolder);
        Directory.CreateDirectory(folder);
        string extension = options.OutputFor(source) switch { MediaOutputFormat.Jpeg => ".jpg", MediaOutputFormat.Png => ".png", MediaOutputFormat.Mp4 => ".mp4", _ => ".gif" };
        string temporary = Path.Combine(folder, ".chippi-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            await codec.ExportAsync(source, temporary, options, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            long bytes = new FileInfo(temporary).Length;
            if (bytes == 0) throw new IOException("Export produced an empty file");
            string stem = Path.GetFileNameWithoutExtension(source.Path);
            for (int number = 1; number <= 100000; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = Path.Combine(folder, stem + (number == 1 ? "" : $" ({number})") + extension);
                try { File.Move(temporary, destination, overwrite: false); return new(destination, bytes); }
                catch (IOException) when (File.Exists(destination)) { }
            }
            throw new IOException("No available output name");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
