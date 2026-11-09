// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Chippi.Media.Tests;

public sealed class RecordingAudioTests
{
    [Fact]
    public unsafe void Buffered_device_delivery_is_not_replaced_with_silence_before_samples_arrive()
    {
        var timeline = new AudioTimeline(); var packets = new RecordingAudioPackets(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1), 480, timeline);
        float[] data = Enumerable.Repeat(.4f, 480).ToArray(), mixed = new float[960 * 2]; long read = 0;
        for (int packet = 0; packet < 100; packet++)
        {
            var at = TimeSpan.FromMilliseconds(packet * 10); var received = at + TimeSpan.FromMilliseconds(210);
            fixed (float* input = data) packets.Write((nint)input, 480, 0, packet * 480, at, received);
            long available = (long)Math.Max(0, (received - RecordingAudio.BufferDelay).TotalSeconds * 48_000);
            while (read + 960 <= available)
            {
                Array.Clear(mixed); timeline.Mix(read, mixed);
                Assert.All(mixed.Skip(read == 0 ? 128 : 0), value => Assert.InRange(value, .39f, .41f)); read += 960;
            }
        }
        packets.Flush();
        float[] tail = new float[(48_000 - read) * 2]; timeline.Mix(read, tail); Assert.All(tail.Take(tail.Length - 64), value => Assert.InRange(value, .39f, .41f));
        Assert.Equal(0, timeline.LateFrames);
    }
    [Theory]
    [InlineData(16_000, 1)] [InlineData(44_100, 1)] [InlineData(48_000, 2)] [InlineData(96_000, 6)]
    public unsafe void Uneven_packets_timestamp_jitter_and_uncertain_timestamps_do_not_create_gaps(int rate, int channels)
    {
        var timeline = new AudioTimeline();
        var packets = new RecordingAudioPackets(WaveFormat.CreateIeeeFloatWaveFormat(rate, channels), 1024, timeline);
        int position = 0, index = 0;
        while (position < rate)
        {
            int count = Math.Min(rate - position, new[] { 127, 480, 311, 251 }[index % 4]);
            var data = Enumerable.Repeat(.4f, count * channels).ToArray();
            bool uncertain = index % 11 == 0;
            var at = TimeSpan.FromSeconds(position / (double)rate + (index == 0 ? 0 : (index % 2 == 0 ? 1 : -1) / 48_000d));
            fixed (float* input = data) packets.Write((nint)input, count, uncertain ? AudioClientBufferFlags.TimestampError : 0, uncertain ? 0 : position, uncertain ? null : at, at + TimeSpan.FromSeconds(count / (double)rate));
            position += count; index++;
        }
        packets.Flush();
        float[] result = new float[48_000 * 2]; timeline.Mix(0, result);
        Assert.All(result.Skip(128).Take(47_000 * 2), sample => Assert.InRange(sample, .39f, .41f));
        Assert.InRange(packets.EndFrame, 47_990, 48_010); Assert.True(packets.TimestampErrors > 0); Assert.Equal(0, packets.Discontinuities);
    }

    [Theory]
    [InlineData(16_000, 16)] [InlineData(44_100, 24)] [InlineData(48_000, 32)]
    public unsafe void Integer_mic_formats_keep_a_clean_tone_across_packet_boundaries(int rate, int bits)
    {
        var timeline = new AudioTimeline(); var packets = new RecordingAudioPackets(new WaveFormat(rate, bits, 1), 480, timeline);
        for (int position = 0; position < rate;)
        {
            int count = Math.Min(317, rate - position); byte[] data = new byte[count * bits / 8];
            for (int i = 0; i < count; i++)
            {
                int sample = (int)(.4 * Math.Sin(2 * Math.PI * 440 * (position + i) / rate) * (bits == 32 ? int.MaxValue : (1 << (bits - 1)) - 1));
                for (int b = 0; b < bits / 8; b++) data[i * bits / 8 + b] = (byte)(sample >> (8 * b));
            }
            var at = TimeSpan.FromSeconds(position / (double)rate);
            fixed (byte* input = data) packets.Write((nint)input, count, 0, position, at, at + TimeSpan.FromSeconds(count / (double)rate));
            position += count;
        }
        float[] audio = new float[48_000 * 2]; timeline.Mix(0, audio);
        Assert.True(ToneResidual(audio, 440) < .01, "Resampled microphone has broadband distortion or packet-edge clicks");
    }

    [Fact]
    public unsafe void Slow_endpoint_clock_drift_is_corrected_without_dropping_or_inserting_samples()
    {
        var timeline = new AudioTimeline(); var packets = new RecordingAudioPackets(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1), 480, timeline);
        float[] data = Enumerable.Repeat(.4f, 480).ToArray(), drain = new float[960]; long read = 0;
        for (int packet = 0; packet < 2000; packet++)
        {
            int position = packet * 480; var at = TimeSpan.FromSeconds(position / 48_000d / 1.0002);
            fixed (float* input = data) packets.Write((nint)input, 480, 0, position, at, at + TimeSpan.FromMilliseconds(10));
            while (read + 480 < packets.EndFrame - 12_000)
            {
                Array.Clear(drain); timeline.Mix(read, drain); read += 480;
                Assert.All(drain.Skip(read == 480 ? 128 : 0), value => Assert.InRange(value, .39f, .41f));
            }
        }
        Assert.InRange(Math.Abs(packets.EndFrame - 960_000 / 1.0002), 0, 48); Assert.Equal(0, timeline.LateFrames);
    }

    [Fact]
    public unsafe void Pause_resets_endpoint_position_and_reanchors_to_the_video_clock()
    {
        var timeline = new AudioTimeline(); var packets = new RecordingAudioPackets(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1), 480, timeline);
        float[] data = Enumerable.Repeat(.4f, 480).ToArray();
        fixed (float* input = data)
        {
            packets.Write((nint)input, 480, 0, 0, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
            packets.Flush(); packets.Reset();
            packets.Write((nint)input, 480, 0, 0, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20));
        }
        float[] resumed = new float[400 * 2]; timeline.Mix(480, resumed);
        Assert.All(resumed.Skip(64), sample => Assert.InRange(sample, .39f, .41f));
    }

    [Fact]
    public unsafe void Genuine_device_gap_remains_silence_and_following_packets_recover()
    {
        var timeline = new AudioTimeline(); var packets = new RecordingAudioPackets(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1), 480, timeline);
        float[] data = Enumerable.Repeat(.4f, 480).ToArray();
        fixed (float* input = data)
        {
            packets.Write((nint)input, 480, 0, 0, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
            packets.Write((nint)input, 480, AudioClientBufferFlags.DataDiscontinuity, 960, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(30));
        }
        float[] gap = new float[400 * 2]; timeline.Mix(480, gap); Assert.All(gap, sample => Assert.Equal(0, sample));
        float[] recovered = new float[400 * 2]; timeline.Mix(960, recovered); Assert.All(recovered.Skip(64), sample => Assert.InRange(sample, .39f, .41f)); Assert.Equal(1, packets.Discontinuities);
    }

    [Fact]
    public void Loud_system_and_mic_mix_has_headroom_instead_of_flat_topped_clipping()
    {
        float[] mixed = Enumerable.Range(0, 4800).Select(i => (float)(1.8 * Math.Sin(2 * Math.PI * 440 * i / 48_000))).ToArray();
        byte[] bytes = new byte[mixed.Length * 2]; RecordingAudio.ToPcm(mixed, bytes, 2);
        for (int i = 0; i < mixed.Length; i++)
        {
            float sample = BitConverter.ToInt16(bytes, i * 2) / 32767f;
            Assert.InRange(Math.Abs(sample - mixed[i] / 2), 0, .00004f);
        }
    }

    internal static double ToneResidual(float[] stereo, double frequency)
    {
        int start = 480, end = stereo.Length / 2 - 480, count = end - start;
        double sine = 0, cosine = 0, power = 0, ss = 0, cc = 0, sc = 0;
        for (int i = start; i < end; i++)
        { double value = stereo[i * 2], angle = 2 * Math.PI * frequency * i / 48_000, s = Math.Sin(angle), c = Math.Cos(angle); sine += value * s; cosine += value * c; power += value * value; ss += s * s; cc += c * c; sc += s * c; }
        if (power / count < 1e-6) return double.PositiveInfinity;
        double determinant = ss * cc - sc * sc;
        double tonePower = ((sine * cc - cosine * sc) * sine + (cosine * ss - sine * sc) * cosine) / determinant;
        return Math.Sqrt(Math.Max(0, power - tonePower) / power);
    }
}
