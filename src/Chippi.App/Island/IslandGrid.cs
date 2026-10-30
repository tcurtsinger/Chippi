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
using System.Windows.Threading;
using Chippi.App.Island.Media;
using Chippi.App.Pet;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>One button on the home, as it shows.</summary>
/// <param name="On">The accent fill: its toggle is on, and it doesn't need attention.</param>
/// <param name="Caution">The glyph and the status in Caution (a muted mic, a feature that needs attention, a toggle that failed).</param>
/// <param name="Split">A toggle with an arrow that opens the page; otherwise the button just opens it.</param>
/// <param name="Page">The page the button opens, when it isn't named like the button (Microphone opens Sound).</param>
internal sealed record IslandTileView(string Key, string Name, Geometry Icon, string Status, bool On = false, bool Caution = false, bool Split = false, string? Page = null);

/// <summary>
/// The island's home, after Windows' Quick Settings (DESIGN.md §5 Island): the Now Playing card, up to
/// eight labelled buttons four across with each tile's status under its name, the volume row when
/// Sound is installed, and a footer with Chippi on the puck's side and Settings on the other. Arrows move
/// among the buttons and their arrows once the island has keyboard focus; Enter opens the focused one (a
/// toggle toggles, as a button does).
/// </summary>
internal sealed class IslandGrid : UserControl
{
    /// <summary>The home holds two rows of four; a third wouldn't fit without scrolling.</summary>
    internal const int MaxTiles = 8;
    internal const int Columns = 4;

    /// <summary>
    /// The footer's Chippi: its center this far in from the panel's side and its bottom, where the puck's center is when
    /// the panel opens on a bottom corner (the puck is 50 square on the panel's corner), at the size the puck's Chippi
    /// shrinks to as it becomes the panel (70% of 40).
    /// </summary>
    internal const double FooterPet = 25, FooterPetSize = 28;

    // The wheel's step on the volume, in percent per notch, as on the taskbar's.
    private const double WheelStep = 2;

    private readonly UniformGrid _tiles = new() { Columns = Columns, Margin = new Thickness(0, 12, -10, -12) };
    private readonly IslandHomeMedia _media = new();
    private readonly Grid _volumeRow = new() { Height = 32, Margin = new Thickness(2, 0, 2, 0), Visibility = Visibility.Collapsed };
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new() { FontSize = 12, Width = 34, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _noOutput = new() { Text = "No output", FontSize = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly LucideIcon _speakerGlyph = new() { Data = LucideIcons.Volume, Size = 16 };
    private readonly Button _speaker = new();
    private readonly Button _settings = new() { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _volumeFailed = new() { Interval = TimeSpan.FromSeconds(4) };
    private IReadOnlyList<IslandTileView> _shown = [];
    private IslandTileVolume? _volumeSource;
    private string? _volumeKey;
    private bool _refreshing;

    public IslandGrid()
    {
        Pet = new ChippiPet { Size = FooterPetSize, Mood = PetMood.Idle, IsDecorative = true, VerticalAlignment = VerticalAlignment.Bottom };
        Content = Build();
        SetPetSide(right: false);
        KeyboardNavigation.SetDirectionalNavigation(_tiles, KeyboardNavigationMode.None);
        PreviewKeyDown += OnKey;
    }

    private DockPanel Build()
    {
        // The footer bleeds to the panel's edges (the card pads 12): Chippi on the puck's side, Settings on the other.
        var gear = new LucideIcon { Data = LucideIcons.Settings, Size = 16 };
        gear.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        _settings.Content = gear;
        _settings.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(_settings, "Settings");
        _settings.ToolTip = "Settings";
        _settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var footer = new Border
        {
            Height = 46, Margin = new Thickness(-12, 0, -12, -12), BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new Grid { Children = { Pet, _settings } },
        };
        footer.SetResourceReference(Border.BackgroundProperty, "IslandFooterBrush");
        footer.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
        DockPanel.SetDock(footer, Dock.Bottom);

        // The speaker mutes, as in Quick Settings; its glyph sits where a bare glyph would (the button is 6 wider each side).
        _speaker.Content = _speakerGlyph;
        _speaker.Margin = new Thickness(-6, 0, 0, 0);
        _speaker.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        _speaker.Click += async (_, _) => await ToggleMuteAsync();
        _volumeFailed.Tick += (_, _) =>
        {
            _volumeFailed.Stop();
            SetVolume(_volumeSource, _volumeKey);
        };
        _volume.SetResourceReference(StyleProperty, "IslandSliderStyle");
        _volume.Margin = new Thickness(12, 0, 0, 0);
        AutomationProperties.SetName(_volume, "System volume");
        _volume.ValueChanged += (_, e) =>
        {
            ShowPercent();
            if (!_refreshing)
                _volumeSource?.Set(e.NewValue / 100);
        };
        _noOutput.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        var arrowGlyph = new LucideIcon { Data = LucideIcons.ChevronRight, Size = 14 };
        arrowGlyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var arrow = new Button { Content = arrowGlyph, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Sound" };
        arrow.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(arrow, "Sound");
        arrow.Click += (_, _) =>
        {
            if (_volumeKey is { } key)
                TileOpened?.Invoke(this, key);
        };
        // The wheel anywhere on the row moves the volume, as on the taskbar's speaker.
        _volumeRow.PreviewMouseWheel += (_, e) =>
        {
            if (!_volume.IsEnabled || _volume.Visibility != Visibility.Visible)
                return;
            _volume.Value = Math.Clamp(_volume.Value + e.Delta / 120.0 * WheelStep, _volume.Minimum, _volume.Maximum);
            e.Handled = true;
        };
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition());
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_volume, 1);
        Grid.SetColumn(_noOutput, 1);
        Grid.SetColumn(_percent, 2);
        Grid.SetColumn(arrow, 3);
        foreach (var part in new UIElement[] { _speaker, _volume, _noOutput, _percent, arrow })
            _volumeRow.Children.Add(part);

        _media.OpenRequested += (_, _) => TileOpened?.Invoke(this, IslandPanelWindow.NowPlayingKey);
        // 10 above the footer: the volume row sits midway between the buttons' last status line and the footer's hairline.
        var body = new Grid { Margin = new Thickness(0, 0, 0, 10) };
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

    /// <summary>
    /// The volume row: shown when a tile offers the system volume, with its arrow opening <paramref name="key"/>'s page.
    /// Muted, the speaker and "Muted" are in Caution; with no output, "No output" takes the slider's place.
    /// </summary>
    internal void SetVolume(IslandTileVolume? volume, string? key)
    {
        _volumeSource = volume;
        _volumeKey = key;
        _volumeRow.Visibility = volume is null ? Visibility.Collapsed : Visibility.Visible;
        if (volume is null)
            return;
        var level = volume.Level();
        bool none = level is null;
        bool muted = !none && volume.Muted?.Invoke() == true;
        _volume.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _noOutput.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        _volume.IsEnabled = !none;
        _speaker.IsEnabled = !none && volume.SetMuted is not null;
        _speakerGlyph.Data = muted || none ? LucideIcons.VolumeMuted : LucideIcons.Volume;
        _speakerGlyph.SetResourceReference(LucideIcon.BrushProperty, none ? "IslandTextTertiaryBrush" : muted ? "WarningBrush" : "IslandTextBrush");
        string action = muted ? "Unmute" : "Mute";
        AutomationProperties.SetName(_speaker, action);
        _speaker.ToolTip = volume.SetMuted is null ? null : action;
        if (!_volume.IsMouseCaptureWithin)
        {
            _refreshing = true;
            try { _volume.Value = (level ?? 0) * 100; }
            finally { _refreshing = false; }
        }
        ShowPercent();
    }

    private bool Muted => _volumeSource?.Muted?.Invoke() == true && _volume.IsEnabled;

    private void ShowPercent()
    {
        if (_volumeFailed.IsEnabled)
            return; // "Failed" holds a moment
        bool muted = Muted;
        _percent.Text = !_volume.IsEnabled ? "" : muted ? "Muted" : $"{Math.Round(_volume.Value).ToString(CultureInfo.CurrentCulture)}%";
        _percent.SetResourceReference(TextBlock.ForegroundProperty, muted ? "WarningTextBrush" : "IslandTextSecondaryBrush");
    }

    // The speaker: mutes or unmutes; a failure says so where the percentage is, for a few seconds.
    private async Task ToggleMuteAsync()
    {
        if (_volumeSource?.SetMuted is not { } setMuted)
            return;
        try
        {
            await setMuted(!Muted);
        }
        catch (Exception ex)
        {
            Log.Warn("Island: muting the output from the home failed", ex);
            _percent.Text = "Failed";
            _percent.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
            _volumeFailed.Stop();
            _volumeFailed.Start();
            return;
        }
        SetVolume(_volumeSource, _volumeKey);
    }

    /// <summary>Puts Chippi on the puck's side of the footer (the side the panel opened from) and Settings on the other.</summary>
    internal void SetPetSide(bool right)
    {
        double inset = FooterPet - FooterPetSize / 2;
        Pet.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Pet.Margin = new Thickness(right ? 0 : inset, 0, right ? inset : 0, inset);
        _settings.HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _settings.Margin = new Thickness(right ? 10 : 0, 0, right ? 0 : 10, 0);
    }

    public void SetTiles(IReadOnlyList<IslandTileView> tiles)
    {
        if (tiles.SequenceEqual(_shown))
            return;
        // The focus comes back to the same part (a button or its arrow) of the same tile.
        var parts = Parts();
        int focusedAt = parts.FindIndex(p => p.Part.IsKeyboardFocused);
        string? focusedKey = focusedAt < 0 ? null : _shown[parts[focusedAt].Tile].Key;
        bool focusedArrow = focusedAt > 0 && parts[focusedAt - 1].Tile == parts[focusedAt].Tile;
        _shown = tiles;
        _tiles.Rows = Math.Max(1, (Math.Min(tiles.Count, MaxTiles) + Columns - 1) / Columns);
        _tiles.Children.Clear();
        foreach (var tile in tiles.Take(MaxTiles))
            _tiles.Children.Add(Tile(tile));
        if (focusedKey is not null && Parts().Where(p => _shown[p.Tile].Key == focusedKey).ToList() is { Count: > 0 } again)
            again[focusedArrow ? again.Count - 1 : 0].Part.Focus();
    }

    /// <summary>The buttons in order: each tile's own (its toggle, when it has one).</summary>
    internal IEnumerable<ButtonBase> Buttons() => _tiles.Children.OfType<Panel>().Select(c => ((Panel)c.Children[0]).Children.OfType<ButtonBase>().First());

    /// <summary>Everything the arrows move through, in reading order: each tile's button, then its arrow when it's split.</summary>
    private List<(int Tile, ButtonBase Part)> Parts() =>
        [.. _tiles.Children.OfType<Panel>().SelectMany((c, i) => ((Panel)c.Children[0]).Children.OfType<ButtonBase>().Select(b => (i, b)))];

    private StackPanel Tile(IslandTileView tile)
    {
        var glyph = new LucideIcon { Data = tile.Icon, Size = 16, HorizontalAlignment = HorizontalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, tile.On ? "IslandOnAccentBrush" : tile.Caution ? "WarningBrush" : "IslandTextBrush");
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
            string page = tile.Page ?? tile.Name;
            var side = new Button { Content = arrowGlyph, Tag = tile.On ? "On" : null, ToolTip = page };
            side.SetResourceReference(StyleProperty, "IslandSplitSideStyle");
            arrowGlyph.SetBinding(LucideIcon.BrushProperty, new System.Windows.Data.Binding(nameof(Foreground)) { Source = side });
            AutomationProperties.SetName(side, $"Open {page}");
            side.Click += (_, _) => TileOpened?.Invoke(this, tile.Key);
            box.ColumnDefinitions.Add(new ColumnDefinition());
            box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(side, 1);
            box.Children.Add(toggle);
            box.Children.Add(side);
            box.SizeChanged += (_, _) => Span(toggle, side);
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

    /// <summary>
    /// Runs one fill across a split's toggle and its arrow (FillFade.Span), so on, the accent gradient doesn't start
    /// again at the arrow and the split reads as one button with a divider.
    /// </summary>
    private static void Span(FrameworkElement main, FrameworkElement side)
    {
        double m = main.ActualWidth, s = side.ActualWidth;
        if (m <= 0 || s <= 0)
            return;
        var toMain = new ScaleTransform((m + s) / m, 1);
        var toSide = new MatrixTransform((m + s) / s, 0, 0, 1, -m / s, 0);
        toMain.Freeze();
        toSide.Freeze();
        FillFade.SetSpan(main, toMain);
        FillFade.SetSpan(side, toSide);
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

    // Enter opens the focused tile (on a split's arrow too; a toggle toggles); the arrow keys move (Next). Tab still leaves the grid.
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not ButtonBase focused || TileOf(focused) is not { } key)
            return;
        if (e.Key == Key.Enter && focused is Button)
        {
            TileOpened?.Invoke(this, key);
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            Next(focused, e.Key)?.Focus();
            e.Handled = true;
        }
    }

    /// <summary>The tile a button or a split's arrow belongs to; null for anything else.</summary>
    internal string? TileOf(ButtonBase part)
    {
        var parts = Parts();
        int at = parts.FindIndex(p => p.Part == part);
        return at < 0 ? null : _shown[parts[at].Tile].Key;
    }

    /// <summary>
    /// Where an arrow key moves from <paramref name="from"/>: left and right through the buttons and the split ones' arrows
    /// in reading order, up and down to the button a row of four away. Null at an edge.
    /// </summary>
    internal ButtonBase? Next(ButtonBase from, Key key)
    {
        var parts = Parts();
        int at = parts.FindIndex(p => p.Part == from);
        if (at < 0)
            return null;
        int tile = parts[at].Tile;
        ButtonBase? Row(int to) => parts.FirstOrDefault(p => p.Tile == to).Part;
        return key switch
        {
            Key.Left => at > 0 ? parts[at - 1].Part : null,
            Key.Right => at + 1 < parts.Count ? parts[at + 1].Part : null,
            Key.Up => Row(tile - Columns),
            Key.Down => Row(tile + Columns),
            _ => null,
        };
    }
}
