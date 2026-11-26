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
using Chippi.UI.Pet;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.MacDesktop.Tests;

/// <summary>A bar too narrow for everything gives things up in order; nothing is drawn over anything else.</summary>
public sealed class NarrowBarTests
{
    private static readonly double[] Menus = [40, 40, 40, 40, 40, 40];

    // The right group's items, left to right: the order they go.
    private static readonly double[] CanGo = [33, 44, 33, 90, 82];

    private static (double LeftEnd, double RightStart) Ends(BarArrangement a, double fixedLeft, double right)
    {
        double kept = right - CanGo.Take(a.RightDropped).Sum();
        return (a.LeftStart + MenuBarLayout.LeftWidth(fixedLeft, Menus, 24, a.MenusShown) - a.NameTrim, a.RightEnd - kept);
    }

    [Fact]
    public void TheIslandOnANarrowBarDropsItemsInsteadOfMovingTheGroupOntoTheMenus()
    {
        // 960 wide (a 1920 laptop at 200%), the island 388–572, every status item showing: 435 doesn't fit right of it.
        var a = MenuBarLayout.Arrange(960, 185, Menus, 24, 435, (388, 572), CanGo, 39);
        var (leftEnd, rightStart) = Ends(a, 185, 435);
        Assert.Equal(960 - MenuBarLayout.Pad, a.RightEnd); // it stays on the right
        Assert.True(rightStart >= 572 + MenuBarLayout.IslandClearance);
        Assert.True(leftEnd <= 388 - MenuBarLayout.IslandClearance);
        Assert.InRange(a.RightDropped, 1, CanGo.Length);
    }

    [Fact]
    public void NothingIsDroppedWhileItFits()
    {
        Assert.Equal(0, MenuBarLayout.Arrange(1920, 185, Menus, 24, 435, (868, 1052), CanGo, 39).RightDropped);
        Assert.Equal(0, MenuBarLayout.Arrange(1920, 185, Menus, 24, 435, null, CanGo, 39).RightDropped);
    }

    [Fact]
    public void WithoutAnIslandTheGroupsNeverOverlap()
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
    public void AnIslandAtTheFarRightOfANarrowBarSharesItsLeft()
    {
        // The island against the right end of a 960 bar: not even the clock fits past it.
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
}

/// <summary>The right side's items: what shows, what can be added, how the list changes and what a press does.</summary>
public sealed class BarItemsTests
{
    private static readonly string[] Installed = ["command-bar", "screen-capture", "clipboard-history", "mic-mute", "keep-awake"];

    private static bool Desktop(string id) => BarItems.Available(id, Installed.Contains, battery: false, languages: false);

    private static bool Laptop(string id) => BarItems.Available(id, _ => true, battery: true, languages: true);

    [Fact]
    public void TheBarStartsWithChippisButtonsAndOnlyWhatThisPcHas()
    {
        // A desktop PC with one input language: no Battery, no Language.
        Assert.Equal([BarItems.Search, BarItems.Capture, BarItems.Clipboard, BarItems.Microphone, BarItems.Sound, BarItems.HiddenIcons, BarItems.Controls],
            BarItems.Shown(null, Desktop));
        // A laptop with two: both, before the hidden icons.
        Assert.Equal(BarItems.Start, BarItems.Shown(null, Laptop));
        // Without its feature, an item isn't there, saved or not; a feature that comes back brings it back.
        Assert.Equal([BarItems.Sound], BarItems.Shown([BarItems.Scratchpad, BarItems.Sound, "gone", BarItems.Sound], Desktop));
        Assert.Equal([BarItems.Scratchpad, BarItems.Sound], BarItems.Shown([BarItems.Scratchpad, BarItems.Sound], Laptop));
        // An emptied bar stays empty: only the clock.
        Assert.Empty(BarItems.Shown([], Laptop));
    }

    [Fact]
    public void AnEditKeepsTheItemsThisPcCantShowNow()
    {
        // The start set on a desktop: Battery and Language have no row, and an edit of the rows doesn't lose them.
        var shown = BarItems.Shown(null, Desktop);
        var edited = BarItems.Moved(shown, BarItems.Search, shown.Count - 1);
        var moved = BarItems.Kept(null, shown, edited);
        Assert.Equal([BarItems.Capture, BarItems.Clipboard, BarItems.Microphone, BarItems.Sound, BarItems.Battery, BarItems.Language, BarItems.HiddenIcons, BarItems.Controls, BarItems.Search], moved);
        Assert.Equal(edited, BarItems.Shown(moved, Desktop));
        Assert.Contains(BarItems.Battery, BarItems.Shown(moved, Laptop));
        // The item they followed is removed: they follow the one before it. What was removed is gone.
        var removed = BarItems.Kept(null, shown, BarItems.Removed(shown, BarItems.Sound));
        Assert.Equal([BarItems.Search, BarItems.Capture, BarItems.Clipboard, BarItems.Microphone, BarItems.Battery, BarItems.Language, BarItems.HiddenIcons, BarItems.Controls], removed);
        // Kept at the left end when nothing before them is on the bar, through an add, and through emptying the bar.
        IReadOnlyList<string> saved = [BarItems.Battery, BarItems.Sound, "gone", BarItems.Language];
        Assert.Equal([BarItems.Battery, BarItems.Sound, BarItems.Language, BarItems.Shelf], BarItems.Kept(saved, [BarItems.Sound], [BarItems.Sound, BarItems.Shelf]));
        Assert.Equal([BarItems.Battery, BarItems.Language], BarItems.Kept(saved, [BarItems.Sound], []));
        // Nothing hidden: the edit as it is.
        Assert.Equal([BarItems.Shelf, BarItems.Sound], BarItems.Kept([BarItems.Sound, BarItems.Shelf], [BarItems.Sound, BarItems.Shelf], [BarItems.Shelf, BarItems.Sound]));
    }

    [Fact]
    public void AddItemOffersTheRestChippisFirst()
    {
        var shown = BarItems.Shown(null, Desktop);
        Assert.Equal([BarItems.KeepAwake, BarItems.Network], BarItems.CanAdd(shown, Desktop));
        var all = BarItems.CanAdd([], Laptop);
        Assert.Equal(BarItems.All.Count, all.Count);
        // Windows' own come last, together.
        Assert.Equal([BarItems.HiddenIcons, BarItems.Network, BarItems.Battery, BarItems.Language], all.Skip(all.Count - 4));
        Assert.All(BarItems.All.TakeLast(4), item => Assert.True(item.Windows));
    }

    [Fact]
    public void TheListMovesAddsAndRemoves()
    {
        string[] list = [BarItems.Search, BarItems.Capture, BarItems.Sound];
        Assert.Equal([BarItems.Capture, BarItems.Sound, BarItems.Search], BarItems.Moved(list, BarItems.Search, 2));
        Assert.Equal([BarItems.Sound, BarItems.Search, BarItems.Capture], BarItems.Moved(list, BarItems.Sound, -3)); // past the end: the end
        Assert.Same(list, BarItems.Moved(list, BarItems.Capture, 1)); // nowhere to go
        Assert.Same(list, BarItems.Moved(list, BarItems.Shelf, 0)); // not on the bar
        Assert.Equal([.. list, BarItems.Shelf], BarItems.Added(list, BarItems.Shelf)); // next to the clock
        Assert.Same(list, BarItems.Added(list, BarItems.Sound));
        Assert.Same(list, BarItems.Added(list, "gone"));
        Assert.Equal([BarItems.Search, BarItems.Sound], BarItems.Removed(list, BarItems.Capture));
    }

    [Fact]
    public void EveryItemDoesOneThing()
    {
        foreach (var item in BarItems.All)
        {
            var press = BarItems.Press(item.Id);
            bool needsTarget = press.Kind is not (BarPressKind.Sound or BarPressKind.HiddenIcons or BarPressKind.Home);
            Assert.Equal(needsTarget, press.Target.Length > 0);
            Assert.NotEqual(LucideIcons.LayoutGrid, Glyphs.Of(item.Id)); // its own glyph
        }
        Assert.Equal(BarItems.All.Count, BarItems.All.Select(item => item.Id).Distinct().Count());
        Assert.All(BarItems.Start, id => Assert.NotNull(BarItems.Find(id)));
        Assert.Equal(new BarPress(BarPressKind.Command, "screen-capture/region"), BarItems.Press(BarItems.Capture)); // the region selector, at once
        Assert.Throws<ArgumentOutOfRangeException>(() => BarItems.Press("gone"));
    }

    [Fact]
    public void TheProcessorsLoadIsTheTimeThatWasntIdle()
    {
        Assert.Null(StatusReadings.Load(null, (100, 1000)));
        Assert.Equal(25, StatusReadings.Load((100, 1000), (400, 1400))); // 300 of 400 idle
        Assert.Equal(100, StatusReadings.Load((100, 1000), (100, 1400)));
        Assert.Equal(0, StatusReadings.Load((100, 1000), (600, 1400)));
        Assert.Null(StatusReadings.Load((100, 1000), (100, 1000))); // no time passed
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

    private static StatusReadings Status(bool laptop, bool muted = false, int? bars = null, int? cpu = null)
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
        Set(nameof(StatusReadings.Cpu), cpu);
        return status;
    }

    /// <summary>The bar as it starts on a desktop PC, and every item there is.</summary>
    private static readonly string[] StartItems = [BarItems.Search, BarItems.Capture, BarItems.Clipboard, BarItems.Microphone, BarItems.Sound, BarItems.HiddenIcons, BarItems.Controls];
    private static readonly string[] EveryItem = [.. BarItems.All.Select(item => item.Id)];
    private static readonly BarStates Busy = new(MicMuted: true, Awake: true, AgentWorking: true, Recording: "0:42");

    // What the island shows. The glyphs are frozen, so any thread's bar can draw them.
    private static readonly BarIslandFace Rest = BarIslandFace.Rest(PetMood.Idle);
    private static readonly BarIslandFace Song = new(PetMood.Listening, IslandLead.Cover, null, null, "Midnight City", "M83", IslandTail.Equalizer, "now-playing", "Now Playing");
    private static readonly BarIslandFace Waiting = new(PetMood.Idle, IslandLead.Attention, LucideIcons.Gauge, null, "Waiting on you", "", IslandTail.None, "agent-usage", "Agents");
    private static readonly BarIslandFace Recording = new(PetMood.Idle, IslandLead.Recording, null, null, "0:42", "", IslandTail.None, "screen-recording", "Screen recording");
    private static readonly BarIslandFace Awake = new(PetMood.Idle, IslandLead.Glyph, LucideIcons.Coffee, null, "1 hr left", "", IslandTail.None, "keep-awake", "Keep awake");

    [Fact]
    public void TheIslandSitsInTheMiddleInTheInkOfTheWallpaperUnderIt() => OnBar((_, controller) =>
    {
        // Dark on the left 55%, light after: the middle of a 1920 bar is still over the dark part.
        var strip = new InkStrip(Enumerable.Range(0, 96).Select(i => i < 53 ? 0.07 : 0.95).ToArray());
        var bar = Bar(controller, strip, "Paint", ["File", "Edit"], StartItems, Status(laptop: false));
        Assert.Null(bar.IslandAt); // a second display's bar, or the island switched off
        bar.SetIsland(Rest, default);
        bar.LayOut(1920);
        var rest = bar.IslandAt!.Value;
        Assert.Equal(960, rest.Left + rest.Width / 2, 0);
        Assert.Equal((BarIsland.Top, BarIsland.Tall), (rest.Top, rest.Height));
        Assert.True(rest.Bottom <= 28, "never taller than the bar");
        Assert.Equal(BarInk.Light, bar.IslandInk);
        Assert.Equal((BarInk.Light, BarInk.Dark), bar.Inks);

        bar.SetIsland(Song, default);
        bar.LayOut(1920);
        var song = bar.IslandAt!.Value;
        Assert.True(song.Width > rest.Width + 100, $"a song is {song.Width} wide, rest {rest.Width}");
        Assert.Equal(960, song.Left + song.Width / 2, 0.5); // it only widens, about the same middle
        Assert.Equal(rest.Height, song.Height);

        bar.SetInk(InkStrip.Flat(0.95));
        Assert.Equal(BarInk.Dark, bar.IslandInk);
        bar.SetIsland(null, default);
        bar.LayOut(1920);
        Assert.Null(bar.IslandAt);
    });

    [Fact]
    public void TheIconOfThePageShowingIsLitAndControlsForTheRest() => OnBar((_, controller) =>
    {
        var bar = Bar(controller, InkStrip.Flat(0), "Notepad", ["File"], StartItems, Status(laptop: false));
        bar.LayOut(1920);
        Assert.Null(bar.Lit);
        bar.SetPage(true, "clipboard");
        Assert.Equal(BarItems.Clipboard, bar.Lit);
        bar.SetPage(true, "scratchpad"); // no icon of its own on this bar
        Assert.Equal(BarItems.Controls, bar.Lit);
        bar.SetPage(true, null); // the home
        Assert.Equal(BarItems.Controls, bar.Lit);
        bar.SetPage(false, "clipboard");
        Assert.Null(bar.Lit);
    });

    [Fact]
    public void LitAndOpensPageFollowWhatAPressDoes()
    {
        string[] shown = [BarItems.Search, BarItems.Clipboard, BarItems.Record, BarItems.Sound, BarItems.Controls];
        Assert.Equal(BarItems.Clipboard, BarItems.Lit("clipboard", shown, recording: false));
        Assert.Equal(BarItems.Search, BarItems.Lit("command-bar", shown, recording: false));
        Assert.Equal(BarItems.Record, BarItems.Lit("recording", shown, recording: false));
        Assert.Equal(BarItems.Controls, BarItems.Lit("recording", shown, recording: true)); // Record is the Stop button then
        Assert.Equal(BarItems.Controls, BarItems.Lit("shelf", shown, recording: false));
        Assert.Equal(BarItems.Controls, BarItems.Lit(null, shown, recording: false));
        Assert.Null(BarItems.Lit("shelf", [BarItems.Sound], recording: false));

        Assert.True(BarItems.OpensPage(BarItems.Clipboard, recording: false));
        Assert.True(BarItems.OpensPage(BarItems.Controls, recording: false));
        Assert.True(BarItems.OpensPage(BarItems.Search, recording: false));
        Assert.True(BarItems.OpensPage(BarItems.Record, recording: false));
        Assert.False(BarItems.OpensPage(BarItems.Record, recording: true));
        Assert.False(BarItems.OpensPage(BarItems.Microphone, recording: false));
        Assert.False(BarItems.OpensPage(BarItems.Sound, recording: false));
        Assert.False(BarItems.OpensPage("nothing", recording: false));
    }

    [Fact]
    public void APressedBarsDisplayIsTakenOnceAndGoesStale()
    {
        var press = new SpotPress();
        Assert.Equal(0, press.Take(100));
        press.Set(7, 1000);
        Assert.Equal(7, press.Take(1000 + SpotPress.Milliseconds));
        Assert.Equal(0, press.Take(1001)); // taken once
        press.Set(7, 5000);
        Assert.Equal(0, press.Take(5001 + SpotPress.Milliseconds)); // Sound or the clock was pressed: a later page isn't this press's
        press.Set(7, 9000);
        press.Set(0, 9010); // a bar without a display of its own
        Assert.Equal(0, press.Take(9020));
    }

    [Fact]
    public void ControlsClosesThePanelWhileItIsTheLitIcon() => OnBar((shell, controller) =>
    {
        string? showing = "shelf";
        bool open = true;
        var asked = new List<string?>();
        int homes = 0;
        shell.ClosePage = key =>
        {
            asked.Add(key);
            if (!open || showing != key)
                return false;
            open = false;
            return true;
        };
        shell.OpenHome = () => homes++;

        // Shelf has no icon on the bar: Controls is lit for it, and a press on Controls closes it.
        controller.SetPage(true, "shelf", 0);
        controller.Press(BarItems.Controls);
        Assert.False(open);
        Assert.Equal(0, homes);
        Assert.Equal([null, "shelf"], asked);

        // Nothing showing: Controls opens the home.
        controller.SetPage(false, null, 0);
        controller.Press(BarItems.Controls);
        Assert.Equal(1, homes);
    });

    [Fact]
    public void AStateRidesBesideTheIslandUnlessTheBarHasItsIconOrTheIslandShowsIt()
    {
        var all = new BarStates(MicMuted: true, Awake: true, AgentWorking: true, Recording: null);
        Assert.Equal([IslandBadge.MicMuted, IslandBadge.Awake, IslandBadge.Agent], MenuBarWindow.IslandBadges(Rest, all, [BarItems.Sound]));
        Assert.Equal([IslandBadge.Awake, IslandBadge.Agent], MenuBarWindow.IslandBadges(Rest, all, StartItems)); // the bar's Microphone shows the mic
        Assert.Empty(MenuBarWindow.IslandBadges(Rest, all, EveryItem));
        Assert.Equal([IslandBadge.Agent], MenuBarWindow.IslandBadges(Awake, all, StartItems)); // the island is showing Keep awake
        Assert.Equal([IslandBadge.Awake], MenuBarWindow.IslandBadges(Waiting, all, StartItems));
        Assert.Empty(MenuBarWindow.IslandBadges(Song, default, StartItems));
    }

    private static MenuBarWindow Bar(MenuBarController controller, InkStrip ink, string app, string[] menus, string[] items, StatusReadings status, BarStates states = default)
    {
        var bar = new MenuBarWindow(controller, 0, ink);
        bar.SetApp(app, menus);
        bar.SetStatus(status);
        bar.SetClock(MenuBarController.ClockText(new DateTime(2026, 11, 25, 10, 2, 0)));
        bar.SetStates(states);
        bar.SetItems(items);
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
        var bar = Bar(controller, InkStrip.Flat(1), "Notepad", ["File", "Edit"], StartItems, Status(laptop: false));
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
    public void ANarrowBarWithTheIslandKeepsItsGroupsApart() => OnBar((shell, controller) =>
    {
        var bar = Bar(controller, InkStrip.Flat(0), "Notepad++", Menus, EveryItem, Status(laptop: true), Busy);
        bar.SetIsland(Song, default);
        bar.LayOut(960);
        var (left, right) = bar.Groups;
        var island = bar.IslandAt!.Value;
        Assert.Equal(480, island.Left + island.Width / 2, 0); // the bar's middle, on a whole pixel
        Assert.True(left.Right <= island.Left - MenuBarLayout.IslandClearance + 0.5, $"left ends at {left.Right}");
        Assert.True(right.Left >= island.Right + MenuBarLayout.IslandClearance - 0.5, $"right starts at {right.Left}");
        Assert.Contains("Notifications", bar.Showing); // the clock never goes
        // The items go from the left: what's left is the end of the list, next to the clock.
        var kept = bar.ItemsShowing;
        Assert.InRange(kept.Count, 1, EveryItem.Length - 1);
        Assert.Equal(EveryItem.Skip(EveryItem.Length - kept.Count), kept);
    });

    [Fact]
    public void TheItemsShowInTheirOrderAndFollowTheirState() => OnBar((_, controller) =>
    {
        string[] items = [BarItems.Sound, BarItems.Microphone, BarItems.Record, BarItems.KeepAwake, BarItems.Search];
        var bar = Bar(controller, InkStrip.Flat(0), "Notepad", ["File"], items, Status(laptop: false));
        bar.LayOut(1920);
        Assert.Equal(items, bar.ItemsShowing);
        // Each is named for what a press does now; the clock comes after them.
        Assert.Equal(["Volume 45%", "Mute microphone", "Screen recording", "Keep awake", "Search", "Notifications"], bar.Showing.TakeLast(6));
        bar.SetStates(Busy);
        bar.LayOut(1920);
        Assert.Equal(["Volume 45%", "Unmute microphone", "Stop recording", "Stop keep awake", "Search", "Notifications"], bar.Showing.TakeLast(6));
        // A new list replaces the old one.
        bar.SetItems([BarItems.Controls]);
        bar.LayOut(1920);
        Assert.Equal(["Controls", "Notifications"], bar.Showing.TakeLast(2));
        bar.SetItems([]);
        bar.LayOut(1920);
        Assert.Empty(bar.ItemsShowing);
        Assert.Equal("Notifications", bar.Showing[^1]);
    });

    [Fact]
    public void AnAppsOwnWindowMenuTakesTheBarsPlace() => OnBar((_, controller) =>
    {
        var bar = Bar(controller, InkStrip.Flat(0), "Notepad++", Menus, StartItems, Status(laptop: false));
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
        var bar = Bar(controller, new InkStrip(columns), "Notepad", ["File", "Edit"], StartItems, Status(laptop: false));
        bar.LayOut(1920);
        Assert.Equal((BarInk.Light, BarInk.Dark), bar.Inks);
    });

    [Fact]
    public void TheGlyphsSayWhatTheyStandFor() => OnBar((_, _) =>
    {
        var desktop = Status(laptop: false);
        var laptop = Status(laptop: true, muted: true, bars: 2);
        Assert.Equal("Volume 45%", MenuBarWindow.Tip(BarItems.Sound, default, desktop));
        Assert.Equal("Muted", MenuBarWindow.Tip(BarItems.Sound, default, laptop));
        Assert.Equal("Ethernet", MenuBarWindow.Tip(BarItems.Network, default, desktop));
        Assert.Equal("Wi-Fi", MenuBarWindow.Tip(BarItems.Network, default, laptop));
        Assert.Equal("Battery 64%", MenuBarWindow.Tip(BarItems.Battery, default, laptop));
        Assert.Equal("Capture region", MenuBarWindow.Tip(BarItems.Capture, default, null));
        Assert.Equal("System", MenuBarWindow.Tip(BarItems.System, default, desktop)); // no reading yet
        Assert.Equal("Hidden icons", MenuBarWindow.Tip(BarItems.HiddenIcons, default, null));
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
        string[] laptopStart = [.. BarItems.Start];
        var rows = new List<(string Name, double Width, InkStrip Ink, Brush Back, BarIslandFace? Island, string[] Items, BarStates States, bool Laptop, bool Muted, string App, string[] Menus)>
        {
            ("white wallpaper, the start set", 1920, InkStrip.Flat(0.99), white, Rest, StartItems, default, false, false, "Notepad++", Menus),
            ("white wallpaper, a song", 1920, InkStrip.Flat(0.99), white, Song, StartItems, default, false, false, "Notepad++", Menus),
            ("dark wallpaper, a song paused, Clipboard showing", 1920, InkStrip.Flat(0.1), dark, Song with { Tail = IslandTail.Paused, Mood = PetMood.Idle }, StartItems, default, false, false, "Notepad++", Menus),
            ("dark wallpaper, an agent waiting", 1920, InkStrip.Flat(0.1), dark, Waiting, StartItems, new BarStates(false, false, true, null), false, false, "Notepad++", Menus),
            ("dark wallpaper, recording, Keep awake on, an agent at work", 1920, InkStrip.Flat(0.1), dark, Recording, StartItems, new BarStates(false, true, true, "0:42"), false, false, "Notepad++", Menus),
            ("dark wallpaper, Keep awake", 1920, InkStrip.Flat(0.1), dark, Awake, StartItems, new BarStates(false, true, false, null), false, false, "Notepad++", Menus),
            ("dark left, light right, a song", 1920, splitStrip, split, Song, StartItems, default, false, false, "Paint", ["File", "Edit", "View", "Help"]),
            ("dark wallpaper, the start set on a laptop", 1920, InkStrip.Flat(0.1), dark, Rest, laptopStart, default, true, false, "Notepad++", Menus),
            ("dark wallpaper, every item at rest", 1920, InkStrip.Flat(0.1), dark, null, EveryItem, default, true, false, "Google Chrome", []),
            ("dark wallpaper, every item, all on", 1920, InkStrip.Flat(0.1), dark, null, EveryItem, Busy, true, true, "Google Chrome", []),
            ("white wallpaper, every item, all on", 1920, InkStrip.Flat(0.99), white, null, EveryItem, Busy, true, true, "Google Chrome", []),
            ("grey 0.49, muted, Wi-Fi 2 of 4", 1920, InkStrip.Flat(0.49), grey, null, [BarItems.Network, BarItems.Sound, BarItems.Microphone], new BarStates(true, false, false, null), true, true, "Google Chrome", []),
            ("dark left, light right", 1920, splitStrip, split, null, StartItems, default, false, false, "Paint", ["File", "Edit", "View", "Help"]),
            ("960 wide, the island, every item", 960, InkStrip.Flat(0.1), dark, Rest, EveryItem, Busy, true, false, "Notepad++", Menus),
            ("960 wide, a song, the start set", 960, InkStrip.Flat(0.1), dark, Song, StartItems, default, false, false, "Notepad++", Menus),
            ("640 wide, long name", 640, InkStrip.Flat(0.99), white, null, StartItems, default, false, false, "Steam Client WebHelper", ["File", "Edit"]),
            ("only the clock", 640, InkStrip.Flat(0.1), dark, null, [], default, false, false, "Notepad", ["File", "Edit"]),
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
                var bar = Bar(controller, row.Ink, row.App, row.Menus, row.Items, Status(row.Laptop, row.Muted, row.Muted ? 2 : null, cpu: 12), row.States);
                bar.SetIsland(row.Island, default);
                bar.SetPage(row.Name.Contains("Clipboard showing"), "clipboard");
                var root = bar.LayOut(row.Width);
                var layer = new DrawingVisual();
                using (var dc = layer.RenderOpen())
                {
                    dc.DrawRectangle(row.Back, null, new Rect(0, top, row.Width, 28));
                    dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, row.Width, 28) },
                        null, new Rect(0, top, row.Width, 28));
                }
                bitmap.Render(layer);
                if (scale == 1.0)
                    report.AppendLine($"{row.Name} ({row.Width}): left {bar.Groups.Left.Left:0}..{bar.Groups.Left.Right:0}, right {bar.Groups.Right.Left:0}..{bar.Groups.Right.Right:0}, inks {bar.Inks}, island {bar.IslandAt?.Left:0}..{bar.IslandAt?.Right:0} {bar.IslandInk}, lit {bar.Lit}")
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
