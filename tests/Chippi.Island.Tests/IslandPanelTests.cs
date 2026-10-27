// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.App.Pet;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

/// <summary>
/// The open island (DESIGN.md §5 Island) with fake tiles: the grid, pages, the last page and the orbit
/// buttons. The window sits far off every screen, so nothing shows on the desktop.
/// </summary>
public sealed class IslandPanelTests
{
    // A work area off any real monitor: the window is shown there, so it never appears on screen.
    private static readonly MonitorArea Offscreen = new(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);
    private static readonly Rect Tab = new(-20000 + 960 - 56, 0, 112, 32);

    internal static IslandTileContribution Part(string key, string name, int order, IslandOrbit orbit = IslandOrbit.None, double width = 328, string status = "")
    {
        var part = new IslandTileContribution(key, new IslandTileSpec(key, name, "Sliders", order, 0,
            () => new Border { Height = 120, Child = new TextBlock { Text = name } })
        {
            Orbit = orbit,
            PageWidth = width,
        });
        part.SetStatus(status);
        return part;
    }

    internal static IReadOnlyList<IslandTileGroup> Tiles(params IslandTileContribution[] parts) => IslandTileContribution.Group(parts);

    private static void WithPanel(Action<IslandPanelWindow> test, IReadOnlyList<IslandTileGroup>? tiles = null)
    {
        var panel = new IslandPanelWindow();
        try
        {
            panel.Initialize();
            panel.SetMedia(new FakeMedia());
            panel.SetTiles(tiles ?? Tiles(Part("keep-awake", "Keep awake", 40, IslandOrbit.Left), Part("sound.mixer", "Volume mixer", 10, IslandOrbit.Right, 344)));
            test(panel);
        }
        finally
        {
            panel.Close();
        }
    }

    private static void Open(IslandPanelWindow panel)
    {
        panel.Open(IslandEdge.Top, Tab, Offscreen);
        Pump(TimeSpan.FromMilliseconds(400));
    }

    private static IslandGrid Grid(IslandPanelWindow panel) =>
        Assert.IsType<IslandGrid>(((ContentControl)panel.FindName("Page")).Content);

    private static List<string> TileNames(IslandPanelWindow panel) =>
        Grid(panel).Buttons().Select(b => AutomationProperties.GetName(b)).ToList();

    private static Button Orbit(IslandPanelWindow panel, string name) =>
        ((Canvas)panel.FindName("Orbit")).Children.OfType<Button>().Single(b => b.Visibility == Visibility.Visible && AutomationProperties.GetName(b) == name);

    private static List<string> OrbitNames(IslandPanelWindow panel) =>
        ((Canvas)panel.FindName("Orbit")).Children.OfType<Button>().Where(b => b.Visibility == Visibility.Visible).Select(b => AutomationProperties.GetName(b)).ToList();

    [Fact]
    public void Opens_on_the_grid_with_now_playing_first_then_tiles_in_grid_order()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            Assert.Null(panel.CurrentPage);
            Assert.Equal(["Now Playing", "Volume mixer", "Keep awake"], TileNames(panel));
        }));
    }

    [Fact]
    public void Settings_order_and_hide_tiles_and_a_hidden_tile_keeps_its_page()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            var hidden = new HashSet<string> { IslandPanelWindow.NowPlayingKey };
            panel.SetLayout(IslandLayout.Default with { Order = ["keep-awake", "now-playing"], Hidden = hidden });
            Open(panel);
            Assert.Equal(["Keep awake", "Volume mixer"], TileNames(panel));
            panel.ShowPage(IslandPanelWindow.NowPlayingKey);
            Assert.Equal(IslandPanelWindow.NowPlayingKey, panel.CurrentPage);
        }));
    }

    [Fact]
    public void Settings_choose_the_round_buttons_and_can_turn_off_the_live_one()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            panel.SetLayout(IslandLayout.Default with { Left = "", Right = IslandPanelWindow.NowPlayingKey, Live = false });
            panel.SetActivities([new IslandActivity("track", IslandActivityKind.Media, "Song", "Player")], PetMood.Idle);
            Open(panel);
            Assert.Equal(["Settings", "Now Playing"], OrbitNames(panel)); // no left button, Now Playing on the right, no live button
            panel.SetLayout(IslandLayout.Default);
            Assert.Equal(["Keep awake", "Settings", "Volume mixer", "Now Playing"], OrbitNames(panel));
        }));
    }

    [Fact]
    public void With_all_features_chosen_it_opens_on_the_grid_even_with_a_page_remembered()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            panel.SetLastPage("keep-awake");
            panel.SetLayout(IslandLayout.Default with { OpensOn = IslandOpensOn.AllFeatures });
            Open(panel);
            Assert.Null(panel.CurrentPage);
            Assert.Contains("Back to Keep awake", OrbitNames(panel));
        }));
    }

    [Fact]
    public void Layout_sorts_listed_keys_first_and_the_rest_in_their_own_order()
    {
        var layout = IslandLayout.Default with { Order = ["c", "a"], Hidden = new HashSet<string> { "b" } };
        Assert.Equal(["c", "a", "b", "d"], layout.Sorted(["a", "b", "c", "d"]));
        Assert.Equal(["c", "a", "d"], layout.Shown(["a", "b", "c", "d"]));
        Assert.Equal("x", IslandLayout.Button(null, _ => true, "x"));
        Assert.Null(IslandLayout.Button("", _ => true, "x"));
        Assert.Null(IslandLayout.Button("gone", _ => false, "x"));
        Assert.Equal("y", IslandLayout.Button("y", _ => true, "x"));
    }

    [Fact]
    public void A_tile_shows_its_status_and_a_shared_tile_the_highest_priority_one()
    {
        var routing = Part("sound.microphone", "Microphone", 30, status: "Live");
        var mute = new IslandTileContribution("mic-mute", new IslandTileSpec("sound.microphone", "Microphone", "Mic", 30, 20, () => new Border()) { StatusPriority = 20 });
        mute.SetStatus("Muted");
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            Assert.Equal(["Now Playing", "Microphone, Muted"], TileNames(panel));
        }, Tiles(routing, mute)));
    }

    [Fact]
    public void A_tile_opens_its_page_at_its_width_and_is_remembered_for_the_next_open()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            string? saved = null;
            panel.LastPageChanged += (_, key) => saved = key;
            Open(panel);
            Grid(panel).Buttons().Single(b => (string)b.Tag == "sound.mixer").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.Equal("sound.mixer", panel.CurrentPage);
            Assert.Equal("sound.mixer", saved);
            Assert.Equal(344, ((FrameworkElement)panel.FindName("Shape")).Width, 3);

            panel.CloseIsland(immediate: true);
            Open(panel);
            Assert.Equal("sound.mixer", panel.CurrentPage);
        }));
    }

    [Fact]
    public void A_page_whose_feature_went_away_falls_back_to_the_grid_now_and_on_the_next_open()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            panel.SetLastPage("keep-awake");
            Open(panel);
            Assert.Equal("keep-awake", panel.CurrentPage);
            panel.SetTiles(Tiles(Part("sound.mixer", "Volume mixer", 10, IslandOrbit.Right)));
            Assert.Null(panel.CurrentPage);
            panel.CloseIsland(immediate: true);
            Open(panel);
            Assert.Null(panel.CurrentPage);
        }));
    }

    [Fact]
    public void Tiles_are_on_screen_only_while_the_open_grid_shows_them()
    {
        var monitor = Part("system-monitor", "System monitor", 60);
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Assert.False(monitor.OnScreen);
            Open(panel);
            Assert.True(monitor.OnScreen);
            panel.ShowPage("system-monitor");
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.False(monitor.OnScreen);
            panel.ShowPage(null);
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.True(monitor.OnScreen);
            panel.CloseIsland(immediate: true);
            Assert.False(monitor.OnScreen);
        }, Tiles(monitor, Part("keep-awake", "Keep awake", 40))));
    }

    [Fact]
    public void A_notice_opens_its_page_once_and_the_header_shows_the_page_note()
    {
        var monitor = Part("system-monitor", "System monitor", 60, width: 640, status: "CPU 12%");
        monitor.SetPageNote("Last 4 min");
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            panel.SetLastPage("keep-awake");
            panel.OpenOn("system-monitor");
            Open(panel);
            Assert.Equal("system-monitor", panel.CurrentPage);
            var texts = Texts((ContentControl)panel.FindName("Page"));
            Assert.Contains("Last 4 min", texts);
            Assert.DoesNotContain("CPU 12%", texts);

            panel.CloseIsland(immediate: true);
            Open(panel);
            Assert.Equal("keep-awake", panel.CurrentPage);
        }, Tiles(monitor, Part("keep-awake", "Keep awake", 40))));
    }

    private static List<string> Texts(DependencyObject root)
    {
        var texts = new List<string>();
        if (root is TextBlock { Text.Length: > 0 } text)
            texts.Add(text.Text);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            texts.AddRange(Texts(child));
        return texts;
    }

    [Fact]
    public void Orbit_buttons_follow_installed_features_and_back_returns_to_the_remembered_page()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            Assert.Equal(["Keep awake", "Settings", "Volume mixer"], OrbitNames(panel)); // nothing remembered: no Back

            Orbit(panel, "Keep awake").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.Equal("keep-awake", panel.CurrentPage);
            Assert.Equal("Active", Orbit(panel, "Keep awake").Tag);
            Orbit(panel, "All features").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.Null(panel.CurrentPage);
            Orbit(panel, "Back to Keep awake").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.Equal("keep-awake", panel.CurrentPage);
        }, Tiles(Part("keep-awake", "Keep awake", 40, IslandOrbit.Left), Part("sound.mixer", "Volume mixer", 10, IslandOrbit.Right))));
    }

    [Fact]
    public void Without_those_features_only_settings_orbits()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            Assert.Equal(["Settings"], OrbitNames(panel));
        }, Tiles(Part("downloads", "File organizer", 50))));
    }

    [Fact]
    public void The_live_button_opens_now_playing_for_music_and_the_microphone_when_muted()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            var muted = new IslandActivity("muted", IslandActivityKind.Status, "Microphone", "Muted") { NeedsAttention = true, TileKey = "sound.microphone" };
            panel.SetActivities([muted], PetMood.Idle);
            Assert.Contains("Microphone", OrbitNames(panel));
            Assert.Contains("Microphone, Muted", TileNames(panel));

            var music = new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify");
            panel.SetActivities([music, muted], PetMood.Idle);
            Assert.Contains("Now Playing", OrbitNames(panel));
            Assert.DoesNotContain("Microphone", OrbitNames(panel));
            Orbit(panel, "Now Playing").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.Equal(IslandPanelWindow.NowPlayingKey, panel.CurrentPage);
            Assert.Equal(440, ((FrameworkElement)panel.FindName("Shape")).Width, 3);
        }, Tiles(Part("sound.microphone", "Microphone", 30, status: "Muted"))));
    }

    [Fact]
    public void The_microphone_tile_shows_mic_off_only_while_the_mute_shortcut_has_it_muted()
    {
        var mute = new IslandTileContribution("mic-mute", new IslandTileSpec("sound.microphone", "Microphone", "Mic", 30, 20, () => new Border()) { StatusPriority = 100 });
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            Assert.Same(LucideIcons.Mic, TileIcon(panel, "sound.microphone"));

            var stopped = new IslandActivity("stopped", IslandActivityKind.Status, "Stopped", "Microphone") { NeedsAttention = true, TileKey = "sound.microphone", ModuleId = "mic-processing" };
            panel.SetActivities([stopped], PetMood.Idle);
            Assert.Same(LucideIcons.Mic, TileIcon(panel, "sound.microphone"));

            var muted = new IslandActivity("muted", IslandActivityKind.Status, "Muted", "Microphone") { NeedsAttention = true, TileKey = "sound.microphone", ModuleId = "mic-mute" };
            panel.SetActivities([stopped, muted], PetMood.Idle);
            Assert.Same(LucideIcons.MicOff, TileIcon(panel, "sound.microphone"));
            Assert.Same(panel.FindResource("WarningBrush"), TileGlyph(panel, "sound.microphone").Brush);

            panel.SetActivities([], PetMood.Idle);
            Assert.Same(LucideIcons.Mic, TileIcon(panel, "sound.microphone"));
        }, Tiles(mute)));
    }

    private static Geometry TileIcon(IslandPanelWindow panel, string key) => TileGlyph(panel, key).Data!;

    private static LucideIcon TileGlyph(IslandPanelWindow panel, string key)
    {
        var tile = Grid(panel).Buttons().Single(b => (string)b.Tag == key);
        tile.ApplyTemplate();
        return Descendants(tile).OfType<LucideIcon>().Single();
    }

    private static IEnumerable<object> Descendants(object parent) =>
        parent is DependencyObject d ? LogicalTreeHelper.GetChildren(d).Cast<object>().SelectMany(c => Descendants(c).Prepend(c)) : [];

    [Fact]
    public void The_orbit_buttons_count_as_the_island_for_outside_clicks()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            var shape = (FrameworkElement)panel.FindName("Shape");
            var window = IslandNative.WindowRect(panel.Handle);
            double left = window.X + Canvas.GetLeft(shape), top = window.Y + Canvas.GetTop(shape);
            Assert.True(panel.Covers(new Point(left + 10, top + 10)));
            Assert.True(panel.Covers(new Point(left - 12 - 20, top + 60 + 20))); // Keep awake's center, under Back's place
            Assert.False(panel.Covers(new Point(left - 12 - 20, top + 12 + 20))); // Back's place, empty on the grid with nothing remembered
            Assert.False(panel.Covers(new Point(left - 12 - 20, top + 12 + 48 * 3 + 20))); // below that column
        }));
    }

    [Fact]
    public void The_shape_grows_from_the_tab_and_ends_at_the_page()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            panel.Open(IslandEdge.Top, Tab, Offscreen);
            var shape = (FrameworkElement)panel.FindName("Shape");
            if (!Chippi.UI.Themes.Motion.IsReduced)
                Assert.Equal(Tab.Width, shape.Width, 3); // the first frame is the tab
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.Equal(328, shape.Width, 3);
            Assert.Equal(1, ((FrameworkElement)panel.FindName("Card")).Opacity);
        }));
    }

    // Karl's S6 review: music starting after the island opened near the bottom of a side edge put the
    // new live button below the work area (1076 against 1032).
    [Fact]
    public void A_live_button_that_appears_after_opening_stays_in_the_work_area()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            var lowTab = new Rect(-20000, 1032 - 76 - 68, 36, 136); // a Left tab at the bottom end margin
            panel.SetLastPage("keep-awake");
            panel.Open(IslandEdge.Left, lowTab, Offscreen);
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.DoesNotContain("Now Playing", OrbitNames(panel));

            panel.SetActivities([new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify")], PetMood.Idle);
            Pump(TimeSpan.FromMilliseconds(100));

            var live = Orbit(panel, "Now Playing");
            var window = IslandNative.WindowRect(panel.Handle);
            double bottom = window.Y + Canvas.GetTop(live) + live.Height;
            Assert.True(bottom <= 1032 - 8, $"live button bottom {bottom}");
        }, Tiles(Part("keep-awake", "Keep awake", 40)))); // no feature buttons, so only the live one reaches below
    }

    // Karl's S6 review: play/pause enabled for Play or Pause alone but always sent Toggle.
    [Theory]
    [InlineData(true, false, true, false, PlayPauseCommand.Pause)] // playing, Pause only
    [InlineData(true, true, false, false, PlayPauseCommand.None)] // playing, Play only: nothing to send
    [InlineData(true, false, false, true, PlayPauseCommand.Toggle)]
    [InlineData(false, true, false, false, PlayPauseCommand.Play)]
    [InlineData(false, false, true, false, PlayPauseCommand.None)] // paused, Pause only
    [InlineData(false, false, false, true, PlayPauseCommand.Toggle)]
    [InlineData(true, true, true, true, PlayPauseCommand.Pause)] // the specific command wins over Toggle
    public void Play_pause_sends_what_the_app_supports_for_its_state(bool playing, bool play, bool pause, bool toggle, PlayPauseCommand expected)
    {
        var track = new MediaTrack("App", "App", "Title", "", playing, false, play, pause, toggle, false, TimeSpan.Zero, TimeSpan.Zero, default, null);
        Assert.Equal(expected, track.PlayPause);
    }

    internal static void Pump(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        do
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            if (DateTime.UtcNow < until)
                Thread.Sleep(10);
        }
        while (DateTime.UtcNow < until);
    }

    internal sealed class FakeMedia : IMediaSessions
    {
        public IReadOnlyList<MediaTrack> Sessions { get; set; } = [];

        public MediaTrack? Current => Sessions.FirstOrDefault();

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        public void Select(string? id) { }

        public Task PreviousAsync() => Task.CompletedTask;

        public Task PlayPauseAsync() => Task.CompletedTask;

        public Task NextAsync() => Task.CompletedTask;

        public void Dispose() { }
    }
}
