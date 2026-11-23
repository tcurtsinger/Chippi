// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Capture;

namespace Chippi.Capture.Tests;

/// <summary>Where the scrolling capture's bar and preview go, and what the preview strip draws. Pure geometry, no windows.</summary>
public sealed class ScrollingPlacementTests
{
    private static readonly PixelRect Display = new(0, 0, 1920, 1080);

    [Fact]
    public void The_preview_sits_outside_the_areas_right_edge_top_aligned()
    {
        var area = new PixelRect(100, 100, 700, 508);
        var preview = ScrollingPlacement.Preview(area, Display, new PixelSize(108, 300));
        Assert.Equal(new PixelRect(area.Right + BarPlacement.Gap, 100, 108, 300), preview);
    }

    [Fact]
    public void The_preview_flips_to_the_left_at_the_displays_right_edge()
    {
        var area = new PixelRect(1100, 100, 780, 600); // 40 px to the right edge: no room for 108
        var preview = ScrollingPlacement.Preview(area, Display, new PixelSize(108, 300));
        Assert.Equal(area.Left - BarPlacement.Gap - 108, preview.X);
        Assert.Equal(100, preview.Y);
        Assert.True(preview.Right <= area.Left);
    }

    [Fact]
    public void The_preview_goes_inside_when_neither_side_has_room_and_never_leaves_the_display()
    {
        var area = new PixelRect(20, 20, 1880, 1040);
        var preview = ScrollingPlacement.Preview(area, Display, new PixelSize(108, 300));
        Assert.True(Display.Contains(preview));
        Assert.Equal(area.Right - BarPlacement.Gap - 108, preview.X);

        // Taller than the room below the area's top: held on the display.
        var tall = ScrollingPlacement.Preview(new PixelRect(100, 900, 700, 170), Display, new PixelSize(108, 500));
        Assert.True(Display.Contains(tall), tall.ToString());
    }

    [Fact]
    public void The_preview_follows_a_display_that_is_not_at_the_origin()
    {
        var left = new PixelRect(-1920, 0, 1920, 1080);
        var area = new PixelRect(-1500, 200, 700, 500);
        var preview = ScrollingPlacement.Preview(area, left, new PixelSize(108, 300));
        Assert.True(left.Contains(preview));
        Assert.Equal(area.Right + BarPlacement.Gap, preview.X);
    }

    [Fact]
    public void The_bar_sits_under_the_area_right_aligned_else_above()
    {
        var size = new PixelSize(300, 50);
        var area = new PixelRect(100, 100, 700, 500);
        var bar = ScrollingPlacement.Bar(area, Display, size);
        Assert.Equal(area.Bottom + BarPlacement.Gap, bar.Y);
        Assert.Equal(area.Right - 300, bar.X);

        var low = new PixelRect(100, 400, 700, 660); // no room below
        var above = ScrollingPlacement.Bar(low, Display, size);
        Assert.True(Display.Contains(above));
        Assert.True(above.Bottom <= low.Top - BarPlacement.Gap + 1 || above.Top >= low.Top, above.ToString());
    }

    [Fact]
    public void A_short_result_draws_the_whole_strip_with_the_frame_at_its_top()
    {
        // 700 wide, one area of 508 tall: the strip is 96 wide and 69.7 tall, the frame fills it (board 3).
        var layout = ScrollingPreviewLayout.Compute(700, 508, 508, [], 0, 0, false, 508);
        Assert.Equal(96, layout.Width);
        Assert.Equal(508 * 96.0 / 700, layout.Height, 6);
        Assert.Equal(0, layout.Offset);
        Assert.Equal(0, layout.ViewportTop, 6);
        Assert.Equal(layout.Height, layout.ViewportHeight, 6);
        Assert.Empty(layout.Seams);
        Assert.Null(layout.Gap);
    }

    [Fact]
    public void A_growing_result_puts_the_seams_and_the_frame_at_the_scale_of_the_strip()
    {
        // Board 4: 700 x 2,180, seams where the strips were joined, the area near the end.
        double scale = 96.0 / 700;
        var layout = ScrollingPreviewLayout.Compute(700, 2180, 508, [508, 1000, 1500], 1672, 0, false, 508);
        Assert.Equal(2180 * scale, layout.Height, 6);
        Assert.Equal(0, layout.Offset);
        Assert.Equal([508 * scale, 1000 * scale, 1500 * scale], layout.Seams);
        Assert.Equal(1672 * scale, layout.ViewportTop, 6);
        Assert.Equal(508 * scale, layout.ViewportHeight, 6);
    }

    [Fact]
    public void A_result_taller_than_the_area_scrolls_with_the_newest_part_in_view()
    {
        // 700 x 4,920 would be 675 tall: shown 508 tall, scrolled to its end (board 7).
        double scale = 96.0 / 700;
        var layout = ScrollingPreviewLayout.Compute(700, 4920, 508, [508, 1000, 4000], 4412, 0, false, 508);
        double strip = 4920 * scale;
        Assert.Equal(508, layout.Height, 6);
        Assert.Equal(strip, layout.StripHeight, 6);
        Assert.Equal(strip - 508, layout.Offset, 6);
        Assert.Equal(4412 * scale - (strip - 508), layout.ViewportTop, 6);
        Assert.Equal([4000 * scale - (strip - 508)], layout.Seams); // the seams above the visible part are gone
        Assert.True(layout.ViewportTop + layout.ViewportHeight <= layout.Height + 1e-6);
    }

    [Fact]
    public void Scrolled_back_up_past_the_shown_part_the_strip_keeps_the_frame_in_view()
    {
        double scale = 96.0 / 700;
        var layout = ScrollingPreviewLayout.Compute(700, 4920, 508, [], 100, 0, false, 508);
        Assert.Equal(100 * scale, layout.Offset, 6);
        Assert.Equal(0, layout.ViewportTop, 6);
    }

    [Fact]
    public void The_gap_is_drawn_only_while_Too_fast_and_only_when_it_is_in_view()
    {
        double scale = 96.0 / 700;
        var with = ScrollingPreviewLayout.Compute(700, 2180, 508, [], 1672, 2000, true, 508);
        Assert.Equal(2000 * scale, with.Gap!.Value, 6);
        var without = ScrollingPreviewLayout.Compute(700, 2180, 508, [], 1672, 2000, false, 508);
        Assert.Null(without.Gap);
        var hidden = ScrollingPreviewLayout.Compute(700, 8000, 508, [], 7492, 100, true, 508);
        Assert.Null(hidden.Gap); // far above the visible part
    }

    [Fact]
    public void An_empty_result_still_gives_a_one_pixel_strip()
    {
        var layout = ScrollingPreviewLayout.Compute(0, 0, 0, [], 0, 0, false, 0);
        Assert.True(layout.Height >= 1);
        Assert.True(layout.Width > 0);
    }
}
