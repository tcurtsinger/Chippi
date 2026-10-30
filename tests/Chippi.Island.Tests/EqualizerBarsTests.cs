// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.App.Island;

namespace Chippi.Island.Tests;

/// <summary>The tab equalizer's bars from the output's peaks (DESIGN.md §5 Tab).</summary>
public sealed class EqualizerBarsTests
{
    private static IReadOnlyList<double> Feed(EqualizerBars bars, float peak, int readings)
    {
        IReadOnlyList<double> last = [];
        for (int i = 0; i < readings; i++)
            last = bars.Next(peak);
        return last;
    }

    [Fact]
    public void Silence_leaves_the_bars_flat()
    {
        Assert.All(Feed(new EqualizerBars(), 0, 40), bar => Assert.True(bar < 0.01, $"{bar}"));
    }

    [Fact]
    public void Quiet_listening_still_fills_the_bars()
    {
        var bars = Feed(new EqualizerBars(), 0.08f, 40); // two seconds at a low volume
        Assert.True(bars[1] > 0.9, $"middle {bars[1]}");
    }

    [Fact]
    public void A_beat_kicks_the_last_bar_up_at_once_and_music_stopping_lets_them_fall()
    {
        var bars = new EqualizerBars();
        Feed(bars, 0.1f, 40);
        Feed(bars, 0.02f, 10); // a quiet stretch
        double before = bars.Next(0.02f)[2];
        Assert.True(bars.Next(0.1f)[2] > before + 0.4, "the beat"); // the level jumps back
        Assert.All(Feed(bars, 0, 40), bar => Assert.True(bar < 0.05, $"{bar}"));
    }
}
