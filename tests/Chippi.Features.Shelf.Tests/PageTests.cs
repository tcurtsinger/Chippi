// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Clipboard.Tests;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Shelf.Tests;

/// <summary>The page on a WPF thread without a window, and the module's tile.</summary>
public sealed class PageTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeHub _hub = new();
    private readonly ShelfModule _module;
    private readonly Ui _ui = new();
    private int _holds, _held;

    public PageTests() => _module = new ShelfModule(path => ShelfStore.Open(path, new TestKeys()));

    public void Dispose()
    {
        _module.StopAsync(default).GetAwaiter().GetResult();
        _folder.Dispose();
    }

    private Task StartAsync() => _module.StartAsync(new ModuleContext(ShelfModule.Id, new Settings(), NullLogger.Instance, new Island(), _ui)
    {
        Clipboard = _hub,
        DataDirectory = _folder.Path,
        HoldIslandOpen = () =>
        {
            _holds++;
            _held++;
            return new Release(() => _held--);
        },
    }, default);

    [Fact]
    public async Task The_tile_counts_items()
    {
        await StartAsync();
        Assert.Equal("No items", _ui.Tile!.Status);
        await _module.Shelf!.AddTextAsync("one");
        Assert.Equal("1 item", _ui.Tile.Status);
        await _module.Shelf.AddTextAsync("two");
        Assert.Equal("2 items", _ui.Tile.Status);
    }

    [Fact]
    public async Task Empty_shows_the_zone_and_focuses_it_then_items_show_their_name_and_caption()
    {
        await StartAsync();
        string pdf = _folder.Touch("invoice-0412.pdf", 2_100_000);
        WpfTestThread.Run(() =>
        {
            var page = (ShelfPage)_ui.Spec!.CreateSection();
            Load(page);
            Assert.True(Shows(page, "No items"));
            Assert.Equal("Shelf, empty", AutomationProperties.GetName((DependencyObject)_ui.Spec.InitialFocus!()!));
            Assert.DoesNotContain(Find<Button>(page), b => AutomationProperties.GetName(b) == "Copy" && IsShown(b));

            Wait(page.Take(Data(DataFormats.FileDrop, new[] { pdf })));
            Wait(page.Take(Data(DataFormats.UnicodeText, "Call Dana about the venue\nBring the plan")));
            Wait(page.Take(Data(DataFormats.UnicodeText, "https://example.org/docs/setup/")));
            page.Refresh();
            Assert.True(Shows(page, "invoice-0412.pdf") && !Shows(page, "PDF · 2.1 MB")); // size and type are in the tooltip
            Assert.Contains(Find<Button>(page), b => b.ToolTip as string == "invoice-0412.pdf\nPDF · 2.1 MB");
            Assert.True(Shows(page, "Call Dana about the venue"));
            Assert.True(Shows(page, "example.org/docs/setup"));
            Assert.Equal("example.org/docs/setup, Link", AutomationProperties.GetName((DependencyObject)_ui.Spec.InitialFocus!()!));
        });
    }

    [Fact]
    public async Task Actions_follow_the_selection_and_arrows_move_it()
    {
        await StartAsync();
        foreach (var text in new[] { "a", "b", "c", "d" })
            await _module.Shelf!.AddTextAsync(text);
        WpfTestThread.Run(() =>
        {
            var page = (ShelfPage)_ui.Spec!.CreateSection();
            Load(page);
            Button Action(string text) => Find<Button>(page).Single(b => AutomationProperties.GetName(b) == text);
            Assert.False(Action("Copy").IsEnabled);
            Assert.False(Action("Remove").IsEnabled);
            page.Move(1); // nothing selected: the first item
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(Find<Button>(page).First(b => AutomationProperties.GetName(b) == "d, Text")));
            page.Move(6); // down a row, six across
            Assert.Equal("Selected", AutomationProperties.GetItemStatus(Find<Button>(page).First(b => AutomationProperties.GetName(b) == "a, Text")));
            Assert.True(Action("Copy").IsEnabled);
            Action("Pin").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        await _module.Shelf!.IdleAsync();
        Assert.True(_module.Shelf.Items.Single(i => i.Text == "a").Pinned);
        WpfTestThread.Run(() =>
        {
            var page = (ShelfPage)_ui.Spec!.CreateSection();
            page.Refresh();
            Assert.Equal("a, Text, pinned", AutomationProperties.GetName(Find<Button>(page).First(b => AutomationProperties.GetName(b)!.StartsWith("a,"))));
        });
    }

    [Fact]
    public async Task A_missing_file_shows_unavailable_and_cannot_be_copied_or_dragged()
    {
        await StartAsync();
        await _module.Shelf!.AddFilesAsync([_folder.File("old-logo.svg")]);
        WpfTestThread.Run(() =>
        {
            var page = (ShelfPage)_ui.Spec!.CreateSection();
            Load(page);
            Assert.True(Shows(page, "old-logo.svg") && Shows(page, "Unavailable"));
            page.Move(1);
            Assert.False(Find<Button>(page).Single(b => AutomationProperties.GetName(b) == "Copy").IsEnabled);
            Assert.True(Find<Button>(page).Single(b => AutomationProperties.GetName(b) == "Remove").IsEnabled);
        });
    }

    [Fact]
    public async Task A_drag_over_the_page_holds_the_island_open_until_it_drops()
    {
        await StartAsync();
        WpfTestThread.Run(() =>
        {
            var page = (ShelfPage)_ui.Spec!.CreateSection();
            Load(page);
            page.Measure(new Size(328, 400));
            page.Arrange(new Rect(0, 0, 328, 400));
            var data = Data(DataFormats.UnicodeText, "dropped");
            Raise(page, DragDrop.DragEnterEvent, data);
            Assert.Equal(1, _held);
            Raise(page, DragDrop.DragOverEvent, data);
            Assert.Equal(1, _holds); // one hold per drag, not per move
            Raise(page, DragDrop.DropEvent, data);
            Assert.Equal(0, _held);
        });
        await _module.Shelf!.IdleAsync();
        Assert.Equal("dropped", Assert.Single(_module.Shelf.Items).Text);
    }

    [Fact]
    public async Task Items_drag_out_as_their_files_text_or_picture()
    {
        await StartAsync();
        string file = _folder.Touch("a.txt", 1);
        await _module.Shelf!.AddFilesAsync([file]);
        await _module.Shelf.AddTextAsync("words");
        await _module.Shelf.AddPictureAsync(ShelfTests.Bitmap(3, 2));
        var items = _module.Shelf.Items;
        WpfTestThread.Run(() =>
        {
            var page = (ShelfPage)_ui.Spec!.CreateSection();
            var files = Wait(page.DataFor(items.Single(i => i.Kind == ShelfKind.Files)))!;
            Assert.Equal([file], files.GetFileDropList().Cast<string>());
            Assert.Equal("words", Wait(page.DataFor(items.Single(i => i.Kind == ShelfKind.Text)))!.GetText());
            Assert.True(Wait(page.DataFor(items.Single(i => i.Kind == ShelfKind.Picture)))!.GetDataPresent("PNG"));
        });
    }

    [Fact]
    public void Virtual_files_alone_are_not_taken()
    {
        WpfTestThread.Run(() =>
        {
            var data = new DataObject();
            data.SetData("FileGroupDescriptorW", new MemoryStream([0]));
            Assert.False(ShelfPage.CanTake(data));
            Assert.True(ShelfPage.CanTake(Data(DataFormats.FileDrop, new[] { @"C:\a.txt" })));
        });
    }

    [Theory]
    [InlineData(1, 6, 3, 66)]
    [InlineData(18, 6, 3, 66)]
    [InlineData(19, 7, 4, 61)]
    [InlineData(28, 7, 4, 61)]
    [InlineData(29, 8, 5, 47.2)]
    [InlineData(40, 8, 5, 47.2)]
    public void Tiles_shrink_as_items_pile_up_and_fill_the_space_above_the_actions(int items, int columns, int rows, double height)
    {
        var layout = ShelfPage.Layout(items);
        Assert.Equal((columns, rows), (layout.Columns, layout.Rows));
        Assert.Equal(height, layout.Height, 3);
        Assert.Equal(496, columns * layout.Width + (columns - 1) * 8, 3);
        Assert.True(rows * layout.Height + (rows - 1) * 8 <= 268 + 0.001); // above the 28 action row and its 8 gap
    }

    [Fact]
    public async Task Forty_items_show_shelf_full_glyphs_only_and_refuse_a_drag()
    {
        await StartAsync();
        for (int i = 0; i < 40; i++)
            await _module.Shelf!.AddTextAsync($"note {i}");
        WpfTestThread.Run(() =>
        {
            var page = (ShelfPage)_ui.Spec!.CreateSection();
            Load(page);
            Assert.True(Shows(page, "Shelf full"));
            Assert.False(Shows(page, "note 39")); // 8 across: the glyph alone, the name in the tooltip
            Assert.Contains(Find<Button>(page), b => b.ToolTip as string == "note 39\nText");
            Assert.Empty(Find<ScrollViewer>(page));
        });
    }

    // ---- Helpers ----

    private static DataObject Data(string format, object value)
    {
        var data = new DataObject();
        data.SetData(format, value);
        return data;
    }

    private static void Raise(UIElement target, RoutedEvent routed, IDataObject data)
    {
        // DragEventArgs has no public constructor; build one the way WPF's OLE bridge does.
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
            [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, target, new Point(10, 10)], null)!;
        args.RoutedEvent = routed;
        target.RaiseEvent(args);
    }

    private static T Wait<T>(Task<T> task)
    {
        while (!task.IsCompleted)
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        return task.GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Settings_lists_every_item_on_60_rows_with_copy_pin_and_remove_and_dims_unavailable_ones()
    {
        await StartAsync();
        string pdf = _folder.Touch("invoice-0412.pdf", 2_100_000);
        string gone = _folder.Touch("old-logo.svg", 10);
        await _module.Shelf!.AddFilesAsync([pdf]);
        await _module.Shelf.AddFilesAsync([gone]);
        File.Delete(gone);
        await _module.Shelf.AddTextAsync("Call Dana about the venue");
        WpfTestThread.Run(() =>
        {
            var page = Assert.IsType<ShelfSettingsPage>(_ui.Page!(null));
            var rows = Find<Border>(page).Where(b => b.MinHeight == Chippi.UI.Controls.SettingsLayout.CaptionRowHeight).ToList();
            Assert.Equal(3, rows.Count);
            Assert.True(Shows(page, "invoice-0412.pdf"));
            Assert.True(Shows(page, "PDF · 2.1 MB"));
            Assert.True(Shows(page, "Unavailable"));
            var unavailable = rows.Single(r => Find<TextBlock>(r).Any(t => t.Text == "Unavailable"));
            Assert.False(Find<Button>(unavailable).Single(b => b.Content as string == "Copy").IsEnabled);
            Assert.Equal(0.45, Find<StackPanel>(unavailable).First().Opacity);

            var text = _module.Shelf.Items.Single(i => i.Kind == ShelfKind.Text);
            var anchor = Find<Chippi.UI.Controls.IconButton>(page).First();
            var menu = page.Menu(anchor, text);
            Assert.False(menu.IsOpen);
            Assert.Equal(new[] { "Pin", "Remove" }, Find<Button>(menu.Child).Select(b => b.Content));
            Find<Button>(menu.Child).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        await _module.Shelf.IdleAsync();
        Assert.True(_module.Shelf.Items.Single(i => i.Kind == ShelfKind.Text).Pinned);
        WpfTestThread.Run(() =>
        {
            var page = new ShelfSettingsPage(_module.Shelf);
            var text = _module.Shelf.Items.Single(i => i.Kind == ShelfKind.Text);
            Find<Button>(page).Single(b => AutomationProperties.GetName(b) == "Copy Call Dana about the venue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = page.Menu(Find<Chippi.UI.Controls.IconButton>(page).First(), text);
            Assert.Equal(new[] { "Unpin", "Remove" }, Find<Button>(menu.Child).Select(b => b.Content));
            Find<Button>(menu.Child).Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        await _module.Shelf.IdleAsync();
        Assert.Equal("Call Dana about the venue", Assert.Single(_hub.Written).Text);
        Assert.Equal(2, _module.Shelf.Items.Count);
    }

    [Fact]
    public async Task Settings_says_no_items_inside_the_card()
    {
        await StartAsync();
        WpfTestThread.Run(() =>
        {
            var page = Assert.IsType<ShelfSettingsPage>(_ui.Page!(null));
            Assert.True(Shows(page, "No items"));
        });
    }

    private static void Wait(Task task) => Wait(task.ContinueWith(_ => true));

    private static void Load(FrameworkElement view) => view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item)
            yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child))
                yield return nested;
    }

    private static bool Shows(DependencyObject root, string text) => Find<TextBlock>(root).Any(t => t.Text == text && IsShown(t));

    private static bool IsShown(DependencyObject element)
    {
        for (var at = element; at is not null; at = LogicalTreeHelper.GetParent(at))
            if (at is UIElement { Visibility: not Visibility.Visible })
                return false;
        return true;
    }

    private sealed class Release(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    private sealed class Ui : IModuleUi
    {
        public IslandTileSpec? Spec;
        public IslandTileContribution? Tile;

        public IIslandTile AddIslandTile(IslandTileSpec spec)
        {
            Spec = spec;
            return Tile = new IslandTileContribution("test", spec);
        }

        public Func<string?, object>? Page;

        public void SetSettingsPage(Func<string?, object> createView) => Page = createView;
    }

    private sealed class Settings : IModuleSettings
    {
        public T Get<T>() where T : class, new() => new();

        public void Save<T>(T value) where T : class { }
    }

    private sealed class Island : IIslandPublisher
    {
        public void Publish(IslandActivity activity) { }

        public void Clear(string key) { }

        public void ReportSuccess(string? message = null) { }
    }
}
