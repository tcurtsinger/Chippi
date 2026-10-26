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

    [Fact]
    public void The_tray_opens_the_island_while_it_shows_and_its_next_click_closes_it()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.TogglePages(Tray(), keyboard: false);
            Assert.True(window.IsIslandOpen);
            Assert.False(window.Panel.IsDocked);
            Assert.True(window.IsHooked);
            window.TogglePages(Tray(), keyboard: false);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void With_the_island_off_the_tray_docks_the_same_pages_above_the_tray()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            window.SetEnabled(false);
            Assert.False(window.IsVisible);
            var tray = Tray();
            window.TogglePages(tray, keyboard: false);
            Assert.True(window.IsIslandOpen);
            Assert.True(window.Panel.IsDocked);
            Assert.True(window.IsHooked);
            // The card sits at the tray's end, 16px in from the screen edge and 12px above the taskbar.
            var work = tray.Display.Work;
            double s = tray.Display.Scale;
            Assert.True(window.Panel.Covers(new Point(work.Right - 20 * s, tray.Display.TaskbarInner - 16 * s)));
            Assert.False(window.Panel.Covers(new Point(work.Right - 8 * s, tray.Display.TaskbarInner - 16 * s)));
            Assert.False(window.Panel.Covers(new Point(work.Right - 20 * s, tray.Display.TaskbarInner - 4 * s)));
            window.PressedAt(new Point(work.Left + 10, work.Top + 10));
            Pump(TimeSpan.Zero);
            Assert.False(window.IsIslandOpen);
            Assert.False(window.IsHooked);
        }));
    }

    [Fact]
    public void Docked_by_the_tray_the_footer_opens_Settings_and_closes_the_pages()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            int requested = 0;
            window.SettingsRequested += (_, _) => requested++;
            window.SetEnabled(false);
            window.TogglePages(Tray(), keyboard: false);
            var footer = (System.Windows.Controls.Panel)window.Panel.FindName("FooterButtons");
            var settings = footer.Children.OfType<System.Windows.Controls.Button>()
                .Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Settings");
            settings.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(1, requested);
            Assert.False(window.IsIslandOpen);
        }));
    }

    [Fact]
    public void A_left_press_on_the_tray_icon_is_left_to_the_tray_and_a_right_press_closes()
    {
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var work = Tray().Display.Work;
            var icon = new Rect(work.Right - 120, work.Bottom + 4, 24, 24);
            window.TrayIconRect = () => icon;
            window.TogglePages(Tray(), keyboard: false);
            var onIcon = new Point(icon.Left + 12, icon.Top + 12);
            window.PressedAt(onIcon, left: true);
            Pump(TimeSpan.Zero);
            Assert.True(window.IsIslandOpen); // the tray's click that follows closes it
            window.PressedAt(onIcon, left: false);
            Pump(TimeSpan.Zero);
            Assert.False(window.IsIslandOpen); // a right click opens the tray menu instead
        }));
    }

    /// <summary>A tray on the primary display: a bottom taskbar 48 DIPs tall, its icon near the right end.</summary>
    private static Chippi.App.Tray.TrayAnchor Tray()
    {
        var area = IslandNative.Area(IslandNative.MonitorAt(new Point(0, 0)))!.Value;
        var m = area.Monitor;
        int bar = (int)Math.Round(48 * area.Scale);
        var monitor = new Chippi.App.Flyout.PixelRect((int)m.Left, (int)m.Top, (int)m.Right, (int)m.Bottom);
        var work = monitor with { Bottom = monitor.Bottom - bar };
        var display = new Chippi.App.Flyout.TrayDisplay(monitor, work, Chippi.App.Flyout.TaskbarEdge.Bottom, work.Bottom, monitor.Right - 100, (uint)Math.Round(96 * area.Scale));
        return new Chippi.App.Tray.TrayAnchor(display, null);
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
