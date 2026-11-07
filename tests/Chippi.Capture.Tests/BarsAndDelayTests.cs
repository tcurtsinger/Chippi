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
    public void Tools_go_outside_the_right_edge_and_the_output_bar_under_meeting_at_the_corner()
    {
        var sel = new PixelRect(400, 300, 600, 400);
        var (tools, output) = BarPlacement.Place(sel, Display, Tools, Output);
        Assert.Equal(new PixelRect(1008, 400, 40, 300), tools); // bottom on the selection's bottom
        Assert.Equal(new PixelRect(800, 708, 200, 40), output); // right end on the selection's right
        MeetAtCorner(tools, output);
    }

    [Fact]
    public void A_selection_shorter_than_the_column_has_it_grow_up_past_its_top()
    {
        var (tools, output) = BarPlacement.Place(new PixelRect(400, 450, 700, 120), Display, Tools, Output);
        Assert.Equal(new PixelRect(1108, 270, 40, 300), tools);
        Assert.Equal(new PixelRect(900, 578, 200, 40), output);
        MeetAtCorner(tools, output);

        // Near the display's top the column stops a gap below it, still beside the selection and clear of the bar.
        var (high, under) = BarPlacement.Place(new PixelRect(400, 20, 700, 120), Display, Tools, Output);
        Assert.Equal(new PixelRect(1108, 8, 40, 300), high);
        Assert.Equal(new PixelRect(900, 148, 200, 40), under);
        Assert.True(high.Intersect(under).IsEmpty);
    }

    [Fact]
    public void At_the_right_edge_the_tools_flip_left_and_at_the_bottom_the_output_bar_goes_above()
    {
        var sel = PixelRect.FromEdges(1500, 700, 1900, 1060);
        var (tools, output) = BarPlacement.Place(sel, Display, Tools, Output);
        Assert.Equal(new PixelRect(1500 - 8 - 40, 700, 40, 300), tools); // grows down from the top-left corner
        Assert.Equal(new PixelRect(1500, 700 - 8 - 40, 200, 40), output); // grows right from it
        Assert.Equal(output.Left - 8, tools.Right);
        Assert.Equal(tools.Top - 8, output.Bottom);
    }

    [Fact]
    public void With_no_room_on_either_side_the_bars_go_inside()
    {
        var sel = PixelRect.FromEdges(30, 30, 1890, 1050); // no room left or right, above or under
        var (tools, output) = BarPlacement.Place(sel, Display, Tools, Output);
        Assert.Equal(1890 - 8 - 40, tools.X);
        Assert.Equal(1050 - 8 - 40, output.Y);
        MeetAtCorner(tools, output);
    }

    [Fact]
    public void A_selection_filling_its_display_has_both_bars_inside_meeting_at_the_corner()
    {
        var (tools, output) = BarPlacement.Place(Display, Display, Tools, Output);
        Assert.True(Display.Contains(tools) && Display.Contains(output));
        Assert.Equal(new PixelRect(1920 - 8 - 40, 1080 - 8 - 40 - 8 - 300, 40, 300), tools);
        Assert.Equal(new PixelRect(1920 - 8 - 40 - 8 - 200, 1080 - 8 - 40, 200, 40), output);
        MeetAtCorner(tools, output);

        // A column taller than the room above the bar stops at the display's top, still clear of the bar.
        var (tall, beside) = BarPlacement.Place(Display, Display, new PixelSize(40, 1060), Output);
        Assert.True(tall.Intersect(beside).IsEmpty);
        Assert.Equal(tall.Left - 8, beside.Right);
    }

    [Fact]
    public void A_small_selection_in_a_display_corner_keeps_the_bars_apart()
    {
        var (tools, output) = BarPlacement.Place(new PixelRect(10, 10, 100, 50), Display, Tools, Output);
        Assert.True(Display.Contains(tools) && Display.Contains(output));
        Assert.True(tools.Intersect(output).IsEmpty);
    }

    /// <summary>The column's bottom-left and the bar's top-right meet across one gap each way, as an L.</summary>
    private static void MeetAtCorner(PixelRect tools, PixelRect output)
    {
        Assert.Equal(output.Right + 8, tools.Left);
        Assert.Equal(tools.Bottom + 8, output.Top);
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
