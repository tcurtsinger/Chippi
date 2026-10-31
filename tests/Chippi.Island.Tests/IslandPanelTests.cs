// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.UI.Pet;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

/// <summary>
/// The open island (DESIGN.md §5 Island) with fake tiles: the grid, pages, the last page and the back arrow
/// buttons. The window sits far off every screen, so nothing shows on the desktop.
/// </summary>
public sealed class IslandPanelTests
{
    // A work area off any real monitor: the window is shown there, so it never appears on screen.
    private static readonly MonitorArea Offscreen = new(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);
    private static readonly Rect Puck = IslandGeometry.Start(Offscreen.Work, 1);

    internal static IslandTileContribution Part(string key, string name, int order, string status = "")
    {
        var part = new IslandTileContribution(key, new IslandTileSpec(key, name, "Sliders", order, 0,
            () => new Border { Height = 120, Child = new TextBlock { Text = name } }));
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
            panel.SetTiles(tiles ?? Tiles(Part("keep-awake", "Keep awake", 40), Part("sound.mixer", "Volume mixer", 10)));
            test(panel);
        }
        finally
        {
            panel.Close();
        }
    }

    private static void Open(IslandPanelWindow panel)
    {
        panel.Open(Puck, Offscreen);
        IslandWindowTests.Settle(panel);
    }

    private static IslandGrid Grid(IslandPanelWindow panel) =>
        Assert.IsType<IslandGrid>(((ContentControl)panel.FindName("Page")).Content);

    private static List<string> TileNames(IslandPanelWindow panel) =>
        Grid(panel).Buttons().Select(b => AutomationProperties.GetName(b)).ToList();

    private static Button Back(IslandPanelWindow panel) =>
        Descendants((FrameworkElement)panel.FindName("Card")).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Back");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private sealed class UnshownSource : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }

    /// <summary>The shape as drawn.</summary>
    private static (double Width, double Height) Size(IslandPanelWindow panel)
    {
        var shape = (FrameworkElement)panel.FindName("Shape");
        return (Math.Round(shape.Width, 3), Math.Round(shape.Height, 3));
    }

    private static Rect Drawn(IslandPanelWindow panel)
    {
        var shape = (FrameworkElement)panel.FindName("Shape");
        return new Rect(Canvas.GetLeft(shape), Canvas.GetTop(shape), shape.Width, shape.Height);
    }

    [Fact]
    public void The_grid_and_every_page_share_one_size_and_a_page_change_never_moves_the_shape()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            var grid = Drawn(panel);
            Assert.Equal(new Rect(grid.X, grid.Y, 520, 360), grid);
            foreach (var key in new[] { "keep-awake", IslandPanelWindow.NowPlayingKey, "sound.mixer" })
            {
                panel.ShowPage(key);
                if (!Chippi.UI.Themes.Motion.IsReduced)
                    Assert.Equal(grid, Drawn(panel)); // the first frame of the change: the same shape, no morph
                IslandWindowTests.Settle(panel);
                Assert.Equal(grid, Drawn(panel));
                Assert.Equal(1, ((FrameworkElement)panel.FindName("Card")).Opacity);
                if (key != IslandPanelWindow.NowPlayingKey)
                {
                    var body = Assert.IsAssignableFrom<FrameworkElement>(panel.PageBody);
                    Assert.Equal((496, 304), (Math.Round(body.ActualWidth, 3), Math.Round(body.ActualHeight, 3)));
                }
            }
            panel.ShowPage(null, remember: false);
            IslandWindowTests.Settle(panel);
            Assert.Equal(grid, Drawn(panel));
        }));
    }

    [Fact]
    public void A_page_change_fades_the_new_page_in()
    {
        if (Chippi.UI.Themes.Motion.IsReduced)
            return; // reduced motion snaps
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            panel.ShowPage("keep-awake");
            var card = (FrameworkElement)panel.FindName("Card");
            Assert.Equal(0, card.Opacity, 3);
            Assert.False(card.IsHitTestVisible); // no click while it can't be seen
            IslandWindowTests.Settle(panel);
            Assert.Equal(1, card.Opacity);
            Assert.True(card.IsHitTestVisible);
        }));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 2)]
    [InlineData(12, 2)] // a third row wouldn't fit: only eight show
    public void The_home_lays_its_buttons_four_across_and_shows_at_most_eight(int count, int rows)
    {
        WpfTestThread.Run(() =>
        {
            var grid = new IslandGrid();
            grid.SetTiles([.. Enumerable.Range(0, count).Select(i => new IslandTileView($"t{i}", $"Tile {i}", Chippi.UI.Controls.LucideIcons.Music, "Status"))]);
            grid.SetVolume(new IslandTileVolume(() => 0.5, _ => { }), "sound");
            double width = IslandPage.Width - 2 * IslandPage.Inset, height = IslandPage.Height - 2 * IslandPage.Inset;
            grid.Measure(new Size(width, height));
            grid.Arrange(new Rect(0, 0, width, height));
            var buttons = grid.Buttons().ToList();
            Assert.Equal(Math.Min(count, 8), buttons.Count);
            Assert.All(buttons, b => Assert.Equal(44, b.ActualHeight, 2));
            var at = buttons.Select(b => b.TranslatePoint(new Point(), grid)).ToList();
            Assert.Equal(rows, at.Select(p => Math.Round(p.Y, 1)).Distinct().Count());
            Assert.Equal(Math.Min(count, 4), at.Select(p => Math.Round(p.X, 1)).Distinct().Count());
            // The rows, the card, the slider and the footer all fit the panel.
            Assert.True(grid.DesiredSize.Height <= height + 0.01, $"home is {grid.DesiredSize.Height} tall");
            var last = buttons[^1].TranslatePoint(new Point(0, 44), grid);
            Assert.True(last.Y <= height - 46 - 32, $"buttons end at {last.Y}");
        });
    }

    [Fact]
    public void Opens_on_the_home_with_the_tiles_in_grid_order_and_now_playing_as_the_card()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            Assert.Null(panel.CurrentPage);
            Assert.Equal(["Volume mixer", "Keep awake"], TileNames(panel));
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
    public void With_all_features_chosen_it_opens_on_the_grid_even_with_a_page_remembered()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            panel.SetLastPage("keep-awake");
            panel.SetLayout(IslandLayout.Default with { OpensOn = IslandOpensOn.AllFeatures });
            Open(panel);
            Assert.Null(panel.CurrentPage);
        }));
    }

    [Fact]
    public void Layout_sorts_listed_keys_first_and_the_rest_in_their_own_order()
    {
        var layout = IslandLayout.Default with { Order = ["c", "a"], Hidden = new HashSet<string> { "b" } };
        Assert.Equal(["c", "a", "b", "d"], layout.Sorted(["a", "b", "c", "d"]));
        Assert.Equal(["c", "a", "d"], layout.Shown(["a", "b", "c", "d"]));
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
            Assert.Equal(["Microphone, Muted"], TileNames(panel));
        }, Tiles(routing, mute)));
    }

    [Fact]
    public void A_tile_opens_its_page_at_the_one_size_and_is_remembered_for_the_next_open()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            string? saved = null;
            panel.LastPageChanged += (_, key) => saved = key;
            Open(panel);
            Grid(panel).Buttons().Single(b => (string)b.Tag == "sound.mixer").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            IslandWindowTests.Settle(panel);
            Assert.Equal("sound.mixer", panel.CurrentPage);
            Assert.Equal("sound.mixer", saved);
            Assert.Equal((520, 360), Size(panel));

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
            panel.SetTiles(Tiles(Part("sound.mixer", "Volume mixer", 10)));
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
            IslandWindowTests.Settle(panel);
            Assert.False(monitor.OnScreen);
            panel.ShowPage(null);
            IslandWindowTests.Settle(panel);
            Assert.True(monitor.OnScreen);
            panel.CloseIsland(immediate: true);
            Assert.False(monitor.OnScreen);
        }, Tiles(monitor, Part("keep-awake", "Keep awake", 40))));
    }

    [Fact]
    public void A_notice_opens_its_page_once_and_the_header_shows_the_page_note()
    {
        var monitor = Part("system-monitor", "System monitor", 60, status: "CPU 12%");
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
    public void Every_page_has_a_back_arrow_to_the_grid_and_the_grid_has_none()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            Assert.DoesNotContain(Descendants((FrameworkElement)panel.FindName("Card")).OfType<Button>(), b => AutomationProperties.GetName(b) == "Back");
            foreach (var key in new[] { "keep-awake", IslandPanelWindow.NowPlayingKey, "sound.mixer" })
            {
                panel.ShowPage(key);
                IslandWindowTests.Settle(panel);
                Assert.Equal(key, panel.CurrentPage);
                var card = (FrameworkElement)panel.FindName("Card");
                card.Measure(new Size(IslandPage.Width, IslandPage.Height));
                card.Arrange(new Rect(0, 0, IslandPage.Width, IslandPage.Height));
                var back = Back(panel);
                Assert.Equal(IslandPage.HeaderHeight, back.ActualHeight);
                Click(back);
                IslandWindowTests.Settle(panel);
                Assert.Null(panel.CurrentPage);
            }
        }));
    }

    [Fact]
    public void Esc_closes_the_island_from_the_grid_and_from_a_page()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            int closes = 0;
            panel.EscapePressed += (_, _) => closes++;
            void Esc() => panel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new UnshownSource(), 0, Key.Escape) { RoutedEvent = UIElement.PreviewKeyDownEvent });
            Esc();
            Assert.Equal(1, closes);
            panel.ShowPage("keep-awake");
            IslandWindowTests.Settle(panel);
            Esc();
            Assert.Equal(2, closes);
        }));
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
            panel.SetActivities([stopped]);
            Assert.Same(LucideIcons.Mic, TileIcon(panel, "sound.microphone"));

            var muted = new IslandActivity("muted", IslandActivityKind.Status, "Muted", "Microphone") { NeedsAttention = true, TileKey = "sound.microphone", ModuleId = "mic-mute" };
            panel.SetActivities([stopped, muted]);
            Assert.Same(LucideIcons.MicOff, TileIcon(panel, "sound.microphone"));
            Assert.Same(panel.FindResource("WarningBrush"), TileGlyph(panel, "sound.microphone").Brush);

            panel.SetActivities([]);
            Assert.Same(LucideIcons.Mic, TileIcon(panel, "sound.microphone"));
        }, Tiles(mute)));
    }

    [Fact]
    public void A_muted_mic_shows_as_needing_attention_not_as_on_and_a_toggle_that_fails_says_so()
    {
        bool muted = true, fail = false;
        var mute = new IslandTileContribution("mic-mute", new IslandTileSpec("sound", "Sound", "AudioLines", 10, 0, () => new Border())
        {
            StatusPriority = 100,
            Toggle = new IslandTileToggle(() => muted, () => fail ? Task.FromException(new InvalidOperationException("No microphone")) : Task.CompletedTask,
                "Microphone", "Mic", () => muted ? "Muted" : "Live"),
        });
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            var activity = new IslandActivity("muted", IslandActivityKind.Status, "Muted", "Microphone") { NeedsAttention = true, TileKey = "sound", ModuleId = "mic-mute" };
            panel.SetActivities([activity]);
            // The accent says on and fine: muted, the button keeps its plain fill and the glyph is MicOff in Caution.
            var toggle = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(Grid(panel).Buttons().Single(b => (string)b.Tag == "sound"));
            Assert.False(toggle.IsChecked);
            Assert.Same(LucideIcons.MicOff, TileIcon(panel, "sound"));
            Assert.Same(panel.FindResource("WarningBrush"), TileGlyph(panel, "sound").Brush);
            Assert.Contains(Descendants(Grid(panel)).OfType<Button>(), b => AutomationProperties.GetName(b) == "Open Sound"); // its arrow opens Sound

            // A toggle that fails says "Failed" under its button.
            muted = false;
            fail = true;
            panel.SetActivities([]);
            toggle = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(Grid(panel).Buttons().Single(b => (string)b.Tag == "sound"));
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(TimeSpan.FromMilliseconds(50));
            Assert.Contains(Descendants(Grid(panel)).OfType<TextBlock>(), t => t.Text == "Failed");
        }, Tiles(mute)));
    }

    [Fact]
    public void Stopped_mic_processing_says_mic_stopped_on_the_home_and_a_muted_mic_still_says_muted()
    {
        bool muted = false;
        var mute = new IslandTileContribution("mic-mute", new IslandTileSpec("sound", "Sound", "AudioLines", 10, 10, () => new Border())
        {
            StatusPriority = 100,
            Toggle = new IslandTileToggle(() => muted, () => Task.CompletedTask, "Microphone", "Mic", () => muted ? "Muted" : "Live"),
        });
        var processing = new IslandTileContribution("mic-processing", new IslandTileSpec("sound", "Sound", "AudioLines", 10, 30, () => new Border()) { StatusPriority = 10 });
        processing.SetStatus("Mic stopped");
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            string[] Texts() => [.. Descendants(Grid(panel)).OfType<TextBlock>().Select(t => t.Text)];
            var stopped = new IslandActivity("stopped", IslandActivityKind.Status, "Mic processing stopped", "Microphone") { NeedsAttention = true, TileKey = "sound", ModuleId = "mic-processing" };
            panel.SetActivities([stopped]);
            Assert.Contains("Mic stopped", Texts()); // why it needs attention, not a Caution "Live"
            Assert.DoesNotContain("Live", Texts());

            muted = true;
            panel.SetActivities([stopped, new IslandActivity("muted", IslandActivityKind.Status, "Muted", "Microphone") { NeedsAttention = true, TileKey = "sound", ModuleId = "mic-mute" }]);
            Assert.Contains("Muted", Texts());
        }, Tiles(mute, processing)));
    }

    [Fact]
    public void A_feature_whose_state_throws_shows_its_button_plain_and_the_others_still_refresh()
    {
        bool broken = true;
        var awake = new IslandTileContribution("keep-awake", new IslandTileSpec("keep-awake", "Keep awake", "Coffee", 40, 0, () => new Border())
        {
            Toggle = new IslandTileToggle(() => broken ? throw new ObjectDisposedException("keep awake") : true, () => Task.CompletedTask,
                Status: () => broken ? throw new ObjectDisposedException("keep awake") : "1 hr left"),
        });
        var clipboard = Part("clipboard", "Clipboard", 70, "23 items");
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            Assert.Equal(["Keep awake", "Clipboard, 23 items"], TileNames(panel));
            var toggle = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(Grid(panel).Buttons().First());
            Assert.False(toggle.IsChecked);

            clipboard.SetStatus("24 items");
            Pump(TimeSpan.FromMilliseconds(50));
            Assert.Equal(["Keep awake", "Clipboard, 24 items"], TileNames(panel)); // the others still refresh

            broken = false; // it reads again
            clipboard.SetStatus("25 items");
            Pump(TimeSpan.FromMilliseconds(50));
            Assert.Equal(["Keep awake, 1 hr left", "Clipboard, 25 items"], TileNames(panel));
            Assert.True(toggle.IsChecked);
        }, Tiles(awake, clipboard)));
    }

    [Fact]
    public void A_second_press_on_a_toggle_waits_for_the_first_to_be_applied()
    {
        var applying = new TaskCompletionSource();
        int toggles = 0;
        var awake = new IslandTileContribution("keep-awake", new IslandTileSpec("keep-awake", "Keep awake", "Coffee", 40, 0, () => new Border())
        {
            Toggle = new IslandTileToggle(() => false, () =>
            {
                toggles++;
                return applying.Task;
            }),
        });
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            var toggle = Grid(panel).Buttons().Single(b => (string)b.Tag == "keep-awake");
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); // a double click
            Assert.Equal(1, toggles);

            applying.SetResult();
            Pump(TimeSpan.FromMilliseconds(50));
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(2, toggles); // applied: the next press toggles again
        }, Tiles(awake)));
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
    public void The_shape_counts_as_the_island_for_outside_clicks()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            Open(panel);
            var shape = (FrameworkElement)panel.FindName("Shape");
            var window = IslandNative.WindowRect(panel.Handle);
            double left = window.X + Canvas.GetLeft(shape), top = window.Y + Canvas.GetTop(shape);
            Assert.True(panel.Covers(new Point(left + 10, top + 10)));
            Assert.False(panel.Covers(new Point(left - 10, top + 183)));
            Assert.False(panel.Covers(new Point(left + 530, top + 183)));
        }));
    }

    [Fact]
    public void The_shape_grows_from_the_puck_and_ends_at_the_page()
    {
        WpfTestThread.Run(() => WithPanel(panel =>
        {
            panel.Open(Puck, Offscreen);
            var shape = (FrameworkElement)panel.FindName("Shape");
            if (!Chippi.UI.Themes.Motion.IsReduced)
                Assert.Equal(Puck.Width, shape.Width, 3); // the first frame is the puck
            IslandWindowTests.Settle(panel);
            Assert.Equal((520, 360), Size(panel));
            Assert.Equal(1, ((FrameworkElement)panel.FindName("Card")).Opacity);
        }));
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

        /// <summary>The player declines every command, as SystemMediaSessions reports it: the task faults.</summary>
        public bool Refuse { get; set; }

        private Task Command() => Refuse ? Task.FromException(new InvalidOperationException("declined")) : Task.CompletedTask;

        public Task PreviousAsync() => Command();

        public Task PlayPauseAsync() => Command();

        public Task NextAsync() => Command();

        public void Dispose() { }
    }
}
