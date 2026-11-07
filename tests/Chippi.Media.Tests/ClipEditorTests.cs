// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Buffers.Binary;
using System.Diagnostics;
using Chippi.Media;
using Chippi.Media.Encoding;
using Chippi.Media.Gif;
using Xunit.Abstractions;

namespace Chippi.Media.Tests;

/// <summary>
/// Trim and Export GIF on a generated clip, read back: the trimmed MP4 is decoded, the GIF's blocks are walked. A PC
/// without the encoders passes with a note.
/// </summary>
public sealed class ClipEditorTests(ITestOutputHelper output) : IDisposable
{
    private const int W = 320;
    private const int H = 180;
    private readonly string _folder = Directory.CreateTempSubdirectory("chippi-media-clip-").FullName;

    private bool Unsupported()
    {
        if (MediaSupport.MissingEncoder is not { } missing) return false;
        output.WriteLine($"Not run: {missing}.");
        return true;
    }

    /// <summary>Frame <paramref name="index"/>: gray level 2 × index, with a white square moving right 4 px a frame.</summary>
    private static byte[] Frame(int index)
    {
        var pixels = new byte[W * H * 4];
        var level = (byte)Math.Min(255, index * 2);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = level;
            pixels[i + 3] = 255;
        }
        var x0 = (index * 4) % (W - 20);
        for (var y = 10; y < 30; y++) pixels.AsSpan(((y * W) + x0) * 4, 20 * 4).Fill(255);
        return pixels;
    }

    private static int Gray(byte[] bgra, int x, int y) => bgra[(((y * W) + x) * 4) + 1];

    /// <summary>A 4-second clip at 30 fps with a 440 Hz tone.</summary>
    private async Task<string> Clip()
    {
        var path = Path.Combine(_folder, "clip.mp4");
        using var writer = new Mp4Writer(path, new VideoFormat(W, H, 30, true), live: false);
        for (var i = 0; i < 120; i++)
        {
            if (i % 3 == 0)
            {
                var samples = new float[4800 * 2];
                var start = AudioMixer.FrameAt(TimeSpan.FromMilliseconds(i / 3 * 100));
                for (var s = 0; s < 4800; s++)
                    samples[2 * s] = samples[(2 * s) + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * (start + s) / AudioMixer.Rate));
                writer.WriteAudio(new AudioBlock(TimeSpan.FromMilliseconds(i / 3 * 100), samples));
            }
            await writer.WriteVideoAsync(new BgraFrame(TimeSpan.Zero, Frame(i), W), TimeSpan.FromTicks(i * TimeSpan.TicksPerSecond / 30), default);
        }
        var result = await writer.FinishAsync(TimeSpan.FromSeconds(4));
        Assert.True(result.Playable);
        Assert.Equal(0, writer.Dropped);
        return path;
    }

    [Fact]
    public async Task Trim_keeps_only_the_span_with_its_audio()
    {
        if (Unsupported()) return;
        var source = await Clip();
        var target = Path.Combine(_folder, "trimmed.mp4");
        var reports = new List<double>();
        var watch = Stopwatch.StartNew();
        await ClipEditor.TrimAsync(source, target, new TrimRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)), new SyncProgress(reports.Add));
        output.WriteLine($"Trimmed 2 s at {W}x{H} in {watch.ElapsedMilliseconds} ms, {new FileInfo(target).Length} bytes.");

        using var reader = MediaReader.Open(target, audio: true);
        Assert.InRange(reader.Duration.TotalSeconds, 1.9, 2.15);
        var frame = new byte[W * H * 4];
        var grays = new List<int>();
        while (reader.ReadFrame(frame) is not null) grays.Add(Gray(frame, 160, 150));
        output.WriteLine($"{grays.Count} frames, gray first {grays[0]}, last {grays[^1]}.");
        Assert.InRange(grays.Count, 58, 62);
        Assert.InRange(grays[0], 52, 68);     // frame 30: 60
        Assert.InRange(grays[^1], 168, 188);  // frame 89: 178
        Assert.True(reader.HasAudio);
        using var audio = MediaReader.Open(target, audio: true);
        var seconds = audio.ReadAllAudio().Length / 2.0 / AudioMixer.Rate;
        Assert.InRange(seconds, 1.9, 2.2);
        Assert.Equal(1, reports[^1]);
        Assert.All(reports.Zip(reports.Skip(1)), p => Assert.True(p.Second >= p.First));
    }

    [Fact]
    public async Task A_cancelled_trim_removes_only_its_partial_file()
    {
        if (Unsupported()) return;
        var source = await Clip();
        var target = Path.Combine(_folder, "cancelled.mp4");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ClipEditor.TrimAsync(source, target, TrimRange.Whole(TimeSpan.FromSeconds(4)), null, cancel.Token));
        Assert.False(File.Exists(target));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Export_gif_loops_at_15_fps_and_lasts_the_clip()
    {
        if (Unsupported()) return;
        var source = await Clip();
        var target = Path.Combine(_folder, "clip.gif");
        var watch = Stopwatch.StartNew();
        var result = await ClipEditor.ExportGifAsync(source, target, TrimRange.Whole(TimeSpan.FromSeconds(4)));
        output.WriteLine($"GIF of 4 s at {W}x{H} in {watch.ElapsedMilliseconds} ms, {new FileInfo(target).Length} bytes, {result.Frames} frames.");

        var gif = GifFile.Read(File.ReadAllBytes(target));
        Assert.False(result.Cut);
        Assert.Equal((W, H), (gif.Width, gif.Height));
        Assert.True(gif.LoopsForever);
        Assert.Equal(result.Frames, gif.Delays.Count);
        Assert.InRange(gif.Delays.Count, 55, 60);     // 60 slots; the square moves every slot
        Assert.Equal(400, gif.Delays.Sum());          // 4 s in hundredths
        Assert.All(gif.Delays, d => Assert.InRange(d, 6, 14));
    }

    [Fact]
    public async Task A_long_clip_exports_its_first_60_seconds()
    {
        if (Unsupported()) return;
        // One frame held for 70 s: cheap to encode, and every later slot is unchanged.
        var source = Path.Combine(_folder, "long.mp4");
        using (var writer = new Mp4Writer(source, new VideoFormat(W, H, 30, false), live: false))
        {
            await writer.WriteVideoAsync(new BgraFrame(TimeSpan.Zero, Frame(5), W), TimeSpan.Zero, default);
            await writer.FinishAsync(TimeSpan.FromSeconds(70));
        }
        var target = Path.Combine(_folder, "long.gif");
        var result = await ClipEditor.ExportGifAsync(source, target, TrimRange.Whole(TimeSpan.FromSeconds(70)));
        var gif = GifFile.Read(File.ReadAllBytes(target));
        Assert.True(result.Cut);
        // A held frame stays one or two GIF frames: CI's software decoder gives the first frame slightly different
        // pixels from the rest. What matters is the cut at 60 s.
        Assert.InRange(gif.Delays.Count, 1, 3);
        Assert.Equal(6000, gif.Delays.Sum());
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Walks a GIF's blocks: its size, the NETSCAPE2.0 loop count and each frame's delay.</summary>
    private sealed record GifFile(int Width, int Height, bool LoopsForever, List<int> Delays)
    {
        public static GifFile Read(byte[] b)
        {
            Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(b, 0, 6));
            var width = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6));
            var height = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(8));
            var at = 13 + ((b[10] & 0x80) != 0 ? 3 * (2 << (b[10] & 7)) : 0);
            var loops = false;
            var delays = new List<int>();
            var delay = 0;
            while (b[at] != 0x3B)
            {
                if (b[at] == 0x21)
                {
                    var label = b[at + 1];
                    at += 2;
                    if (label == 0xF9) delay = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at + 2));
                    if (label == 0xFF && System.Text.Encoding.ASCII.GetString(b, at + 1, 11) == "NETSCAPE2.0")
                        loops = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at + 12 + 2)) == 0;
                    at = SkipBlocks(b, at);
                }
                else
                {
                    Assert.Equal(0x2C, b[at]);
                    var flags = b[at + 9];
                    at += 10 + ((flags & 0x80) != 0 ? 3 * (2 << (flags & 7)) : 0) + 1;
                    at = SkipBlocks(b, at);
                    delays.Add(delay);
                }
            }
            return new GifFile(width, height, loops, delays);
        }

        private static int SkipBlocks(byte[] b, int at)
        {
            while (b[at] != 0) at += b[at] + 1;
            return at + 1;
        }
    }
}
