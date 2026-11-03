// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Island;

namespace Chippi.Island.Tests;

public sealed class PuckSurfaceTests
{
    private static PuckSurface Arrange(double width, double extension, bool left = false, double height = 50, double corner = 25)
    {
        var surface = new PuckSurface
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(corner),
        };
        surface.SetTab(extension, left);
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
        return surface;
    }

    [Theory]
    [InlineData(25)]
    [InlineData(46)]
    [InlineData(67)]
    public void The_tab_and_its_transparent_shoulders_mirror_about_the_puck(double extension) => WpfTestThread.Run(() =>
    {
        double width = 50 + extension;
        var rightward = Arrange(width, extension);
        var leftward = Arrange(width, extension, left: true);
        for (double x = 0.5; x < width; x += 2)
            for (double y = 0.5; y < 50; y += 3)
                Assert.Equal(rightward.ContainsLocal(new Point(x, y)), leftward.ContainsLocal(new Point(width - x, y)));

        Assert.True(rightward.ContainsLocal(new Point(width - 1, 25)));
        Assert.True(rightward.ContainsLocal(new Point(width - 13, 13)));
        Assert.False(rightward.ContainsLocal(new Point(width - 13, 10)));
        Assert.False(rightward.ContainsLocal(new Point(width - 13, 40)));
        Assert.False(rightward.ContainsLocal(new Point(width + 1, 25)));
        // The fillet rounds the shoulder outside both the circle and the 26-DIP-high rail.
        Assert.True(rightward.ContainsLocal(new Point(46.5, 11.65)));
        Assert.False(rightward.ContainsLocal(new Point(46.5, 10.6)));
    });

    [Fact]
    public void The_puck_end_and_tab_share_a_single_fill_and_outer_rim() => WpfTestThread.Run(() =>
    {
        const int width = 96, height = 50;
        var surface = Arrange(width, width - height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var pixels = new int[width * height];
        bitmap.CopyPixels(pixels, width * 4, 0);
        // A bordered circle or tab drawn over its sibling leaves a black seam inside this row.
        for (int x = 2; x < width - 2; x++)
            Assert.Equal(-1, pixels[25 * width + x]);
        Assert.Equal(0, pixels[5 * width + 64]); // The empty shoulder is transparent.
        Assert.NotEqual(-1, pixels[25 * width + width - 1]); // The far cap still has its outer edge.
    });

    [Fact]
    public void A_partial_retraction_has_the_same_outline_as_a_settled_tab_of_that_width() => WpfTestThread.Run(() =>
    {
        const double width = 75;
        var shrinking = Arrange(width, 46);
        var settled = Arrange(width, 25);
        for (double x = 0.5; x < width; x += 2)
            for (double y = 0.5; y < 50; y += 2)
                Assert.Equal(settled.ContainsLocal(new Point(x, y)), shrinking.ContainsLocal(new Point(x, y)));

        var circle = Arrange(50, 25);
        Assert.False(circle.ContainsLocal(new Point(45, 3)));
        Assert.True(circle.ContainsLocal(new Point(25, 1)));
    });

    [Theory]
    [InlineData(256, 50, 25)]
    [InlineData(164, 118, 8)]
    public void The_shoulders_relax_to_the_actual_bar_or_panel_corners(double width, double height, double radius) => WpfTestThread.Run(() =>
    {
        var shape = Arrange(width, 46, height: height, corner: radius);
        var rounded = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        for (double x = 0.5; x < width; x += 4)
            for (double y = 0.5; y < height; y += 4)
                Assert.Equal(rounded.FillContains(new Point(x, y)), shape.ContainsLocal(new Point(x, y)));
    });
}
