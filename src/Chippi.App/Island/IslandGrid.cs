// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.App.Island.Media;
using Chippi.App.Pet;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>One button on the home, as it shows.</summary>
/// <param name="On">The accent fill: its toggle is on.</param>
/// <param name="Caution">The status in Caution (a muted mic).</param>
/// <param name="Split">A toggle with an arrow that opens the page; otherwise the button just opens it.</param>
internal sealed record IslandTileView(string Key, string Name, Geometry Icon, string Status, bool On = false, bool Caution = false, bool Split = false);

/// <summary>
/// The island's home, after Windows' Quick Settings (DESIGN.md §5 Island): the Now Playing card, up to
/// eight labelled buttons four across with each tile's status under its name, the volume slider when
/// Sound is installed, and a footer with the pet, "Chippi" and Settings. Arrows move among the buttons
/// once the island has keyboard focus; Enter opens the focused one (a toggle toggles, as a button does).
/// </summary>
internal sealed class IslandGrid : UserControl
{
    /// <summary>The home holds two rows of four; a third wouldn't fit without scrolling.</summary>
    internal const int MaxTiles = 8;
    internal const int Columns = 4;

    private readonly UniformGrid _tiles = new() { Columns = Columns, Margin = new Thickness(0, 12, -10, -12) };
    private readonly IslandHomeMedia _media = new();
    private readonly Grid _volumeRow = new() { Height = 32, Margin = new Thickness(2, 0, 2, 0), Visibility = Visibility.Collapsed };
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new() { FontSize = 12, Width = 30, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private IReadOnlyList<IslandTileView> _shown = [];
    private IslandTileVolume? _volumeSource;
    private string? _volumeKey;
    private bool _refreshing;

    public IslandGrid()
    {
        Pet = new ChippiPet { Size = 20, Mood = PetMood.Idle, IsDecorative = true, VerticalAlignment = VerticalAlignment.Center };
        Content = Build();
        KeyboardNavigation.SetDirectionalNavigation(_tiles, KeyboardNavigationMode.None);
        PreviewKeyDown += OnKey;
    }

    private DockPanel Build()
    {
        // The footer bleeds to the panel's edges (the card pads 12).
        var name = new TextBlock { Text = "Chippi", FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
        var gear = new LucideIcon { Data = LucideIcons.Settings, Size = 16 };
        gear.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var settings = new Button { Content = gear, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Right };
        settings.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(settings, "Settings");
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { Pet, name } };
        var footer = new Border
        {
            Height = 46, Margin = new Thickness(-12, 0, -12, -12), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(IslandPage.Inset, 0, 10, 0),
            Child = new Grid { Children = { left, settings } },
        };
        footer.SetResourceReference(Border.BackgroundProperty, "IslandFooterBrush");
        footer.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
        DockPanel.SetDock(footer, Dock.Bottom);

        var speaker = new LucideIcon { Data = LucideIcons.Volume, Size = 16, VerticalAlignment = VerticalAlignment.Center };
        speaker.SetResourceReference(LucideIcon.BrushProperty, "IslandTextBrush");
        _volume.SetResourceReference(StyleProperty, "IslandSliderStyle");
        _volume.Margin = new Thickness(12, 0, 0, 0);
        AutomationProperties.SetName(_volume, "System volume");
        _volume.ValueChanged += (_, e) =>
        {
            ShowPercent();
            if (!_refreshing)
                _volumeSource?.Set(e.NewValue / 100);
        };
        _percent.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        var arrowGlyph = new LucideIcon { Data = LucideIcons.ChevronRight, Size = 14 };
        arrowGlyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var arrow = new Button { Content = arrowGlyph, Margin = new Thickness(6, 0, 0, 0) };
        arrow.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(arrow, "Sound");
        arrow.Click += (_, _) =>
        {
            if (_volumeKey is { } key)
                TileOpened?.Invoke(this, key);
        };
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition());
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_volume, 1);
        Grid.SetColumn(_percent, 2);
        Grid.SetColumn(arrow, 3);
        foreach (var part in new UIElement[] { speaker, _volume, _percent, arrow })
            _volumeRow.Children.Add(part);

        _media.OpenRequested += (_, _) => TileOpened?.Invoke(this, IslandPanelWindow.NowPlayingKey);
        var body = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_tiles, 1);
        Grid.SetRow(_volumeRow, 3);
        body.Children.Add(_media);
        body.Children.Add(_tiles);
        body.Children.Add(_volumeRow);
        return new DockPanel { Children = { footer, body } };
    }

    /// <summary>The footer's pet.</summary>
    public ChippiPet Pet { get; }

    /// <summary>The Now Playing card.</summary>
    internal IslandHomeMedia Media => _media;

    /// <summary>A button was clicked (or its arrow), or Enter pressed on it: the tile's key.</summary>
    public event EventHandler<string>? TileOpened;

    /// <summary>A split button's toggle was pressed: the tile's key.</summary>
    public event EventHandler<string>? TileToggled;

    /// <summary>The footer's Settings button.</summary>
    public event EventHandler? SettingsRequested;

    internal void SetMedia(IMediaSessions? media) => _media.SetMedia(media);

    /// <summary>The slider: shown when a tile offers the system volume, with its arrow opening <paramref name="key"/>'s page.</summary>
    internal void SetVolume(IslandTileVolume? volume, string? key)
    {
        _volumeSource = volume;
        _volumeKey = key;
        _volumeRow.Visibility = volume is null ? Visibility.Collapsed : Visibility.Visible;
        if (volume is null || _volume.IsMouseCaptureWithin)
            return;
        _refreshing = true;
        try
        {
            var level = volume.Level();
            _volume.IsEnabled = level is not null;
            _volume.Value = (level ?? 0) * 100;
            ShowPercent();
        }
        finally { _refreshing = false; }
    }

    private void ShowPercent() =>
        _percent.Text = _volume.IsEnabled ? $"{Math.Round(_volume.Value).ToString(CultureInfo.CurrentCulture)}%" : "";

    public void SetTiles(IReadOnlyList<IslandTileView> tiles)
    {
        if (tiles.SequenceEqual(_shown))
            return;
        var focused = (Keyboard.FocusedElement as FrameworkElement)?.Tag as string;
        _shown = tiles;
        _tiles.Rows = Math.Max(1, (Math.Min(tiles.Count, MaxTiles) + Columns - 1) / Columns);
        _tiles.Children.Clear();
        foreach (var tile in tiles.Take(MaxTiles))
            _tiles.Children.Add(Tile(tile));
        if (focused is not null && Buttons().FirstOrDefault(b => (string)b.Tag == focused) is { } again)
            again.Focus();
    }

    /// <summary>The buttons in order: each tile's own (its toggle, when it has one).</summary>
    internal IEnumerable<ButtonBase> Buttons() => _tiles.Children.OfType<Panel>().Select(c => ((Panel)c.Children[0]).Children.OfType<ButtonBase>().First());

    private StackPanel Tile(IslandTileView tile)
    {
        var glyph = new LucideIcon { Data = tile.Icon, Size = 16, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, tile.Split && tile.On ? "IslandOnAccentBrush" : tile.Caution ? "WarningBrush" : "IslandTextBrush");
        ButtonBase main;
        var box = new Grid { Height = 44 };
        if (tile.Split)
        {
            var toggle = new ToggleButton { IsChecked = tile.On, Content = glyph, Tag = tile.Key };
            toggle.SetResourceReference(StyleProperty, "IslandSplitMainStyle");
            // The module decides: the button follows its state, not the click.
            toggle.Click += (_, _) =>
            {
                toggle.IsChecked = tile.On;
                TileToggled?.Invoke(this, tile.Key);
            };
            var arrowGlyph = new LucideIcon { Data = LucideIcons.ChevronRight, Size = 13 };
            var side = new Button { Content = arrowGlyph, Tag = tile.On ? "On" : null };
            side.SetResourceReference(StyleProperty, "IslandSplitSideStyle");
            arrowGlyph.SetBinding(LucideIcon.BrushProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = side });
            AutomationProperties.SetName(side, $"Open {tile.Name}");
            side.Click += (_, _) => TileOpened?.Invoke(this, tile.Key);
            box.ColumnDefinitions.Add(new ColumnDefinition());
            box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(side, 1);
            box.Children.Add(toggle);
            box.Children.Add(side);
            main = toggle;
        }
        else
        {
            var button = new Button { Content = glyph, Tag = tile.Key };
            button.SetResourceReference(StyleProperty, "IslandTileStyle");
            button.Click += (_, _) => TileOpened?.Invoke(this, tile.Key);
            box.Children.Add(button);
            main = button;
        }
        AutomationProperties.SetName(main, tile.Status.Length > 0 ? $"{tile.Name}, {tile.Status}" : tile.Name);

        var name = new TextBlock { Text = tile.Name, FontSize = 12, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
        var status = new TextBlock { Text = tile.Status, FontSize = 11, Margin = new Thickness(0, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        status.SetResourceReference(TextBlock.ForegroundProperty, tile.Caution ? "WarningTextBrush" : "IslandTextTertiaryBrush");

        // A long name or status is cut off under the button; the tooltip carries all of it.
        void Tip()
        {
            bool cut = Cut(name, tile.Name) || Cut(status, tile.Status);
            main.ToolTip = !cut ? null : tile.Status.Length == 0 ? tile.Name : $"{tile.Name} · {tile.Status}";
        }
        name.SizeChanged += (_, _) => Tip();
        status.SizeChanged += (_, _) => Tip();
        return new StackPanel { Margin = new Thickness(0, 0, 10, 12), Children = { box, name, status } };
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

    // Arrows move through the buttons, four across; Enter opens the focused tile. Tab still leaves the grid.
    private void OnKey(object sender, KeyEventArgs e)
    {
        var buttons = Buttons().ToList();
        if (Keyboard.FocusedElement is not ButtonBase focused)
            return;
        int at = buttons.IndexOf(focused);
        if (at < 0)
            return;
        int to = e.Key switch
        {
            Key.Left => at - 1,
            Key.Right => at + 1,
            Key.Up => at - Columns,
            Key.Down => at + Columns,
            _ => int.MinValue,
        };
        if (e.Key == Key.Enter && focused is Button)
        {
            TileOpened?.Invoke(this, (string)focused.Tag);
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
