// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Media;
using Chippi.Media.Gif;

namespace Chippi.Media.Tests;

public sealed class ArithmeticTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Timeline_cuts_each_pause_out()
    {
        var timeline = new RecordingTimeline();
        Assert.Null(timeline.Map(S(1)));
        timeline.Begin(S(10));
        Assert.Null(timeline.Map(S(9)));
        Assert.Equal(S(2), timeline.Map(S(12)));
        timeline.Pause(S(13));
        Assert.Null(timeline.Map(S(14)));
        Assert.Equal(S(3), timeline.Elapsed(S(20)));
        timeline.Resume(S(20));
        timeline.Pause(S(21));
        timeline.Resume(S(25));
        Assert.Equal(S(2.5), timeline.Map(S(12.5)));
        Assert.Null(timeline.Map(S(15)));
        Assert.Equal(S(3), timeline.Map(S(20)));
        Assert.Null(timeline.Map(S(22)));
        Assert.Equal(S(5), timeline.Map(S(26)));
        Assert.Equal(S(4), timeline.Elapsed(S(23)));
        Assert.Equal(S(5), timeline.Elapsed(S(26)));
    }

    [Fact]
    public void Mixer_joins_jittered_packets_and_fills_real_gaps_with_silence()
    {
        var mixer = new AudioMixer(1);
        var first = mixer.Add(0, TimeSpan.Zero, Fill(0.5f, 480));
        // 5 ms late: inside the jitter, joined on.
        var second = mixer.Add(0, TimeSpan.FromMilliseconds(15), Fill(0.5f, 480));
        Assert.Equal(TimeSpan.Zero, first.Single().Time);
        Assert.Equal(TimeSpan.FromMilliseconds(10), second.Single().Time);
        // 80 ms later than expected: silence first.
        var third = mixer.Add(0, TimeSpan.FromMilliseconds(100), Fill(0.5f, 480));
        var block = third.Single();
        Assert.Equal(TimeSpan.FromMilliseconds(20), block.Time);
        Assert.Equal(90 * 48, block.Frames);
        Assert.Equal(0f, block.Samples[0]);
        Assert.Equal(0.5f, block.Samples[^1]);
        // Overlapping what's there by 50 ms: the overlap is dropped.
        var fourth = mixer.Add(0, TimeSpan.FromMilliseconds(60), Fill(0.25f, 4800));
        Assert.Equal(TimeSpan.FromMilliseconds(110), fourth.Single().Time);
        Assert.Equal(50 * 48, fourth.Single().Frames);
    }

    [Fact]
    public void Mixer_clips_the_sum()
    {
        var mixer = new AudioMixer(2);
        Assert.Empty(mixer.Add(0, TimeSpan.Zero, Fill(0.8f, 48)));
        var mixed = mixer.Add(1, TimeSpan.Zero, Fill(0.8f, 48)).Single();
        Assert.All(mixed.Samples, s => Assert.Equal(1f, s));
    }

    [Fact]
    public void Pacer_keeps_the_first_frame_of_each_slot()
    {
        var pacer = new FramePacer(30);
        Assert.Equal(TimeSpan.Zero, pacer.Take(TimeSpan.Zero));
        Assert.Null(pacer.Take(TimeSpan.FromMilliseconds(20)));
        Assert.Equal(TimeSpan.FromTicks(333_333), pacer.Take(TimeSpan.FromMilliseconds(34)));
        // Nothing moved for a second: the next frame lands in its own slot.
        Assert.Equal(TimeSpan.FromTicks(13_333_333), pacer.Take(TimeSpan.FromMilliseconds(1340)));
        Assert.Equal(3, pacer.Kept);
    }

    [Fact]
    public void Gif_runs_at_15_fps_with_delays_that_add_up()
    {
        Assert.Equal(15, GifPlan.FrameCount(S(1)));
        Assert.Equal(16, GifPlan.FrameCount(S(1.01)));
        Assert.Equal(0, GifPlan.FrameCount(TimeSpan.Zero));
        Assert.Equal([7, 6, 7], Enumerable.Range(0, 3).Select(i => GifPlan.Delay(i)));
        Assert.Equal(100, Enumerable.Range(0, 15).Sum(i => GifPlan.Delay(i)));
        Assert.Equal(6000, Enumerable.Range(0, 900).Sum(i => GifPlan.Delay(i)));
        Assert.Equal(GifPlan.Delay(3) + GifPlan.Delay(4) + GifPlan.Delay(5), GifPlan.Delay(3, 3));
        Assert.Equal(TimeSpan.FromTicks(666_666), GifPlan.FrameTime(1));
    }

    [Fact]
    public void Gif_is_cut_to_60_seconds_from_the_start_of_the_span()
    {
        var (whole, cut) = GifPlan.Span(TrimRange.Whole(S(45)));
        Assert.False(cut);
        Assert.Equal(TrimRange.Whole(S(45)), whole);
        var (longer, cutLonger) = GifPlan.Span(TrimRange.Whole(S(95)));
        Assert.True(cutLonger);
        Assert.Equal(new TrimRange(TimeSpan.Zero, S(60)), longer);
        var (trimmed, cutTrimmed) = GifPlan.Span(new TrimRange(S(20), S(90)));
        Assert.True(cutTrimmed);
        Assert.Equal(new TrimRange(S(20), S(80)), trimmed);
        Assert.False(GifPlan.Span(new TrimRange(S(20), S(80))).Cut);
    }

    [Fact]
    public void Trim_handles_stay_inside_and_apart()
    {
        Assert.Equal(new TrimRange(S(2.5), S(7.5)), TrimRange.FromBar(0.25, 0.75, S(10)));
        Assert.Equal(new TrimRange(S(2.5), S(7.5)), TrimRange.FromBar(0.75, 0.25, S(10)));
        Assert.Equal(TrimRange.Whole(S(10)), TrimRange.FromBar(-1, 2, S(10)));
        Assert.Equal(new TrimRange(S(5), S(5.5)), TrimRange.FromBar(0.5, 0.5, S(10)));
        Assert.Equal(new TrimRange(S(9.5), S(10)), TrimRange.FromBar(1, 1, S(10)));
        Assert.Equal(TrimRange.Whole(S(0.3)), TrimRange.FromBar(0.2, 0.4, S(0.3)));
        Assert.True(TrimRange.Whole(S(10)).IsWhole(S(10)));
        Assert.False(new TrimRange(S(1), S(10)).IsWhole(S(10)));
    }

    [Fact]
    public void Gif_diff_writes_only_the_change()
    {
        var diff = new GifFrameDiff(4, 3);
        var frame = new byte[4 * 3 * 4];
        Array.Fill(frame, (byte)100);
        var first = diff.Next(frame)!;
        Assert.Equal((0, 0, 4, 3), (first.X, first.Y, first.Width, first.Height));
        Assert.All(first.Changed, Assert.True);

        // Encoder noise: no change.
        frame[0] = 104;
        Assert.Null(diff.Next(frame));

        // One pixel at (2, 1) and one at (3, 2) change.
        Paint(frame, 4, 2, 1, 200);
        Paint(frame, 4, 3, 2, 10);
        var patch = diff.Next(frame)!;
        Assert.Equal((2, 1, 2, 2), (patch.X, patch.Y, patch.Width, patch.Height));
        Assert.Equal([true, false, false, true], patch.Changed);
        Assert.Equal(200, patch.Pixels[0]);

        // Slow drift adds up against what's shown, not the last frame.
        frame[0] = 105;
        Assert.Null(diff.Next(frame));
        frame[0] = 107;
        var drift = diff.Next(frame)!;
        Assert.Equal((0, 0, 1, 1), (drift.X, drift.Y, drift.Width, drift.Height));
    }

    private static void Paint(byte[] frame, int width, int x, int y, byte value) =>
        frame.AsSpan(((y * width) + x) * 4, 3).Fill(value);

    private static float[] Fill(float value, int frames)
    {
        var samples = new float[frames * AudioMixer.Channels];
        Array.Fill(samples, value);
        return samples;
    }
}
