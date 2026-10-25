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

    /// <summary>A real tab window on the primary display (no-activate, so it takes no focus), closed afterwards.</summary>
    private static void WithShownIsland(Action<IslandWindow> test)
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
