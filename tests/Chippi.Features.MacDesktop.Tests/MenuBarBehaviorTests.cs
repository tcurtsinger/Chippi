// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.MacDesktop.MenuBar;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.MacDesktop.Tests;

/// <summary>A bar too narrow for everything gives things up in order; nothing is drawn over anything else.</summary>
public sealed class NarrowBarTests
{
    private static readonly double[] Menus = [40, 40, 40, 40, 40, 40];

    // Controls, the language, the hidden icons, the badges, then network-volume-battery: the order they go.
    private static readonly double[] CanGo = [33, 44, 33, 90, 82];

    private static (double LeftEnd, double RightStart) Ends(BarArrangement a, double fixedLeft, double right)
    {
        double kept = right - CanGo.Take(a.RightDropped).Sum();
        return (a.LeftStart + MenuBarLayout.LeftWidth(fixedLeft, Menus, 24, a.MenusShown) - a.NameTrim, a.RightEnd - kept);
    }

    [Fact]
    public void ACenteredNotchOnANarrowBarDropsItemsInsteadOfMovingTheGroupOntoTheMenus()
    {
        // 960 wide (a 1920 laptop at 200%), the notch 388–572, every status item showing: 435 doesn't fit right of it.
        var a = MenuBarLayout.Arrange(960, 185, Menus, 24, 435, (388, 572), CanGo, 39);
        var (leftEnd, rightStart) = Ends(a, 185, 435);
        Assert.Equal(960 - MenuBarLayout.Pad, a.RightEnd); // it stays on the right
        Assert.True(rightStart >= 572 + MenuBarLayout.NotchClearance);
        Assert.True(leftEnd <= 388 - MenuBarLayout.NotchClearance);
        Assert.InRange(a.RightDropped, 1, CanGo.Length);
    }

    [Fact]
    public void NothingIsDroppedWhileItFits()
    {
        Assert.Equal(0, MenuBarLayout.Arrange(1920, 185, Menus, 24, 435, (868, 1052), CanGo, 39).RightDropped);
        Assert.Equal(0, MenuBarLayout.Arrange(1920, 185, Menus, 24, 435, null, CanGo, 39).RightDropped);
    }

    [Fact]
    public void WithoutANotchTheGroupsNeverOverlap()
    {
        foreach (double width in new[] { 1920.0, 1280, 960, 768, 640, 560, 480, 420 })
        {
            var a = MenuBarLayout.Arrange(width, 265, Menus, 24, 435, null, CanGo, 115);
            var (leftEnd, rightStart) = Ends(a, 265, 435);
            Assert.True(leftEnd <= rightStart, $"{width}: left ends at {leftEnd}, right starts at {rightStart}");
        }
    }

    [Fact]
    public void TheAppsNameIsCutOnlyAfterTheMenusAndTheItemsHaveGone()
    {
        // 420 wide: the menus are all in », the lesser items are gone, and the name still doesn't fit.
        var a = MenuBarLayout.Arrange(420, 265, Menus, 24, 435, null, CanGo, 115);
        Assert.Equal(0, a.MenusShown);
        Assert.Equal(CanGo.Length, a.RightDropped);
        Assert.True(a.NameTrim > 0);
        // A little wider, the items have gone and the name is still whole.
        var wider = MenuBarLayout.Arrange(480, 265, Menus, 24, 435, null, CanGo, 115);
        Assert.Equal(CanGo.Length, wider.RightDropped);
        Assert.Equal(0, wider.NameTrim);
    }

    [Fact]
    public void ANotchAtTheFarRightOfANarrowBarSharesItsLeft()
    {
        // The notch against the right end of a 960 bar: not even the clock fits past it.
        var a = MenuBarLayout.Arrange(960, 185, Menus, 24, 435, (760, 944), CanGo, 39);
        var (leftEnd, rightStart) = Ends(a, 185, 435);
        Assert.Equal(750, a.RightEnd);
        Assert.True(leftEnd <= rightStart);
    }
}

/// <summary>A display's bar stays hidden for the fullscreen app on it, whichever display the keyboard is on.</summary>
public sealed class FullscreenHoldsTests
{
    private const nint Main = 1, Second = 2, Film = 100, Browser = 200, Desktop = 300;
    private readonly HashSet<nint> _covering = [Film];

    private bool Covers(nint window, nint display) => _covering.Contains(window);

    [Fact]
    public void AClickOnTheOtherDisplayLeavesTheFilmsBarHidden()
    {
        var holds = new FullscreenHolds();
        Assert.Equal([Second], holds.Update(Film, frontFullscreen: true, Second, frontOrdinary: true, Covers));
        // A browser on the main display comes to the front: the film still covers the second.
        Assert.Equal([Second], holds.Update(Browser, false, Main, true, Covers));
        // So does a click on the desktop, or Start.
        Assert.Equal([Second], holds.Update(Desktop, false, Main, false, Covers));
    }

    [Fact]
    public void TheBarComesBackWhenTheFilmStopsCoveringItsDisplay()
    {
        var holds = new FullscreenHolds();
        holds.Update(Film, true, Second, true, Covers);
        _covering.Clear(); // closed, minimized, or out of fullscreen with the same window still in front
        Assert.Empty(holds.Update(Film, false, Second, true, Covers));
    }

    [Fact]
    public void AnotherAppInFrontOnThatDisplayBringsTheBarBack()
    {
        var holds = new FullscreenHolds();
        holds.Update(Film, true, Second, true, Covers);
        Assert.Empty(holds.Update(Browser, false, Second, true, Covers));
    }

    [Fact]
    public void EachDisplayHoldsItsOwn()
    {
        var holds = new FullscreenHolds();
        _covering.Add(Browser);
        holds.Update(Film, true, Second, true, Covers);
        Assert.Equal([Main, Second], holds.Update(Browser, true, Main, true, Covers).Order());
        _covering.Remove(Film);
        Assert.Equal([Main], holds.Update(Browser, true, Main, true, Covers));
    }
}

/// <summary>The app in front is held still while a menu is open, and caught up with when the last one closes.</summary>
public sealed class FrontGateTests
{
    [Fact]
    public void AChangeWhileAMenuIsOpenIsReadWhenItCloses()
    {
        var gate = new FrontGate();
        Assert.True(gate.Changed());
        gate.Opened();
        Assert.False(gate.Changed()); // Window › Minimize ran: the next window came forward before the menu had gone
        Assert.True(gate.Closed());
        Assert.False(gate.MenuOpen);
    }

    [Fact]
    public void NothingIsReadAgainWhenNothingChanged()
    {
        var gate = new FrontGate();
        gate.Opened();
        Assert.False(gate.Closed());
    }

    [Fact]
    public void MovingAcrossTheTitlesWaitsForTheLastMenu()
    {
        var gate = new FrontGate();
        gate.Opened(); // File
        gate.Opened(); // Edit opens before File has faded out
        Assert.False(gate.Changed());
        Assert.False(gate.Closed()); // File gone, Edit still open
        Assert.True(gate.Closed());
    }
}

public sealed class MenuBarTitleTests
{
    [Theory]
    [InlineData("Window", true)]
    [InlineData("window ", true)]
    [InlineData("Windows", false)]
    [InlineData("View", false)]
    public void TheAppsOwnWindowMenu(string title, bool own) => Assert.Equal(own, AppMenus.IsWindowTitle(title));

    [Theory]
    [InlineData("Help", true)]
    [InlineData("?", true)]
    [InlineData("Tools", false)]
    public void TheAppsHelpMenu(string title, bool help) => Assert.Equal(help, AppMenus.IsHelpTitle(title));

    [Fact]
    public void MoveToNamesEachDisplayApart()
    {
        Assert.Equal(["OMEN 34c", "S27R65"], MenuBarController.DisplayNames([("OMEN 34c", "OMEN 34c (3440×1440, main)"), ("S27R65", "S27R65 (1920×1080)")]));
        // Two of the same monitor: their full names tell them apart, numbered when even those match.
        Assert.Equal(["U2720Q (3840×2160, main)", "U2720Q (3840×2160)"],
            MenuBarController.DisplayNames([("U2720Q", "U2720Q (3840×2160, main)"), ("U2720Q", "U2720Q (3840×2160)")]));
        Assert.Equal(["U2720Q (3840×2160) 1", "U2720Q (3840×2160) 2"],
            MenuBarController.DisplayNames([("U2720Q", "U2720Q (3840×2160)"), ("U2720Q", "U2720Q (3840×2160)")]));
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData(4, 3)]
    [InlineData(3, 2)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void WifiArcsFollowTheSignal(int? bars, int lit) => Assert.Equal(lit, MenuBarWindow.WifiGlyph.Lit(bars));

    [Fact]
    public void TheDesktopIsNotFileExplorer()
    {
        // A folder window is File Explorer (the program still calls itself "Windows Explorer"); the bare desktop is the Desktop.
        Assert.Equal("File Explorer", AppNames.Name(AppIdentity.Desktop.Path));
        Assert.Equal("Desktop", AppIdentity.Desktop.Name);
    }

    [Fact]
    public void AFlyoutHangsUnderItsItemInsideTheDisplay()
    {
        var bar = new Rect(3440, 0, 1920, 28); // the second display's bar
        // Quick Settings, 360 × 330, opened from the item at 4800: centered under it, 6 below the bar.
        Assert.Equal((4620, 34), MenuBarController.FlyoutPlace(new Size(360, 330), 4800, bar, 1080));
        // From the clock at the far right: kept 6 inside the display's edge, on that display.
        Assert.Equal((5360 - 6 - 360, 34), MenuBarController.FlyoutPlace(new Size(360, 330), 5340, bar, 1080));
        // Taller than the room under the bar (notifications over the calendar): as low as it must be, no lower.
        Assert.Equal((5360 - 6 - 400, 1080 - 1060), MenuBarController.FlyoutPlace(new Size(400, 1060), 5340, bar, 1080));
        // At 150%, the gap scales with the bar.
        Assert.Equal(42 + 9, MenuBarController.FlyoutPlace(new Size(540, 495), 900, new Rect(0, 0, 2880, 42), 1620).Top);
    }
}

/// <summary>An app is called what the Start menu calls it.</summary>
public sealed class AppCatalogTests
{
    private static readonly AppCatalog Catalog = new(
    [
        new CatalogApp("Steam", "", @"C:\Program Files (x86)\Steam\steam.exe"),
        new CatalogApp("Discord", "com.squirrel.Discord.Discord", @"C:\Users\me\AppData\Local\Discord\Update.exe", "--processStart Discord.exe"),
        new CatalogApp("Git Bash", "", @"C:\Program Files\Git\git-bash.exe"),
        new CatalogApp("Git GUI", "", @"C:\Program Files\Git\cmd\git-gui.exe"),
        new CatalogApp("Python 3.12 (64-bit)", "", @"C:\Python312\python.exe"),
        new CatalogApp("IDLE", "", @"C:\Python312\pythonw.exe", @"C:\Python312\Lib\idlelib\idle.pyw"),
        new CatalogApp("Command Prompt", "", @"C:\Windows\System32\cmd.exe"),
        new CatalogApp("YouTube", "Chrome._crx_agimnkijcaahngcdmfeangaknmldooml", @"C:\Program Files\Google\Chrome\Application\chrome_proxy.exe", "--app-id=agimnk"),
        new CatalogApp("Google Chrome", "Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
        new CatalogApp("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", ""),
        new CatalogApp("Uninstall Steam", "", @"C:\Program Files (x86)\Steam\uninstall.exe"),
        new CatalogApp("SDK folder", "", @"C:\Windows\explorer.exe", @"C:\SDK"),
        new CatalogApp("Notes", "", @"C:\Tools\editor.exe", "--notes"),
        new CatalogApp("Editor", "", @"C:\Tools\editor.exe"),
    ], [@"C:\Windows", @"C:\Windows\System32", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\me\AppData\Local"]);

    [Fact]
    public void AHelperThatDrawsTheWindowIsNamedAfterItsApp()
    {
        // Steam's window belongs to a helper three folders down; a Squirrel app's to a versioned copy beside its updater.
        Assert.Equal("Steam", Catalog.NameOf(@"C:\Program Files (x86)\Steam\bin\cef\cef.win7x64\steamwebhelper.exe", null));
        Assert.Equal("Discord", Catalog.NameOf(@"C:\Users\me\AppData\Local\Discord\app-1.0.9172\Discord.exe", null));
    }

    [Fact]
    public void ItsIdentityComesFirstThenItsProgram()
    {
        // A web app in the browser's own program is the web app, by the window's identity.
        Assert.Equal("YouTube", Catalog.NameOf(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "Chrome._crx_agimnkijcaahngcdmfeangaknmldooml"));
        Assert.Equal("Google Chrome", Catalog.NameOf(@"C:\Program Files\Google\Chrome\Application\chrome.exe", null));
        Assert.Equal("Calculator", Catalog.NameOf(null, "microsoft.windowscalculator_8wekyb3d8bbwe!app"));
        // Two shortcuts to one program: the one without arguments.
        Assert.Equal("Editor", Catalog.NameOf(@"C:\Tools\editor.exe", null));
    }

    [Fact]
    public void NothingIsNamedAfterANeighbourItMightNotBe()
    {
        // Several apps in the folder above: it could be any of them.
        Assert.Null(Catalog.NameOf(@"C:\Program Files\Git\mingw64\bin\gitk.exe", null));
        // A folder everything shares: a neighbour there says nothing.
        Assert.Null(Catalog.NameOf(@"C:\Windows\System32\mmc.exe", null));
        Assert.Null(Catalog.NameOf(@"C:\Program Files\Unlisted\app.exe", null));
        Assert.Null(Catalog.NameOf("app.exe", null));
        // A shortcut that opens a folder through Explorer doesn't name Explorer.
        Assert.Null(Catalog.NameOf(@"C:\Windows\explorer.exe", null));
        Assert.Null(Catalog.NameOf(@"C:\Python312\pythonw.exe", null));
        Assert.Null(AppCatalog.Empty.NameOf(@"C:\Tools\editor.exe", "Chrome"));
    }
}

/// <summary>
/// The real bar, built without a window: what takes a press, what shows on a narrow bar, and (with CHIPPI_ISLAND_SHOTS
/// naming a folder) <c>menu-bar.png</c> and <c>menu-bar-2x.png</c>, the bar over different wallpapers and widths. Shows
/// nothing.
/// </summary>
public sealed class MenuBarOffscreenTests
{
    private static readonly string[] Menus = ["File", "Edit", "Search", "View", "Encoding", "Language", "Settings", "Tools", "Macro", "Run", "Plugins", "Window", "?"];

    private sealed class Settings : IModuleSettings
    {
        public T Get<T>() where T : class, new() => new();
        public void Save<T>(T value) where T : class { }
    }

    private sealed class Ui : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new NotSupportedException();
        public void SetSettingsPage(Func<string?, object> createView) { }
    }

    /// <summary>Runs <paramref name="test"/> with a bar controller on a thread of its own, as WPF needs.</summary>
    private static void OnBar(Action<MacShell, MenuBarController> test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            MenuBarController? controller = null;
            try
            {
                var context = new ModuleContext("mac-desktop", new Settings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher("mac-desktop"), new Ui());
                var shell = new MacShell(context, null);
                controller = new MenuBarController(shell);
                test(shell, controller);
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                controller?.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new InvalidOperationException(error.ToString(), error);
    }

    private static StatusReadings Status(bool laptop, bool muted = false, int? bars = null)
    {
        var status = new StatusReadings(Dispatcher.CurrentDispatcher);
        status.Dispose(); // no timers: what it shows is set here
        void Set(string property, object? value) => typeof(StatusReadings).GetProperty(property)!.SetValue(status, value);
        Set(nameof(StatusReadings.Connection), laptop ? StatusReadings.Network.WiFi : StatusReadings.Network.Wired);
        Set(nameof(StatusReadings.Bars), bars);
        Set(nameof(StatusReadings.Battery), laptop ? 64 : null);
        Set(nameof(StatusReadings.Charging), false);
        Set(nameof(StatusReadings.Language), laptop ? "ENG" : null);
        Set(nameof(StatusReadings.Volume), 45);
        Set(nameof(StatusReadings.Muted), muted);
        return status;
    }

    private static MenuBarWindow Bar(MenuBarController controller, InkStrip ink, string app, string[] menus, int badges, StatusReadings status)
    {
        var bar = new MenuBarWindow(controller, 0, ink);
        bar.SetApp(app, menus);
        bar.SetStatus(status);
        bar.SetClock(MenuBarController.ClockText(new DateTime(2026, 11, 25, 10, 2, 0)));
        var all = new List<(string, Geometry, Brush, Brush, Action)>
        {
            ("Microphone muted", LucideIcons.MicOff, new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)), Brushes.White, () => { }),
            ("Keep awake", LucideIcons.Coffee, new LinearGradientBrush(Color.FromRgb(0xB7, 0x8F, 0xFF), Color.FromRgb(0x82, 0xAE, 0xFF), new Point(0, 0), new Point(1, 1)), new SolidColorBrush(Color.FromRgb(0x17, 0x0F, 0x2E)), () => { }),
            ("Agents", Glyphs.Sparkle, new SolidColorBrush(Color.FromRgb(0xE8, 0x87, 0x5B)), Brushes.White, () => { }),
        };
        bar.SetBadges(all.Take(badges).ToList());
        return bar;
    }

    private static MenuBarWindow.BarItem? ItemAt(Canvas root, double x, double y)
    {
        // The plain hit test: the input one skips everything that isn't on a screen.
        DependencyObject? hit = VisualTreeHelper.HitTest(root, new Point(x, y))?.VisualHit;
        while (hit is not null and not MenuBarWindow.BarItem)
            hit = VisualTreeHelper.GetParent(hit);
        return hit as MenuBarWindow.BarItem;
    }

    [Fact]
    public void TheEdgesAndCornersOfTheBarTakeAPress() => OnBar((_, controller) =>
    {
        var bar = Bar(controller, InkStrip.Flat(1), "Notepad", ["File", "Edit"], 0, Status(laptop: false));
        var root = bar.LayOut(1920);
        Assert.Equal("Chippi", System.Windows.Automation.AutomationProperties.GetName(ItemAt(root, 0.5, 0.5)!)); // the corner
        Assert.Equal("Notifications", System.Windows.Automation.AutomationProperties.GetName(ItemAt(root, 1919.5, 0.5)!));
        // The top and bottom rows of pixels, over a title.
        var file = bar.Groups.Left.Left + 29 + 2 + 70;
        Assert.NotNull(ItemAt(root, file, 0.5));
        Assert.NotNull(ItemAt(root, file, 27.5));
        Assert.Same(ItemAt(root, file, 0.5), ItemAt(root, file, 14));
    });

    [Fact]
    public void ANarrowBarWithTheNotchKeepsItsGroupsApart() => OnBar((shell, controller) =>
    {
        shell.Notch = () => (0, 388, 572);
        var bar = Bar(controller, InkStrip.Flat(0), "Notepad++", Menus, 3, Status(laptop: true));
        bar.LayOut(960);
        var (left, right) = bar.Groups;
        Assert.True(left.Right <= 388 - MenuBarLayout.NotchClearance + 0.5, $"left ends at {left.Right}");
        Assert.True(right.Left >= 572 + MenuBarLayout.NotchClearance - 0.5, $"right starts at {right.Left}");
        Assert.Contains("Notifications", bar.Showing); // the clock never goes
        Assert.DoesNotContain("Controls", bar.Showing); // the first to go
    });

    [Fact]
    public void AnAppsOwnWindowMenuTakesTheBarsPlace() => OnBar((_, controller) =>
    {
        var bar = Bar(controller, InkStrip.Flat(0), "Notepad++", Menus, 0, Status(laptop: false));
        bar.LayOut(1920);
        Assert.Single(bar.Showing, name => name == "Window");
        // Without one, the bar's Window goes before the app's Help.
        bar.SetApp("Paint", ["File", "Edit", "View", "Help"]);
        bar.LayOut(1920);
        Assert.Equal(["Chippi", "Paint", "File", "Edit", "View", "Window", "Help"], bar.Showing.Take(7));
    });

    [Fact]
    public void EachGroupWearsTheInkOfItsSideOfTheWallpaper() => OnBar((_, controller) =>
    {
        var columns = Enumerable.Range(0, 96).Select(i => i < 53 ? 0.07 : 0.95).ToArray(); // dark left, light right
        var bar = Bar(controller, new InkStrip(columns), "Notepad", ["File", "Edit"], 0, Status(laptop: false));
        bar.LayOut(1920);
        Assert.Equal((BarInk.Light, BarInk.Dark), bar.Inks);
    });

    [Fact]
    public void TheGlyphsSayWhatTheyStandFor() => OnBar((_, _) =>
    {
        Assert.Equal("Ethernet · Volume 45%", MenuBarWindow.StatusText(Status(laptop: false)));
        Assert.Equal("Wi-Fi · Muted · Battery 64%", MenuBarWindow.StatusText(Status(laptop: true, muted: true, bars: 2)));
    });

    [Fact]
    public void Shots() => OnBar((shell, controller) =>
    {
        string? output = Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS");
        if (string.IsNullOrWhiteSpace(output))
            return;
        Directory.CreateDirectory(output);
        var white = new SolidColorBrush(Color.FromRgb(0xFD, 0xFD, 0xFD));
        var dark = new LinearGradientBrush(Color.FromRgb(0x10, 0x18, 0x2C), Color.FromRgb(0x2B, 0x1B, 0x3F), 0);
        var grey = new SolidColorBrush(Color.FromRgb(0x7D, 0x7D, 0x7D)); // 0.49: white ink
        var split = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x12, 0x12, 0x16), 0), new(Color.FromRgb(0x12, 0x12, 0x16), 0.55),
            new(Color.FromRgb(0xF4, 0xF2, 0xEE), 0.56), new(Color.FromRgb(0xF4, 0xF2, 0xEE), 1),
        }, 0);
        var splitStrip = new InkStrip(Enumerable.Range(0, 96).Select(i => i < 53 ? 0.07 : 0.95).ToArray());
        var rows = new List<(string Name, double Width, InkStrip Ink, Brush Back, double? Notch, int Badges, bool Laptop, bool Muted, string App, string[] Menus)>
        {
            ("white wallpaper", 1920, InkStrip.Flat(0.99), white, 184, 0, false, false, "Notepad++", Menus),
            ("dark wallpaper, every status item", 1920, InkStrip.Flat(0.1), dark, 184, 3, true, false, "Notepad++", Menus),
            ("grey 0.49, muted, Wi-Fi 2 of 4", 1920, InkStrip.Flat(0.49), grey, null, 1, true, true, "Google Chrome", []),
            ("dark left, light right", 1920, splitStrip, split, null, 0, true, false, "Paint", ["File", "Edit", "View", "Help"]),
            ("960 wide, notch, 3 badges", 960, InkStrip.Flat(0.1), dark, 184, 3, true, false, "Notepad++", Menus),
            ("960 wide, notch 280, 1 badge", 960, InkStrip.Flat(0.1), dark, 280, 1, true, false, "Notepad++", Menus),
            ("640 wide, long name", 640, InkStrip.Flat(0.99), white, null, 3, true, false, "Steam Client WebHelper", ["File", "Edit"]),
        };
        var report = new StringBuilder();
        foreach (double scale in new[] { 1.0, 2.0 })
        {
            double total = rows.Count * 40 + 8, widest = rows.Max(r => r.Width);
            var bitmap = new RenderTargetBitmap((int)(widest * scale), (int)(total * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var backdrop = new DrawingVisual();
            using (var dc = backdrop.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x48)), null, new Rect(0, 0, widest, total));
            bitmap.Render(backdrop);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                double top = 8 + i * 40;
                (double Left, double Right)? notch = row.Notch is { } n ? ((row.Width - n) / 2, (row.Width + n) / 2) : null;
                shell.Notch = () => notch is { } at ? (0, at.Left, at.Right) : null;
                var bar = Bar(controller, row.Ink, row.App, row.Menus, row.Badges, Status(row.Laptop, row.Muted, row.Muted ? 2 : null));
                var root = bar.LayOut(row.Width);
                var layer = new DrawingVisual();
                using (var dc = layer.RenderOpen())
                {
                    dc.DrawRectangle(row.Back, null, new Rect(0, top, row.Width, 28));
                    if (notch is { } at)
                        dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(at.Left, top - 10, at.Right - at.Left, 38), 10, 10);
                    dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, row.Width, 28) },
                        null, new Rect(0, top, row.Width, 28));
                }
                bitmap.Render(layer);
                if (scale == 1.0)
                    report.AppendLine($"{row.Name} ({row.Width}): left {bar.Groups.Left.Left:0}..{bar.Groups.Left.Right:0}, right {bar.Groups.Right.Left:0}..{bar.Groups.Right.Right:0}, inks {bar.Inks}")
                        .AppendLine("  " + string.Join(" | ", bar.Showing));
            }
            Save(bitmap, Path.Combine(output, scale == 1.0 ? "menu-bar.png" : "menu-bar-2x.png"));
        }
        // This PC's Start menu, read as the bar reads it: how many names, how long, and what the programs with a window
        // open now would be called (their file names only).
        var catalogTimer = System.Diagnostics.Stopwatch.StartNew();
        var catalog = AppCatalog.Read();
        report.AppendLine($"app names: {catalog.Count} keys in {catalogTimer.ElapsedMilliseconds} ms");
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == 0 || AppNames.ImagePath((uint)process.Id) is not { } path)
                    continue;
                string? id = ShellProperties.AppId(new Chippi.Core.Actions.ListedWindow(process.MainWindowHandle, (uint)process.Id, "", "", path, false, 0));
                report.AppendLine($"  {Path.GetFileName(path),-36} {(id is not null ? catalog.NameOf(null, id) : null) ?? catalog.NameOf(path, null) ?? "(not listed)"}{(id is null ? "" : "  [by identity]")}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        // This PC's default output, read as the bar reads it (never set): that the reading works here, and how long it takes.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        bool read = VolumeReading.Read() is not null;
        report.AppendLine($"volume read: {(read ? "yes" : "no output")}, {timer.Elapsed.TotalMilliseconds:0.0} ms");
        File.WriteAllText(Path.Combine(output, "menu-bar.txt"), report.ToString());
    });

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
