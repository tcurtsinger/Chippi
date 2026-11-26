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
    public void EverythingFitsWithoutAnIsland()
    {
        var arrangement = MenuBarLayout.Arrange(1920, 200, Menus, 24, 300, null);
        Assert.Equal(Menus.Length, arrangement.MenusShown);
        Assert.Equal(MenuBarLayout.Pad, arrangement.LeftStart);
        Assert.Equal(1920 - MenuBarLayout.Pad, arrangement.RightEnd);
    }

    [Fact]
    public void MenusCollapseIntoOverflowBeforeTheCenteredIsland()
    {
        // The island at 400–584: the left group may end at 390.
        var arrangement = MenuBarLayout.Arrange(984, 200, Menus, 24, 200, (400, 584));
        Assert.True(arrangement.Overflows(Menus.Length));
        Assert.True(arrangement.LeftStart + MenuBarLayout.LeftWidth(200, Menus, 24, arrangement.MenusShown) <= 390);
    }

    [Fact]
    public void AnIslandOverTheRightGroupMovesItLeft()
    {
        var arrangement = MenuBarLayout.Arrange(1920, 200, Menus, 24, 300, (1700, 1884));
        Assert.Equal(1690, arrangement.RightEnd);
    }

    [Fact]
    public void AnIslandAtTheFarLeftStartsTheLeftGroupAfterIt()
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

    private static readonly Rect Display = new(0, 0, 1920, 1080);

    [Fact]
    public void FillCoversTheDisplayAndCropsTheRest()
    {
        // A 4000 × 2000 picture filling a 1920 × 1080 display: drawn 2160 × 1080, 120 off each side.
        var drawn = WallpaperInk.Drawn(4000, 2000, Display, Display, WallpaperFit.Fill);
        Assert.Equal(-120, drawn.X, 6);
        Assert.Equal(0, drawn.Y, 6);
        Assert.Equal(2160, drawn.Width, 6);
        Assert.Equal(1080, drawn.Height, 6);
    }

    [Fact]
    public void FitAndCenterLeaveTheBackgroundAroundThePicture()
    {
        // A wide picture fitted to the display leaves the background above it: the bar is over the colour, not the picture.
        var fitted = WallpaperInk.Drawn(3440, 1440, Display, Display, WallpaperFit.Fit);
        Assert.Equal(1920, fitted.Width, 3);
        Assert.True(fitted.Top > 28);
        Assert.Equal(new Rect(320, 180, 1280, 720), WallpaperInk.Drawn(1280, 720, Display, Display, WallpaperFit.Center));
        Assert.Equal(new Rect(0, 0, 1920, 1080), WallpaperInk.Drawn(640, 480, Display, Display, WallpaperFit.Stretch));
    }

    [Fact]
    public void SpanGivesEachDisplayItsOwnPartOfOnePicture()
    {
        // Two displays side by side, 3440 and 1920 wide, one 5360 × 1440 picture across both.
        var desktop = new Rect(0, 0, 5360, 1440);
        var second = new Rect(3440, 0, 1920, 1080);
        Assert.Equal(new Rect(0, 0, 5360, 1440), WallpaperInk.Drawn(5360, 1440, new Rect(0, 0, 3440, 1440), desktop, WallpaperFit.Span));
        Assert.Equal(new Rect(-3440, 0, 5360, 1440), WallpaperInk.Drawn(5360, 1440, second, desktop, WallpaperFit.Span));
    }

    [Fact]
    public void TheStripReadsThePictureWhereItIsAndTheBackgroundElsewhere()
    {
        // A picture dark on its left half and white on its right, centered on a wider display over a mid-grey background.
        static double Picture(double u, double v) => u < 0.5 ? 0 : 1;
        var strip = WallpaperInk.Strip(8, 1600, 28, new Rect(400, 0, 800, 600), tile: false, 0.5, Picture);
        Assert.Equal([0.5, 0.5, 0, 0, 1, 1, 0.5, 0.5], strip);
        // Tiled, it repeats from where it starts.
        var tiled = WallpaperInk.Strip(8, 1600, 28, new Rect(0, 0, 400, 300), tile: true, 0.5, Picture);
        Assert.Equal([0, 1, 0, 1, 0, 1, 0, 1], tiled);
        // Fitted below the bar, the bar is all background.
        Assert.All(WallpaperInk.Strip(8, 1600, 28, new Rect(0, 100, 1600, 600), tile: false, 0.5, Picture), column => Assert.Equal(0.5, column));
    }

    [Fact]
    public void EachGroupTakesItsInkFromWhatItSitsOver()
    {
        // Dark on the left 60%, light on the right 40%: white ink for the menus, dark ink for the clock.
        var strip = new InkStrip([0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.9, 0.9, 0.9, 0.9]);
        Assert.Equal(BarInk.Light, strip.Ink(0, 0.3));
        Assert.Equal(BarInk.Dark, strip.Ink(0.8, 1));
        Assert.Equal(0.42, strip.Average(0, 1), 6);
        Assert.Equal(BarInk.Light, InkStrip.Flat(0.2).Ink(0.4, 0.6));
    }

    [Fact]
    public void AGroupKeepsItsInkNearTheThreshold()
    {
        var strip = InkStrip.Flat(0.52);
        Assert.Equal(BarInk.Dark, strip.Ink(0, 1));
        Assert.Equal(BarInk.Light, strip.Ink(0, 1, BarInk.Light)); // within the hold: no flip for a title more or less
        Assert.Equal(BarInk.Dark, InkStrip.Flat(0.6).Ink(0, 1, BarInk.Light));
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
            [new DockPin(@"C:\pins\Edge.lnk", @"C:\Edge\msedge.exe", "Edge"), new DockPin(@"C:\pins\Notes.lnk", null, "Notes")],
            [Window(@"C:\Code\code.exe", "Code", 1), Window(@"C:\Edge\MSEDGE.exe", "Edge", 2), Window(@"C:\Code\code.exe", "Code", 3)]);
        Assert.Equal(["Edge", "Notes", "Code"], apps.Select(a => a.Name));
        Assert.True(apps[0].Running);
        Assert.False(apps[1].Running);
        Assert.False(apps[2].Pinned);
        Assert.Equal(2, apps[2].Windows.Count);
    }

    [Fact]
    public void StoreAppPinsClaimTheirWindowsByAppId()
    {
        // Terminal's taskbar shortcut has no file target, only its ID: its window goes under the pin, not after it.
        var terminal = Window(@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal\WindowsTerminal.exe", "Terminal", 1);
        var apps = DockApps.Compose([new DockPin(@"C:\pins\Terminal.lnk", null, "Terminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App")], [terminal],
            w => w.Handle == 1 ? "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" : null);
        Assert.Single(apps);
        Assert.True(apps[0].Running);
    }

    [Fact]
    public void AWebAppKeepsItsWindowsFromItsBrowsersPin()
    {
        const string edge = @"C:\Edge\msedge.exe";
        var apps = DockApps.Compose(
            [new DockPin(@"C:\pins\Edge.lnk", edge, "Edge", "MSEdge"), new DockPin(@"C:\pins\Outlook.lnk", edge, "Outlook", "MSEdge._crx_outlook")],
            [Window(edge, "Edge", 1), Window(edge, "Outlook", 2), Window(edge, "Edge", 3)],
            w => w.Handle == 2 ? "MSEdge._crx_outlook" : "MSEdge");
        Assert.Equal(2, apps.Count);
        Assert.Equal([1, 3], apps[0].Windows.Select(w => (int)w.Handle));
        Assert.Equal([2], apps[1].Windows.Select(w => (int)w.Handle));
    }

    [Fact]
    public void AWindowWithoutAnIdFallsBackToThePinsProgram()
    {
        const string chrome = @"C:\Chrome\chrome.exe";
        var apps = DockApps.Compose([new DockPin(@"C:\pins\Chrome.lnk", chrome, "Chrome", "Chrome")], [Window(chrome, "Chrome", 1)], _ => null);
        Assert.Single(apps);
        Assert.True(apps[0].Running);
    }

    [Fact]
    public void ASquirrelAppsWindowsGoToItsLaunchersPin()
    {
        // Claude's and Discord's shortcuts run App\x.exe; their windows run App\app-1.2.3\x.exe and carry no ID.
        const string stub = @"C:\Users\T\AppData\Local\AnthropicClaude\claude.exe";
        var apps = DockApps.Compose([new DockPin(@"C:\pins\Claude.lnk", stub, "Claude", "com.squirrel.AnthropicClaude.claude")],
            [Window(@"C:\Users\T\AppData\Local\AnthropicClaude\app-7.37095.0\claude.exe", "Claude", 1)], _ => null);
        Assert.Single(apps);
        Assert.True(apps[0].Running);
        Assert.False(DockApps.SameProgram(stub, @"C:\Users\T\AppData\Local\Other\app-1.0\claude.exe"));
        Assert.False(DockApps.SameProgram(stub, @"C:\Users\T\AppData\Local\AnthropicClaude\tools\claude.exe"));
    }

    [Fact]
    public void RunningAppsKeepTheirPlaceWhenBroughtToTheFront()
    {
        var order = new List<string>();
        var first = DockApps.Arrange(DockApps.Compose([], [Window(@"C:\a.exe", "A", 1), Window(@"C:\b.exe", "B", 2)]), order);
        var later = DockApps.Arrange(DockApps.Compose([], [Window(@"C:\b.exe", "B", 2), Window(@"C:\a.exe", "A", 1)]), order);
        Assert.Equal(first.Select(a => a.Name), later.Select(a => a.Name));
        var closed = DockApps.Arrange(DockApps.Compose([], [Window(@"C:\b.exe", "B", 2)]), order);
        Assert.Equal(["B"], closed.Select(a => a.Name));
    }

    [Fact]
    public void UnpinnedStoreAppsOpenAgainThroughTheirId()
    {
        var apps = DockApps.Compose([], [Window(@"C:\Program Files\WindowsApps\Calc\CalculatorApp.exe", "Calculator", 1)], _ => "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", apps[0].Launch);
        Assert.False(apps[0].Pinned);
    }

    [Fact]
    public void DraggingAPinRightLandsWhereItIsLetGo()
    {
        // Pins A, B, C at 24, 78, 132: A let go between B and C lands second, not last.
        double[] centers = [24, 78, 132];
        Assert.Equal(1, DockApps.InsertIndex(centers, 0, 100));
        Assert.Equal(["b", "a", "c"], DockApps.Move(["a", "b", "c"], "a", DockApps.InsertIndex(centers, 0, 100)));
        Assert.Equal(2, DockApps.InsertIndex(centers, 0, 150));
        Assert.Equal(1, DockApps.InsertIndex(centers, 2, 50));
        Assert.Equal(0, DockApps.InsertIndex(centers, -1, 10));
        Assert.Equal(3, DockApps.InsertIndex(centers, -1, 150));
    }

    [Fact]
    public void ATakenNameGetsANumberAsExplorerGivesIt()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "report.pdf", "report (2).pdf" };
        Assert.Equal("report (3).pdf", DockApps.FreeName("report.pdf", taken.Contains));
        Assert.Equal("notes.txt", DockApps.FreeName("notes.txt", taken.Contains));
    }

    [Fact]
    public void TaskbarPinsKeepTheTaskbarsOrder()
    {
        string[] links = [@"C:\t\Chrome.lnk", @"C:\t\File Explorer.lnk", @"C:\t\Terminal.lnk", @"C:\t\Spotify.lnk"];
        byte[] favorites = [0, 0x14, .. System.Text.Encoding.Unicode.GetBytes("\0Terminal.lnk\0\0x"), 3, .. System.Text.Encoding.Unicode.GetBytes("File Explorer.lnk"), 9, 9,
            .. System.Text.Encoding.Unicode.GetBytes("Chrome.lnk")];
        Assert.Equal([@"C:\t\Terminal.lnk", @"C:\t\File Explorer.lnk", @"C:\t\Chrome.lnk", @"C:\t\Spotify.lnk"], DockApps.TaskbarOrder(links, favorites));
        Assert.Equal([@"C:\t\File Explorer.lnk", @"C:\t\Chrome.lnk", @"C:\t\Spotify.lnk", @"C:\t\Terminal.lnk"], DockApps.TaskbarOrder(links, null));
    }

    [Fact]
    public void WinAndADigitAloneOpenTheDocksApps()
    {
        Assert.True(DockKeys.Matches(0x31, win: true, shift: false, control: false, alt: false));
        Assert.True(DockKeys.Matches(0x39, win: true, shift: false, control: false, alt: false));
        Assert.False(DockKeys.Matches(0x30, win: true, shift: false, control: false, alt: false));
        Assert.False(DockKeys.Matches(0x31, win: false, shift: false, control: false, alt: false));
        Assert.False(DockKeys.Matches(0x31, win: true, shift: true, control: false, alt: false));
    }

    [Fact]
    public void MovingAPinPlacesItAtTheIndex()
    {
        Assert.Equal(["b", "a", "c"], DockApps.Move(["a", "b", "c"], "a", 1));
        Assert.Equal(["a", "b", "c", "d"], DockApps.Move(["a", "b", "c"], "d", 99));
    }
}
