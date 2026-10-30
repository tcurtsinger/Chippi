// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Island;

namespace Chippi.Island.Tests;

/// <summary>DESIGN.md §5: where the widget starts, how its place is saved, where a drop lands, and where the panel opens.</summary>
public sealed class IslandGeometryTests
{
    // 1920×1080, with a 48px taskbar on the edge each test names.
    private static readonly Rect Monitor = new(0, 0, 1920, 1080);

    public enum Taskbar
    {
        Bottom,
        Top,
        Left,
        Right,
        Hidden,
    }

    private static Rect Work(Taskbar taskbar, Rect? monitor = null, double scale = 1)
    {
        var m = monitor ?? Monitor;
        double bar = 48 * scale;
        return taskbar switch
        {
            Taskbar.Top => new Rect(m.Left, m.Top + bar, m.Width, m.Height - bar),
            Taskbar.Left => new Rect(m.Left + bar, m.Top, m.Width - bar, m.Height),
            Taskbar.Right => new Rect(m.Left, m.Top, m.Width - bar, m.Height),
            Taskbar.Hidden => m,
            _ => new Rect(m.Left, m.Top, m.Width, m.Height - bar),
        };
    }

    private static readonly Size Panel = new(520, 360);

    [Theory]
    [InlineData(Taskbar.Bottom)]
    [InlineData(Taskbar.Top)]
    [InlineData(Taskbar.Left)]
    [InlineData(Taskbar.Right)]
    [InlineData(Taskbar.Hidden)]
    public void It_starts_24_in_from_the_work_areas_bottom_right_corner_whatever_edge_the_taskbar_is_on(Taskbar taskbar)
    {
        var work = Work(taskbar);
        foreach (var start in new[] { IslandGeometry.Start(work, 1), IslandGeometry.PuckRect(null, null, work, 1) })
        {
            Assert.Equal(new Rect(work.Right - 24 - 50, work.Bottom - 24 - 50, 50, 50), start);
            Assert.True(work.Contains(start));
        }
    }

    [Fact]
    public void Where_it_starts_the_puck_becomes_the_panel_sharing_its_lower_right_corner()
    {
        var work = Work(Taskbar.Bottom);
        var puck = IslandGeometry.Start(work, 1);
        var origin = IslandGeometry.PanelOrigin(puck, Panel, work, 1);
        var panel = new Rect(origin, Panel);
        Assert.Equal(puck.Right, panel.Right); // the puck's outer corner is the panel's (Travis, 2026-10-30)
        Assert.Equal(puck.Bottom, panel.Bottom);
        Assert.Equal(new Rect(1920 - 24 - 520, 1032 - 24 - 360, 520, 360), panel);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void The_bar_grows_from_the_puck_toward_the_middle_at_one_width(double scale)
    {
        var work = Work(Taskbar.Bottom);
        var right = IslandGeometry.Start(work, scale);
        var bar = IslandGeometry.BarRect(right, IslandGeometry.OpensLeft(right, work), scale);
        Assert.Equal(new Rect(right.Right - 256 * scale, right.Top, 256 * scale, right.Height), bar); // its outer end where the puck's is
        var left = IslandGeometry.PuckRect(0, 0.5, work, scale);
        Assert.Equal(new Rect(left.Left, left.Top, 256 * scale, left.Height), IslandGeometry.BarRect(left, IslandGeometry.OpensLeft(left, work), scale));
        // The bar shares the puck's outer corner, so the panel grown out of either sits in the same place.
        var panel = new Rect(IslandGeometry.PanelOrigin(right, new Size(520 * scale, 360 * scale), work, scale), new Size(520 * scale, 360 * scale));
        Assert.True(panel.Contains(bar));
    }

    /// <summary>The puck flush in each corner, with a taskbar on each edge: the panel opens toward the middle and stays 8 inside.</summary>
    public static TheoryData<Corner, Taskbar> EveryCornerAndTaskbar()
    {
        var data = new TheoryData<Corner, Taskbar>();
        foreach (var corner in Enum.GetValues<Corner>())
            foreach (var taskbar in Enum.GetValues<Taskbar>())
                data.Add(corner, taskbar);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCornerAndTaskbar))]
    public void In_every_corner_the_puck_becomes_the_panel_toward_the_middle_inside_the_work_area(Corner corner, Taskbar taskbar)
    {
        var work = Work(taskbar);
        var puck = IslandBounds.Puck(corner, work);
        Assert.True(work.Contains(puck));
        bool right = corner is Corner.TopRight or Corner.BottomRight, bottom = corner is Corner.BottomLeft or Corner.BottomRight;
        Assert.Equal(right ? work.Right : work.Left, right ? puck.Right : puck.Left);
        Assert.Equal(bottom ? work.Bottom : work.Top, bottom ? puck.Bottom : puck.Top);

        var panel = new Rect(IslandGeometry.PanelOrigin(puck, Panel, work, 1), Panel);
        Assert.Equal(right, IslandGeometry.OpensLeft(puck, work));
        Assert.Equal(bottom, IslandGeometry.OpensUp(puck, work));
        // On the puck's outer corner, moved in only the 8 the work area keeps: the panel covers where the puck was.
        Assert.Equal(right ? puck.Right - 8 : puck.Left + 8, right ? panel.Right : panel.Left);
        Assert.Equal(bottom ? puck.Bottom - 8 : puck.Top + 8, bottom ? panel.Bottom : panel.Top);
        Assert.True(panel.Contains(new Point(puck.Left + puck.Width / 2, puck.Top + puck.Height / 2)));
        var inside = work;
        inside.Inflate(-8, -8);
        Assert.True(inside.Contains(panel), $"{panel} not inside {inside}");
    }

    [Fact]
    public void On_a_screen_too_small_for_the_panel_the_panel_stays_inside()
    {
        var work = new Rect(0, 0, 960, 492);
        var puck = IslandGeometry.PuckRect(0.5, 0.5, work, 1);
        var panel = new Rect(IslandGeometry.PanelOrigin(puck, Panel, work, 1), Panel);
        var inside = work;
        inside.Inflate(-8, -8);
        Assert.True(inside.Contains(panel));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.25, 0.8)]
    public void A_saved_place_survives_a_round_trip_on_a_scaled_monitor_left_of_the_main_one(double x, double y)
    {
        var monitor = new Rect(-2880, -200, 2880, 1620);
        var work = Work(Taskbar.Bottom, monitor, 1.5);
        var puck = IslandGeometry.PuckRect(x, y, work, 1.5);
        Assert.Equal(75, puck.Width);
        Assert.True(work.Contains(puck));
        var (fx, fy) = IslandGeometry.Fractions(puck, work);
        Assert.Equal(x, fx, 9);
        Assert.Equal(y, fy, 9);
    }

    [Fact]
    public void A_resolution_change_keeps_the_puck_on_screen_at_the_same_fractions()
    {
        var big = Work(Taskbar.Bottom);
        var (x, y) = IslandGeometry.Fractions(new Rect(1700, 900, 50, 50), big);
        var small = new Rect(0, 0, 1280, 672);
        var puck = IslandGeometry.PuckRect(x, y, small, 1);
        Assert.True(small.Contains(puck));
        var (fx, fy) = IslandGeometry.Fractions(puck, small);
        Assert.Equal(x, fx, 9);
        Assert.Equal(y, fy, 9);
    }

    [Theory]
    [InlineData(double.NaN, 0.5)]
    [InlineData(0.5, double.PositiveInfinity)]
    public void A_bad_saved_place_starts_at_the_bottom_right(double x, double y)
    {
        var work = Work(Taskbar.Bottom);
        Assert.Equal(IslandGeometry.Start(work, 1), IslandGeometry.PuckRect(x, y, work, 1));
    }

    [Fact]
    public void A_saved_place_out_of_range_is_kept_on_screen()
    {
        var work = Work(Taskbar.Bottom);
        Assert.Equal(new Rect(1870, 0, 50, 50), IslandGeometry.PuckRect(7, -3, work, 1));
    }

    [Theory]
    [InlineData(-30, 500, 0, 500)] // past the left edge
    [InlineData(1900, 500, 1870, 500)] // past the right edge
    [InlineData(800, -20, 800, 0)] // past the top
    [InlineData(800, 1060, 800, 982)] // on the taskbar
    [InlineData(-30, 1060, 0, 982)] // past a corner
    [InlineData(800, 500, 800, 500)] // inside: stays put
    public void A_drop_slides_the_least_distance_that_keeps_all_of_the_puck_inside(double x, double y, double expectedX, double expectedY)
    {
        var placed = IslandGeometry.Inside(new Rect(x, y, 50, 50), Work(Taskbar.Bottom));
        Assert.Equal(new Rect(expectedX, expectedY, 50, 50), placed);
    }

    [Fact]
    public void Sizes_scale_with_the_monitor()
    {
        var work = Work(Taskbar.Bottom);
        var start = IslandGeometry.Start(work, 2);
        Assert.Equal(new Rect(1920 - 48 - 100, 1032 - 48 - 100, 100, 100), start);
        var panel = new Rect(IslandGeometry.PanelOrigin(start, new Size(1040, 720), work, 2), new Size(1040, 720));
        Assert.Equal(start.Bottom, panel.Bottom);
        Assert.Equal(start.Right, panel.Right);
        Assert.Equal(new Rect(start.Right - 512, start.Top, 512, 100), IslandGeometry.BarRect(start, opensLeft: true, 2));
    }
}
