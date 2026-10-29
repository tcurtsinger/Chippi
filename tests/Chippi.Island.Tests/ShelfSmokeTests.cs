// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.App.Flyout;
using Chippi.App.Island;
using Chippi.Clipboard.Storage;
using Chippi.Core.Clipboard;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Shelf;

namespace Chippi.Island.Tests;

/// <summary>Real Shelf module, page and shell, with invented drops and a fake key/clipboard.
/// The two Visible tests create windows and run on CI; the other cases never create an HWND.</summary>
public sealed class ShelfSmokeTests
{
    [Theory]
    [InlineData(IslandEdge.Top)]
    [InlineData(IslandEdge.Left)]
    [InlineData(IslandEdge.Right)]
    public void Shelf_drop_routes_through_the_panel_releases_the_hold_and_fits_the_island_on_a_960_by_540_screen(IslandEdge edge) => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture();
        var panel = fixture.Window.Panel;
        var page = fixture.Page;
        panel.SetTiles(fixture.Tiles);
        Set(panel, "_page", "shelf");
        Call(panel, "Attach", "shelf");
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            Assert.True(fixture.Tile.Spec.OpensOnDrag);
            Assert.Equal("No items", fixture.Tile.Status);
            var data = new DataObject(DataFormats.UnicodeText, "Invented dropped text");
            var enter = Raise(page, DragDrop.DragEnterEvent, data);
            Assert.True(enter.Handled);
            Assert.Equal(DragDropEffects.Copy, enter.Effects);
            Assert.True(Get<IslandOpenHold>(fixture.Window, "_openHolds").IsHeld);
            var drop = Raise(page, DragDrop.DropEvent, data);
            fixture.Idle();
            Assert.True(drop.Handled);
            Assert.Equal(DragDropEffects.Copy, drop.Effects); // the panel must preserve the page's result
            Assert.False(Get<IslandOpenHold>(fixture.Window, "_openHolds").IsHeld);
            Assert.Equal("Invented dropped text", Assert.Single(fixture.Shelf.Items).Text);
            Assert.Equal("1 item", fixture.Tile.Status);
            // Fill past one 6 x 3 page so the tiles shrink to 7 x 4, as well as the empty drop zone.
            for (int i = 0; i < 20; i++) fixture.Shelf.AddTextAsync($"Invented item {i}").GetAwaiter().GetResult();
            Call(page, "Refresh");
            Assert.IsType<Button>(fixture.Tile.Spec.InitialFocus!());
            IslandBounds.Place(panel, edge, 960, 540);
            IslandBounds.Check(panel, edge, 960, 540);
            Assert.False(panel.IsVisible);
            Assert.Equal(IntPtr.Zero, new System.Windows.Interop.WindowInteropHelper(panel).Handle);
        }
        finally { page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    });

    [Theory]
    [InlineData("text")]
    [InlineData("files")]
    [InlineData("picture")]
    public void Preparing_drag_out_payload_keeps_the_item_and_source_files(string kind) => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture();
        string source = System.IO.Path.Combine(fixture.Directory, "invented.txt");
        System.IO.Directory.CreateDirectory(fixture.Directory);
        System.IO.File.WriteAllText(source, "Invented source, never moved");
        if (kind == "files") fixture.Shelf.AddFilesAsync([source]).GetAwaiter().GetResult();
        else if (kind == "picture")
        {
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 20, 40, 60, 255 }, 4);
            bitmap.Freeze(); fixture.Shelf.AddPictureAsync(bitmap).GetAwaiter().GetResult();
        }
        else fixture.Shelf.AddTextAsync("Invented drag text").GetAwaiter().GetResult();
        var item = Assert.Single(fixture.Shelf.Items);
        var pending = (Task<DataObject?>)Call(fixture.Page, "DataFor", item)!;
        Until(() => pending.IsCompleted);
        var payload = pending.GetAwaiter().GetResult();
        Assert.NotNull(payload);
        if (kind == "files") Assert.Equal(new[] { source }, payload.GetFileDropList().Cast<string>());
        else if (kind == "picture") { Assert.True(payload.ContainsImage()); Assert.True(payload.GetDataPresent("PNG")); }
        else Assert.Equal("Invented drag text", payload.GetText());
        Assert.Equal(item.Id, Assert.Single(fixture.Shelf.Items).Id);
        Assert.Equal("Invented source, never moved", System.IO.File.ReadAllText(source));
        Assert.False(fixture.Window.IsVisible);
        Assert.Equal(IntPtr.Zero, new System.Windows.Interop.WindowInteropHelper(fixture.Window).Handle);
        // Native DoDragDrop is deliberately not invoked here. Copy-only negotiation and accepted,
        // canceled and pinned drag-out outcomes are on the live checklist, not claimed by this test.
    });

    [Fact]
    public void Visible_shelf_tab_waits_250_ms_opens_without_keyboard_and_closes_after_500_ms() => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture();
        var window = fixture.Window;
        var timers = Manual(window);
        window.Initialize();
        window.SetMonitor(IslandNative.MonitorAt(new Point(0, 0)));
        window.SetTiles(fixture.Tiles);
        window.CursorAt = () => new Point(-30000, -30000);
        var tab = (Button)window.FindName("Tab");
        Assert.Equal(DragDropEffects.None, Raise(tab, DragDrop.DragEnterEvent).Effects);
        Assert.True(timers[IslandWindow.DragOpenDelay].Running);
        Assert.False(window.IsIslandOpen);
        timers[IslandWindow.DragOpenDelay].Fire();
        Assert.True(window.IsIslandOpen);
        Assert.True(window.OpenedByDrag); Assert.False(window.Panel.HasKeyboard);
        Assert.Same(fixture.Page, fixture.Tile.Spec.CreateSection());
        Raise(window.Panel, DragDrop.DragLeaveEvent);
        Assert.True(timers[IslandWindow.DragCloseDelay].Running);
        Assert.True(window.IsIslandOpen);
        Raise(window.Panel, DragDrop.DragOverEvent); // reentry cancels the pending close
        Assert.False(timers[IslandWindow.DragCloseDelay].Running);
        Assert.True(window.IsIslandOpen);
        Raise(window.Panel, DragDrop.DragLeaveEvent);
        timers[IslandWindow.DragCloseDelay].Fire();
        Assert.False(window.IsIslandOpen);
    });

    [Fact]
    public void Visible_shelf_drop_keeps_copy_effect_and_ends_the_drag_close_mode() => WpfTestThread.Run(() =>
    {
        using var fixture = new Fixture();
        var window = fixture.Window;
        var timers = Manual(window);
        window.Initialize(); window.SetMonitor(IslandNative.MonitorAt(new Point(0, 0)));
        window.SetTiles(fixture.Tiles); window.CursorAt = () => new Point(-30000, -30000);
        Raise((Button)window.FindName("Tab"), DragDrop.DragEnterEvent);
        timers[IslandWindow.DragOpenDelay].Fire();
        Assert.True(window.IsIslandOpen);
        Raise(fixture.Page, DragDrop.DragEnterEvent);
        Assert.Equal(DragDropEffects.Copy, Raise(fixture.Page, DragDrop.DropEvent).Effects);
        fixture.Idle(); Assert.Single(fixture.Shelf.Items);
        Assert.False(window.OpenedByDrag);
        Raise(window.Panel, DragDrop.DragLeaveEvent);
        Assert.False(timers[IslandWindow.DragCloseDelay].Running);
        Assert.True(window.IsIslandOpen);
    });

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
        public void Fire() { Assert.True(Running); Running = false; tick(); }
    }
    internal sealed class Fixture : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chippi-c6-shelf-" + Guid.NewGuid().ToString("N"));
        public IslandWindow Window { get; } = new();
        private readonly ShelfModule _module = new(path => ShelfStore.Open(path, new Keys()));
        private readonly Ui _ui = new();
        public IslandTileContribution Tile => _ui.Tile!;
        public IReadOnlyList<IslandTileGroup> Tiles => IslandTileContribution.Group([Tile]);
        public ShelfService Shelf => _module.Shelf!;
        public FrameworkElement Page => (FrameworkElement)Tile.Spec.CreateSection();
        public Fixture() => _module.StartAsync(new ModuleContext("shelf", new Settings(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, new Island(), _ui)
        { DataDirectory = Directory, Clipboard = new Hub(), HoldIslandOpen = Window.HoldIslandOpen }, default).GetAwaiter().GetResult();
        public void Idle() => ((Task)Call(Shelf, "IdleAsync")!).GetAwaiter().GetResult();
        public void Dispose()
        {
            Window.Close(); _module.StopAsync(default).GetAwaiter().GetResult();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
    private static DragEventArgs Raise(UIElement target, RoutedEvent routed, IDataObject? data = null)
    {
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [data ?? new DataObject(DataFormats.UnicodeText, "Invented drop"), DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy | DragDropEffects.Move, target, new Point(1, 1)], null)!;
        args.RoutedEvent = routed; target.RaiseEvent(args); return args;
    }
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Until(Func<bool> done)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!done() && DateTime.UtcNow < end) Pump(TimeSpan.FromMilliseconds(20));
        Assert.True(done());
    }
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
    private static T Get<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static object? Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private sealed class Keys : IKeyProtector { public byte[] Protect(byte[] key) => key.ToArray(); public byte[] Unprotect(byte[] key) => key.ToArray(); }
    private sealed class Settings : IModuleSettings { public T Get<T>() where T : class, new() => new(); public void Save<T>(T value) where T : class { } }
    private sealed class Ui : IModuleUi { public IslandTileContribution? Tile; public IIslandTile AddIslandTile(IslandTileSpec spec) => Tile = new("shelf", spec); public Func<string?, object>? Page; public void SetSettingsPage(Func<string?, object> factory) => Page = factory; }
    private sealed class Island : IIslandPublisher { public void Publish(IslandActivity activity) => throw new InvalidOperationException(); public void Clear(string key) { } public void ReportSuccess(string? message = null) => throw new InvalidOperationException(); }
    private sealed class Hub : IClipboardHub
    {
        public IDisposable Listen(Action<ClipCopy> copied, Func<string, bool>? skip = null) => throw new InvalidOperationException("Shelf must not listen.");
        public Task<ClipRead> ReadAsync(CancellationToken cancel = default) => throw new InvalidOperationException();
        public Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default) => throw new InvalidOperationException("A drag must not write the clipboard.");
        public PasteTarget? CaptureTarget() => throw new InvalidOperationException();
        public Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default) => throw new InvalidOperationException();
        public Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default) => throw new InvalidOperationException();
        public void Beep() => throw new InvalidOperationException();
    }
}
