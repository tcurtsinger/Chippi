// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Clipboard;
using Chippi.UI.Controls;

namespace Chippi.Features.Shelf;

/// <summary>
/// Settings › Shelf (2026-10-28, brief F3 #11): every item on one 60 row (glyph, name, kind caption, the pin
/// when pinned, Copy, and ⋯ with Pin / Unpin and Remove). Unavailable items are dimmed. Rows drag out like the
/// island's tiles: an unpinned item leaves once it's taken, a pinned one stays.
/// </summary>
public sealed class ShelfSettingsPage : UserControl
{
    private readonly ShelfService _shelf;
    private readonly Card _list = new();
    private long? _copied;
    private Point? _pressAt;
    private long? _pressId;
    private bool _dragging;

    public ShelfSettingsPage(ShelfService shelf)
    {
        _shelf = shelf;
        Content = _list;
        Loaded += (_, _) =>
        {
            _shelf.Changed += OnChanged;
            Refresh();
        };
        Unloaded += (_, _) => _shelf.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        if (_dragging)
            return;
        _list.Children.Clear();
        foreach (var item in _shelf.Items)
            _list.Children.Add(Row(item));
        if (_list.Children.Count == 0)
            _list.Children.Add(SettingsLayout.Empty("No items"));
    }

    private Border Row(ShelfItem item)
    {
        bool missing = ShelfFormat.State(item) == ShelfFormat.FileState.Missing;
        var grid = new Grid();
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var glyph = Glyph(ShelfFormat.Glyph(item));
        glyph.Margin = new Thickness(0, 0, 12, 0);
        Add(grid, glyph, 0);
        var name = Text(ShelfFormat.Name(item), "BodyTextStyle", "TextPrimaryBrush");
        var caption = Text(ShelfFormat.Caption(item), "CaptionTextStyle", "TextSecondaryBrush");
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Children = { name, caption } };
        Add(grid, label, 1);
        if (item.Pinned)
        {
            var pin = Glyph(LucideIcons.Pin);
            pin.Margin = new Thickness(0, 0, 12, 0);
            Add(grid, pin, 2);
        }
        var copy = new Button { Content = _copied == item.Id ? "Copied" : "Copy", IsEnabled = !missing, VerticalAlignment = VerticalAlignment.Center };
        copy.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        AutomationProperties.SetName(copy, $"Copy {ShelfFormat.Name(item)}");
        copy.Click += async (_, _) => await Copy(item);
        Add(grid, copy, 3);
        var more = new IconButton { Icon = LucideIcons.Ellipsis, ToolTip = "More", Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(more, $"More for {ShelfFormat.Name(item)}");
        more.Click += (_, _) => Menu(more, item).IsOpen = true;
        Add(grid, more, 4);

        var row = new Border { MinHeight = SettingsLayout.CaptionRowHeight, Padding = new Thickness(16, 8, 8, 8), Background = Brushes.Transparent, Child = grid };
        if (missing)
            glyph.Opacity = label.Opacity = 0.45;
        AutomationProperties.SetName(row, $"{ShelfFormat.Name(item)}, {ShelfFormat.Caption(item)}{(item.Pinned ? ", pinned" : "")}");
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && Within<ButtonBase>(source, row))
                return; // Copy and ⋯ keep their clicks
            _pressAt = e.GetPosition(this);
            _pressId = item.Id;
        };
        row.PreviewMouseLeftButtonUp += (_, _) => _pressAt = null;
        row.MouseMove += (_, e) => DragOut(item, row, e);
        return row;
    }

    /// <summary>The row's ⋯: Pin or Unpin, then Remove.</summary>
    internal Popup Menu(FrameworkElement anchor, ShelfItem item)
    {
        var stack = new StackPanel();
        var border = new Border { Child = stack, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        var popup = new Popup { Child = border, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        foreach (var (text, action) in new (string, Action)[]
        {
            (item.Pinned ? "Unpin" : "Pin", () => _ = _shelf.PinAsync(item.Id, !item.Pinned)),
            ("Remove", () => _ = _shelf.RemoveAsync(item.Id)),
        })
        {
            var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 160 };
            button.SetResourceReference(StyleProperty, "FlyoutRowButtonStyle");
            button.Click += (_, _) => { popup.IsOpen = false; action(); };
            stack.Children.Add(button);
        }
        border.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            popup.IsOpen = false;
            anchor.Focus();
            e.Handled = true;
        };
        popup.Opened += (_, _) => stack.Children.OfType<Button>().FirstOrDefault()?.Focus();
        return popup;
    }

    private async Task Copy(ShelfItem item)
    {
        if (await _shelf.CopyAsync(item) != ClipStatus.Ok)
            return;
        _copied = item.Id;
        Refresh();
    }

    /// <summary>As on the island (notes #49): taken, an unpinned item leaves; canceled, it stays.</summary>
    private async void DragOut(ShelfItem item, Border row, MouseEventArgs e)
    {
        if (_pressAt is not { } from || _pressId != item.Id || e.LeftButton != MouseButtonState.Pressed || _dragging)
            return;
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - from.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y - from.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _pressAt = null;
        if (ShelfFormat.State(item) == ShelfFormat.FileState.Missing)
            return;
        _dragging = true;
        try
        {
            var data = await ShelfPage.DataFor(_shelf, item);
            if (data is null || Mouse.LeftButton != MouseButtonState.Pressed)
                return;
            row.Opacity = 0.4;
            var effect = DragDrop.DoDragDrop(row, data, DragDropEffects.Copy);
            if (effect == DragDropEffects.Copy && !item.Pinned)
                await _shelf.RemoveAsync(item.Id);
        }
        finally
        {
            _dragging = false;
            Refresh();
        }
    }

    private static bool Within<T>(DependencyObject source, DependencyObject stop) where T : DependencyObject
    {
        for (var at = source; at is not null && at != stop; at = VisualTreeHelper.GetParent(at))
            if (at is T)
                return true;
        return false;
    }

    private static TextBlock Text(string text, string style, string brush)
    {
        var block = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };
        block.SetResourceReference(StyleProperty, style);
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    private static LucideIcon Glyph(Geometry data)
    {
        var glyph = new LucideIcon { Data = data, Size = 16, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextSecondaryBrush");
        return glyph;
    }

    private static void Add(Grid grid, UIElement child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
}
