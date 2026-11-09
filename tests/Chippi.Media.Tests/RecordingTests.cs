// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;

namespace Chippi.Media.Tests;

public sealed class RecordingTests
{
    [Fact]
    public void Paused_clock_rejects_stale_packets_and_removes_multiple_pauses()
    {
        long now = Stopwatch.Frequency;
        var clock = new RecordingClock(() => now);
        Assert.Null(clock.At(now)); clock.Start(); now += Stopwatch.Frequency * 2;
        Assert.Equal(TimeSpan.FromSeconds(2), clock.Elapsed);
        clock.Pause(); long beforePause = now; now += Stopwatch.Frequency * 20;
        Assert.Null(clock.At(now)); Assert.Equal(TimeSpan.FromSeconds(2), clock.Elapsed);
        clock.Resume(); Assert.Null(clock.At(beforePause)); now += Stopwatch.Frequency;
        Assert.Equal(TimeSpan.FromSeconds(3), clock.Elapsed);
        clock.Pause(); now += Stopwatch.Frequency * 9; clock.Resume(); now += Stopwatch.Frequency;
        Assert.Equal(TimeSpan.FromSeconds(4), clock.Elapsed);
    }
    [Fact]
    public void Audio_ring_bounds_backlog_mixes_sources_and_does_not_replay_stale_samples()
    {
        var ring = new AudioTimeline();
        ring.Write(0, [.3f, .2f, .1f, .4f]); ring.Write(96_000, [1, 1]);
        float[] output = [.1f, .1f, .1f, .1f]; ring.Mix(0, output);
        Assert.Equal(.4f, output[0], 5); Assert.Equal(.5f, output[3], 5);
        ring.Write(0, [1, 1]); Array.Clear(output); ring.Mix(2, output); Assert.All(output, x => Assert.Equal(0, x));
        ring.Write(96_000, [.5f, .6f]); Array.Clear(output); ring.Mix(96_000, output); Assert.Equal(.5f, output[0]);
    }
    [Theory]
    [InlineData(3840, 2160, 1920, 1080)] [InlineData(1281, 721, 1280, 720)] [InlineData(3440, 1440, 2580, 1080)]
    public void Low_impact_size_preserves_aspect_and_even_dimensions(int width, int height, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), new RecordingOptions().OutputSize(width, height));
    [Fact]
    public void Surround_mapping_uses_speaker_positions_and_excludes_LFE()
    {
        Assert.Equal((0f, 0f), RecordingAudio.ChannelGain(8, 3));
        Assert.Equal((.5f, 0f), RecordingAudio.ChannelGain(0x10, 2));
        Assert.Equal((0f, .5f), RecordingAudio.ChannelGain(0x20, 3));
        Assert.Equal((.5f, .5f), RecordingAudio.ChannelGain(4, 2));
    }
    [Fact]
    public async Task Export_refuses_original_destination_before_touching_any_file()
    {
        string file = Path.Combine(Path.GetTempPath(), "chippi-export-" + Guid.NewGuid() + ".mp4");
        File.WriteAllText(file, "original");
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => RecordingExports.TrimAsync(file, file, TimeSpan.Zero, TimeSpan.FromSeconds(1), null, default));
            await Assert.ThrowsAsync<ArgumentException>(() => RecordingExports.GifAsync(file, file, TimeSpan.Zero, TimeSpan.FromSeconds(1), null, default));
            Assert.Equal("original", File.ReadAllText(file));
        }
        finally { File.Delete(file); }
    }
    [Fact]
    public async Task Gif_loop_extension_follows_global_palette_and_preserves_image_blocks()
    {
        byte[] original = [71, 73, 70, 56, 57, 97, 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 255, 255, 255, 0x2c, 0x3b];
        using var output = new MemoryStream();
        await RecordingExports.AddGifLoopAsync(new MemoryStream(original), output, default);
        byte[] result = output.ToArray();
        Assert.Equal(original[..19], result[..19]); Assert.Equal(original[19..], result[^2..]);
        Assert.Contains("NETSCAPE2.0", System.Text.Encoding.ASCII.GetString(result));
    }
}
