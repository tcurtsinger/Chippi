// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.App.Island;
using Chippi.Core.Island;

namespace Chippi.Island.Tests;

/// <summary>The widget's window itself, built on its own UI thread with the app's resources. Run fullscreen-check first: most show it.</summary>
public sealed class IslandWindowTests
{
    private static Chippi.App.Island.Media.MediaTrack Track(string title, bool playing = true) =>
        new("spotify", "Spotify", title, "Radiohead", playing, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(4), DateTimeOffset.Now, null);

    private static readonly IslandActivity Playing = new("track", IslandActivityKind.Media, "Everything", "Spotify") { TileKey = IslandPanelWindow.NowPlayingKey };

    [Fact]
    public void Esc_closes_the_panel_without_throwing_on_the_dispatcher()
    {
        Exception? thrown = null;
        WpfTestThread.Run(() =>
        {
            var window = new IslandWindow();
            DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
            {
                thrown = e.Exception;
                e.Handled = true; // as App does, which is why it went unnoticed
            };
            Dispatcher.CurrentDispatcher.UnhandledException += handler;
            try
            {
                window.Escape();
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
                Dispatcher.PushFrame(frame);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.UnhandledException -= handler;
                window.Close();
            }
        });
        Assert.Null(thrown);
    }

    [Fact]
    public void Hovering_brightens_the_rim_and_wakes_the_blob_but_never_grows_the_widget()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Assert.Equal(1, window.PuckScaleTarget);
            Assert.Equal(Chippi.UI.Pet.PetMood.Sleepy, window.Pet.Mood);
            Hover(window, Mouse.MouseEnterEvent);
            Assert.Equal(1, window.PuckScaleTarget);
            Assert.Equal(50, window.PuckButton.Width);
            Assert.IsType<System.Windows.Media.DrawingBrush>(window.Body.BorderBrush); // the rim blends over 83ms, like Windows' own controls
            Pump(TimeSpan.FromMilliseconds(200));
            Assert.Same(window.FindResource("IslandWidgetHoverBorderBrush"), window.Body.BorderBrush);
            Assert.Equal(Chippi.UI.Pet.PetMood.Idle, window.Pet.Mood);
            Hover(window, Mouse.MouseLeaveEvent);
            Pump(TimeSpan.FromMilliseconds(200));
            Assert.Same(window.FindResource("IslandWidgetBorderBrush"), window.Body.BorderBrush);
            Assert.Equal(Chippi.UI.Pet.PetMood.Sleepy, window.Pet.Mood);
        }));
    }

    [Fact]
    public void Music_playing_turns_the_puck_into_the_bar_scrolling_the_title_only_when_it_does_not_fit()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var media = new IslandPanelTests.FakeMedia { Sessions = [Track("Everything In Its Right Place (Live at the Ancient Theatre)")] };
            window.SetMedia(media);
            window.SetActivities([Playing], Chippi.UI.Pet.PetMood.Idle);
            Assert.True(window.BarShown);
            Pump(TimeSpan.FromMilliseconds(400)); // the unfold
            Assert.Equal(IslandGeometry.BarWidth, window.PuckButton.ActualWidth, 1); // one width, whatever the title
            Assert.Equal(Visibility.Visible, window.Cover.Visibility);
            Assert.Equal(Visibility.Visible, window.Equalizer.Visibility);
            Assert.Equal("Radiohead", window.Detail.Text); // the artist under the title
            Assert.Equal(123, window.Song.Width); // too long: held to its 123
            Assert.Equal(window.Lines.ActualWidth, window.Song.Width, 1); // its column, so the fade's last 8 aren't cut off
            if (!Chippi.UI.Themes.Motion.IsReduced)
            {
                Assert.Equal(Visibility.Visible, window.SongCopy.Visibility); // and a second copy follows it round
                var first = ((System.Windows.Media.LinearGradientBrush)window.Song.OpacityMask).GradientStops[0];
                Assert.Equal(255, first.Color.A); // held at the start, its first letter is whole: the fade opens as it moves
            }

            // Hovering doesn't start the song over (a restart sets a new fade).
            var fade = window.Song.OpacityMask;
            Hover(window, Mouse.MouseEnterEvent);
            Assert.Same(fade, window.Song.OpacityMask);
            Hover(window, Mouse.MouseLeaveEvent);
            Assert.Same(fade, window.Song.OpacityMask);

            // With the panel out the bar keeps its song for the fold back, but stops scrolling; closed, it scrolls again.
            Click(window);
            Assert.True(window.IsIslandOpen);
            Assert.Equal(Visibility.Collapsed, window.SongCopy.Visibility);
            window.Escape();
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.False(window.IsIslandOpen);
            if (!Chippi.UI.Themes.Motion.IsReduced)
                Assert.Equal(Visibility.Visible, window.SongCopy.Visibility);

            media.Sessions = [Track("Creep")];
            media.Raise();
            Assert.Equal(Visibility.Collapsed, window.SongCopy.Visibility); // fits: holds still
            Assert.Equal(Math.Ceiling(window.SongText.DesiredSize.Width), window.Song.Width);
            Assert.Null(window.Song.OpacityMask);
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal(IslandGeometry.BarWidth, window.PuckButton.ActualWidth, 1); // still the same bar

            window.SetActivities([], Chippi.UI.Pet.PetMood.Idle); // another app took over: the song goes
            Assert.False(window.BarShown);
        }));
    }

    [Fact]
    public void On_the_left_the_bar_mirrors_so_its_two_circles_sit_in_its_round_ends()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.SetMedia(new IslandPanelTests.FakeMedia { Sessions = [Track("Creep")] });
            window.SetActivities([Playing], Chippi.UI.Pet.PetMood.Idle);
            foreach (var (x, chippiRight) in new[] { ((double?)null, true), (0.0, false) })
            {
                window.SetPlacement(x, x is null ? null : 1);
                Assert.Equal(chippiRight, window.OpensLeft); // a puck at the right grows its bar to the left
                Pump(TimeSpan.FromMilliseconds(400)); // the unfold
                double Left(FrameworkElement part) => part.TranslatePoint(default, window.PuckButton).X;
                double bar = window.PuckButton.ActualWidth;
                // The cover 8 in from the far round end, Chippi at the other, the bars beside Chippi, the lines between.
                double cover = chippiRight ? Left(window.Cover) : bar - Left(window.Cover) - window.Cover.ActualWidth;
                Assert.Equal(8, cover, 1);
                Assert.True(chippiRight
                    ? Left(window.Cover) < Left(window.Lines) && Left(window.Lines) < Left(window.Equalizer) && Left(window.Equalizer) < Left(window.PuckEnd)
                    : Left(window.PuckEnd) < Left(window.Equalizer) && Left(window.Equalizer) < Left(window.Lines) && Left(window.Lines) < Left(window.Cover));
                Assert.Equal(123, window.Lines.ActualWidth, 1); // the same room either way
            }
        }));
    }

    [Fact]
    public void A_paused_song_keeps_the_bar_with_a_pause_mark_until_the_hold_runs_out()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var timers = new List<(TimeSpan Interval, Action Tick)>();
            window.CreateTimer = (interval, tick) => { timers.Add((interval, tick)); return new StillTimer(); };
            var media = new IslandPanelTests.FakeMedia { Sessions = [Track("Creep")] };
            window.SetMedia(media);
            window.SetActivities([Playing], Chippi.UI.Pet.PetMood.Idle);
            Assert.Equal(Chippi.UI.Pet.PetMood.Listening, window.Pet.Mood);

            media.Sessions = [Track("Creep", playing: false)];
            window.SetActivities([], Chippi.UI.Pet.PetMood.Sleepy); // paused: the activity goes
            Assert.True(window.BarShown);
            Assert.Equal(Visibility.Visible, window.PausedMark.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Equalizer.Visibility);
            Assert.Equal(Chippi.UI.Pet.PetMood.Idle, window.Pet.Mood); // the headphones come off

            var hold = Assert.Single(timers, t => t.Interval == IslandWindow.PauseHold);
            hold.Tick();
            Assert.False(window.BarShown); // folded back into the puck
        }));
    }

    [Fact]
    public void The_widget_is_held_at_the_pucks_outer_side_so_the_bar_grows_toward_the_middle()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var area = window.CurrentArea!.Value;
            Assert.Equal(IslandGeometry.Start(area.Work, area.Scale), window.PuckRect); // where it starts
            Assert.True(window.OpensLeft);
            Assert.True(double.IsNaN(Canvas.GetLeft(window.PuckButton))); // held by its right side
            AssertPuckOnScreen(window);

            window.SetPlacement(0, 0); // the top-left corner
            Assert.Equal(IslandGeometry.PuckRect(0, 0, area.Work, area.Scale), window.PuckRect);
            Assert.False(window.OpensLeft);
            Assert.True(double.IsNaN(Canvas.GetRight(window.PuckButton))); // held by its left side
            AssertPuckOnScreen(window);

            window.SetPlacement(null, null);
            Assert.Equal(IslandGeometry.Start(area.Work, area.Scale), window.PuckRect);
        }));
    }

    [Fact]
    public void While_its_monitor_is_disconnected_it_starts_at_the_main_displays_bottom_right()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var area = window.CurrentArea!.Value;
            window.SetPlacement(0, 0);
            window.SetMonitor(area.Handle, fallback: true);
            Assert.Equal(IslandGeometry.Start(area.Work, area.Scale), window.PuckRect);
            window.SetMonitor(area.Handle, fallback: false);
            Assert.Equal(IslandGeometry.PuckRect(0, 0, area.Work, area.Scale), window.PuckRect);
        }));
    }

    [Fact]
    public void Hovering_never_opens_the_panel()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Hover(window, Mouse.MouseEnterEvent);
            Pump(TimeSpan.FromMilliseconds(400)); // longer than the old 150ms hover-open
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void A_click_turns_the_puck_into_the_panel_on_its_outer_corner()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Click(window);
            Assert.True(window.IsIslandOpen);
            Assert.True(window.IsHooked);
            Assert.Equal(Visibility.Hidden, window.Body.Visibility); // the panel is the widget's shape now
            Pump(TimeSpan.FromMilliseconds(400)); // the morph out of the puck
            Assert.Equal(0, window.PuckEnd.Opacity); // nothing of the puck left over the panel
            var area = window.CurrentArea!.Value;
            var panel = window.Panel.Footprint;
            Assert.True(area.Work.Contains(panel));
            var puck = window.PuckRect;
            Assert.Equal(puck.Right, panel.Right, 1); // the puck's corner is the panel's, up and left from where it starts
            Assert.Equal(puck.Bottom, panel.Bottom, 1);
            window.Escape();
            Pump(TimeSpan.FromMilliseconds(400)); // folding back
            Assert.False(window.IsIslandOpen);
            Assert.Equal(Visibility.Visible, window.Body.Visibility);
            Assert.Equal(1, window.PuckEnd.Opacity);
        }));
    }

    [Fact]
    public void The_bar_becomes_the_panel_and_folds_back_into_the_bar()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.SetMedia(new IslandPanelTests.FakeMedia { Sessions = [Track("Creep")] });
            window.SetActivities([Playing], Chippi.UI.Pet.PetMood.Idle);
            Pump(TimeSpan.FromMilliseconds(400)); // the unfold
            var bar = window.WidgetRect;
            Assert.Equal(IslandGeometry.BarRect(window.PuckRect, window.OpensLeft, window.CurrentArea!.Value.Scale), bar);
            Click(window);
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.Equal(IslandPanelWindow.NowPlayingKey, window.Panel.CurrentPage); // the song's bar opens Now Playing
            Assert.Equal(0, window.BarLayer.Opacity);
            window.Escape();
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.True(window.BarShown);
            Assert.Equal(1, window.BarLayer.Opacity);
            Assert.Equal(bar, window.WidgetRect);
        }));
    }

    [Fact]
    public void A_click_outside_closes_the_panel_and_one_on_the_puck_does_not()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Click(window);
            var puck = window.PuckRect;
            window.PressedAt(new Point(puck.Left + puck.Width / 2, puck.Top + puck.Height / 2));
            Pump(TimeSpan.Zero);
            Assert.True(window.IsIslandOpen);
            window.PressedAt(new Point(puck.Left + puck.Width / 2, puck.Bottom - 2000));
            Pump(TimeSpan.Zero);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void The_song_bar_opens_Now_Playing()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.SetMedia(new IslandPanelTests.FakeMedia { Sessions = [Track("Creep")] });
            window.SetActivities([Playing], Chippi.UI.Pet.PetMood.Idle);
            window.SetLastPage(null);
            Click(window);
            Assert.True(window.IsIslandOpen);
            Assert.Equal(IslandPanelWindow.NowPlayingKey, window.Panel.CurrentPage);
        }));
    }

    [Fact]
    public void Esc_closes_the_open_panel_and_unhooks()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Click(window);
            window.Escape();
            Pump(TimeSpan.Zero);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void A_drag_lifts_the_puck_closes_the_panel_and_drops_it_where_it_is_let_go()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var area = window.CurrentArea!.Value;
            var puck = window.PuckRect;
            var pointer = new Point(puck.Left + 10, puck.Top + 20); // off the puck's center: it stays held there
            window.PointerAt = () => pointer;
            IslandPlacementEventArgs? chosen = null;
            window.PlacementChosen += (_, e) => chosen = e;

            Click(window);
            Raise(window, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            pointer = UnevenDrop(area, puck.Size);
            Raise(window, new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
            Assert.Equal(1.12, window.PuckScaleTarget);
            Assert.Equal(-6, window.PuckTiltTarget);
            Raise(window, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            Pump(TimeSpan.FromMilliseconds(300)); // the slide, if any
            Assert.False(window.IsIslandOpen);
            Assert.Equal(0, window.PuckTiltTarget);

            var expected = new Rect(pointer.X - 10, pointer.Y - 20, puck.Width, puck.Height);
            Assert.Equal(expected, window.PuckRect);
            Assert.NotNull(chosen);
            Assert.Equal(IslandGeometry.Fractions(expected, area.Work), (chosen!.X, chosen.Y));
            Assert.Equal(IslandGeometry.OpensLeft(expected, area.Work), window.OpensLeft);
            AssertPuckOnScreen(window);
        }));
    }

    [Fact]
    public void A_drop_past_an_edge_or_on_the_taskbar_is_kept_inside_the_work_area()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var area = window.CurrentArea!.Value;
            var w = area.Work;
            foreach (var pointer in new[] { new Point(w.Right - 2, w.Bottom - 2), new Point(w.Left + 1, w.Top + 1), new Point(w.Left + w.Width / 2, area.Monitor.Bottom - 2) })
            {
                var puck = window.DropAt(pointer);
                Assert.True(w.Contains(puck), $"{puck} not inside {w}");
                Assert.Equal(puck, window.PuckRect);
                Pump(TimeSpan.FromMilliseconds(250));
                AssertPuckOnScreen(window);
            }
        }));
    }

    [Fact]
    public void Hiding_for_a_fullscreen_app_closes_the_panel_and_unhooks()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Click(window);
            window.SetSuppressed(true);
            Assert.False(window.IsVisible);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void A_second_launch_opens_the_panel_while_the_widget_shows()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Assert.True(window.ShowPages(keyboard: false));
            Assert.True(window.IsIslandOpen);
            Assert.True(window.IsHooked);
            Assert.True(window.ShowPage(IslandPanelWindow.NowPlayingKey));
            Assert.Equal(IslandPanelWindow.NowPlayingKey, window.Panel.CurrentPage);
        }));
    }

    [Fact]
    public void With_the_widget_off_nothing_opens_and_the_caller_is_told()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.SetEnabled(false);
            Assert.False(window.IsVisible);
            Assert.False(window.ShowPages(keyboard: true));
            Assert.False(window.ShowPage(IslandPanelWindow.NowPlayingKey));
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void When_the_widget_can_t_show_a_second_launch_or_a_notice_opens_Settings()
    {
        var commands = new Chippi.App.Shell.ShellCommands();
        var opened = new List<string?>();
        bool showing = false;
        commands.RegisterSettings(opened.Add);
        commands.RegisterPages(_ => showing, _ => showing);

        commands.ShowPages();
        commands.ShowPage("system-monitor");
        Assert.Equal([null, null], opened); // the last page used

        showing = true;
        commands.ShowPages();
        commands.ShowPage("system-monitor");
        Assert.Equal(2, opened.Count);
    }

    [Fact]
    public void A_press_anywhere_off_the_panel_closes_it_tray_icon_included()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.ShowPages(keyboard: false);
            var area = window.CurrentArea!.Value;
            window.PressedAt(new Point(area.Monitor.Right - 100, area.Monitor.Bottom - 20)); // where the tray sits
            Pump(TimeSpan.Zero);
            Assert.False(window.IsIslandOpen);
        }));
    }

    /// <summary>A timer that never fires on its own: the test runs its tick.</summary>
    private sealed class StillTimer : IIslandTimer
    {
        public void Start() { }

        public void Stop() { }
    }

    /// <summary>The widget's window holds the puck at <see cref="IslandWindow.PuckRect"/> (its 62 DIP room, in physical pixels).</summary>
    private static void AssertPuckOnScreen(IslandWindow window)
    {
        var rect = IslandNative.WindowRect(window.Handle);
        var puck = window.PuckRect;
        double scale = window.CurrentArea!.Value.Scale;
        Assert.Equal(puck.Top + puck.Height / 2 - 62 * scale, rect.Top, 1);
        double center = window.OpensLeft ? rect.Right - 62 * scale : rect.Left + 62 * scale;
        Assert.Equal(puck.Left + puck.Width / 2, center, 1);
    }

    /// <summary>
    /// A drop point near the work area's middle (held 10 right and 20 down of the puck's corner) whose saved fractions
    /// don't give back exactly the same place, so anything that re-places the puck from them after the drop shows,
    /// whatever the screen's size.
    /// </summary>
    private static Point UnevenDrop(MonitorArea area, Size puck)
    {
        var w = area.Work;
        for (int dx = 0; dx < 500; dx++)
        {
            var at = new Point(Math.Floor(w.Left + w.Width / 2) + dx, Math.Floor(w.Top + w.Height / 2));
            var rect = new Rect(at.X - 10, at.Y - 20, puck.Width, puck.Height);
            var (x, y) = IslandGeometry.Fractions(rect, w);
            if (IslandGeometry.PuckRect(x, y, w, area.Scale) != rect)
                return at;
        }
        throw new InvalidOperationException("No drop point near the middle loses anything to the fractions");
    }

    /// <summary>A real widget on the primary display (no-activate, so it takes no focus), closed afterwards.</summary>
    internal static void WithShownIsland(Action<IslandWindow> test)
    {
        var window = new IslandWindow();
        try
        {
            window.Initialize();
            window.SetMonitor(IslandNative.MonitorAt(new Point(0, 0)));
            Assert.True(window.IsVisible);
            test(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Hover(IslandWindow window, RoutedEvent routed) =>
        Raise(window, new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = routed });

    /// <summary>A press and release without movement: the pointer stays where it was.</summary>
    private static void Click(IslandWindow window)
    {
        // The pointer holds still from the press to the release, whatever the real one does: moved 4 between them, it's a drag.
        var was = window.PointerAt;
        var at = was();
        window.PointerAt = () => at;
        Raise(window, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        Raise(window, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        window.PointerAt = was;
    }

    private static void Raise(IslandWindow window, RoutedEventArgs args) => window.PuckButton.RaiseEvent(args);

    /// <summary>Runs the dispatcher for <paramref name="time"/>, then until everything queued has run.</summary>
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
}

/// <summary>
/// Tests that change reduced motion, a process-wide setting, run alone after the rest: island tests
/// share one UI thread and interleave, and the change reaches every window and Settings page open.
/// </summary>
[CollectionDefinition("ReducedMotion", DisableParallelization = true)]
public sealed class ReducedMotionCollection;

[Collection("ReducedMotion")]
public sealed class IslandMotionTests
{
    [Fact]
    public void Reduced_motion_turned_on_while_a_song_scrolls_holds_it_at_once()
    {
        var motion = Chippi.UI.Themes.Motion.Override;
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            try
            {
                Chippi.UI.Themes.Motion.Override = false;
                window.SetMedia(new IslandPanelTests.FakeMedia
                {
                    Sessions = [new("spotify", "Spotify", "Everything In Its Right Place (Live at the Ancient Theatre)", "Radiohead",
                        true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(4), DateTimeOffset.Now, null)],
                });
                window.SetActivities([new IslandActivity("track", IslandActivityKind.Media, "Everything", "Spotify")], Chippi.UI.Pet.PetMood.Idle);
                Assert.Equal(Visibility.Visible, window.SongCopy.Visibility); // too long: it scrolls
                Chippi.UI.Themes.Motion.Override = true;
                Assert.Equal(Visibility.Collapsed, window.SongCopy.Visibility); // holds at once
                Chippi.UI.Themes.Motion.Override = false;
                Assert.Equal(Visibility.Visible, window.SongCopy.Visibility); // and scrolls again
            }
            finally
            {
                Chippi.UI.Themes.Motion.Override = motion;
            }
        }));
    }

    [Fact]
    public void With_reduced_motion_the_unfold_and_the_lift_snap()
    {
        var motion = Chippi.UI.Themes.Motion.Override;
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            try
            {
                Chippi.UI.Themes.Motion.Override = true;
                var puck = window.PuckRect;
                var pointer = new Point(puck.Left + puck.Width / 2, puck.Top + puck.Height / 2);
                window.PointerAt = () => pointer;
                window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                Assert.Equal(1, window.PuckScale.ScaleX, 9); // hover never changes the size
                window.SetActivities([new IslandActivity("timer", IslandActivityKind.Timer, "18:42", "Focus timer") { Progress = 0.4 }], Chippi.UI.Pet.PetMood.Idle);
                Assert.Equal(IslandGeometry.BarWidth, window.PuckButton.Width); // the bar is there at once: no unfold
                Assert.Equal(1, window.Bar.Opacity);
                window.SetActivities([], Chippi.UI.Pet.PetMood.Idle);
                Assert.Equal(IslandGeometry.Puck, window.PuckButton.Width); // and gone at once
                window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                pointer = new Point(pointer.X - 200, pointer.Y - 200);
                window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
                Assert.Equal(1.12, window.PuckScale.ScaleX, 9);
                Assert.Equal(-6, window.PuckTilt.Angle, 9);
                window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                Assert.Equal(new Rect(pointer.X - puck.Width / 2, pointer.Y - puck.Height / 2, puck.Width, puck.Height), window.PuckRect); // no slide: it's there
            }
            finally
            {
                Chippi.UI.Themes.Motion.Override = motion;
            }
        }));
    }
}
