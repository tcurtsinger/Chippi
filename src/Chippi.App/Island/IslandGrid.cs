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
/// <param name="Caution">The dot, the status and the glyph in Caution (a muted mic).</param>
internal sealed record IslandTileView(string Key, string Name, Geometry Icon, string Status, bool Active = false, bool Caution = false);

/// <summary>How the grid lays out a number of tiles, and what each tile shows at that size.</summary>
internal sealed record IslandGridLayout(int Columns, int Rows, double Glyph, double NameSize, double StatusSize)
{
    /// <summary>No status line: the smallest tiles show the glyph and the name only.</summary>
    public bool ShowsStatus => StatusSize > 0;

    /// <summary>DESIGN.md §5 Island (revised 2026-10-28): the tiles always fill the body, more columns as they grow.</summary>
    public static IslandGridLayout For(int tiles) => tiles switch
    {
        <= 3 => new(Math.Max(1, tiles), 1, 20, 12, 11),
        <= 4 => new(2, 2, 20, 12, 11),
        <= 6 => new(3, 2, 20, 12, 11),
        <= 9 => new(3, 3, 20, 12, 11),
        <= 12 => new(4, 3, 20, 12, 11),
        <= 15 => new(5, 3, 16, 11, 10),
        <= 20 => new(5, 4, 16, 11, 10),
        <= 24 => new(6, 4, 14, 10, 0),
        _ => new(6, (tiles + 5) / 6, 14, 10, 0),
    };
}

/// <summary>
/// The island's grid (DESIGN.md §5 Island): a 24px header with ChippiPet, Chippi and the clock, 8 below
/// it, then the tiles filling the 496 × 304 body 8px apart, laid out by <see cref="IslandGridLayout"/>.
/// Arrows move among the tiles once the island has keyboard focus, and Enter opens the focused one.
/// </summary>
internal sealed class IslandGrid : UserControl
{
    private readonly UniformGrid _tiles = new() { Margin = new Thickness(0, 0, -8, -8) };
    private readonly TextBlock _clock = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<IslandTileView> _shown = [];

    public IslandGrid()
    {
        Pet = new ChippiPet { Size = 20, Mood = PetMood.Idle, IsDecorative = true, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = "Chippi", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _clock.SetResourceReference(StyleProperty, "NumberTextStyle");
        _clock.FontSize = 12;
        _clock.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        DockPanel.SetDock(_clock, Dock.Right);
        var header = new DockPanel { Height = 24, Margin = new Thickness(0, 0, 0, 8), Children = { _clock, new StackPanel { Orientation = Orientation.Horizontal, Children = { Pet, name } } } };
        DockPanel.SetDock(header, Dock.Top);
        Content = new DockPanel { Children = { header, _tiles } };
        KeyboardNavigation.SetDirectionalNavigation(_tiles, KeyboardNavigationMode.None);
        PreviewKeyDown += OnKey;
    }

    /// <summary>The header's ChippiPet.</summary>
    public ChippiPet Pet { get; }

    /// <summary>The layout for the tiles showing now.</summary>
    internal IslandGridLayout Layout { get; private set; } = IslandGridLayout.For(0);

    /// <summary>A tile was clicked, or Enter pressed on it.</summary>
    public event EventHandler<string>? TileOpened;

    public void SetClock(DateTime now) => _clock.Text = now.ToString("t", CultureInfo.CurrentCulture);

    public void SetTiles(IReadOnlyList<IslandTileView> tiles)
    {
        if (tiles.SequenceEqual(_shown))
            return;
        var focused = (Keyboard.FocusedElement as FrameworkElement)?.Tag as string;
        _shown = tiles;
        Layout = IslandGridLayout.For(tiles.Count);
        _tiles.Columns = Layout.Columns;
        _tiles.Rows = Layout.Rows;
        _tiles.Children.Clear();
        foreach (var tile in tiles)
            _tiles.Children.Add(Tile(tile, Layout));
        if (focused is not null && Buttons().FirstOrDefault(b => (string)b.Tag == focused) is { } again)
            again.Focus();
    }

    internal IEnumerable<Button> Buttons() => _tiles.Children.OfType<Button>();

    private Button Tile(IslandTileView tile, IslandGridLayout layout)
    {
        var glyph = new LucideIcon { Data = tile.Icon, Size = layout.Glyph, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, tile.Caution ? "WarningBrush" : tile.Active ? "IslandAccentBrush" : "IslandTextBrush");
        var name = new TextBlock { Text = tile.Name, FontSize = layout.NameSize, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 6, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { glyph, name } };
        TextBlock? status = null;
        if (layout.ShowsStatus)
        {
            status = new TextBlock { Text = tile.Status, FontSize = layout.StatusSize, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(6, 1, 6, 0) };
            status.SetResourceReference(TextBlock.ForegroundProperty, tile.Caution ? "WarningTextBrush" : "IslandTextTertiaryBrush");
            stack.Children.Add(status);
        }
        var body = new Grid { Children = { stack } };
        if (tile.Active || tile.Caution)
        {
            var dot = new Ellipse { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0) };
            dot.SetResourceReference(Shape.FillProperty, tile.Caution ? "WarningBrush" : "IslandAccentBrush");
            body.Children.Add(dot);
        }
        var button = new Button { Content = body, Tag = tile.Key, Margin = new Thickness(0, 0, 8, 8) };
        button.SetResourceReference(StyleProperty, "IslandTileStyle");
        AutomationProperties.SetName(button, tile.Status.Length > 0 ? $"{tile.Name}, {tile.Status}" : tile.Name);
        // A long name or status is cut off on the tile; the tooltip carries all of it.
        void Tip()
        {
            bool cut = Cut(name, tile.Name) || (status is not null && Cut(status, tile.Status));
            button.ToolTip = !cut ? null : status is null || tile.Status.Length == 0 ? tile.Name : $"{tile.Name} · {tile.Status}";
        }
        name.SizeChanged += (_, _) => Tip();
        if (status is not null)
            status.SizeChanged += (_, _) => Tip();
        button.Click += (_, _) => TileOpened?.Invoke(this, tile.Key);
        return button;
    }

    /// <summary>Whether <paramref name="text"/> is cut off in <paramref name="block"/>: wider than one line, or taller than a wrapping block's MaxHeight.</summary>
    internal static bool Cut(TextBlock block, string text)
    {
        var full = new FormattedText(text, CultureInfo.CurrentCulture, block.FlowDirection,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);
        if (block.TextWrapping == TextWrapping.NoWrap)
            return full.Width > block.ActualWidth + 0.5;
        full.MaxTextWidth = block.ActualWidth;
        if (!double.IsNaN(block.LineHeight))
            full.LineHeight = block.LineHeight;
        return full.Height > block.MaxHeight + 0.5;
    }

    // Arrows move through whatever columns are showing; Enter opens the focused tile. Tab still leaves the grid.
    private void OnKey(object sender, KeyEventArgs e)
    {
        var buttons = Buttons().ToList();
        if (Keyboard.FocusedElement is not Button focused)
            return;
        int at = buttons.IndexOf(focused);
        if (at < 0)
            return;
        int columns = Layout.Columns;
        int to = e.Key switch
        {
            Key.Left => at - 1,
            Key.Right => at + 1,
            Key.Up => at - columns,
            Key.Down => at + columns,
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
