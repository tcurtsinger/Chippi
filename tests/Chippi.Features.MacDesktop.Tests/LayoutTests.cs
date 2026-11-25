// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.Core.Actions;
using Chippi.Features.MacDesktop.Dock;
using Chippi.Features.MacDesktop.MenuBar;
using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.Tests;

public sealed class AppBarStripTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void StripsHugTheirEdge()
    {
        Assert.Equal(new Rect(0, 0, 1920, 28), AppBarStrips.Strip(ScreenEdge.Top, Screen, 28));
        Assert.Equal(new Rect(0, 1010, 1920, 70), AppBarStrips.Strip(ScreenEdge.Bottom, Screen, 70));
        Assert.Equal(new Rect(1850, 0, 70, 1080), AppBarStrips.Strip(ScreenEdge.Right, Screen, 70));
    }

    [Fact]
    public void AShorterSideStripIsStillGranted()
    {
        var wanted = AppBarStrips.Strip(ScreenEdge.Left, Screen, 70);
        Assert.True(AppBarStrips.Granted(ScreenEdge.Left, new Rect(0, 28, 70, 1052), wanted));
        Assert.False(AppBarStrips.Granted(ScreenEdge.Bottom, new Rect(0, 962, 1920, 70), AppBarStrips.Strip(ScreenEdge.Bottom, Screen, 70)));
    }

    [Fact]
    public void APushedStripKeepsItsDepth() =>
        Assert.Equal(new Rect(0, 940, 1920, 70), AppBarStrips.KeepThickness(ScreenEdge.Bottom, new Rect(0, 962, 1920, 48), 70));
}

public sealed class MenuBarLayoutTests
{
    private static readonly double[] Menus = [40, 40, 40, 40, 40, 40];

    [Fact]
    public void EverythingFitsWithoutANotch()
    {
        var arrangement = MenuBarLayout.Arrange(1920, 200, Menus, 24, 300, null);
        Assert.Equal(Menus.Length, arrangement.MenusShown);
        Assert.Equal(MenuBarLayout.Pad, arrangement.LeftStart);
        Assert.Equal(1920 - MenuBarLayout.Pad, arrangement.RightEnd);
    }

    [Fact]
    public void MenusCollapseIntoOverflowBeforeTheCenteredNotch()
    {
        // The notch at 400–584: the left group may end at 390.
        var arrangement = MenuBarLayout.Arrange(984, 200, Menus, 24, 200, (400, 584));
        Assert.True(arrangement.Overflows(Menus.Length));
        Assert.True(arrangement.LeftStart + MenuBarLayout.LeftWidth(200, Menus, 24, arrangement.MenusShown) <= 390);
    }

    [Fact]
    public void ANotchOverTheRightGroupMovesItLeft()
    {
        var arrangement = MenuBarLayout.Arrange(1920, 200, Menus, 24, 300, (1700, 1884));
        Assert.Equal(1690, arrangement.RightEnd);
    }

    [Fact]
    public void ANotchAtTheFarLeftStartsTheLeftGroupAfterIt()
    {
        var arrangement = MenuBarLayout.Arrange(1920, 200, Menus, 24, 300, (0, 184));
        Assert.Equal(194, arrangement.LeftStart);
    }
}

public sealed class WallpaperInkTests
{
    [Fact]
    public void WhiteInkOnDarkWallpaper()
    {
        Assert.Equal(BarInk.Light, WallpaperInk.For(0.2));
        Assert.Equal(BarInk.Dark, WallpaperInk.For(0.5));
        Assert.Equal(BarInk.Dark, WallpaperInk.For(0.9));
    }

    [Fact]
    public void LuminanceWeighsGreenMost()
    {
        Assert.Equal(1, WallpaperInk.Luminance(255, 255, 255), 6);
        Assert.Equal(0, WallpaperInk.Luminance(0, 0, 0), 6);
        Assert.True(WallpaperInk.Luminance(0, 255, 0) > WallpaperInk.Luminance(255, 0, 0));
    }

    [Fact]
    public void AverageReadsBgra()
    {
        byte[] pixels = [0, 0, 0, 255, 255, 255, 255, 255];
        Assert.Equal(0.5, WallpaperInk.Average(pixels), 6);
        Assert.Equal(WallpaperInk.Threshold, WallpaperInk.Average([]));
    }

    [Fact]
    public void FillCropsToTheMiddleAndTakesTheBarsShare()
    {
        // A 4000 × 2000 picture filling a 1920 × 1080 display: 3555 × 2000 shown, centered; the bar is 28/1080 of it.
        var strip = WallpaperInk.UnderBar(4000, 2000, 1920, 1080, 28, WallpaperFit.Fill);
        Assert.Equal(222, strip.X);
        Assert.Equal(0, strip.Y);
        Assert.Equal(52, strip.Height);
        var top = WallpaperInk.UnderBar(1920, 1080, 1920, 1080, 28, WallpaperFit.Center);
        Assert.Equal(new Int32Rect(0, 0, 1920, 28), top);
    }
}

public sealed class DockLayoutTests
{
    [Fact]
    public void MagnificationFallsOffToRestThreeIconsAway()
    {
        Assert.Equal(72, DockLayout.Magnified(0, 72));
        Assert.Equal(60, DockLayout.Magnified(1.5, 72), 6);
        Assert.Equal(DockLayout.Icon, DockLayout.Magnified(3, 72));
        Assert.Equal(DockLayout.Icon, DockLayout.Magnified(double.NaN, 72));
    }

    [Fact]
    public void SizesAreAtRestWithoutAPointer()
    {
        Assert.All(DockLayout.Sizes(5, null, 72), size => Assert.Equal(DockLayout.Icon, size));
        var sizes = DockLayout.Sizes(5, DockLayout.Icon / 2, 72);
        Assert.Equal(72, sizes[0]);
        Assert.True(sizes[1] > sizes[2]);
    }

    private static ListedWindow Window(string exe, string app, nint handle = 1) => new(handle, 1, app, app, exe, false, 0);

    [Fact]
    public void PinsComeFirstThenRunningAppsInZOrder()
    {
        var apps = DockApps.Compose(
            [(@"C:\pins\Edge.lnk", @"C:\Edge\msedge.exe", "Edge"), (@"C:\pins\Notes.lnk", null, "Notes")],
            [Window(@"C:\Code\code.exe", "Code", 1), Window(@"C:\Edge\MSEDGE.exe", "Edge", 2), Window(@"C:\Code\code.exe", "Code", 3)]);
        Assert.Equal(["Edge", "Notes", "Code"], apps.Select(a => a.Name));
        Assert.True(apps[0].Running);
        Assert.False(apps[1].Running);
        Assert.False(apps[2].Pinned);
        Assert.Equal(2, apps[2].Windows.Count);
    }

    [Fact]
    public void MovingAPinPlacesItAtTheIndex()
    {
        Assert.Equal(["b", "a", "c"], DockApps.Move(["a", "b", "c"], "a", 1));
        Assert.Equal(["a", "b", "c", "d"], DockApps.Move(["a", "b", "c"], "d", 99));
    }
}
