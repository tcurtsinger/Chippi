// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.WindowLayouts.Tests;

public sealed class SnapGeometryTests
{
    [Theory]
    [InlineData(-1920, -200, 1919, 1040)]
    [InlineData(0, 0, 900, 1560)]
    [InlineData(1920, 40, 2560, 1400)]
    public void ThirdsShareEdgesAndFillWorkArea(int x, int y, int width, int height)
    {
        var work = new SnapRect(x, y, width, height);
        var thirds = SnapGeometry.BuiltIns.Where(z => z.Id is "left-third" or "middle-third" or "right-third")
            .Select(z => SnapGeometry.Place(work, z)).ToArray();
        Assert.Equal(x, thirds[0].X);
        Assert.Equal(thirds[0].X + thirds[0].Width, thirds[1].X);
        Assert.Equal(thirds[1].X + thirds[1].Width, thirds[2].X);
        Assert.Equal(x + width, thirds[2].X + thirds[2].Width);
        Assert.All(thirds, r => { Assert.Equal(y, r.Y); Assert.Equal(height, r.Height); });
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(9, 4)]
    [InlineData(24, 24)]
    public void WindowGapIsBetweenZonesAndEdgeGapIsOnlyAtTheScreenEdge(int gap, int edge)
    {
        var work = new SnapRect(-1900, 40, 1901, 1040);
        var left = SnapGeometry.PlaceWithGaps(work, SnapGeometry.BuiltIns[0], gap, edge);
        var right = SnapGeometry.PlaceWithGaps(work, SnapGeometry.BuiltIns[1], gap, edge);
        Assert.Equal(gap, right.X - left.X - left.Width);
        Assert.Equal(work.X + edge, left.X);
        Assert.Equal(work.X + work.Width - edge, right.X + right.Width);
        Assert.Equal(work.Y + edge, left.Y);
        Assert.Equal(work.Height - edge * 2, left.Height);
    }

    [Fact]
    public void OversizedMinimumKeepsTitleBarAtWorkAreaOrigin()
    {
        Assert.Equal(new SnapRect(-900, 40, 1400, 1800),
            SnapGeometry.Reachable(new(-400, 500, 450, 700), new(-900, 40, 900, 1500), 1400, 1800));
    }

    [Fact]
    public void GapCannotEraseZone()
    {
        Assert.Equal(new SnapRect(4, 4, 2, 2),
            SnapGeometry.Place(new(0, 0, 10, 10), new("tiny", "Tiny", 0, 0, 1, 1), 100));
    }

    [Fact]
    public void NonFiniteOrOutsideZonesAreRejected()
    {
        Assert.False(new SnapZone("bad", "Bad", double.NaN, 0, 1, 1).IsValid);
        Assert.Throws<ArgumentException>(() => SnapGeometry.Place(new(0, 0, 900, 1500),
            new("bad", "Bad", .8, 0, .3, 1)));
    }
}
