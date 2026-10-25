// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Chippi.App.Pet;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>One tile on the grid, as the island shows it.</summary>
/// <param name="Active">Something is live on it: the 6px dot.</param>
/// <param name="Caution">The dot, and the status, in Caution (a muted mic).</param>
internal sealed record IslandTileView(string Key, string Name, Geometry Icon, string Status, bool Active = false, bool Caution = false, bool CautionIcon = false);

/// <summary>
/// The island's grid (DESIGN.md §5 Island): a 28px header with Gumdrop, Chippi and the clock, then
/// the tiles three across with 8px gaps. Arrows move among the tiles once the island has keyboard
/// focus, and Enter opens the focused one.
/// </summary>
internal sealed class IslandGrid : UserControl
{
    private readonly UniformGrid _tiles = new() { Columns = 3, Margin = new Thickness(0, 8, -8, -8) };
    private readonly TextBlock _clock = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<IslandTileView> _shown = [];

    public IslandGrid()
    {
        Pet = new Gumdrop { Size = 22, Mood = PetMood.Idle, IsDecorative = true, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = "Chippi", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _clock.SetResourceReference(StyleProperty, "NumberTextStyle");
        _clock.FontSize = 12;
        _clock.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        DockPanel.SetDock(_clock, Dock.Right);
        var header = new DockPanel { Height = 28, Children = { _clock, new StackPanel { Orientation = Orientation.Horizontal, Children = { Pet, name } } } };
        Content = new StackPanel { Children = { header, _tiles } };
        KeyboardNavigation.SetDirectionalNavigation(_tiles, KeyboardNavigationMode.None);
        PreviewKeyDown += OnKey;
    }

    /// <summary>The header's Gumdrop.</summary>
    public Gumdrop Pet { get; }

    /// <summary>A tile was clicked, or Enter pressed on it.</summary>
    public event EventHandler<string>? TileOpened;

    public void SetClock(DateTime now) => _clock.Text = now.ToString("t", CultureInfo.CurrentCulture);

    public void SetTiles(IReadOnlyList<IslandTileView> tiles)
    {
        if (tiles.SequenceEqual(_shown))
            return;
        var focused = (Keyboard.FocusedElement as FrameworkElement)?.Tag as string;
        _shown = tiles;
        _tiles.Children.Clear();
        foreach (var tile in tiles)
            _tiles.Children.Add(Tile(tile));
        if (focused is not null && Buttons().FirstOrDefault(b => (string)b.Tag == focused) is { } again)
            again.Focus();
    }

    internal IEnumerable<Button> Buttons() => _tiles.Children.OfType<Button>();

    private Button Tile(IslandTileView tile)
    {
        var glyph = new LucideIcon { Data = tile.Icon, Size = 20, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, tile.CautionIcon ? "WarningBrush" : tile.Active && !tile.Caution ? "IslandAccentBrush" : "IslandTextBrush");
        var name = new TextBlock { Text = tile.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var status = new TextBlock { Text = tile.Status, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 1, 6, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, tile.Caution ? "WarningTextBrush" : "IslandTextTertiaryBrush");
        var body = new Grid
        {
            Children =
            {
                new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { glyph, name, status } },
            },
        };
        if (tile.Active)
        {
            var dot = new Ellipse { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0) };
            dot.SetResourceReference(Shape.FillProperty, tile.Caution ? "WarningBrush" : "IslandAccentBrush");
            body.Children.Add(dot);
        }
        var button = new Button { Content = body, Tag = tile.Key, Margin = new Thickness(0, 0, 8, 8) };
        button.SetResourceReference(StyleProperty, "IslandTileStyle");
        AutomationProperties.SetName(button, tile.Status.Length > 0 ? $"{tile.Name}, {tile.Status}" : tile.Name);
        // A long status is cut off on the tile; the tooltip and the accessible name carry all of it.
        status.SizeChanged += (_, _) =>
        {
            var full = new FormattedText(tile.Status, CultureInfo.CurrentCulture, status.FlowDirection,
                new Typeface(status.FontFamily, status.FontStyle, status.FontWeight, status.FontStretch), status.FontSize, Brushes.White,
                VisualTreeHelper.GetDpi(status).PixelsPerDip);
            button.ToolTip = full.Width > status.ActualWidth + 0.5 ? tile.Status : null;
        };
        button.Click += (_, _) => TileOpened?.Invoke(this, tile.Key);
        return button;
    }

    // Arrows move among the three columns; Enter opens the focused tile. Tab still leaves the grid.
    private void OnKey(object sender, KeyEventArgs e)
    {
        var buttons = Buttons().ToList();
        if (Keyboard.FocusedElement is not Button focused)
            return;
        int at = buttons.IndexOf(focused);
        if (at < 0)
            return;
        int to = e.Key switch
        {
            Key.Left => at - 1,
            Key.Right => at + 1,
            Key.Up => at - 3,
            Key.Down => at + 3,
            _ => int.MinValue,
        };
        if (e.Key == Key.Enter)
        {
            TileOpened?.Invoke(this, (string)buttons[at].Tag);
            e.Handled = true;
        }
        else if (to != int.MinValue)
        {
            if (to >= 0 && to < buttons.Count)
                buttons[to].Focus();
            e.Handled = true;
        }
    }
}
