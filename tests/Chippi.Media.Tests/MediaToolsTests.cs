// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Security.Cryptography;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;

namespace Chippi.Media.Tests;

public sealed class MediaToolsTests
{
    [Theory]
    [InlineData(MediaFit.Fit, 100, 50)]
    [InlineData(MediaFit.Fill, 100, 100)]
    [InlineData(MediaFit.Stretch, 100, 100)]
    public void Resize_keeps_aspect_or_uses_an_explicit_fill_or_stretch(MediaFit fit, int width, int height)
    {
        var size = new MediaToolOptions { Tool = MediaTool.Resize, Percent = 0, Width = 100, Height = 100, Fit = fit }.SizeFor(200, 100);
        Assert.Equal(width, size.Width); Assert.Equal(height, size.Height);
        if (fit == MediaFit.Fill) Assert.Equal(50, size.CropX);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(13, 7)]
    [InlineData(7, 13)]
    public void Default_resize_does_not_enlarge_and_tiny_dimensions_stay_valid(int width, int height)
    {
        var size = new MediaToolOptions { Tool = MediaTool.Resize }.SizeFor(width, height);
        Assert.InRange(size.Width, 1, width); Assert.InRange(size.Height, 1, height);
        Assert.Throws<ArgumentException>(() => new MediaToolOptions { Tool = MediaTool.Resize, Width = 0 }.SizeFor(width, height));
    }

    [Fact]
    public async Task A_batch_is_sequential_keeps_originals_and_can_retry_a_failed_row()
    {
        using var fixture = new Files();
        string good = fixture.File("good.png"), bad = fixture.File("bad.png"), third = fixture.File("third.png");
        var hashes = new[] { good, bad, third }.ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        var codec = new FakeCodec { FailName = "bad.png" }; var batch = new MediaBatch(codec);
        await batch.AddAsync([good, bad, third, good]); Assert.Equal(3, batch.Jobs.Count);
        await batch.RunAsync(new() { Format = MediaOutputFormat.Png });
        Assert.Equal([MediaJobState.Done, MediaJobState.Failed, MediaJobState.Done], batch.Jobs.Select(j => j.State));
        Assert.Equal(["good.png", "bad.png", "third.png"], codec.Calls);
        Assert.Equal(1, codec.MaximumConcurrent);
        Assert.EndsWith("good (2).png", batch.Jobs[0].Output!.Path);
        codec.FailName = null; await batch.RunAsync(new() { Format = MediaOutputFormat.Png }, batch.Jobs[1]);
        Assert.All(batch.Jobs, j => Assert.Equal(MediaJobState.Done, j.State));
        foreach (var hash in hashes) Assert.Equal(hash.Value, SHA256.HashData(File.ReadAllBytes(hash.Key)));
        Assert.Empty(Directory.GetFiles(fixture.Root, ".chippi-*"));
    }

    [Fact]
    public async Task Cancellation_removes_only_the_current_partial_and_retains_completed_exports()
    {
        using var fixture = new Files();
        var batch = new MediaBatch(new FakeCodec { BeforeExport = name => { if (name == "second.png") batchHolder!.Cancel(); } });
        batchHolder = batch;
        await batch.AddAsync([fixture.File("first.png"), fixture.File("second.png"), fixture.File("third.png")]);
        await batch.RunAsync(new());
        Assert.Equal([MediaJobState.Done, MediaJobState.Cancelled, MediaJobState.Cancelled], batch.Jobs.Select(j => j.State));
        Assert.True(File.Exists(batch.Jobs[0].Output!.Path)); Assert.Empty(Directory.GetFiles(fixture.Root, ".chippi-*"));
        batchHolder = null;
    }
    private MediaBatch? batchHolder;

    [Fact]
    public async Task Disk_full_stops_after_one_failure_and_leaves_every_source_intact()
    {
        using var fixture = new Files();
        var codec = new FakeCodec { ExportFailure = new IOException("Fixture disk full", unchecked((int)0x80070070)) };
        var batch = new MediaBatch(codec);
        await batch.AddAsync([fixture.File("first.png"), fixture.File("second.png")]);
        await batch.RunAsync(new());
        Assert.Equal("Disk full", batch.Error); Assert.Equal([MediaJobState.Failed, MediaJobState.Cancelled], batch.Jobs.Select(j => j.State));
        Assert.Single(codec.Calls); Assert.Empty(Directory.GetFiles(fixture.Root, ".chippi-*"));
        Assert.All(batch.Jobs, job => Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(job.Path)));
    }

    [Fact]
    public async Task Stopping_during_inspection_cancels_and_drains_the_reader()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batch = new MediaBatch(new FakeCodec { Inspect = async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); } });
        var add = batch.AddAsync([Path.Combine(Path.GetTempPath(), "fixture.png")]);
        await entered.Task; await batch.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => add);
        Assert.False(batch.IsInspecting); Assert.Empty(batch.Jobs);
    }

    [Fact]
    public void Preflight_explains_mismatched_formats_long_gifs_and_oversized_resize()
    {
        var image = new MediaFileInfo("photo.png", 100, 4000, 3000, false, TimeSpan.Zero);
        Assert.Contains("MP4 needs", MediaToolsCodec.Refusal(image, new() { Format = MediaOutputFormat.Mp4 }));
        var video = image with { Path = "clip.mp4", IsVideo = true, Duration = TimeSpan.FromSeconds(61) };
        Assert.Contains("60 seconds", MediaToolsCodec.Refusal(video, new() { Format = MediaOutputFormat.Gif }));
        Assert.Contains("Too large", MediaToolsCodec.Refusal(image, new() { Tool = MediaTool.Resize, Percent = 400 }));
        Assert.Contains("48", MediaToolsCodec.Refusal(video with { Width = 64, Height = 48 }, new() { Tool = MediaTool.Resize }));
        var resized = new MediaToolOptions { Tool = MediaTool.Resize, Percent = 0, Width = 1280, Height = 720, Fit = MediaFit.Stretch }.SizeFor(video);
        Assert.Equal(960, resized.Width); Assert.Equal(720, resized.Height);
    }

    [Fact]
    public async Task Missing_input_is_refused_and_existing_outputs_are_never_replaced()
    {
        using var fixture = new Files(); var codec = new FakeCodec(); var batch = new MediaBatch(codec);
        string input = fixture.File("photo.png"); string first = fixture.File("photo.jpg"), second = fixture.File("photo (2).jpg");
        byte[] firstHash = SHA256.HashData(File.ReadAllBytes(first)), secondHash = SHA256.HashData(File.ReadAllBytes(second));
        await batch.AddAsync([input, Path.Combine(fixture.Root, "missing.png")]);
        Assert.Equal(MediaJobState.Refused, batch.Jobs[1].State);
        await batch.RunAsync(new()); Assert.EndsWith("photo (3).jpg", batch.Jobs[0].Output!.Path);
        Assert.Equal(firstHash, SHA256.HashData(File.ReadAllBytes(first))); Assert.Equal(secondHash, SHA256.HashData(File.ReadAllBytes(second)));
    }

    [Fact]
    public async Task Native_png_resize_and_jpeg_alpha_background_roundtrip_without_changing_the_source()
    {
        using var fixture = new Files(); string source = Path.Combine(fixture.Root, "source.png");
        await Image(source, 64, 32, alpha: 0); byte[] hash = SHA256.HashData(File.ReadAllBytes(source));
        var codec = new MediaToolsCodec(); var info = await codec.InspectAsync(source, default);
        foreach (var fit in Enum.GetValues<MediaFit>())
        {
            var result = await MediaToolExport.RunAsync(codec, info, new() { Tool = MediaTool.Resize, Percent = 0, Width = 16, Height = 16, Fit = fit });
            var exported = await codec.InspectAsync(result.Path, default);
            Assert.Equal(16, exported.Width); Assert.Equal(fit == MediaFit.Fit ? 8 : 16, exported.Height);
        }
        var jpeg = await MediaToolExport.RunAsync(codec, info, new() { Format = MediaOutputFormat.Jpeg, Background = 0x00ff00, Quality = MediaQuality.High });
        using var bytes = File.OpenRead(jpeg.Path); using var stream = bytes.AsRandomAccessStream(); var decoder = await BitmapDecoder.CreateAsync(stream);
        byte[] pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        Assert.InRange(pixels[1], (byte)245, (byte)255); Assert.InRange(pixels[0], (byte)0, (byte)10); Assert.InRange(pixels[2], (byte)0, (byte)10);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(source)));
    }

    [Fact]
    public async Task Native_exif_is_applied_to_pixels_and_private_metadata_is_removed_by_default()
    {
        using var fixture = new Files(); string path = Path.Combine(fixture.Root, "rotated.jpg");
        await Image(path, 64, 32, orientation: 6, author: "Fixture owner");
        var codec = new MediaToolsCodec(); var info = await codec.InspectAsync(path, default);
        Assert.Equal(32, info.Width); Assert.Equal(64, info.Height);
        var result = await MediaToolExport.RunAsync(codec, info, new() { Format = MediaOutputFormat.Jpeg });
        var output = await codec.InspectAsync(result.Path, default); Assert.Equal(32, output.Width); Assert.Equal(64, output.Height);
        using var input = File.OpenRead(result.Path); using var stream = input.AsRandomAccessStream(); var decoder = await BitmapDecoder.CreateAsync(stream);
        var metadata = await decoder.BitmapProperties.GetPropertiesAsync(["System.Author"]);
        Assert.False(metadata.TryGetValue("System.Author", out var owner) && owner.Value is string[] values && values.Contains("Fixture owner"));
        var kept = await MediaToolExport.RunAsync(codec, info, new() { Format = MediaOutputFormat.Jpeg, KeepMetadata = true });
        using var keptInput = File.OpenRead(kept.Path); using var keptStream = keptInput.AsRandomAccessStream(); var keptDecoder = await BitmapDecoder.CreateAsync(keptStream);
        var keptMetadata = await keptDecoder.BitmapProperties.GetPropertiesAsync(["System.Author"]);
        Assert.Contains("Fixture owner", Assert.IsType<string[]>(keptMetadata["System.Author"].Value));
        var png = await MediaToolExport.RunAsync(codec, info, new() { Format = MediaOutputFormat.Png, KeepMetadata = true });
        Assert.Equal(32, (await codec.InspectAsync(png.Path, default)).Width);
    }

    [Fact]
    public async Task Native_animated_gif_mp4_resize_compress_and_gif_export_roundtrip()
    {
        using var fixture = new Files(); string gif = Path.Combine(fixture.Root, "animated.gif");
        await Image(gif, 320, 240, frames: 2);
        var codec = new MediaToolsCodec(); var input = await codec.InspectAsync(gif, default); Assert.True(input.Animated);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var movie = await MediaToolExport.RunAsync(codec, input, new() { Format = MediaOutputFormat.Mp4 }, cancellationToken: timeout.Token);
        var video = await codec.InspectAsync(movie.Path, timeout.Token); Assert.True(video.IsVideo); Assert.InRange(video.Duration.TotalSeconds, .9, 1.2);
        var movieFile = await StorageFile.GetFileFromPathAsync(movie.Path);
        await movieFile.Properties.SavePropertiesAsync(new Dictionary<string, object> { ["System.Title"] = "Fixture private title" });
        var resized = await MediaToolExport.RunAsync(codec, video, new() { Tool = MediaTool.Resize }, cancellationToken: timeout.Token);
        var small = await codec.InspectAsync(resized.Path, timeout.Token); Assert.Equal(160, small.Width); Assert.Equal(120, small.Height);
        var strippedFile = await StorageFile.GetFileFromPathAsync(resized.Path);
        var stripped = await strippedFile.Properties.RetrievePropertiesAsync(["System.Title"]);
        Assert.NotEqual("Fixture private title", stripped["System.Title"]);
        var compressed = await MediaToolExport.RunAsync(codec, video, new() { Tool = MediaTool.Compress, Quality = MediaQuality.Small }, cancellationToken: timeout.Token);
        Assert.True((await codec.InspectAsync(compressed.Path, timeout.Token)).IsVideo);
        var kept = await MediaToolExport.RunAsync(codec, video, new() { Format = MediaOutputFormat.Mp4, KeepMetadata = true }, cancellationToken: timeout.Token);
        var keptFile = await StorageFile.GetFileFromPathAsync(kept.Path);
        Assert.Equal("Fixture private title", (await keptFile.Properties.RetrievePropertiesAsync(["System.Title"]))["System.Title"]);
        var exportedGif = await MediaToolExport.RunAsync(codec, small, new() { Format = MediaOutputFormat.Gif }, cancellationToken: timeout.Token);
        Assert.True((await codec.InspectAsync(exportedGif.Path, timeout.Token)).Animated);
    }

    [Fact]
    public async Task Native_partial_gif_frames_keep_logical_canvas_background_and_restore_previous()
    {
        using var fixture = new Files(); string gif = Path.Combine(fixture.Root, "partial.gif");
        using (var output = new BinaryWriter(File.Create(gif)))
        {
            output.Write("GIF89a"u8); output.Write((ushort)96); output.Write((ushort)96);
            output.Write(new byte[] { 0x81, 0, 0, 255, 255, 0, 255, 0, 0, 0, 0, 255, 0, 255, 0 });
            foreach (var (left, top, color, disposal) in new[] { (0, 0, 1, 2), (48, 48, 2, 3), (0, 48, 3, 1) })
            {
                output.Write(new byte[] { 0x21, 0xf9, 4, (byte)(disposal << 2), 50, 0, 0, 0, 0x2c });
                output.Write((ushort)left); output.Write((ushort)top); output.Write((ushort)48); output.Write((ushort)48); output.Write((byte)0); output.Write((byte)2);
                var data = new List<byte>(); int bits = 0, count = 0;
                void Code(int code) { bits |= code << count; count += 3; while (count >= 8) { data.Add((byte)bits); bits >>= 8; count -= 8; } }
                for (int pixel = 0; pixel < 48 * 48; pixel++) { Code(4); Code(color); }
                Code(5); if (count > 0) data.Add((byte)bits);
                for (int at = 0; at < data.Count; at += 255) { int size = Math.Min(255, data.Count - at); output.Write((byte)size); output.Write(data.GetRange(at, size).ToArray()); }
                output.Write((byte)0);
            }
            output.Write((byte)0x3b);
        }
        var codec = new MediaToolsCodec(); var input = await codec.InspectAsync(gif, default); Assert.Equal(96, input.Width); Assert.Equal(96, input.Height);
        using (var bytes = File.OpenRead(gif))
        using (var stream = bytes.AsRandomAccessStream())
        {
            var decoder = await BitmapDecoder.CreateAsync(stream); var frame = await decoder.GetFrameAsync(0);
            var pixels = (await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[..4]);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var movie = await MediaToolExport.RunAsync(codec, input, new() { Format = MediaOutputFormat.Mp4 }, cancellationToken: timeout.Token);
        var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(movie.Path));
        var composition = new MediaComposition(); composition.Clips.Add(clip);
        foreach (var (time, x, y, red, green, blue) in new[] { (.1, 24, 24, true, false, false), (.6, 24, 24, true, true, false), (.6, 72, 72, false, false, true), (1.1, 72, 72, true, true, false), (1.1, 24, 72, false, true, false) })
        {
            using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(time), 96, 96, VideoFramePrecision.NearestFrame);
            var decoder = await BitmapDecoder.CreateAsync(thumbnail);
            byte[] pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            int at = (y * 96 + x) * 4;
            Assert.True(blue == (pixels[at] > 128) && green == (pixels[at + 1] > 128) && red == (pixels[at + 2] > 128), $"At {time}s {x},{y}: BGR={pixels[at]},{pixels[at+1]},{pixels[at+2]}; quadrants=" + string.Join(" / ", new[] { (24,24), (72,24), (24,72), (72,72) }.Select(p => string.Join(",", pixels.Skip((p.Item2*96+p.Item1)*4).Take(3)))));
        }
    }

    [Fact]
    public async Task One_hundred_small_native_images_decode_after_the_batch_and_keep_source_hashes()
    {
        using var fixture = new Files(); var codec = new MediaToolsCodec(); var batch = new MediaBatch(codec);
        await Image(Path.Combine(fixture.Root, "seed.png"), 320, 240, checker: true);
        await Image(Path.Combine(fixture.Root, "seed.jpg"), 320, 240);
        var sources = Enumerable.Range(0, 100).Select(i => Path.Combine(fixture.Root, $"image-{i}" + (i % 2 == 0 ? ".png" : ".jpg"))).ToArray();
        foreach (string path in sources) File.Copy(Path.Combine(fixture.Root, "seed" + Path.GetExtension(path)), path);
        var hashes = sources.ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        await batch.AddAsync(sources); await batch.RunAsync(new() { OutputFolder = Path.Combine(fixture.Root, "exports") });
        Assert.All(batch.Jobs, job => Assert.Equal(MediaJobState.Done, job.State));
        foreach (var job in batch.Jobs)
        {
            var output = await codec.InspectAsync(job.Output!.Path, default); Assert.Equal(320, output.Width); Assert.Equal(240, output.Height);
            Assert.Equal(hashes[job.Path], SHA256.HashData(File.ReadAllBytes(job.Path)));
        }
        Assert.Empty(Directory.GetFiles(fixture.Root, ".chippi-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Native_video_cancellation_drains_callbacks_and_deletes_its_partial()
    {
        using var fixture = new Files(); string source = Path.Combine(fixture.Root, "cancel.gif");
        await Image(source, 320, 240, frames: 30);
        var codec = new MediaToolsCodec(); var input = await codec.InspectAsync(source, default);
        byte[] hash = SHA256.HashData(File.ReadAllBytes(source));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var progress = new CancelOnProgress(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaToolExport.RunAsync(codec, input, new() { Format = MediaOutputFormat.Mp4 }, progress, cancellation.Token));
        Assert.Empty(Directory.GetFiles(fixture.Root, ".chippi-*")); Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(source)));
    }
    private sealed class CancelOnProgress(CancellationTokenSource cancellation) : IProgress<double>
    {
        private int _requested;
        public void Report(double value) { if (value > 0 && Interlocked.Exchange(ref _requested, 1) == 0) _ = Task.Run(() => cancellation.Cancel()); }
    }

    [Fact]
    public async Task Fant_downsampling_preserves_the_average_of_fine_detail_better_than_cubic()
    {
        using var fixture = new Files(); string source = Path.Combine(fixture.Root, "checker.png");
        await Image(source, 213, 127, checker: true);
        var codec = new MediaToolsCodec(); var info = await codec.InspectAsync(source, default);
        var options = new MediaToolOptions { Tool = MediaTool.Resize, Percent = 0, Width = 17, Height = 11, Fit = MediaFit.Stretch };
        var errors = new List<double>();
        foreach (var interpolation in new[] { BitmapInterpolationMode.Fant, BitmapInterpolationMode.Cubic })
        {
            string output = Path.Combine(fixture.Root, interpolation + ".png");
            await MediaImageExport.ExportAsync(info, output, options, null, default, interpolation);
            using var bytes = File.OpenRead(output); using var stream = bytes.AsRandomAccessStream(); var decoder = await BitmapDecoder.CreateAsync(stream);
            byte[] pixels = (await decoder.GetPixelDataAsync()).DetachPixelData();
            errors.Add(Enumerable.Range(0, pixels.Length / 4).Average(i => Math.Abs(pixels[i * 4] - 127.5)));
        }
        Assert.True(errors[0] <= errors[1], $"Fant mean error {errors[0]:0.00}, Cubic {errors[1]:0.00}");
    }

    private static async Task Image(string path, uint width, uint height, byte alpha = 255, ushort orientation = 1, string? author = null, int frames = 1, bool checker = false)
    {
        using var input = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite); using var stream = input.AsRandomAccessStream();
        Guid format = Path.GetExtension(path) switch { ".jpg" => BitmapEncoder.JpegEncoderId, ".gif" => BitmapEncoder.GifEncoderId, _ => BitmapEncoder.PngEncoderId };
        var encoder = await BitmapEncoder.CreateAsync(format, stream);
        for (int frame = 0; frame < frames; frame++)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { if (checker) { byte value = (byte)((i / 4 % width + i / 4 / width) % 2 * 255); pixels[i] = pixels[i + 1] = pixels[i + 2] = value; } else pixels[i + (frame == 0 ? 2 : 0)] = 255; pixels[i + 3] = alpha; }
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels);
            var metadata = new BitmapPropertySet();
            if (orientation != 1) metadata["/app1/ifd/{ushort=274}"] = new BitmapTypedValue(orientation, PropertyType.UInt16);
            if (author != null) metadata["System.Author"] = new BitmapTypedValue(new[] { author }, PropertyType.StringArray);
            if (frames > 1) { metadata["/grctlext/Delay"] = new BitmapTypedValue((ushort)50, PropertyType.UInt16); metadata["/grctlext/Disposal"] = new BitmapTypedValue((byte)1, PropertyType.UInt8); }
            if (metadata.Count > 0) await encoder.BitmapProperties.SetPropertiesAsync(metadata);
            if (frame < frames - 1) await encoder.GoToNextFrameAsync(); else await encoder.FlushAsync();
        }
    }

    private sealed class Files : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "chippi-convert-test-" + Guid.NewGuid().ToString("N"));
        public Files() => Directory.CreateDirectory(Root);
        public string File(string name) { string path = Path.Combine(Root, name); System.IO.File.WriteAllBytes(path, [1, 2, 3]); return path; }
        public void Dispose() { Directory.Delete(Root, recursive: true); }
    }
    private sealed class FakeCodec : IMediaToolsCodec
    {
        public List<string> Calls { get; } = [];
        public string? FailName { get; set; }
        public Action<string>? BeforeExport { get; init; }
        public Exception? ExportFailure { get; init; }
        public Func<string, CancellationToken, Task<MediaFileInfo>>? Inspect { get; init; }
        public int MaximumConcurrent { get; private set; }
        private int _active;
        public Task<MediaFileInfo> InspectAsync(string path, CancellationToken cancellationToken) => Inspect != null ? Inspect(path, cancellationToken) : File.Exists(path) ? Task.FromResult(new MediaFileInfo(path, new FileInfo(path).Length, 64, 32, false, TimeSpan.Zero)) : Task.FromException<MediaFileInfo>(new FileNotFoundException());
        public async Task ExportAsync(MediaFileInfo file, string destination, MediaToolOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            Calls.Add(file.Name); MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref _active));
            try
            {
                await File.WriteAllBytesAsync(destination, [4, 5, 6], cancellationToken); BeforeExport?.Invoke(file.Name); cancellationToken.ThrowIfCancellationRequested();
                if (ExportFailure != null) throw ExportFailure;
                if (file.Name == FailName) throw new InvalidOperationException("Fixture failure");
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
