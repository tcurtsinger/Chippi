// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Island;

namespace Chippi.Island.Tests;

public sealed class BeatDetectorTests
{
    // The output's meter reads about every 30ms.
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(30);

    [Fact]
    public void A_steady_level_has_no_beats()
    {
        var beats = new BeatDetector();
        var found = Feed(beats, Enumerable.Repeat(0.4f, 200));
        Assert.Empty(found);
    }

    [Fact]
    public void A_kick_over_the_music_every_half_second_is_a_beat_each_time()
    {
        var beats = new BeatDetector();
        // A 0.25 bed with a 0.7 kick every 500ms (120 a minute), held for one tick.
        var level = Enumerable.Range(0, 200).Select(i => i % 17 == 0 ? 0.7f : 0.25f);
        var found = Feed(beats, level);
        Assert.InRange(found.Count, 10, 12);
        Assert.All(found, b => Assert.InRange(b.Strength, 0.3, 1));
        Assert.All(found.Zip(found.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= BeatDetector.Gap));
    }

    [Fact]
    public void Two_kicks_closer_than_the_gap_are_one_beat()
    {
        var beats = new BeatDetector();
        var level = new List<float>(Enumerable.Repeat(0.25f, 40)) { 0.7f, 0.25f, 0.25f, 0.7f };
        level.AddRange(Enumerable.Repeat(0.25f, 10));
        Assert.Single(Feed(beats, level));
    }

    [Fact]
    public void Music_too_quiet_to_hear_never_beats_and_a_new_song_starts_from_its_own_level()
    {
        var beats = new BeatDetector();
        var quiet = Enumerable.Range(0, 200).Select(i => i % 17 == 0 ? (float)(BeatDetector.Floor * 0.9) : 0.01f);
        Assert.Empty(Feed(beats, quiet));

        // After a loud song, a quieter one still beats once the detector is reset.
        var loud = new BeatDetector();
        Feed(loud, Enumerable.Repeat(0.9f, 100));
        loud.Reset();
        var level = Enumerable.Range(0, 100).Select(i => i % 17 == 0 ? 0.5f : 0.2f);
        Assert.NotEmpty(Feed(loud, level));
    }

    private static List<(TimeSpan At, double Strength)> Feed(BeatDetector beats, IEnumerable<float> level)
    {
        var found = new List<(TimeSpan, double)>();
        var at = TimeSpan.FromSeconds(10);
        foreach (var peak in level)
        {
            if (beats.Next(peak, at) is { } strength)
                found.Add((at, strength));
            at += Tick;
        }
        return found;
    }
}
