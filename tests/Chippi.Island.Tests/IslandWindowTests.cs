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
    private static Chippi.App.Island.Media.MediaTrack Track(string title) =>
        new("spotify", "Spotify", title, "Radiohead", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(4), DateTimeOffset.Now, null);

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
    public void Hovering_grows_the_puck_4px_and_wakes_the_blob_and_leaving_shrinks_it_back()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Assert.Equal(1, window.PuckScaleTarget);
            Assert.Equal(Chippi.App.Pet.PetMood.Sleepy, window.Pet.Mood);
            Hover(window, Mouse.MouseEnterEvent);
            Assert.Equal(54, 50 * window.PuckScaleTarget, 9);
            Assert.Equal(Chippi.App.Pet.PetMood.Idle, window.Pet.Mood);
            Hover(window, Mouse.MouseLeaveEvent);
            Assert.Equal(1, window.PuckScaleTarget);
            Assert.Equal(Chippi.App.Pet.PetMood.Sleepy, window.Pet.Mood);
        }));
    }

    [Fact]
    public void Music_playing_shows_the_song_chip_scrolling_only_when_it_does_not_fit()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var media = new IslandPanelTests.FakeMedia { Sessions = [Track("Everything In Its Right Place (Live at the Ancient Theatre)")] };
            window.SetMedia(media);
            window.SetActivities([Playing], Chippi.App.Pet.PetMood.Idle);
            Assert.Equal(Visibility.Visible, window.Chip.Visibility);
            Assert.Equal(Visibility.Visible, window.Cover.Visibility);
            Assert.Equal(Visibility.Visible, window.Equalizer.Visibility);
            Assert.Equal(150, window.Song.Width); // too long: held to 150
            if (!Chippi.UI.Themes.Motion.IsReduced)
                Assert.Equal(Visibility.Visible, window.SongCopy.Visibility); // and a second copy follows it round

            // Hovering grows the puck without starting the song over (a restart sets a new fade).
            var fade = window.Song.OpacityMask;
            Hover(window, Mouse.MouseEnterEvent);
            Assert.Same(fade, window.Song.OpacityMask);
            Hover(window, Mouse.MouseLeaveEvent);
            Assert.Same(fade, window.Song.OpacityMask);

            media.Sessions = [Track("Creep")];
            media.Raise();
            Assert.Equal(Visibility.Collapsed, window.SongCopy.Visibility); // fits: holds still, and the chip hugs it
            Assert.Equal(Math.Ceiling(window.SongText.DesiredSize.Width), window.Song.Width);
            Assert.Null(window.Song.OpacityMask);

            window.SetActivities([], Chippi.App.Pet.PetMood.Idle); // paused: the activity goes
            Assert.Equal(Visibility.Collapsed, window.Chip.Visibility);
        }));
    }

    [Fact]
    public void The_chip_sits_on_the_side_toward_the_middle_and_the_puck_where_it_was_placed()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var area = window.CurrentArea!.Value;
            Assert.Equal(IslandGeometry.Start(area.Work, area.Scale), window.PuckRect); // where it starts
            Assert.True(window.OpensLeft);
            Assert.True(double.IsNaN(Canvas.GetLeft(window.Chip)));
            AssertPuckOnScreen(window);

            window.SetPlacement(0, 0); // the top-left corner
            Assert.Equal(IslandGeometry.PuckRect(0, 0, area.Work, area.Scale), window.PuckRect);
            Assert.False(window.OpensLeft);
            Assert.True(double.IsNaN(Canvas.GetRight(window.Chip)));
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
    public void A_click_opens_the_panel_beside_the_puck_and_another_click_on_the_puck_closes_it()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Click(window);
            Assert.True(window.IsIslandOpen);
            Assert.True(window.IsHooked);
            Assert.Equal(Visibility.Collapsed, window.Chip.Visibility);
            Pump(TimeSpan.FromMilliseconds(400)); // the morph out of the puck
            var area = window.CurrentArea!.Value;
            var panel = window.Panel.Footprint;
            Assert.True(area.Work.Contains(panel));
            Assert.Equal(window.PuckRect.Left, panel.Right, 1); // beside it, up and left from where it starts
            Click(window);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
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
    public void The_song_chip_opens_Now_Playing()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.SetMedia(new IslandPanelTests.FakeMedia { Sessions = [Track("Creep")] });
            window.SetActivities([Playing], Chippi.App.Pet.PetMood.Idle);
            window.SetLastPage(null);
            window.Chip.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            window.Chip.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
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
            pointer = new Point(area.Work.Left + area.Work.Width / 2, area.Work.Top + area.Work.Height / 2);
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
        Raise(window, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        Raise(window, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
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
                window.SetActivities([new IslandActivity("track", IslandActivityKind.Media, "Everything", "Spotify")], Chippi.App.Pet.PetMood.Idle);
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
    public void With_reduced_motion_the_hover_and_the_lift_snap()
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
                Assert.Equal(54, 50 * window.PuckScale.ScaleX, 9); // already there: no easing
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
