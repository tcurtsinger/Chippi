// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Island;
using Chippi.Core.Modules;

namespace Chippi.Island.Tests;

/// <summary>The Mac-style desktop's notch: its place on a display's bar, the snap to center, and the panel grown out of it.</summary>
public sealed class NotchGeometryTests
{
    private static readonly Rect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void TheNotchHangsFromTheTopEdge()
    {
        var notch = NotchGeometry.Rest(Monitor, 1, NotchWindow.RestWidth, 0.5);
        Assert.Equal(new Rect(868, 0, 184, 28), notch);
        Assert.Equal(new Rect(0, 0, 368, 56), NotchGeometry.Rest(Monitor, 2, NotchWindow.RestWidth, 0));
        Assert.Equal(1920 - 280, NotchGeometry.Rest(Monitor, 1, NotchWindow.WideWidth, 1).Left);
    }

    [Fact]
    public void WithinFortyOfTheCenterItSnapsThere()
    {
        Assert.Equal(0.5, NotchGeometry.PlaceFor(Monitor, 1, 184, 960 + 39, 40));
        Assert.Equal(0.5, NotchGeometry.PlaceFor(Monitor, 1.5, 276, 960 - 59, 40));
        double off = NotchGeometry.PlaceFor(Monitor, 1, 184, 960 + 41, 40);
        Assert.NotEqual(0.5, off);
        Assert.Equal(960 + 41, NotchGeometry.Rest(Monitor, 1, 184, off).Left + 92, 6);
    }

    [Fact]
    public void DroppedPastTheEdgeItStaysOnTheBar()
    {
        Assert.Equal(0, NotchGeometry.PlaceFor(Monitor, 1, 184, -300, 40));
        Assert.Equal(1, NotchGeometry.PlaceFor(Monitor, 1, 184, 4000, 40));
        // A second display to the left.
        var left = new Rect(-1280, 0, 1280, 1024);
        Assert.Equal(new Rect(-1280 + 548, 0, 184, 28), NotchGeometry.Rest(left, 1, 184, NotchGeometry.PlaceFor(left, 1, 184, -640, 40)));
    }

    [Fact]
    public void ThePanelGrowsDownFromTheNotchCenteredOnIt()
    {
        var work = new Rect(0, 28, 1920, 982);
        var notch = NotchGeometry.Rest(Monitor, 1, NotchWindow.RestWidth, 0.5);
        var panel = IslandGeometry.NotchPanelRect(notch, work, 1);
        Assert.Equal(0, panel.Top);
        Assert.Equal(notch.Left + notch.Width / 2, panel.Left + panel.Width / 2, 6);
        Assert.Equal(new Size(IslandPage.Width, IslandPage.Height), panel.Size);
    }

    [Fact]
    public void AwayFromTheCenterThePanelStaysEightInside()
    {
        var work = new Rect(0, 28, 1920, 982);
        var leftNotch = NotchGeometry.Rest(Monitor, 1, NotchWindow.RestWidth, 0);
        Assert.Equal(IslandGeometry.Gap, IslandGeometry.NotchPanelRect(leftNotch, work, 1).Left);
        var rightNotch = NotchGeometry.Rest(Monitor, 1.25, NotchWindow.RestWidth, 1);
        var panel = IslandGeometry.NotchPanelRect(rightNotch, work, 1.25);
        Assert.Equal(work.Right - IslandGeometry.Gap * 1.25, panel.Right, 6);
    }

    [Fact]
    public void NothingGoingOnIsTheRestingNotch() =>
        Assert.True(NotchEars.For([], [], null).IsRest);
}
