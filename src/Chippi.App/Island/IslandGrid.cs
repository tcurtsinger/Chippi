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
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Pet;

namespace Chippi.App.Island;

/// <summary>One destination and its live state. On is the module's actual switch state, independent of Caution.</summary>
internal sealed record IslandTileView(string Key, string Name, Geometry Icon, string Status, bool On = false,
    bool Caution = false, bool HasSwitch = false, string? Page = null, bool Waiting = false);

/// <summary>The tool gallery, its separate quick controls, and the media and pet footer.</summary>
internal sealed class IslandGrid : UserControl
{
    internal const int Columns = 4;
    // The footer pet meets the widget on its opening corner.
    internal const double FooterPet = 25, FooterPetSize = PetSize.Footer;
    private const double HeaderHeight = 44, QuickHeight = 42, FooterHeight = 45, TileRowHeight = 77;
    private const double WheelStep = 2;

    private readonly UniformGrid _tiles = new() { Columns = Columns, Rows = 1, Margin = new Thickness(0, 0, -IslandGalleryTile.Gutter, 0) };
    private readonly StackPanel _switches = new() { Orientation = Orientation.Horizontal };
    private readonly IslandHomeMedia _media = new();
    private readonly TextBlock _heading = new() { Text = "Tools", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _empty = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _emptyAction = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _find = new();
    private readonly Button _back = IconButton(LucideIcons.ChevronLeft, "Back to tools");
    private readonly SearchBox _search = new() { Placeholder = "Find a tool", Height = 28, FontSize = 13 };
    private readonly Border _quickRow = new() { Height = QuickHeight, Visibility = Visibility.Collapsed };
    private readonly Grid _volumeRow = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Visibility = Visibility.Collapsed };
    private readonly Slider _volume = new() { Width = 96, Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new() { FontSize = 11, Width = 32, TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _noOutput = new() { Text = "No output", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly LucideIcon _speakerGlyph = new() { Data = LucideIcons.Volume, Size = 15 };
    private readonly Button _speaker = new();
    private readonly Button _settings = IconButton(LucideIcons.Settings, "Settings");
    private readonly Button _boop = new() { Width = 32, Height = 32, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly DispatcherTimer _volumeFailed = new() { Interval = TimeSpan.FromSeconds(4) };
    private IReadOnlyList<IslandTileView> _shown = [];
    private IslandTileVolume? _volumeSource;
    private string? _volumeKey;
    private bool _refreshing;

    public IslandGrid()
    {
        Pet = new ChippiPet { Size = FooterPetSize, Mood = PetMood.Idle, IsDecorative = true,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        _emptyAction.SetResourceReference(StyleProperty, "IslandButtonStyle");
        _emptyAction.Click += (_, _) =>
        {
            if (IsSearching && !string.IsNullOrWhiteSpace(_search.Text))
            {
                _search.Focus();
                _search.Clear();
            }
            else SettingsRequested?.Invoke(this, EventArgs.Empty);
        };
        var body = new Grid { Margin = new Thickness(0, -12, 0, -12) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FooterHeight) });

        var emptyState = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _empty, _emptyAction } };
        var gallery = new Grid { Children = { _tiles, emptyState } };
        Grid.SetRow(gallery, 1);
        Grid.SetRow(_quickRow, 2);
        var footer = BuildFooter();
        Grid.SetRow(footer, 3);
        body.Children.Add(BuildHeader());
        body.Children.Add(gallery);
        body.Children.Add(_quickRow);
        body.Children.Add(footer);
        BuildQuickRow();
        Content = body;
        Filter();
        SetPetSide(right: false);
        KeyboardNavigation.SetDirectionalNavigation(_tiles, KeyboardNavigationMode.None);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, e) => { OpenSearch(); e.Handled = true; }));
        PreviewKeyDown += OnKey;
    }

    public ChippiPet Pet { get; }
    internal IslandHomeMedia Media => _media;
    public event EventHandler<string>? TileOpened;
    public event EventHandler<string>? TileToggled;
    public event EventHandler? SettingsRequested;
    internal event EventHandler? SearchRequested;
    internal bool IsSearching => _search.Visibility == Visibility.Visible;
    // The card sizes to the unfiltered tool set so typing a query doesn't move the widget or its controls.
    internal double HomeHeight => Math.Min(IslandPage.Height, HeaderHeight + FooterHeight + IslandGalleryTile.Gutter
        + TileRowHeight * _tiles.Rows + (_quickRow.Visibility == Visibility.Visible ? QuickHeight : 0));

    internal void SetMedia(IMediaSessions? media) => _media.SetMedia(media);

    private static Button IconButton(Geometry icon, string name)
    {
        var glyph = new LucideIcon { Data = icon, Size = 16 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var button = new Button { Content = glyph, ToolTip = name };
        button.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, name);
        return button;
    }

    private Grid BuildHeader()
    {
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.Visibility = _back.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(_search, "Find a tool");
        _search.TextChanged += (_, _) => Filter();
        var findGlyph = new LucideIcon { Data = LucideIcons.Search, Size = 14, Margin = new Thickness(0, 0, 6, 0) };
        findGlyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        _find.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { findGlyph, new TextBlock { Text = "Find a tool" } } };
        _find.SetResourceReference(StyleProperty, "IslandButtonStyle");
        _find.Padding = new Thickness(9, 0, 9, 0);
        _find.ToolTip = "Find a tool (Ctrl+F)";
        AutomationProperties.SetName(_find, "Find a tool");
        AutomationProperties.SetAcceleratorKey(_find, "Ctrl+F");
        _find.Click += (_, _) => OpenSearch();
        _back.Click += (_, _) => CloseSearch();
        Grid.SetColumn(_heading, 1);
        Grid.SetColumn(_search, 1);
        Grid.SetColumn(_find, 2);
        foreach (var item in new UIElement[] { _back, _heading, _search, _find }) bar.Children.Add(item);
        return bar;
    }

    private void OpenSearch()
    {
        _heading.Visibility = _find.Visibility = Visibility.Collapsed;
        _back.Visibility = _search.Visibility = Visibility.Visible;
        SearchRequested?.Invoke(this, EventArgs.Empty);
        _search.Focus();
        _search.SelectAll();
    }

    internal bool CloseSearch()
    {
        if (!IsSearching) return false;
        // Move focus while the field is still visible, before WPF drops its keyboard focus.
        _heading.Visibility = _find.Visibility = Visibility.Visible;
        if (IsKeyboardFocusWithin) _find.Focus();
        _back.Visibility = _search.Visibility = Visibility.Collapsed;
        _search.Clear();
        return true;
    }

    internal bool FocusTile(string? key)
    {
        if (key == IslandPanelWindow.NowPlayingKey)
            return _media.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        if (Buttons().FirstOrDefault(b => Equals(b.Tag, key)) is { } tile) return tile.Focus();
        if (IsSearching) return _search.Focus();
        return (Buttons().FirstOrDefault() ?? _find).Focus();
    }

    private Border BuildFooter()
    {
        _settings.Width = _settings.Height = 32;
        _settings.VerticalAlignment = VerticalAlignment.Bottom;
        _settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _boop.Content = Pet;
        _boop.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(_boop, "Chippi");
        _boop.Click += (_, _) => Pet.Boop();
        _media.VerticalAlignment = VerticalAlignment.Bottom;
        _media.OpenRequested += (_, _) => TileOpened?.Invoke(this, IslandPanelWindow.NowPlayingKey);
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(-12, 0, -12, 0),
            Child = new Grid { Children = { _media, _settings, _boop } } };
        footer.SetResourceReference(Border.BackgroundProperty, "IslandFooterBrush");
        footer.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
        KeyboardNavigation.SetTabNavigation(footer, KeyboardNavigationMode.Local);
        KeyboardNavigation.SetControlTabNavigation(footer, KeyboardNavigationMode.Local);
        return footer;
    }

    internal void SetPetSide(bool right)
    {
        double inset = FooterPet - _boop.Width / 2;
        _boop.HorizontalAlignment = _settings.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _boop.Margin = new Thickness(inset, 0, inset, inset);
        _settings.Margin = new Thickness(inset + 36, 0, inset + 36, inset);
        _media.Margin = right ? new Thickness(8, 0, 82, inset) : new Thickness(82, 0, 8, inset);
        _boop.TabIndex = right ? 2 : 0;
        _settings.TabIndex = 1;
        _media.TabIndex = right ? 0 : 2;
        // WPF uses the Button inside the media control as a tab stop. The wrapper is its local order group.
        KeyboardNavigation.SetTabNavigation(_media, KeyboardNavigationMode.Local);
        KeyboardNavigation.SetControlTabNavigation(_media, KeyboardNavigationMode.Local);
    }

    private void BuildQuickRow()
    {
        var controls = new Grid();
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_volumeRow, 1);
        controls.Children.Add(_switches);
        controls.Children.Add(_volumeRow);
        _quickRow.Child = controls;

        _speaker.Content = _speakerGlyph;
        _speaker.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        _speaker.Click += async (_, _) => await ToggleMuteAsync();
        _volumeFailed.Tick += (_, _) => { _volumeFailed.Stop(); SetVolume(_volumeSource, _volumeKey); };
        _volume.SetResourceReference(StyleProperty, "IslandSliderStyle");
        AutomationProperties.SetName(_volume, "System volume");
        _volume.ValueChanged += (_, e) =>
        {
            ShowPercent();
            if (!_refreshing) _volumeSource?.Set(e.NewValue / 100);
        };
        _volumeRow.PreviewMouseWheel += (_, e) =>
        {
            if (!_volume.IsEnabled) return;
            _volume.Value = Math.Clamp(_volume.Value + e.Delta / 120.0 * WheelStep, _volume.Minimum, _volume.Maximum);
            e.Handled = true;
        };
        _noOutput.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_volume, 1);
        Grid.SetColumn(_noOutput, 1);
        Grid.SetColumn(_percent, 2);
        foreach (var p in new UIElement[] { _speaker, _volume, _noOutput, _percent }) _volumeRow.Children.Add(p);
    }

    internal void SetVolume(IslandTileVolume? volume, string? key)
    {
        _volumeSource = volume;
        _volumeKey = key;
        _volumeRow.Visibility = volume is null ? Visibility.Collapsed : Visibility.Visible;
        ShowQuickRow();
        if (volume is null) return;
        var level = volume.Level();
        bool none = level is null;
        bool muted = !none && volume.Muted?.Invoke() == true;
        _volume.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _noOutput.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        _volume.IsEnabled = !none;
        _speaker.IsEnabled = !none && volume.SetMuted is not null;
        _speakerGlyph.Data = muted || none ? LucideIcons.VolumeMuted : LucideIcons.Volume;
        _speakerGlyph.SetResourceReference(LucideIcon.BrushProperty, none ? "IslandTextTertiaryBrush" : muted ? "WarningBrush" : "IslandTextBrush");
        string action = muted ? "Unmute system volume" : "Mute system volume";
        AutomationProperties.SetName(_speaker, action);
        _speaker.ToolTip = volume.SetMuted is null ? null : action;
        foreach (var brush in new[] { "IslandAccentFillBrush", "IslandAccentBrush" })
        {
            if (muted) _volume.Resources[brush] = TryFindResource("IslandTextTertiaryBrush");
            else _volume.Resources.Remove(brush);
        }
        if (!_volume.IsMouseCaptureWithin)
        {
            _refreshing = true;
            try { _volume.Value = (level ?? 0) * 100; }
            finally { _refreshing = false; }
        }
        ShowPercent();
    }

    private bool Muted => _volume.IsEnabled && _volumeSource?.Muted?.Invoke() == true;

    private void ShowPercent()
    {
        _percent.Visibility = _volume.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_volumeFailed.IsEnabled) return;
        bool muted = Muted;
        _percent.Text = !_volume.IsEnabled ? "" : muted ? "Muted" : $"{Math.Round(_volume.Value).ToString(CultureInfo.CurrentCulture)}%";
        _percent.SetResourceReference(TextBlock.ForegroundProperty, muted ? "WarningTextBrush" : "IslandTextSecondaryBrush");
    }

    private async Task ToggleMuteAsync()
    {
        if (_volumeSource?.SetMuted is not { } setMuted) return;
        try { await setMuted(!Muted); }
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

    public void SetTiles(IReadOnlyList<IslandTileView> tiles)
    {
        if (tiles.SequenceEqual(_shown)) return;
        string? refocusTile = null;
        string? refocusSwitch = null;
        var built = _tiles.Children.OfType<IslandGalleryTile>().ToList();
        var switches = _switches.Children.OfType<IslandGallerySwitch>().ToList();
        var quick = tiles.Where(t => t.HasSwitch).ToList();
        if (tiles.Select(t => t.Key).SequenceEqual(_shown.Select(t => t.Key)))
        {
            for (int i = 0; i < built.Count; i++)
                if (tiles[i] != _shown[i]) built[i].Show(tiles[i]);
        }
        else
        {
            refocusTile = built.FirstOrDefault(b => b.IsKeyboardFocused)?.Tag as string;
            _tiles.Children.Clear();
            foreach (var tile in tiles)
                _tiles.Children.Add(new IslandGalleryTile(tile, key => TileOpened?.Invoke(this, key)));
            _tiles.Rows = Math.Max(1, (tiles.Count + Columns - 1) / Columns);
        }
        if (quick.Select(t => t.Key).SequenceEqual(switches.Select(b => (string)b.Tag)))
        {
            for (int i = 0; i < quick.Count; i++) switches[i].Show(quick[i]);
        }
        else
        {
            refocusSwitch = switches.FirstOrDefault(b => b.IsKeyboardFocused)?.Tag as string;
            _switches.Children.Clear();
            foreach (var tile in quick)
                _switches.Children.Add(new IslandGallerySwitch(tile, key => TileToggled?.Invoke(this, key)));
        }
        _shown = tiles;
        Filter();
        ShowQuickRow();
        if (refocusTile is not null) FocusTile(refocusTile);
        if (refocusSwitch is not null && _switches.Children.OfType<IslandGallerySwitch>()
            .FirstOrDefault(b => Equals(b.Tag, refocusSwitch))?.Focus() != true) FocusTile(null);
    }

    private void ShowQuickRow() => _quickRow.Visibility = _switches.Children.Count > 0 || _volumeSource is not null
        ? Visibility.Visible : Visibility.Collapsed;

    private void Filter()
    {
        string query = _search.Text.Trim();
        for (int i = 0; i < _shown.Count; i++)
            _tiles.Children[i].Visibility = _shown[i].Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = _emptyAction.Visibility = Buttons().Any() ? Visibility.Collapsed : Visibility.Visible;
        bool filtering = query.Length > 0;
        _empty.Text = filtering ? "No matching tools" : "No tools to show";
        string action = filtering ? "Clear search" : "Open settings";
        _emptyAction.Content = action;
        AutomationProperties.SetName(_emptyAction, action);
    }

    internal IEnumerable<ButtonBase> Buttons() => _tiles.Children.OfType<ButtonBase>().Where(b => b.Visibility == Visibility.Visible);
    internal string? TileOf(ButtonBase button) => button is IslandGalleryTile ? button.Tag as string : null;

    internal ButtonBase? Next(ButtonBase from, Key key)
    {
        var buttons = Buttons().ToList();
        int at = buttons.IndexOf(from);
        if (at < 0) return null;
        int next = key switch { Key.Left => at - 1, Key.Right => at + 1, Key.Up => at - Columns, Key.Down => at + Columns, _ => -1 };
        return next >= 0 && next < buttons.Count ? buttons[next] : null;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (IsSearching && e.Key == Key.Escape)
        {
            e.Handled = CloseSearch();
            return;
        }
        if (_search.IsKeyboardFocused && e.Key == Key.Enter)
        {
            if (Buttons().FirstOrDefault()?.Tag is string key) TileOpened?.Invoke(this, key);
            e.Handled = true;
            return;
        }
        if (_search.IsKeyboardFocused && e.Key == Key.Down && Buttons().FirstOrDefault() is { } first)
        {
            first.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Keyboard.FocusedElement is IslandGalleryTile from)
        {
            if (Next(from, e.Key) is { } next) next.Focus();
            else if (IsSearching && e.Key == Key.Up) _search.Focus();
            e.Handled = true;
        }
    }

    internal static bool Cut(TextBlock block, string text)
    {
        var full = new FormattedText(text, CultureInfo.CurrentCulture, block.FlowDirection,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);
        if (block.TextWrapping == TextWrapping.NoWrap) return full.Width > block.ActualWidth + 0.5;
        full.MaxTextWidth = block.ActualWidth;
        if (!double.IsNaN(block.LineHeight)) full.LineHeight = block.LineHeight;
        return full.Height > block.MaxHeight + 0.5;
    }
}
