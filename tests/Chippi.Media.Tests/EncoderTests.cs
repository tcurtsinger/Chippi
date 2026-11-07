// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Media;
using Chippi.Media.Encoding;
using Xunit.Abstractions;

namespace Chippi.Media.Tests;

/// <summary>
/// The real writer, round-tripped: synthetic frames and a tone go through Media Foundation into an MP4, and the file
/// is decoded to check it. Nothing reads the screen. A PC without the encoders (an N edition, or a server image
/// without Media Foundation) passes with a note.
/// </summary>
public sealed class EncoderTests(ITestOutputHelper output) : IDisposable
{
    private const int W = 640;
    private const int H = 360;
    private readonly string _folder = Directory.CreateTempSubdirectory("chippi-media-enc-").FullName;

    private bool Unsupported()
    {
        if (MediaSupport.MissingEncoder is not { } missing) return false;
        output.WriteLine($"Not run: {missing}.");
        return true;
    }

    /// <summary>Frame <paramref name="index"/>: gray level 4 × index, with a white square that moves right 8 px a frame.</summary>
    private static byte[] Frame(int index)
    {
        var pixels = new byte[W * H * 4];
        var level = (byte)Math.Min(255, index * 4);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = level;
            pixels[i + 3] = 255;
        }
        var x0 = (index * 8) % (W - 40);
        for (var y = 20; y < 60; y++)
            pixels.AsSpan(((y * W) + x0) * 4, 40 * 4).Fill(255);
        return pixels;
    }

    private static AudioBlock Tone(TimeSpan at, int frames, double hz, float amplitude)
    {
        var samples = new float[frames * 2];
        var start = AudioMixer.FrameAt(at);
        for (var i = 0; i < frames; i++)
        {
            var value = (float)(amplitude * Math.Sin(2 * Math.PI * hz * (start + i) / AudioMixer.Rate));
            samples[2 * i] = samples[(2 * i) + 1] = value;
        }
        return new AudioBlock(at, samples);
    }

    private static int Gray(byte[] bgra, int x, int y) => bgra[(((y * W) + x) * 4) + 1];

    [Fact]
    public async Task Writes_an_mp4_that_decodes_to_the_frames_and_the_tone()
    {
        if (Unsupported()) return;
        var path = Path.Combine(_folder, "tone.mp4");
        var started = DateTime.UtcNow;
        using (var writer = new Mp4Writer(path, new VideoFormat(W, H, 30, true)))
        {
            for (var i = 0; i < 60; i++)
            {
                writer.WriteVideo(new BgraFrame(TimeSpan.Zero, Frame(i), W), TimeSpan.FromTicks(i * TimeSpan.TicksPerSecond / 30));
                if (i % 3 == 0) writer.WriteAudio(Tone(TimeSpan.FromMilliseconds(i / 3 * 100), 4800, 440, 0.5f));
            }
            var result = await writer.FinishAsync(TimeSpan.FromSeconds(2));
            Assert.True(result.Playable);
            Assert.Equal(0, writer.Dropped);
        }
        output.WriteLine($"Encoded 2 s at {W}x{H} in {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms, {new FileInfo(path).Length} bytes.");

        using var reader = MediaReader.Open(path, audio: true);
        Assert.Equal((W, H), (reader.Width, reader.Height));
        Assert.InRange(reader.Duration.TotalSeconds, 1.9, 2.15);
        var frame = new byte[W * H * 4];
        var times = new List<TimeSpan>();
        var grays = new List<int>();
        while (reader.ReadFrame(frame) is { } time)
        {
            times.Add(time);
            grays.Add(Gray(frame, 320, 300));
            if (times.Count == 1)
            {
                Assert.InRange(Gray(frame, 20, 40), 230, 255);      // the square, top left
                Assert.InRange(Gray(frame, 20, H - 40), 0, 12);     // not flipped
            }
        }
        output.WriteLine($"Decoded {times.Count} frames; gray at first {grays[0]}, last {grays[^1]}.");
        Assert.InRange(times.Count, 59, 61);
        Assert.InRange(grays[0], 0, 12);
        Assert.InRange(grays[^1], 224, 248);   // frame 59: 236

        Assert.Equal(48_000, reader.SampleRate);
        Assert.Equal(2, reader.Channels);
        using var audio = MediaReader.Open(path, audio: true);
        var samples = audio.ReadAllAudio();
        var seconds = samples.Length / 2.0 / 48_000;
        // The middle second, away from AAC's priming at the start.
        var middle = samples.AsSpan(48_000, 96_000).ToArray();
        var rms = Math.Sqrt(middle.Average(s => (double)s * s));
        var crossings = 0;
        for (var i = 2; i < middle.Length; i += 2) if ((middle[i - 2] < 0) != (middle[i] < 0)) crossings++;
        output.WriteLine($"Audio {seconds:F2} s, RMS {rms:F3}, {crossings} zero crossings in 1 s.");
        Assert.InRange(seconds, 1.9, 2.2);
        Assert.InRange(rms, 0.30, 0.40);        // 0.5 / sqrt 2 = 0.354
        Assert.InRange(crossings, 860, 900);    // 440 Hz
    }

    [Fact]
    public async Task A_still_screen_is_held_to_the_end_and_has_no_audio_track()
    {
        if (Unsupported()) return;
        var path = Path.Combine(_folder, "still.mp4");
        using (var writer = new Mp4Writer(path, new VideoFormat(W, H, 30, false)))
        {
            writer.WriteVideo(new BgraFrame(TimeSpan.Zero, Frame(10), W), TimeSpan.Zero);
            await writer.FinishAsync(TimeSpan.FromSeconds(3));
        }
        using var reader = MediaReader.Open(path, audio: true);
        Assert.InRange(reader.Duration.TotalSeconds, 2.9, 3.1);
        Assert.False(reader.HasAudio);
        var frame = new byte[W * H * 4];
        TimeSpan? last = null;
        while (reader.ReadFrame(frame) is { } time) last = time;
        Assert.NotNull(last);
        Assert.InRange(Gray(frame, 320, 300), 34, 46);
    }

    [Fact]
    public async Task Abort_leaves_the_file_for_the_caller_and_nothing_running()
    {
        if (Unsupported()) return;
        var path = Path.Combine(_folder, "aborted.mp4");
        var writer = new Mp4Writer(path, new VideoFormat(W, H, 30, false));
        writer.WriteVideo(new BgraFrame(TimeSpan.Zero, Frame(0), W), TimeSpan.Zero);
        await writer.AbortAsync();
        writer.Dispose();
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Odd_sizes_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new Mp4Writer(Path.Combine(_folder, "odd.mp4"), new VideoFormat(641, 360, 30, false)));
        Assert.False(File.Exists(Path.Combine(_folder, "odd.mp4")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }
    }
}
