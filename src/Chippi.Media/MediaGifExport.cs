// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using System.Buffers.Binary;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Chippi.Media;

internal static class MediaGifExport
{
    public static async Task ToVideoAsync(MediaFileInfo info, string destination, MediaToolOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try { await EncodeAsync(info, destination, options, progress, cancellationToken, hardware: true); }
        catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested && ex.Message.Contains("encoding unavailable", StringComparison.Ordinal))
        {
            if (File.Exists(destination)) File.Delete(destination);
            await EncodeAsync(info, destination, options, progress, cancellationToken, hardware: false);
        }
    }

    private static async Task EncodeAsync(MediaFileInfo info, string destination, MediaToolOptions options, IProgress<double>? progress, CancellationToken cancellationToken, bool hardware)
    {
        await using var input = File.OpenRead(info.Path);
        var logical = ReadCanvas(input);
        using var inputStream = input.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(inputStream).AsTask(cancellationToken);
        var frames = new List<(BitmapFrame Frame, int Left, int Top, int Disposal, bool Transparent, TimeSpan Delay)>();
        for (uint i = 0; i < decoder.FrameCount; i++)
        {
            var frame = await decoder.GetFrameAsync(i).AsTask(cancellationToken);
            var properties = await frame.BitmapProperties.GetPropertiesAsync(["/imgdesc/Left", "/imgdesc/Top", "/grctlext/Delay", "/grctlext/Disposal", "/grctlext/TransparencyFlag"]).AsTask(cancellationToken);
            int Value(string name, int fallback) => properties.TryGetValue(name, out var value) ? Convert.ToInt32(value.Value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
            int left = Value("/imgdesc/Left", 0), top = Value("/imgdesc/Top", 0);
            if ((long)left + frame.PixelWidth > logical.Width || (long)top + frame.PixelHeight > logical.Height) throw new InvalidOperationException("GIF frame exceeds its canvas");
            frames.Add((frame, left, top, Value("/grctlext/Disposal", 0), Value("/grctlext/TransparencyFlag", 0) != 0, TimeSpan.FromMilliseconds(Math.Max(1, Value("/grctlext/Delay", 10)) * 10)));
        }
        int width = logical.Width, height = logical.Height;
        if ((long)width * height > MediaFileInfo.MaxImagePixels) throw new InvalidOperationException("Too large");
        var outputSize = options.SizeFor(info with { Width = width, Height = height });
        var video = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
        video.FrameRate.Numerator = 30; video.FrameRate.Denominator = 1;
        var stream = new MediaStreamSource(new VideoStreamDescriptor(video))
        { CanSeek = false, BufferTime = TimeSpan.Zero, Duration = TimeSpan.FromTicks(frames.Sum(f => f.Delay.Ticks)) };
        stream.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        byte[] canvas = new byte[checked(width * height * 4)];
        Fill(canvas, width, 0, 0, width, height, frames[0].Transparent ? 0 : logical.Background);
        int index = 0; TimeSpan position = TimeSpan.Zero; Exception? failure = null;
        using var serial = new SemaphoreSlim(1);
        using var requestsLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var requests = new List<Task>(); var requestGate = new object(); bool stopping = false;
        async Task Sample(MediaStreamSourceSampleRequest request)
        {
            var deferral = request.GetDeferral();
            await serial.WaitAsync(CancellationToken.None);
            try
            {
                requestsLifetime.Token.ThrowIfCancellationRequested();
                if (index >= frames.Count) { request.Sample = null; return; }
                var item = frames[index];
                byte[]? restore = item.Disposal == 3 ? (byte[])canvas.Clone() : null;
                var pixels = (await item.Frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(requestsLifetime.Token)).DetachPixelData();
                Composite(canvas, width, pixels, (int)item.Frame.PixelWidth, (int)item.Frame.PixelHeight, item.Left, item.Top);
                // Uncompressed RGB samples use bottom-up scanlines in Media Foundation.
                var copy = new byte[canvas.Length];
                for (int y = 0; y < height; y++) Buffer.BlockCopy(canvas, y * width * 4, copy, (height - 1 - y) * width * 4, width * 4);
                MediaImageExport.Flatten(copy, options.Background);
                var sample = MediaStreamSample.CreateFromBuffer(copy.AsBuffer(), position); sample.Duration = item.Delay;
                request.Sample = sample; position += item.Delay; index++;
                if (item.Disposal == 3) canvas = restore!;
                else if (item.Disposal == 2)
                    Fill(canvas, width, item.Left, item.Top, (int)item.Frame.PixelWidth, (int)item.Frame.PixelHeight, item.Transparent ? 0 : logical.Background);
            }
            catch (Exception ex) { if (!requestsLifetime.IsCancellationRequested) { failure = ex; stream.NotifyError(MediaStreamSourceErrorStatus.Other); } }
            finally { serial.Release(); deferral.Complete(); }
        }
        TypedEventHandler<MediaStreamSource, MediaStreamSourceSampleRequestedEventArgs> requested = (_, e) =>
        {
            lock (requestGate)
            {
                if (stopping) { e.Request.Sample = null; return; }
                requests.RemoveAll(t => t.IsCompleted);
                requests.Add(Sample(e.Request));
            }
        };
        stream.SampleRequested += requested;
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Audio = null; profile.Video.Width = (uint)Math.Max(2, outputSize.Width & ~1); profile.Video.Height = (uint)Math.Max(2, outputSize.Height & ~1);
        profile.Video.Bitrate = (uint)Math.Clamp((long)profile.Video.Width * profile.Video.Height * (options.Quality == MediaQuality.High ? 6 : options.Quality == MediaQuality.Small ? 2 : 4), 64_000, 50_000_000);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(destination)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(destination), CreationCollisionOption.FailIfExists);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = hardware, AlwaysReencode = true };
        try
        {
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(stream, output, profile).AsTask(cancellationToken);
            if (!prepared.CanTranscode) throw new InvalidOperationException("Video encoding unavailable · " + prepared.FailureReason);
            await prepared.TranscodeAsync().AsTask(cancellationToken, progress);
        }
        catch when (failure != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); throw; }
        catch (System.Runtime.InteropServices.COMException ex) { throw new InvalidOperationException($"GIF video encoding unavailable ({ex.HResult:X8})", ex); }
        finally
        {
            Task[] draining;
            lock (requestGate) { stopping = true; stream.SampleRequested -= requested; draining = requests.ToArray(); }
            requestsLifetime.Cancel(); await Task.WhenAll(draining);
        }
    }

    internal static (int Width, int Height, uint Background) ReadCanvas(Stream source)
    {
        long position = source.Position;
        try
        {
            source.Position = 0;
            Span<byte> header = stackalloc byte[13]; source.ReadExactly(header);
            if (!header[..6].SequenceEqual("GIF87a"u8) && !header[..6].SequenceEqual("GIF89a"u8)) throw new InvalidDataException("Invalid GIF header");
            int width = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]), height = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
            if (width == 0 || height == 0 || (long)width * height > MediaFileInfo.MaxImagePixels) throw new InvalidOperationException("Too large");
            uint background = 0;
            if ((header[10] & 0x80) != 0)
            {
                byte[] palette = new byte[3 * (1 << ((header[10] & 7) + 1))]; source.ReadExactly(palette);
                int offset = header[11] * 3;
                if (offset + 2 < palette.Length) background = 0xff000000u | (uint)palette[offset] << 16 | (uint)palette[offset + 1] << 8 | palette[offset + 2];
            }
            return (width, height, background);
        }
        finally { source.Position = position; }
    }

    internal static void Fill(byte[] canvas, int canvasWidth, int left, int top, int width, int height, uint background)
    {
        for (int y = top; y < top + height; y++) MemoryMarshal.Cast<byte, uint>(canvas.AsSpan((y * canvasWidth + left) * 4, width * 4)).Fill(background);
    }

    internal static void Composite(byte[] canvas, int canvasWidth, byte[] pixels, int width, int height, int left, int top)
    {
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int source = (y * width + x) * 4, target = ((y + top) * canvasWidth + x + left) * 4;
            if (pixels[source + 3] == 0) continue;
            Buffer.BlockCopy(pixels, source, canvas, target, 4);
        }
    }
}
