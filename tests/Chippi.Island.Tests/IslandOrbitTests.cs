// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Island;
using Chippi.Core.Config;

namespace Chippi.Island.Tests;

/// <summary>DESIGN.md §5 Island, orbit buttons: two pairs beside the shape along its edge, each centered on it.</summary>
public sealed class IslandOrbitTests
{
    private static readonly OrbitSlot[] All = [OrbitSlot.Back, OrbitSlot.Left, OrbitSlot.Settings, OrbitSlot.Right, OrbitSlot.Live];
    private static readonly Rect Shape = new(100, 0, 328, 200);

    private static Dictionary<OrbitSlot, Rect> Layout(IslandEdge edge, Rect shape, params OrbitSlot[] slots) =>
        IslandGeometry.OrbitLayout(edge, shape, slots).ToDictionary(o => o.Slot, o => o.Rect);

    [Fact]
    public void On_top_each_pair_is_centered_on_the_shapes_height_10px_out()
    {
        var at = Layout(IslandEdge.Top, Shape, All);
        // The pair is 70 tall (32 + 6 + 32), so it starts 100 - 35 = 65 down a 200-high shape.
        Assert.Equal(new Rect(58, 65, 32, 32), at[OrbitSlot.Back]);
        Assert.Equal(new Rect(58, 103, 32, 32), at[OrbitSlot.Left]);
        Assert.Equal(new Rect(438, 65, 32, 32), at[OrbitSlot.Settings]);
        Assert.Equal(new Rect(438, 103, 32, 32), at[OrbitSlot.Right]);
        Assert.Equal(new Rect(248, 210, 32, 32), at[OrbitSlot.Live]);
    }

    [Fact]
    public void On_top_a_short_shape_keeps_its_pairs_8px_below_the_screen_edge()
    {
        var at = Layout(IslandEdge.Top, new Rect(100, 0, 328, 60), All);
        Assert.Equal(8, at[OrbitSlot.Back].Y);
        Assert.Equal(46, at[OrbitSlot.Left].Y);
    }

    [Theory]
    [InlineData(IslandEdge.Left, 438)] // the open side is the right
    [InlineData(IslandEdge.Right, 58)]
    public void On_a_side_one_pair_sits_above_and_one_below_centered_on_the_width(IslandEdge edge, double liveX)
    {
        var at = Layout(edge, Shape, All);
        // The pair is 70 wide, centered on 264: from 229.
        Assert.Equal(new Rect(229, -42, 32, 32), at[OrbitSlot.Back]);
        Assert.Equal(new Rect(267, -42, 32, 32), at[OrbitSlot.Left]);
        Assert.Equal(new Rect(229, 210, 32, 32), at[OrbitSlot.Settings]);
        Assert.Equal(new Rect(267, 210, 32, 32), at[OrbitSlot.Right]);
        Assert.Equal(new Rect(liveX, 84, 32, 32), at[OrbitSlot.Live]); // centered on the height, on the open side
    }

    [Theory]
    [InlineData(IslandEdge.Top)]
    [InlineData(IslandEdge.Left)]
    public void A_missing_button_leaves_its_place_empty_so_nothing_moves_when_it_appears(IslandEdge edge)
    {
        var all = Layout(edge, Shape, All);
        var some = Layout(edge, Shape, OrbitSlot.Left, OrbitSlot.Settings);
        Assert.Equal(all[OrbitSlot.Left], some[OrbitSlot.Left]);
        Assert.Equal(all[OrbitSlot.Settings], some[OrbitSlot.Settings]);
        Assert.False(some.ContainsKey(OrbitSlot.Back));
        Assert.False(some.ContainsKey(OrbitSlot.Live));
    }

    [Fact]
    public void Reach_is_how_far_the_buttons_stick_out()
    {
        Assert.Equal(new Thickness(42, 0, 42, 42), IslandGeometry.OrbitReach(IslandEdge.Top, new Size(328, 200), All));
        Assert.Equal(new Thickness(0, 42, 42, 42), IslandGeometry.OrbitReach(IslandEdge.Left, new Size(328, 200), All));
        Assert.Equal(new Thickness(42, 42, 0, 42), IslandGeometry.OrbitReach(IslandEdge.Right, new Size(328, 200), All));
    }

    [Fact]
    public void Near_a_screen_end_the_shape_moves_in_until_its_buttons_fit()
    {
        var work = new Rect(0, 0, 1920, 1032);
        var tab = new Rect(160 - 68, 0, 136, 36); // tab centered at the 160px end margin
        var reach = new Thickness(42, 0, 42, 42);
        var origin = IslandGeometry.PanelOrigin(IslandEdge.Top, tab, new Size(440, 200), work, 1, reach);
        Assert.Equal(new Point(42 + 8, 0), origin); // without the buttons it could sit at 8
    }

    [Theory]
    [InlineData(1032 - 76 - 68, 1032 - 8 - 42 - 200)] // tab at the bottom end margin: the pair below fits
    [InlineData(76 - 68, 8 + 42)] // tab at the top end margin: the pair above fits
    public void On_a_side_the_pairs_keep_the_shape_clear_of_the_screen_ends(double tabTop, double expectedTop)
    {
        var work = new Rect(0, 0, 1920, 1032);
        var tab = new Rect(0, tabTop, 36, 136);
        var reach = IslandGeometry.OrbitReach(IslandEdge.Left, new Size(328, 200), All);
        var origin = IslandGeometry.PanelOrigin(IslandEdge.Left, tab, new Size(328, 200), work, 1, reach);
        Assert.Equal(0, origin.X); // still flat on the edge
        Assert.Equal(expectedTop, origin.Y);
    }
}
