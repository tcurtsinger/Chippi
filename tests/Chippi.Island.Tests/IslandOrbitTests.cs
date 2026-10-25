// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Island;
using Chippi.Core.Config;

namespace Chippi.Island.Tests;

/// <summary>DESIGN.md §5 Island, orbit buttons: where they sit around the shape and how they keep it on screen.</summary>
public sealed class IslandOrbitTests
{
    private static readonly OrbitSlot[] All = [OrbitSlot.Back, OrbitSlot.Left, OrbitSlot.Settings, OrbitSlot.Right, OrbitSlot.Live];
    private static readonly Rect Shape = new(100, 0, 328, 200);

    private static Dictionary<OrbitSlot, Rect> Layout(IslandEdge edge, params OrbitSlot[] slots) =>
        IslandGeometry.OrbitLayout(edge, Shape, slots).ToDictionary(o => o.Slot, o => o.Rect);

    [Fact]
    public void On_top_back_and_keep_awake_sit_left_settings_and_mixer_right_12px_out_48px_apart()
    {
        var at = Layout(IslandEdge.Top, All);
        Assert.Equal(new Rect(48, 12, 40, 40), at[OrbitSlot.Back]);
        Assert.Equal(new Rect(48, 60, 40, 40), at[OrbitSlot.Left]);
        Assert.Equal(new Rect(440, 12, 40, 40), at[OrbitSlot.Settings]);
        Assert.Equal(new Rect(440, 60, 40, 40), at[OrbitSlot.Right]);
        Assert.Equal(new Rect(244, 212, 40, 40), at[OrbitSlot.Live]);
    }

    [Fact]
    public void Back_keeps_its_place_when_hidden_so_nothing_moves_when_it_appears()
    {
        var at = Layout(IslandEdge.Top, OrbitSlot.Left, OrbitSlot.Settings);
        Assert.Equal(new Rect(48, 60, 40, 40), at[OrbitSlot.Left]);
        Assert.False(at.ContainsKey(OrbitSlot.Back));
        Assert.False(at.ContainsKey(OrbitSlot.Live));
        var side = Layout(IslandEdge.Left, OrbitSlot.Settings, OrbitSlot.Right);
        Assert.Equal(new[] { 60.0, 108 }, new[] { side[OrbitSlot.Settings].Y, side[OrbitSlot.Right].Y }); // a missing Keep awake closes up
    }

    [Theory]
    [InlineData(IslandEdge.Left, 440)] // the open side is the right
    [InlineData(IslandEdge.Right, 48)]
    public void On_a_side_edge_all_four_stack_on_the_open_side_in_order(IslandEdge edge, double x)
    {
        var at = Layout(edge, All);
        Assert.Equal(new[] { 12.0, 60, 108, 156 }, new[] { OrbitSlot.Back, OrbitSlot.Left, OrbitSlot.Settings, OrbitSlot.Right }.Select(s => at[s].Y));
        Assert.All(new[] { OrbitSlot.Back, OrbitSlot.Left, OrbitSlot.Settings, OrbitSlot.Right }, s => Assert.Equal(x, at[s].X));
        Assert.Equal(new Rect(244, 212, 40, 40), at[OrbitSlot.Live]);
    }

    [Fact]
    public void Reach_is_how_far_the_buttons_stick_out()
    {
        var reach = IslandGeometry.OrbitReach(IslandEdge.Top, new Size(328, 200), All);
        Assert.Equal(new Thickness(52, 0, 52, 52), reach);
        // A short page on a side edge: the column of four runs past its bottom.
        var side = IslandGeometry.OrbitReach(IslandEdge.Left, new Size(328, 120), [OrbitSlot.Back, OrbitSlot.Left, OrbitSlot.Settings, OrbitSlot.Right]);
        Assert.Equal(new Thickness(0, 0, 52, 196 - 120), side);
    }

    [Fact]
    public void Near_a_screen_end_the_shape_moves_in_until_its_buttons_fit()
    {
        var work = new Rect(0, 0, 1920, 1032);
        var tab = new Rect(160 - 68, 0, 136, 36); // tab centered at the 160px end margin
        var reach = new Thickness(52, 0, 52, 52);
        var origin = IslandGeometry.PanelOrigin(IslandEdge.Top, tab, new Size(440, 200), work, 1, reach);
        Assert.Equal(new Point(52 + 8, 0), origin); // without the buttons it could sit at 8
    }

    [Fact]
    public void On_a_side_edge_the_buttons_below_keep_the_shape_up_from_the_bottom()
    {
        var work = new Rect(0, 0, 1920, 1032);
        var tab = new Rect(0, 1032 - 76 - 68, 36, 136); // tab at the bottom end margin
        var origin = IslandGeometry.PanelOrigin(IslandEdge.Left, tab, new Size(328, 200), work, 1, new Thickness(0, 0, 52, 52));
        Assert.Equal(0, origin.X); // still flat on the edge
        Assert.Equal(1032 - 8 - 52 - 200, origin.Y);
    }
}
