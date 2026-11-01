// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.App.Island;

/// <summary>
/// Picks the beats out of the default output's live peak level, for Chippi to bob to while he listens: a peak well
/// above the level's recent average, loud enough to hear, and no sooner than a beat at 214 a minute would allow.
/// </summary>
internal sealed class BeatDetector
{
    /// <summary>How far above the recent average a peak has to jump to be a beat.</summary>
    internal const double Rise = 1.3;

    /// <summary>Below this the music is too quiet to bob to.</summary>
    internal const double Floor = 0.08;

    /// <summary>The fewest time between two beats.</summary>
    internal static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(280);

    // The recent average follows the level with a time constant of half a second, so it tracks a song's loudness, not its beats.
    private const double Memory = 0.5;

    private double _average;
    private TimeSpan? _last;
    private TimeSpan? _beat;

    /// <summary>
    /// The next peak, at <paramref name="now"/> on any steady clock: a beat's strength (0.3 to 1, by how far it
    /// jumped), or null.
    /// </summary>
    public double? Next(float peak, TimeSpan now)
    {
        double before = _average;
        double dt = _last is { } last ? Math.Max(0, (now - last).TotalSeconds) : double.PositiveInfinity;
        _last = now;
        _average += (1 - Math.Exp(-dt / Memory)) * (peak - _average);
        if (peak < Floor || before <= 0 || peak < before * Rise || (_beat is { } beat && now - beat < Gap))
            return null;
        _beat = now;
        return Math.Clamp((peak / before - 1) / 0.8, 0.3, 1);
    }

    /// <summary>The music stopped: the next song starts from its own level.</summary>
    public void Reset() => (_average, _last, _beat) = (0, null, null);
}
