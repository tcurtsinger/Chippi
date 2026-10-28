// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Chippi.Core.Clipboard;
using Chippi.UI.Controls;

namespace Chippi.Features.Shelf;

/// <summary>
/// The Shelf page (design notes #37 to #50, #61): a three-column grid of items, newest first, with Copy,
/// Pin and Remove under it. The whole page takes a drop; an item drags out, Copy only. The island stays
/// open while a drag is over the page or an item is being dragged out.
/// </summary>
internal sealed class ShelfPage : UserControl
{
    private const double ItemWidth = 96, ItemHeight = 84, Gap = 8, Columns = 3;
    private const double GridHeight = 3 * ItemHeight + 2 * Gap; // 268: three rows, then it scrolls (notes #7)

    private readonly ShelfService _shelf;
    private readonly Func<IDisposable> _hold;
    private readonly Border _zone = new() { Height = 148, Focusable = true }; // empty: the dashed zone
    private readonly Rectangle _zoneStroke = Dashed(8);
    private readonly LucideIcon _zoneGlyph = Glyph(LucideIcons.Inbox, 20, "IslandTextTertiaryBrush");
    private readonly WrapPanel _items = new() { Width = Columns * ItemWidth + (Columns - 1) * Gap };
    private readonly ScrollViewer _scroller = new() { MaxHeight = GridHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly Grid _grid = new();
    private readonly Rectangle _gridStroke = Dashed(8);
    private readonly Button _copy = Button("Copy"), _pin = Button("Pin"), _remove = Button("Remove");
    private readonly StackPanel _caution = Caution();
    private readonly DockPanel _actions = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Dictionary<long, (byte[] Png, BitmapSource Image)> _thumbnails = []; // by item, checked against its bytes
    private IReadOnlyList<ShelfItem> _shown = [];
    private long? _selected;
    private string? _status; // "Clipboard busy" after a failed Copy
    private bool _over; // a drag that can drop is over the page
    private IDisposable? _dropHold;
    private long? _dragging;
    private Point? _pressAt;
    private long _pressId;

    public ShelfPage(ShelfService shelf, Func<IDisposable> hold)
    {
        _shelf = shelf;
        _hold = hold;

        // Empty (notes #41): a dashed zone with Inbox and "No items"; during a drag its stroke and glyph turn accent.
        var empty = Text("No items", 13, "IslandTextTertiaryBrush");
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        _zoneGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        _zoneGlyph.Margin = new Thickness(0, 0, 0, 8);
        _zone.Child = new Grid { Children = { _zoneStroke, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _zoneGlyph, empty } } } };
        _zone.FocusVisualStyle = null;
        _zone.GotKeyboardFocus += (_, _) => _zoneStroke.StrokeThickness = 2;
        _zone.LostKeyboardFocus += (_, _) => _zoneStroke.StrokeThickness = 1;
        AutomationProperties.SetName(_zone, "Shelf, empty");

        // The grid gets a dashed accent outline 4px outside it while a drag is over it.
        _gridStroke.Margin = new Thickness(-4);
        _gridStroke.SetResourceReference(Shape.StrokeProperty, "IslandAccentBrush");
        _gridStroke.Visibility = Visibility.Collapsed;
        _scroller.Content = _items;
        _scroller.Margin = new Thickness(0, 0, -8, 0); // the bar sits in the page's padding, not over the third column
        _items.HorizontalAlignment = HorizontalAlignment.Left;
        _grid.Children.Add(_gridStroke);
        _grid.Children.Add(_scroller);
        AutomationProperties.SetName(_items, "Shelf");

        _copy.Click += (_, _) => Copy();
        _pin.Click += (_, _) => Pin();
        _remove.Click += (_, _) => Remove();
        _pin.Margin = _remove.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _copy, _pin, _remove } };
        DockPanel.SetDock(buttons, Dock.Right);
        _caution.VerticalAlignment = VerticalAlignment.Center;
        _actions.Children.Add(buttons);
        _actions.Children.Add(_caution);

        Content = new StackPanel { Children = { _zone, _grid, _actions } };
        AutomationProperties.SetName(this, "Shelf");

        AllowDrop = true;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        PreviewKeyDown += OnKey;

        Loaded += (_, _) =>
        {
            _shelf.Changed += OnChanged;
            _selected = null;
            _status = null;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _shelf.Changed -= OnChanged;
            EndDrop();
            _thumbnails.Clear(); // no picture stays in memory while the page is closed
        };
        Refresh();
    }

    /// <summary>Where the keyboard lands when the page opens: the selected or first item, or the empty zone (notes #58).</summary>
    public UIElement InitialFocus =>
        _items.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, _selected)) as UIElement
        ?? _items.Children.OfType<Button>().FirstOrDefault() as UIElement
        ?? _zone;

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    // ---- Drawing ----

    internal void Refresh()
    {
        _shown = _shelf.Items;
        if (_selected is { } id && !_shown.Any(i => i.Id == id))
            _selected = null;
        foreach (var gone in _thumbnails.Keys.Where(key => !_shown.Any(i => i.Id == key)).ToList())
            _thumbnails.Remove(gone);

        bool empty = _shown.Count == 0;
        _zone.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _grid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _zoneStroke.SetResourceReference(Shape.StrokeProperty, _over ? "IslandAccentBrush" : "IslandTabBorderBrush");
        _zoneGlyph.SetResourceReference(LucideIcon.BrushProperty, _over ? "IslandAccentBrush" : "IslandTextTertiaryBrush");
        _gridStroke.Visibility = _over && !empty ? Visibility.Visible : Visibility.Collapsed;

        bool hadFocus = _items.IsKeyboardFocusWithin;
        _items.Children.Clear();
        int index = 0;
        if (_over && !empty)
            _items.Children.Add(Cell(Slot(), index++)); // where the drop will land: first, newest first
        foreach (var item in _shown)
            _items.Children.Add(Cell(Item(item), index++));
        if (hadFocus)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => InitialFocus.Focus());

        // Actions (notes #38, #42, #44): hidden when the Shelf is empty, disabled with nothing selected.
        var selected = Selected;
        string limit = _status ?? ShelfFormat.Limit(_shelf.Limit);
        _actions.Visibility = empty && limit.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ((Panel)_copy.Parent).Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _copy.IsEnabled = selected is not null && ShelfFormat.State(selected) != ShelfFormat.FileState.Missing;
        _pin.IsEnabled = _remove.IsEnabled = selected is not null;
        _pin.Content = selected?.Pinned == true ? "Unpin" : "Pin";
        ShowCaution(limit);
    }

    private static FrameworkElement Cell(FrameworkElement content, int index)
    {
        content.Width = ItemWidth;
        content.Height = ItemHeight;
        content.Margin = new Thickness(0, 0, index % Columns == Columns - 1 ? 0 : Gap, Gap);
        return content;
    }

    private static FrameworkElement Slot()
    {
        var slot = Dashed(8);
        slot.SetResourceReference(Shape.StrokeProperty, "IslandTabBorderBrush");
        return new Grid { Children = { slot } };
    }

    private Button Item(ShelfItem item)
    {
        bool selected = item.Id == _selected;
        bool missing = ShelfFormat.State(item) == ShelfFormat.FileState.Missing;
        FrameworkElement visual = item.Kind == ShelfKind.Picture && Thumbnail(item) is { } thumbnail
            ? new Image { Source = thumbnail, Width = 48, Height = 28, Stretch = Stretch.UniformToFill }
            : Glyph(ShelfFormat.Glyph(item), 20, missing ? "IslandTextTertiaryBrush" : "IslandTextTertiaryBrush");
        visual.HorizontalAlignment = HorizontalAlignment.Center;
        visual.VerticalAlignment = VerticalAlignment.Center;
        var glyphSlot = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 4), Children = { visual } };
        var name = Text(ShelfFormat.Name(item), 12, missing ? "IslandTextTertiaryBrush" : "IslandTextBrush");
        var caption = Text(ShelfFormat.Caption(item), 12, "IslandTextTertiaryBrush");
        name.HorizontalAlignment = caption.HorizontalAlignment = HorizontalAlignment.Center;
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Children = { glyphSlot, name, caption } };

        // Selected: a 1px accent stroke over a .10 fill (the tile's .06 plus .04).
        var mark = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        if (selected)
        {
            mark.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
            mark.SetResourceReference(Border.BorderBrushProperty, "IslandAccentBrush");
        }
        var content = new Grid { Children = { mark, body } };
        if (item.Pinned)
        {
            var pin = Glyph(LucideIcons.Pin, 12, "IslandTextTertiaryBrush");
            pin.HorizontalAlignment = HorizontalAlignment.Right;
            pin.VerticalAlignment = VerticalAlignment.Top;
            pin.Margin = new Thickness(0, 6, 6, 0);
            content.Children.Add(pin);
        }

        var button = new Button { Content = content, Tag = item.Id, Opacity = item.Id == _dragging ? 0.4 : 1 };
        button.SetResourceReference(StyleProperty, "IslandTileStyle");
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, $"{ShelfFormat.Name(item)}, {ShelfFormat.Caption(item)}{(item.Pinned ? ", pinned" : "")}");
        AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
        button.Click += (_, _) => Select(item.Id);
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressAt = e.GetPosition(this);
            _pressId = item.Id;
        };
        button.PreviewMouseMove += (_, e) => DragOut(item, button, e);
        button.PreviewMouseLeftButtonUp += (_, _) => _pressAt = null;
        return button;
    }

    private BitmapSource? Thumbnail(ShelfItem item)
    {
        if (item.Thumbnail is not { Length: > 0 } png)
            return null;
        if (_thumbnails.TryGetValue(item.Id, out var cached) && cached.Png.AsSpan().SequenceEqual(png))
            return cached.Image;
        try
        {
            var image = ShelfPictures.Load(png);
            _thumbnails[item.Id] = (png, image);
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- Actions ----

    private ShelfItem? Selected => _selected is { } id ? _shown.FirstOrDefault(i => i.Id == id) : null;

    private void Select(long id)
    {
        _status = null;
        _selected = id;
        Refresh();
    }

    private async void Copy()
    {
        if (Selected is not { } item || ShelfFormat.State(item) == ShelfFormat.FileState.Missing)
            return;
        var status = await _shelf.CopyAsync(item);
        _status = status is ClipStatus.Ok or ClipStatus.Empty ? null : "Clipboard busy";
        Refresh();
    }

    private void Pin()
    {
        if (Selected is { } item)
            _ = _shelf.PinAsync(item.Id, !item.Pinned);
    }

    /// <summary>No confirmation (notes #46): a file item is a reference, and nothing outside Chippi changes.</summary>
    private void Remove()
    {
        if (Selected is not { } item)
            return;
        int at = _shown.ToList().FindIndex(i => i.Id == item.Id);
        _selected = (_shown.ElementAtOrDefault(at + 1) ?? _shown.ElementAtOrDefault(at - 1))?.Id;
        _status = null;
        _ = _shelf.RemoveAsync(item.Id);
    }

    // ---- Keyboard (notes #61) ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        int by = e.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -(int)Columns, Key.Down => (int)Columns, _ => 0 };
        if (by != 0 && _items.IsKeyboardFocusWithin)
        {
            Move(by);
            e.Handled = true;
            return;
        }
        if (!_items.IsKeyboardFocusWithin || Selected is null)
            return;
        switch (e.Key)
        {
            case Key.C when ctrl:
                Copy();
                break;
            case Key.P when ctrl:
                Pin();
                break;
            case Key.Delete when Keyboard.Modifiers == ModifierKeys.None:
                Remove();
                break;
            case Key.Apps:
            case Key.F10 when Keyboard.Modifiers == ModifierKeys.Shift:
                OpenMenu();
                break;
            case Key.System when e.SystemKey == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift:
                OpenMenu();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Arrows move the focus and the selection follows (notes #61).</summary>
    internal void Move(int by)
    {
        if (_shown.Count == 0)
            return;
        int at = _selected is { } id ? _shown.ToList().FindIndex(i => i.Id == id) : -1;
        int next = at < 0 ? 0 : Math.Clamp(at + by, 0, _shown.Count - 1);
        Select(_shown[next].Id);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => InitialFocus.Focus());
    }

    /// <summary>Shift+F10 or the Menu key: Copy, Pin (Unpin), Remove.</summary>
    private void OpenMenu()
    {
        if (Selected is not { } item || InitialFocus is not FrameworkElement anchor)
            return;
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None };
        var list = new StackPanel { Margin = new Thickness(4) };
        foreach (var (text, enabled, click) in new (string, bool, Action)[]
        {
            ("Copy", ShelfFormat.State(item) != ShelfFormat.FileState.Missing, Copy),
            (item.Pinned ? "Unpin" : "Pin", true, Pin),
            ("Remove", true, Remove),
        })
        {
            var entry = new Button { Content = text, IsEnabled = enabled };
            entry.SetResourceReference(StyleProperty, "IslandMenuItemStyle");
            entry.Click += (_, _) =>
            {
                popup.IsOpen = false;
                click();
            };
            list.Children.Add(entry);
        }
        var panel = new Border { MinWidth = 160, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = list };
        panel.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        popup.Child = panel;
        popup.Opened += (_, _) => list.Children.OfType<Button>().FirstOrDefault(b => b.IsEnabled)?.Focus();
        popup.IsOpen = true;
    }

    // ---- Dropping in (notes #40, #41) ----

    /// <summary>What a drop holds that the Shelf keeps: files win, then a picture, then text. Virtual files (attachments) aren't (architecture §6).</summary>
    internal static bool CanTake(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap)
        || data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool take = _dragging is null && CanTake(e.Data);
        e.Effects = take ? DragDropEffects.Copy : DragDropEffects.None; // never Move: Chippi can't delete the source
        e.Handled = true;
        if (!take || _over)
            return;
        _over = true;
        _dropHold ??= _hold();
        Refresh();
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        // Leaving a child for another inside the page still raises DragLeave.
        var at = e.GetPosition(this);
        if (at.X >= 0 && at.Y >= 0 && at.X < ActualWidth && at.Y < ActualHeight)
            return;
        EndDrop();
        Refresh();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        bool take = _dragging is null && CanTake(e.Data);
        e.Effects = take ? DragDropEffects.Copy : DragDropEffects.None;
        EndDrop();
        _status = null;
        Refresh();
        if (take)
            _ = Take(e.Data);
    }

    private void EndDrop()
    {
        _over = false;
        _dropHold?.Dispose();
        _dropHold = null;
    }

    /// <summary>Reads the drop on the UI thread and keeps it as one item (notes #40).</summary>
    internal Task Take(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            return _shelf.AddFilesAsync(files);
        if (Picture(data) is { } picture)
            return _shelf.AddPictureAsync(picture);
        if ((data.GetData(DataFormats.UnicodeText) ?? data.GetData(DataFormats.Text)) is string text && text.Length > 0)
            return _shelf.AddTextAsync(text);
        return Task.CompletedTask;
    }

    private static BitmapSource? Picture(IDataObject data)
    {
        try
        {
            BitmapSource? source = null;
            if (data.GetData("PNG") is MemoryStream png)
                source = BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            else if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
                source = bitmap;
            source?.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- Dragging out (notes #49) ----

    private async void DragOut(ShelfItem item, Button button, MouseEventArgs e)
    {
        if (_pressAt is not { } from || _pressId != item.Id || e.LeftButton != MouseButtonState.Pressed || _dragging is not null)
            return;
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - from.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y - from.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _pressAt = null;
        if (ShelfFormat.State(item) == ShelfFormat.FileState.Missing)
            return;
        _dragging = item.Id;
        using var hold = _hold();
        try
        {
            var data = await DataFor(item);
            if (data is null || Mouse.LeftButton != MouseButtonState.Pressed)
                return;
            _selected = item.Id;
            button.Opacity = 0.4; // dims while dragged; the item isn't redrawn under the drag
            var effect = DragDrop.DoDragDrop(button, data, DragDropEffects.Copy);
            // Accepted: an unpinned item leaves; a pinned one stays. Canceled: it stays.
            if (effect == DragDropEffects.Copy && !item.Pinned)
                await _shelf.RemoveAsync(item.Id);
        }
        finally
        {
            _dragging = null;
            Refresh();
        }
    }

    /// <summary>What an item drags out as: its files, its text, or its picture.</summary>
    internal async Task<DataObject?> DataFor(ShelfItem item)
    {
        var data = new DataObject();
        switch (item.Kind)
        {
            case ShelfKind.Files:
                var files = new StringCollection();
                files.AddRange([.. item.Files ?? []]);
                data.SetFileDropList(files);
                break;
            case ShelfKind.Picture:
                if (await _shelf.PictureAsync(item.Id) is not { } png)
                    return null;
                data.SetData("PNG", new MemoryStream(png));
                data.SetImage(ShelfPictures.Load(png));
                break;
            default:
                data.SetText(item.Text ?? "", TextDataFormat.UnicodeText);
                break;
        }
        return data;
    }

    // ---- Parts ----

    private void ShowCaution(string text)
    {
        var block = (TextBlock)_caution.Children[1];
        block.Text = text;
        _caution.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(_caution, text);
    }

    private static StackPanel Caution()
    {
        var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, "WarningTextBrush");
        var line = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Children = { dot, Text("", 12, "WarningTextBrush") } };
        AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
        return line;
    }

    private static Rectangle Dashed(double radius) => new() { RadiusX = radius, RadiusY = radius, StrokeThickness = 1, StrokeDashArray = [4, 3], SnapsToDevicePixels = true };

    private static TextBlock Text(string text, double size, string brush)
    {
        var block = new TextBlock { Text = text, FontSize = size, LineHeight = 16, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static LucideIcon Glyph(Geometry data, double size, string brush)
    {
        var glyph = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }

    private static Button Button(string text)
    {
        var button = new Button { Content = text };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        return button;
    }
}
