// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;

namespace Chippi.Core.Tests;

/// <summary>Where Snap's Next monitor and the App switcher put a window on another display. Work areas in physical pixels.</summary>
public sealed class DisplayMoveTests
{
    // Travis's displays: a 3440x1440 ultrawide (primary) and a 1920x1080 to its left, each with a 48px taskbar.
    private static readonly (int, int, int, int) Wide = (0, 0, 3440, 1392), Side = (-1920, 0, 1920, 1032);

    [Fact]
    public void Same_size_displays_keep_the_place_and_size()
    {
        var other = (3440, 0, 3440, 1392);
        Assert.Equal((3540, 50, 800, 600), DisplayMove.Fit((100, 50, 800, 600), Wide, other));
    }

    [Fact]
    public void To_a_smaller_display_it_shrinks_by_the_tighter_side_and_keeps_its_place()
    {
        // The left half of the ultrawide: the width ratio (0.558) is tighter than the height's (0.741).
        Assert.Equal((-1920, 0, 960, 777), DisplayMove.Fit((0, 0, 1720, 1392), Wide, Side));
        Assert.Equal((-960, 0, 960, 777), DisplayMove.Fit((1720, 0, 1720, 1392), Wide, Side));
    }

    [Fact]
    public void To_a_larger_display_it_keeps_its_size()
    {
        Assert.Equal((860, 135, 960, 700), DisplayMove.Fit((-1440, 100, 960, 700), Side, Wide));
    }

    [Fact]
    public void To_a_denser_display_with_more_pixels_it_never_grows()
    {
        var fourK = (3440, 0, 3840, 2088); // 3840x2160 at 150%
        Assert.Equal((5360, 0, 1720, 1392), DisplayMove.Fit((1720, 0, 1720, 1392), Wide, fourK));
    }

    [Fact]
    public void A_window_hanging_off_the_edge_lands_inside_the_work_area()
    {
        var (x, y, width, height) = DisplayMove.Fit((3000, 1200, 800, 600), Wide, Side);
        Assert.Equal((447, 335), (width, height));
        Assert.Equal((-447, 697), (x, y)); // flush with the right edge and the taskbar
        Assert.True(y + height <= 1032);
    }

    [Fact]
    public void A_window_larger_than_the_display_is_cut_to_it()
    {
        Assert.Equal((-1920, 0, 1920, 1032), DisplayMove.Fit((-50, -50, 4000, 2000), Wide, Side));
    }

    [Fact]
    public void A_removed_display_refuses()
    {
        Assert.Throws<ArgumentException>(() => DisplayMove.Fit((0, 0, 10, 10), Wide, (0, 0, 0, 0)));
    }
}
