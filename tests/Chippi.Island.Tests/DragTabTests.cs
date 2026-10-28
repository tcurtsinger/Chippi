// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Chippi.App.Island;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

/// <summary>A drag from another app over the closed tab (Phase 5 notes #48). Run fullscreen-check first: one test shows the island.</summary>
public sealed class DragTabTests
{
    private static IReadOnlyList<IslandTileGroup> Tiles(bool opensOnDrag) => IslandTileContribution.Group(
    [
        new IslandTileContribution("shelf", new IslandTileSpec("shelf", "Shelf", nameof(LucideIcons.Inbox), 90, 0, () => new Border { Height = 100 }) { OpensOnDrag = opensOnDrag }),
    ]);

    [Fact]
    public void A_drag_over_the_tab_swells_it_with_the_pages_glyph_and_never_drops_on_it() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(Tiles(opensOnDrag: true));
            var tab = (Button)window.FindName("Tab");
            var glyph = (LucideIcon)window.FindName("DropGlyph");
            var over = Raise(tab, DragDrop.DragEnterEvent);
            Assert.Equal(DragDropEffects.None, over.Effects);
            Assert.Equal(Visibility.Visible, glyph.Visibility);
            Assert.Same(LucideIcons.Inbox, glyph.Data);
            Assert.Equal(IslandGeometry.TabSize(IslandEdge.Top, live: true, swollen: true), window.TabTarget);

            Raise(tab, DragDrop.DragLeaveEvent);
            Assert.Equal(Visibility.Collapsed, glyph.Visibility);
            Assert.Equal(IslandGeometry.TabSize(IslandEdge.Top, live: false), window.TabTarget);

            Raise(tab, DragDrop.DragEnterEvent);
            Assert.Equal(DragDropEffects.None, Raise(tab, DragDrop.DropEvent).Effects);
            Assert.Equal(Visibility.Collapsed, glyph.Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Without_a_page_that_opens_on_drag_the_tab_ignores_drags() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(Tiles(opensOnDrag: false));
            Raise((Button)window.FindName("Tab"), DragDrop.DragEnterEvent);
            Assert.Equal(Visibility.Collapsed, ((LucideIcon)window.FindName("DropGlyph")).Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void After_250_ms_the_island_opens_without_the_keyboard_and_closes_500_ms_after_the_drag_leaves() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.Initialize();
            window.SetMonitor(IslandNative.MonitorAt(new Point(0, 0)));
            window.SetTiles(Tiles(opensOnDrag: true));
            var away = new Point(-30000, -30000);
            window.CursorAt = () => away;
            var tab = (Button)window.FindName("Tab");

            var timers = Manual(window);

            Raise(tab, DragDrop.DragEnterEvent);
            var open = timers[IslandWindow.DragOpenDelay];
            Assert.True(open.Running);
            Assert.False(window.IsIslandOpen); // passing over the tab doesn't open it
            open.Fire();
            Assert.True(window.IsIslandOpen);
            Assert.True(window.OpenedByDrag);
            Assert.False(window.Panel.HasKeyboard);

            // Over the panel the drag keeps it open; leaving starts the 500 ms close, and coming back stops it.
            Raise(window.Panel, DragDrop.DragLeaveEvent);
            var close = timers[IslandWindow.DragCloseDelay];
            Assert.True(close.Running);
            Raise(window.Panel, DragDrop.DragOverEvent);
            Assert.False(close.Running);
            Assert.True(window.IsIslandOpen);
            Raise(window.Panel, DragDrop.DragLeaveEvent);
            close.Fire();
            Assert.False(window.IsIslandOpen);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_drag_that_leaves_the_tab_before_250_ms_opens_nothing() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(Tiles(opensOnDrag: true));
            var timers = Manual(window);
            var tab = (Button)window.FindName("Tab");
            Raise(tab, DragDrop.DragEnterEvent);
            Raise(tab, DragDrop.DragLeaveEvent);
            Assert.False(timers[IslandWindow.DragOpenDelay].Running);
            Assert.False(window.IsIslandOpen);
        }
        finally { window.Close(); }
    });

    /// <summary>The window makes its drag timers through CreateTimer: bypassing it would leave this empty, and the timing tests fail.</summary>
    [Fact]
    public void The_drag_timers_come_from_the_injected_factory_with_the_approved_delays() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(Tiles(opensOnDrag: true));
            var timers = Manual(window);
            Raise((Button)window.FindName("Tab"), DragDrop.DragEnterEvent); // makes the open timer
            window.ClosePages(); // makes the close timer, to stop it
            Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)], timers.Keys.Order());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_drop_ends_the_drag_opened_mode() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.Initialize();
            window.SetMonitor(IslandNative.MonitorAt(new Point(0, 0)));
            window.SetTiles(Tiles(opensOnDrag: true));
            window.CursorAt = () => new Point(-30000, -30000);
            var timers = Manual(window);
            Raise((Button)window.FindName("Tab"), DragDrop.DragEnterEvent);
            timers[IslandWindow.DragOpenDelay].Fire();
            Assert.True(window.IsIslandOpen);
            Raise(window.Panel, DragDrop.DropEvent);
            Assert.False(window.OpenedByDrag);
            Raise(window.Panel, DragDrop.DragLeaveEvent);
            Assert.False(timers.TryGetValue(IslandWindow.DragCloseDelay, out var close) && close.Running);
            Assert.True(window.IsIslandOpen); // normal closing: a click away or Esc
        }
        finally { window.Close(); }
    });

    private static DragEventArgs Raise(UIElement target, RoutedEvent routed)
    {
        var data = new DataObject(DataFormats.UnicodeText, "invented");
        // DragEventArgs has no public constructor; build one the way WPF's OLE bridge does.
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, target, new Point(1, 1)], null)!;
        args.RoutedEvent = routed;
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>Gives the window timers the test fires itself, by interval.</summary>
    private static Dictionary<TimeSpan, ManualTimer> Manual(IslandWindow window)
    {
        var timers = new Dictionary<TimeSpan, ManualTimer>();
        window.CreateTimer = (interval, tick) => timers[interval] = new ManualTimer(tick);
        return timers;
    }

    private sealed class ManualTimer(Action tick) : IIslandTimer
    {
        public bool Running { get; private set; }

        public void Start() => Running = true;

        public void Stop() => Running = false;

        public void Fire()
        {
            Assert.True(Running);
            Running = false;
            tick();
        }
    }
}
