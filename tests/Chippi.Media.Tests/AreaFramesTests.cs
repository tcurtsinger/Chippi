// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Capture;

namespace Chippi.Media.Tests;

public sealed class AreaFramesTests
{
    [Fact]
    public void Rows_are_copied_out_of_a_padded_staging_pitch()
    {
        const int width = 5, height = 4, pitch = 64; // 20 bytes of pixels, 44 of padding per row
        var mapped = new byte[pitch * height];
        for (int y = 0; y < height; y++)
            for (int i = 0; i < width * 4; i++) mapped[y * pitch + i] = (byte)(y * 50 + i);
        for (int y = 0; y < height; y++)
            for (int i = width * 4; i < pitch; i++) mapped[y * pitch + i] = 0xEE; // padding must not leak
        var tight = new byte[width * 4 * height];
        AreaReadback.CopyRows(mapped, pitch, width, height, tight);
        for (int y = 0; y < height; y++)
            for (int i = 0; i < width * 4; i++) Assert.Equal((byte)(y * 50 + i), tight[y * width * 4 + i]);
        Assert.DoesNotContain((byte)0xEE, tight);
    }

    [Fact]
    public void The_last_row_needs_no_padding_after_it()
    {
        const int width = 3, height = 2, pitch = 32;
        var mapped = new byte[pitch + width * 4]; // exactly the last row's pixels
        Array.Fill(mapped, (byte)7);
        var tight = new byte[width * 4 * height];
        AreaReadback.CopyRows(mapped, pitch, width, height, tight);
        Assert.All(tight, b => Assert.Equal(7, b));
    }

    [Fact]
    public void Short_buffers_and_small_pitches_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaReadback.CopyRows(new byte[100], 8, 4, 2, new byte[32]));
        Assert.Throws<ArgumentException>(() => AreaReadback.CopyRows(new byte[100], 32, 4, 2, new byte[16]));
        Assert.Throws<ArgumentException>(() => AreaReadback.CopyRows(new byte[40], 32, 4, 2, new byte[32]));
    }

    [Theory]
    [InlineData(0, 0, 2560, 1440, 2560, 1440, true)]
    [InlineData(100, 50, 700, 900, 2560, 1440, true)]
    [InlineData(2000, 0, 700, 900, 2560, 1440, false)]
    [InlineData(-1, 0, 10, 10, 2560, 1440, false)]
    [InlineData(0, 0, 0, 10, 2560, 1440, false)]
    public void An_area_must_fit_in_the_frame(int left, int top, int width, int height, int frameWidth, int frameHeight, bool fits) =>
        Assert.Equal(fits, AreaReadback.Fits(left, top, width, height, frameWidth, frameHeight));

    [Fact]
    public void A_request_maps_the_area_onto_its_display_and_checks_it_lies_inside()
    {
        var left = new PixelRect(-1920, 0, 1920, 1080);
        var request = new AreaFramesRequest(1, left, new PixelRect(-1500, 100, 700, 900));
        Assert.Equal(new PixelRect(420, 100, 700, 900), request.AreaOnDisplay);
        Assert.True(request.IsValid);
        Assert.False(new AreaFramesRequest(1, left, new PixelRect(-100, 100, 700, 900)).IsValid);   // crosses the display's edge
        Assert.False(new AreaFramesRequest(1, left, new PixelRect(-1500, 100, 0, 900)).IsValid);    // empty
    }

    [Fact]
    public async Task An_area_outside_its_display_is_refused_before_anything_starts()
    {
        using var frames = new WgcAreaFrames();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            frames.StartAsync(new AreaFramesRequest(1, new PixelRect(0, 0, 100, 100), new PixelRect(50, 50, 100, 100))));
    }

    // Reads a few frames of a display, so it captures the screen: only when CHIPPI_SCROLL_WGC is set (a Sandbox or an away session).
    [Fact]
    public async Task Live_frames_of_a_display_area_arrive_as_tight_rows()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_SCROLL_WGC") is null) return;
        var screen = RecordingSources.Get(RecordingTarget.Screen).First();
        int width = Math.Min(400, screen.Width), height = Math.Min(300, screen.Height);
        using var frames = new WgcAreaFrames();
        var got = new List<AreaFrame>();
        var enough = new TaskCompletionSource();
        frames.Frame += frame => { lock (got) { got.Add(frame); if (got.Count >= 3) enough.TrySetResult(); } };
        string? failed = null;
        frames.Failed += reason => { failed = reason; enough.TrySetResult(); };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await frames.StartAsync(new AreaFramesRequest(screen.Handle, new PixelRect(0, 0, screen.Width, screen.Height), new PixelRect(0, 0, width, height)), timeout.Token);
        await enough.Task.WaitAsync(timeout.Token);
        Assert.Null(failed);
        lock (got)
        {
            Assert.All(got, frame =>
            {
                Assert.Equal(width, frame.Width); Assert.Equal(height, frame.Height);
                Assert.Equal(width * height * 4, frame.Bgra.Length);
            });
        }
    }

    [Fact]
    public void A_captured_screen_is_made_opaque_without_touching_its_colors()
    {
        var bgra = new byte[] { 1, 2, 3, 0, 4, 5, 6, 128, 7, 8, 9, 255 };
        AreaReadback.ForceOpaque(bgra);
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255 }, bgra);
    }
}
