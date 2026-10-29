// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.App.Island;
using Chippi.Core.Config;

namespace Chippi.Island.Tests;

/// <summary>The tab window itself, built on its own UI thread with the app's resources.</summary>
public sealed class IslandWindowTests
{
    [Fact]
    public void Esc_closes_the_island_without_throwing_on_the_dispatcher()
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
    public void Hovering_swells_the_tab_and_leaving_shrinks_it_back()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Assert.Equal(IslandGeometry.TabSize(IslandEdge.Top, live: false), window.TabTarget);
            Hover(window, Mouse.MouseEnterEvent);
            Assert.Equal(IslandGeometry.TabSize(IslandEdge.Top, live: false, swollen: true), window.TabTarget);
            Hover(window, Mouse.MouseLeaveEvent);
            Assert.Equal(IslandGeometry.TabSize(IslandEdge.Top, live: false), window.TabTarget);
        }));
    }

    [Fact]
    public void Music_playing_names_the_song_on_a_220_tab_scrolling_only_when_it_does_not_fit()
    {
        static Chippi.App.Island.Media.MediaTrack Track(string title) =>
            new("spotify", "Spotify", title, "Radiohead", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(4), DateTimeOffset.Now, null);
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var media = new IslandPanelTests.FakeMedia { Sessions = [Track("Everything In Its Right Place (Live at the Ancient Theatre)")] };
            window.SetMedia(media);
            var playing = new Chippi.Core.Island.IslandActivity("track", Chippi.Core.Island.IslandActivityKind.Media, "Everything", "Spotify");
            window.SetActivities([playing], Chippi.App.Pet.PetMood.Idle);
            Assert.Equal(new Size(220, 32), window.TabTarget);
            Assert.Equal(Visibility.Visible, window.Song.Visibility);
            if (!Chippi.UI.Themes.Motion.IsReduced)
                Assert.Equal(Visibility.Visible, window.SongCopy.Visibility); // too long: a second copy follows it round

            // Hovering swells the tab around the song without starting it over (a restart sets a new fade).
            var fade = window.Song.OpacityMask;
            Hover(window, Mouse.MouseEnterEvent);
            Assert.Equal(new Size(236, 36), window.TabTarget);
            Assert.Same(fade, window.Song.OpacityMask);
            Hover(window, Mouse.MouseLeaveEvent);
            Assert.Same(fade, window.Song.OpacityMask);

            media.Sessions = [Track("Creep")];
            media.Raise();
            Assert.Equal(Visibility.Collapsed, window.SongCopy.Visibility); // fits: holds still

            window.SetActivities([], Chippi.App.Pet.PetMood.Idle); // paused: the activity goes
            Assert.Equal(IslandGeometry.TabSize(IslandEdge.Top, live: false), window.TabTarget);
            Assert.Equal(Visibility.Collapsed, window.Song.Visibility);
        }));
    }

    [Fact]
    public void A_saved_side_edge_shows_at_top_center()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.SetPlacement(IslandEdge.Top, 0.5);
            var center = IslandNative.WindowRect(window.Handle);
            window.SetPlacement(IslandEdge.Top, 0.2);
            Assert.NotEqual(center, IslandNative.WindowRect(window.Handle));
            window.SetPlacement(IslandEdge.Left, 0.9); // docked low on the left before side edges were retired
            Assert.Equal(center, IslandNative.WindowRect(window.Handle));
        }));
    }

    [Fact]
    public void Hovering_never_opens_the_island()
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
    public void A_click_opens_the_island_and_another_click_on_the_tab_closes_it()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Click(window);
            Assert.True(window.IsIslandOpen);
            Assert.True(window.IsHooked);
            Click(window);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void A_click_outside_closes_the_island_and_one_on_it_does_not()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Click(window);
            var tab = IslandNative.WindowRect(window.Handle);
            window.PressedAt(new Point(tab.Left + tab.Width / 2, tab.Top + tab.Height / 2));
            Pump(TimeSpan.Zero);
            Assert.True(window.IsIslandOpen);
            window.PressedAt(new Point(tab.Left + tab.Width / 2, tab.Bottom + 2000));
            Pump(TimeSpan.Zero);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void Esc_closes_the_open_island_and_unhooks()
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
    public void A_drag_never_opens_the_island_and_closes_an_open_one()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var tab = IslandNative.WindowRect(window.Handle);
            var pointer = new Point(tab.Left + tab.Width / 2, tab.Top + tab.Height / 2);
            var start = pointer;
            window.PointerAt = () => pointer;

            Click(window);
            Raise(window, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            pointer = new Point(start.X + 40, start.Y + 40);
            Raise(window, new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
            Raise(window, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            Pump(TimeSpan.Zero);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void Hiding_for_a_fullscreen_app_closes_the_island_and_unhooks()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            Click(window);
            window.SetSuppressed(true);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void A_second_launch_opens_the_island_while_it_shows()
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
    public void With_the_island_off_nothing_opens_and_the_caller_is_told()
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
    public void When_the_island_can_t_show_a_second_launch_or_a_notice_opens_Settings()
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
    public void A_press_anywhere_off_the_island_closes_it_tray_icon_included()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.ShowPages(keyboard: false);
            var area = IslandNative.Area(IslandNative.MonitorAt(new Point(0, 0)))!.Value;
            window.PressedAt(new Point(area.Monitor.Right - 100, area.Monitor.Bottom - 20)); // where the tray sits
            Pump(TimeSpan.Zero);
            Assert.False(window.IsIslandOpen);
        }));
    }
    /// <summary>A real tab window on the primary display (no-activate, so it takes no focus), closed afterwards.</summary>
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

    private static void Raise(IslandWindow window, RoutedEventArgs args) => window.Tab.RaiseEvent(args);

    /// <summary>Runs the dispatcher for <paramref name="time"/>, then until everything queued has run.</summary>
    private static void Pump(TimeSpan time)
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
                window.SetActivities([new Chippi.Core.Island.IslandActivity("track", Chippi.Core.Island.IslandActivityKind.Media, "Everything", "Spotify")], Chippi.App.Pet.PetMood.Idle);
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
}
