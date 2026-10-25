// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Island;
using Chippi.Core.Config;

namespace Chippi.Island.Tests;

/// <summary>DESIGN.md §5 Island: where the tab docks, what a drop is refused, and where the island opens.</summary>
public sealed class IslandGeometryTests
{
    // 1920×1080 with a 48px taskbar at the bottom.
    private static readonly Rect Monitor = new(0, 0, 1920, 1080);
    private static readonly Rect Work = new(0, 0, 1920, 1032);

    [Fact]
    public void A_drop_near_the_top_docks_on_top_where_it_was_dropped()
    {
        var snap = IslandGeometry.Snap(new Point(960, 10), Monitor, Work, 1);
        Assert.Equal((IslandEdge.Top, 0.5), snap);
    }

    [Theory]
    [InlineData(5, 500, IslandEdge.Left)]
    [InlineData(1910, 500, IslandEdge.Right)]
    public void A_drop_near_a_side_docks_on_that_side(double x, double y, IslandEdge edge)
    {
        var snap = IslandGeometry.Snap(new Point(x, y), Monitor, Work, 1);
        Assert.Equal(edge, snap?.Edge);
        Assert.Equal((500 - 64) / (1032 - 128.0), snap!.Value.Place, 6);
    }

    [Theory]
    [InlineData(960, 1030)] // the bottom, where the taskbar is
    [InlineData(960, 1079)] // on the taskbar itself
    [InlineData(4, 4)] // top-left corner
    [InlineData(1916, 4)] // top-right corner
    [InlineData(159, 2)] // top edge, inside the 160px end margin
    [InlineData(1761, 2)]
    [InlineData(2, 63)] // left edge, inside the 64px end margin
    [InlineData(2, 970)]
    [InlineData(1918, 40)] // right edge, inside the margin
    public void Drops_on_the_taskbar_edge_the_bottom_or_a_corner_are_refused(double x, double y) =>
        Assert.Null(IslandGeometry.Snap(new Point(x, y), Monitor, Work, 1));

    [Fact]
    public void The_end_margins_are_inclusive()
    {
        Assert.Equal((IslandEdge.Top, 0.0), IslandGeometry.Snap(new Point(160, 2), Monitor, Work, 1));
        Assert.Equal((IslandEdge.Top, 1.0), IslandGeometry.Snap(new Point(1760, 2), Monitor, Work, 1));
        Assert.Equal((IslandEdge.Left, 0.0), IslandGeometry.Snap(new Point(2, 64), Monitor, Work, 1));
    }

    [Fact]
    public void Margins_scale_with_the_monitor()
    {
        // At 150%, the top margin is 240 physical pixels.
        Assert.Null(IslandGeometry.Snap(new Point(239, 2), Monitor, Work, 1.5));
        Assert.Equal(0.0, IslandGeometry.Snap(new Point(240, 2), Monitor, Work, 1.5)!.Value.Place);
    }

    [Fact]
    public void A_taskbar_on_the_left_refuses_the_left_edge()
    {
        var work = new Rect(48, 0, 1872, 1080);
        Assert.Equal(ScreenEdge.Left, IslandGeometry.TaskbarEdge(Monitor, work));
        Assert.Null(IslandGeometry.Snap(new Point(50, 540), Monitor, work, 1));
        Assert.Null(IslandGeometry.Snap(new Point(960, 1078), Monitor, work, 1)); // the bottom is never a dock
        Assert.Equal(IslandEdge.Right, IslandGeometry.Snap(new Point(1918, 540), Monitor, work, 1)?.Edge);
    }

    [Fact]
    public void A_taskbar_on_top_refuses_the_top_edge()
    {
        var work = new Rect(0, 48, 1920, 1032);
        Assert.Equal(ScreenEdge.Top, IslandGeometry.TaskbarEdge(Monitor, work));
        Assert.Null(IslandGeometry.Snap(new Point(960, 50), Monitor, work, 1));
    }

    [Fact]
    public void A_saved_edge_the_taskbar_has_since_taken_docks_on_top()
    {
        var work = new Rect(0, 0, 1872, 1080); // taskbar moved to the right
        Assert.Equal(IslandEdge.Top, IslandGeometry.Docked(IslandEdge.Right, Monitor, work));
        Assert.Equal(new Point(960 - 24, 0), IslandGeometry.Anchor(IslandEdge.Right, 0.5, Monitor, work, 1));
        Assert.Equal(IslandEdge.Left, IslandGeometry.Docked(IslandEdge.Left, Monitor, work));
    }

    [Fact]
    public void An_auto_hidden_taskbar_takes_no_edge()
    {
        Assert.Null(IslandGeometry.TaskbarEdge(Monitor, Monitor));
        Assert.Equal(IslandEdge.Left, IslandGeometry.Snap(new Point(2, 540), Monitor, Monitor, 1)?.Edge);
    }

    [Theory]
    [InlineData(IslandEdge.Top, 0.0)]
    [InlineData(IslandEdge.Top, 0.37)]
    [InlineData(IslandEdge.Left, 1.0)]
    [InlineData(IslandEdge.Right, 0.81)]
    public void A_saved_place_survives_a_round_trip_on_a_scaled_monitor_left_of_the_main_one(IslandEdge edge, double place)
    {
        var monitor = new Rect(-2560, 0, 2560, 1440);
        var work = new Rect(-2560, 0, 2560, 1380);
        var anchor = IslandGeometry.Anchor(edge, place, monitor, work, 1.5);
        var snap = IslandGeometry.Snap(anchor, monitor, work, 1.5);
        Assert.Equal(edge, snap?.Edge);
        Assert.Equal(place, snap!.Value.Place, 6);
    }

    [Fact]
    public void A_bad_saved_place_docks_in_the_middle()
    {
        Assert.Equal(new Point(960, 0), IslandGeometry.Anchor(IslandEdge.Top, double.NaN, Monitor, Work, 1));
        Assert.Equal(new Point(1760, 0), IslandGeometry.Anchor(IslandEdge.Top, 7, Monitor, Work, 1));
    }

    [Fact]
    public void The_tab_is_64_by_18_at_rest_and_84_by_26_live_turned_on_the_sides()
    {
        Assert.Equal(new Size(64, 18), IslandGeometry.TabSize(IslandEdge.Top, live: false));
        Assert.Equal(new Size(84, 26), IslandGeometry.TabSize(IslandEdge.Top, live: true));
        Assert.Equal(new Size(18, 64), IslandGeometry.TabSize(IslandEdge.Left, live: false));
        Assert.Equal(new Size(26, 84), IslandGeometry.TabSize(IslandEdge.Right, live: true));
    }

    [Fact]
    public void A_swollen_tab_is_about_a_fifth_longer_and_4px_deeper()
    {
        Assert.Equal(new Size(76, 22), IslandGeometry.TabSize(IslandEdge.Top, live: false, swollen: true));
        Assert.Equal(new Size(100, 30), IslandGeometry.TabSize(IslandEdge.Top, live: true, swollen: true));
        Assert.Equal(new Size(22, 76), IslandGeometry.TabSize(IslandEdge.Left, live: false, swollen: true));
        Assert.Equal(new Size(30, 100), IslandGeometry.TabSize(IslandEdge.Right, live: true, swollen: true));
    }

    [Fact]
    public void The_tab_sits_flat_against_its_edge_centered_on_its_place()
    {
        Assert.Equal(new Rect(918, 0, 84, 26), IslandGeometry.TabRect(IslandEdge.Top, new Point(960, 0), new Size(84, 26)));
        Assert.Equal(new Rect(0, 458, 26, 84), IslandGeometry.TabRect(IslandEdge.Left, new Point(0, 500), new Size(26, 84)));
        Assert.Equal(new Rect(1894, 458, 26, 84), IslandGeometry.TabRect(IslandEdge.Right, new Point(1920, 500), new Size(26, 84)));
    }

    [Fact]
    public void The_island_grows_out_of_the_tab_flat_on_the_same_edge()
    {
        var panel = new Size(328, 300);
        var top = new Rect(918, 0, 84, 26);
        Assert.Equal(new Point(796, 0), IslandGeometry.PanelOrigin(IslandEdge.Top, top, panel, Work, 1));
        var left = new Rect(0, 458, 26, 84);
        Assert.Equal(new Point(0, 350), IslandGeometry.PanelOrigin(IslandEdge.Left, left, panel, Work, 1));
        var right = new Rect(1894, 458, 26, 84);
        Assert.Equal(new Point(1920 - 328, 350), IslandGeometry.PanelOrigin(IslandEdge.Right, right, panel, Work, 1));
    }

    [Fact]
    public void The_island_stays_8px_inside_the_ends_of_its_edge()
    {
        var panel = new Size(328, 300);
        // A left tab near the top end: centering would put the island above the screen.
        var left = new Rect(0, 22, 26, 84);
        Assert.Equal(8, IslandGeometry.PanelOrigin(IslandEdge.Left, left, panel, Work, 1).Y);
        // A top tab at the end of the 160 margin at 200%: centering would cross the left edge.
        var top = new Rect(320 - 84, 0, 168, 52);
        Assert.Equal(16, IslandGeometry.PanelOrigin(IslandEdge.Top, top, new Size(656, 600), Work, 2).X);
        // A right tab near the bottom end.
        var right = new Rect(1894, 900, 26, 84);
        Assert.Equal(1032 - 8 - 300, IslandGeometry.PanelOrigin(IslandEdge.Right, right, panel, Work, 1).Y);
    }
}
