// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio.Processing;

namespace Chippi.Features.Sound.Processing;

/// <summary>Mic processing's saved settings (<c>modules.mic-processing</c>). Levels are in dB, as the page shows them.</summary>
public sealed record ProcessingSettings
{
    /// <summary>The source that follows Audio routing's microphone list.</summary>
    public const string PinnedSource = "pinned";

    /// <summary>The user's switch. What actually runs is <see cref="ProcessingStatus"/>.</summary>
    public bool Enabled { get; init; }

    public bool Suppression { get; init; } = true;

    public bool Gate { get; init; } = true;

    /// <summary>Hopper's 0.015 amplitude (brief §2: −36.5 dB, not the mockup's sample −50).</summary>
    public double GateDb { get; init; } = -36.5;

    public bool Agc { get; init; } = true;

    public double TargetDb { get; init; } = -18.4;

    public double MaxGainDb { get; init; } = 20;

    /// <summary><see cref="PinnedSource"/>, or one mic's endpoint ID.</summary>
    public string Source { get; init; } = PinnedSource;

    /// <summary>"Default device": CABLE Output takes the capture roles while live.</summary>
    public bool SetDefault { get; init; } = true;

    // The mockup's slider ranges (behavior spec, Main:851-853).
    public static (double Min, double Max) GateRange { get; } = (-70, -20);

    public static (double Min, double Max) TargetRange { get; } = (-30, -6);

    public static (double Min, double Max) MaxGainRange { get; } = (0, 30);

    /// <summary>Clamps every level into its range and fills a missing source.</summary>
    public ProcessingSettings Normalized() => this with
    {
        GateDb = Clamp(GateDb, GateRange),
        TargetDb = Clamp(TargetDb, TargetRange),
        MaxGainDb = Clamp(MaxGainDb, MaxGainRange),
        Source = string.IsNullOrWhiteSpace(Source) ? PinnedSource : Source,
    };

    /// <summary>Copies the DSP values into the live object the running pipeline reads.</summary>
    public void ApplyTo(MicSettings live)
    {
        live.Suppression = Suppression;
        live.Gate = Gate;
        live.GateThreshold = MicSettings.FromDecibels(GateDb);
        live.Agc = Agc;
        live.AgcTarget = MicSettings.FromDecibels(TargetDb);
        live.AgcMaxGain = MicSettings.FromDecibels(MaxGainDb);
    }

    private static double Clamp(double value, (double Min, double Max) range) =>
        double.IsFinite(value) ? Math.Clamp(value, range.Min, range.Max) : range.Max;
}
