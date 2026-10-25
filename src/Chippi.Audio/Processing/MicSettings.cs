// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Audio.Processing;

/// <summary>
/// Live parameters for the mic chain, shared by the DSP stages (which read them on the audio thread)
/// and Mic processing's views (which write them), so a change is heard at once. Single bool and float
/// fields read and write atomically, so there is no lock. Defaults are Hopper's.
/// </summary>
public sealed class MicSettings
{
    /// <summary>RNNoise neural noise suppression.</summary>
    public bool Suppression { get; set; } = true;

    /// <summary>Downward noise gate, a gentle backstop after suppression.</summary>
    public bool Gate { get; set; } = true;

    /// <summary>Linear amplitude below which the gate closes (0.015 is about -36.5 dBFS).</summary>
    public float GateThreshold { get; set; } = 0.015f;

    /// <summary>Automatic gain control.</summary>
    public bool Agc { get; set; } = true;

    /// <summary>Loudness (smoothed RMS) the AGC brings speech to (0.12 is about -18.4 dBFS).</summary>
    public float AgcTarget { get; set; } = 0.12f;

    /// <summary>Most the AGC amplifies (10 is +20 dB).</summary>
    public float AgcMaxGain { get; set; } = 10f;

    public static float FromDecibels(double db) => (float)Math.Pow(10, db / 20);

    public static double ToDecibels(float amplitude) => 20 * Math.Log10(amplitude);
}
