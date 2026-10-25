// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio.Processing;
using NAudio.Wave;

namespace Chippi.Audio.Tests;

public class DspTests
{
    private const int Rate = 48000;

    /// <summary>A 48 kHz mono sine of the given amplitude, endless.</summary>
    private sealed class Sine(float amplitude) : ISampleProvider
    {
        private long _n;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
                buffer[offset + i] = amplitude * MathF.Sin(2 * MathF.PI * 440 * _n++ / Rate);
            return count;
        }
    }

    private static float Peak(ISampleProvider chain, int seconds = 1)
    {
        var buffer = new float[480];
        var peak = 0f;
        for (var i = 0; i < seconds * Rate / buffer.Length; i++)
        {
            var read = chain.Read(buffer, 0, buffer.Length);
            peak = buffer.Take(read).Select(MathF.Abs).Max();
        }
        return peak; // the last block's peak, after the stage settled
    }

    [Fact]
    public void Gate_closes_below_its_threshold_and_passes_above_it()
    {
        var settings = new MicSettings();
        Assert.Equal(0f, Peak(new NoiseGate(new Sine(0.005f), settings)), 3);
        Assert.Equal(0.5f, Peak(new NoiseGate(new Sine(0.5f), settings)), 2);
    }

    [Fact]
    public void Gate_off_passes_everything()
    {
        Assert.Equal(0.005f, Peak(new NoiseGate(new Sine(0.005f), new MicSettings { Gate = false })), 3);
    }

    [Fact]
    public void Agc_brings_quiet_speech_up_toward_its_target_within_max_gain()
    {
        var settings = new MicSettings();
        var quiet = Peak(new AutoGain(new Sine(0.05f), settings), seconds: 3);
        // RMS target 0.12 is a sine peak of about 0.17; 0.05 * max gain 10 would allow 0.5.
        Assert.InRange(quiet, 0.15f, 0.19f);
    }

    [Fact]
    public void Agc_never_exceeds_its_max_gain_or_full_scale()
    {
        var capped = Peak(new AutoGain(new Sine(0.01f), new MicSettings { AgcMaxGain = 2f }), seconds: 3);
        Assert.InRange(capped, 0.019f, 0.0201f); // RMS 0.007 is above the silence floor; gain stops at 2
        Assert.InRange(Peak(new AutoGain(new Sine(1f), new MicSettings()), seconds: 3), 0f, 1f);
    }

    [Fact]
    public void Decibels_convert_both_ways()
    {
        Assert.Equal(0.015f, MicSettings.FromDecibels(-36.48), 4);
        Assert.Equal(20, MicSettings.ToDecibels(10f), 3);
    }

    [Fact]
    public void Rnnoise_loads_for_this_architecture_and_processes_frames()
    {
        Assert.True(File.Exists(RnnoiseNative.LibraryPath), RnnoiseNative.LibraryPath);
        using var suppressor = new RnnoiseSuppressor(new Sine(0.1f), new MicSettings());
        var buffer = new float[1000]; // not a multiple of the 480-sample frame
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(buffer.Length, suppressor.Read(buffer, 0, buffer.Length));
            Assert.All(buffer, s => Assert.True(float.IsFinite(s)));
        }
        Assert.InRange(suppressor.VoiceProbability, 0f, 1f);
    }
}
