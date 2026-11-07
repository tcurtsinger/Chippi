// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Features.Recording.Overlay;
using Chippi.Media;
using Chippi.Media.Capture;
using Chippi.Media.Encoding;

namespace Chippi.Features.Recording;

/// <summary>The real PC: Windows Graphics Capture, WASAPI, Media Foundation and WIC (Chippi.Media), and the overlay windows.</summary>
/// <param name="temp">The folder only recordings' temporary files use; emptied of old ones at start.</param>
public sealed class WindowsRecordingPc(string temp) : IRecordingPc
{
    /// <summary>The pill's face (Lumen's): time, Pause or Resume, Stop. Without one only the frame shows.</summary>
    public Func<RecordingController, FrameworkElement>? PillFace { get; set; }

    public string? Missing => !WgcVideoSource.IsSupported ? "Windows Graphics Capture isn't available." : MediaSupport.MissingEncoder;

    public IMediaWriterFactory Writers { get; } = new Mp4WriterFactory();

    public IVideoSource Display(nint monitor, Chippi.Media.Capture.PixelRect? region, bool cursor) => WgcVideoSource.ForDisplay(monitor, region, cursor);

    public IVideoSource Window(nint window, bool cursor) => WgcVideoSource.ForWindow(window, cursor);

    public IAudioSource SystemSound() => WasapiAudioSource.SystemSound();

    public IAudioSource Mic() => WasapiAudioSource.Mic();

    public string TempFile()
    {
        Directory.CreateDirectory(temp);
        return Path.Combine(temp, Guid.NewGuid().ToString("N") + ".mp4");
    }

    /// <summary>Deletes temporary files a crash or a quit left behind.</summary>
    public void ClearTemp()
    {
        if (!Directory.Exists(temp)) return;
        foreach (var file in Directory.EnumerateFiles(temp, "*.mp4"))
        {
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public string DefaultSaveFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Chippi");

    public IRecordingOverlay ShowOverlay(RecordingTarget target, RecordingController recording) =>
        new RecordingOverlay(target, PillFace?.Invoke(recording));

    public BitmapSource? Poster(string path)
    {
        try
        {
            using var reader = MediaReader.Open(path);
            var pixels = new byte[reader.Stride * reader.Height];
            if (reader.ReadFrame(pixels) is null) return null;
            var image = BitmapSource.Create(reader.Width, reader.Height, 96, 96, PixelFormats.Bgra32, null, pixels, reader.Stride);
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Core.Diagnostics.Log.Info($"Recording: the poster couldn't be read ({ex.GetType().Name})");
            return null;
        }
    }

    public Task TrimAsync(string source, string target, TrimRange span, IProgress<double> progress, CancellationToken cancel) =>
        ClipEditor.TrimAsync(source, target, span, progress, cancel);

    public Task<GifResult> GifAsync(string source, string target, TrimRange span, IProgress<double> progress, CancellationToken cancel) =>
        ClipEditor.ExportGifAsync(source, target, span, progress, cancel);
}
