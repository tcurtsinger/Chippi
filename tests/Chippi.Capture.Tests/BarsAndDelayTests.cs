// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture.Tests;

/// <summary>The bars' placement and the delay (docs/phase-7/capture-layer.md, The bars' placement and The freeze step 4).</summary>
public sealed class BarsAndDelayTests
{
    private static readonly PixelRect Display = new(0, 0, 1920, 1080);
    private static readonly PixelSize Tools = new(40, 300);
    private static readonly PixelSize Output = new(200, 40);

    [Fact]
    public void Tools_go_outside_the_right_edge_and_the_output_bar_under_right_aligned()
    {
        var sel = new PixelRect(400, 300, 600, 400);
        var (tools, output) = BarPlacement.Place(sel, Display, Tools, Output);
        Assert.Equal(new PixelRect(1008, 300, 40, 300), tools);
        Assert.Equal(new PixelRect(800, 708, 200, 40), output);
    }

    [Fact]
    public void At_the_right_edge_the_tools_flip_left_and_at_the_bottom_the_output_bar_goes_above()
    {
        var sel = PixelRect.FromEdges(1500, 700, 1900, 1060);
        var (tools, output) = BarPlacement.Place(sel, Display, Tools, Output);
        Assert.Equal(1500 - 8 - 40, tools.X);
        Assert.Equal(700 - 8 - 40, output.Y);
        Assert.Equal(1900 - 200, output.X);
    }

    [Fact]
    public void With_no_room_on_either_side_the_bars_go_inside()
    {
        var sel = PixelRect.FromEdges(30, 30, 1890, 1050); // no room left or right, above or under
        var (tools, output) = BarPlacement.Place(sel, Display, Tools, Output);
        Assert.Equal(1890 - 8 - 40, tools.X);
        Assert.Equal(1050 - 8 - 40, output.Y);
        Assert.True(tools.Intersect(output).IsEmpty);
    }

    [Fact]
    public void A_selection_filling_its_display_has_both_bars_inside_and_apart()
    {
        var (tools, output) = BarPlacement.Place(Display, Display, Tools, Output);
        Assert.True(Display.Contains(tools) && Display.Contains(output));
        Assert.Equal(1920 - 8 - 40, tools.X);
        Assert.Equal(1080 - 8 - 40, output.Y);
        Assert.True(tools.Intersect(output).IsEmpty);
        Assert.Equal(1920 - 8 - 200, output.X);

        // A tool column tall enough to reach the output bar pushes it left.
        var (tall, beside) = BarPlacement.Place(Display, Display, new PixelSize(40, 1060), Output);
        Assert.True(tall.Intersect(beside).IsEmpty);
        Assert.Equal(tall.Left - 8, beside.Right);
    }

    [Fact]
    public void Bars_stay_on_the_release_display_never_across_two()
    {
        var left = new PixelRect(-1920, 360, 1920, 1080);
        var sel = PixelRect.FromEdges(-300, 500, 300, 900); // spans both; released on the left one
        var (tools, output) = BarPlacement.Place(sel, left, Tools, Output);
        Assert.True(left.Contains(tools));
        Assert.True(left.Contains(output));
    }

    [Fact]
    public void A_bar_with_no_size_is_left_out()
    {
        var (tools, output) = BarPlacement.Place(new PixelRect(400, 300, 600, 400), Display, default, Output);
        Assert.True(tools.IsEmpty);
        Assert.False(output.IsEmpty);
    }

    [Fact]
    public void The_countdown_ticks_each_second_then_freezes_once()
    {
        var time = new ManualTime();
        var ticks = new List<int>();
        int done = 0;
        using var countdown = new Countdown(3, time, ticks.Add, () => done++);
        Assert.Equal([3], ticks);
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal([3], ticks);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal([3, 2, 1], ticks);
        Assert.Equal(1, done);
        Assert.False(countdown.IsRunning);
        Assert.False(countdown.Cancel());
    }

    [Fact]
    public void A_cancelled_countdown_never_freezes()
    {
        var time = new ManualTime();
        int done = 0;
        var countdown = new Countdown(5, time, _ => { }, () => done++);
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(3, countdown.SecondsLeft);
        Assert.True(countdown.Cancel());
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, done);
    }
}
