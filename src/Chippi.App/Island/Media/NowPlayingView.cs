// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.App.Pet;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Island.Media;

/// <summary>
/// The Now Playing page (DESIGN.md §5 Island, revised 2026-10-30), after Apple Music's widget: the
/// header names the app (a menu of apps when two or more have sessions), then 156px artwork at the
/// top of the body beside the title, artist and previous / play-pause / next, each disabled when the
/// app doesn't offer it, with the progress under them. No artwork shows ChippiPet in its place;
/// nothing playing, ChippiPet asleep.
/// </summary>
internal sealed class NowPlayingView : UserControl
{
    private const double Art = 156;

    private readonly IMediaSessions _media;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Button _source = new();
    private readonly TextBlock _sourceText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly LucideIcon _chevron = new() { Data = LucideIcons.ChevronDown, Size = 12, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Popup _sources = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private readonly Border _art = new() { Width = Art, Height = Art, CornerRadius = new CornerRadius(6), ClipToBounds = true };
    private readonly ChippiPet _artPet = new() { Size = 76, Mood = PetMood.Listening, IsDecorative = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new()
    {
        FontSize = 20, FontWeight = FontWeights.SemiBold, LineHeight = 26, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        MaxHeight = 52, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _artist = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Grid _progress = new() { Height = 18, Margin = new Thickness(10, 0, 10, 0) };
    private readonly Border _fill = new() { Height = 4, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(2) };
    private readonly TextBlock _elapsed = new() { FontSize = 11, MinWidth = 30, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _remaining = new() { FontSize = 11, MinWidth = 30, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _timeline = new() { Margin = new Thickness(0, 22, 0, 0) };
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly LucideIcon _playGlyph = new() { Data = LucideIcons.Play, Size = 24, Filled = true };
    private readonly Button _next;
    private readonly StackPanel _player;
    private readonly ChippiPet _asleep = new() { Size = 72, Mood = PetMood.Sleepy, IsDecorative = true, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _empty;

    public NowPlayingView(IMediaSessions media, Action? back = null)
    {
        _media = media;
        SetResourceReference(ForegroundProperty, "IslandTextBrush");
        _tick.Tick += (_, _) => ShowTime();

        // The header: the page's name, and on the right the app playing, like every page's status.
        var heading = new TextBlock { Text = "Now Playing", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _source.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        _source.Width = double.NaN;
        _source.Margin = new Thickness(0, 0, -6, 0);
        _chevron.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        _sourceText.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _source.Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 6, 0), Children = { _sourceText, _chevron } };
        AutomationProperties.SetName(_source, "Media source");
        _source.Click += (_, _) => OpenSources();
        _sources.PlacementTarget = _source;
        DockPanel.SetDock(_source, Dock.Right);
        var header = new DockPanel { Height = IslandPage.HeaderHeight, Margin = new Thickness(0, 0, 0, IslandPage.Gap), LastChildFill = true, Children = { _source, heading } };
        if (back is not null)
            header.Children.Insert(0, IslandPanelWindow.BackButton(back));

        // The card: artwork, then the title and artist over the transport; the progress under both.
        // Level with the art: the title's capitals with its top, the play disc with its bottom.
        RenderOptions.SetBitmapScalingMode(_art, BitmapScalingMode.HighQuality);
        _art.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        _artist.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _title.SizeChanged += (_, _) => Tip(_title);
        _artist.SizeChanged += (_, _) => Tip(_artist);
        _previous = Transport(Glyph(LucideIcons.SkipBack, 24), 48, "Previous track", () => _media.PreviousAsync());
        _playPause = Transport(_playGlyph, 52, "Play", () => _media.PlayPauseAsync());
        _next = Transport(Glyph(LucideIcons.SkipForward, 24), 48, "Next track", () => _media.NextAsync());
        // The play disc: the accent fill and an on-accent glyph, with no shadow (the No-Glow Rule).
        _playPause.SetResourceReference(StyleProperty, "IslandPlayDiscStyle");
        _playPause.Width = _playPause.Height = 52;
        _playPause.Margin = new Thickness(8, 0, 8, 0);
        _playGlyph.SetResourceReference(LucideIcon.BrushProperty, "IslandOnAccentBrush");
        // Grouped 8 apart, the previous glyph on the title's left edge (12 in from its 48 button).
        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(-12, 0, 0, 0), Children = { _previous, _playPause, _next } };
        var meta = new Grid { Children = { new StackPanel { Margin = new Thickness(0, -6, 0, 0), Children = { _title, _artist } }, transport } };
        Grid.SetColumn(meta, 2);
        var row = new Grid { Height = Art, ColumnDefinitions = { new() { Width = new GridLength(Art) }, new() { Width = new GridLength(16) }, new() }, Children = { _art, meta } };

        foreach (var time in new[] { _elapsed, _remaining })
        {
            time.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
            Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        }
        _progress.ColumnDefinitions.Add(new ColumnDefinition());
        _progress.ColumnDefinitions.Add(new ColumnDefinition());
        // A bar, not a slider: the page shows the position but doesn't seek, so it has no thumb. The track
        // is the faint empty-bar fill, so the tertiary fill of a paused track still shows where it is.
        var bar = new Border { Height = 4, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(2) };
        bar.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush");
        Grid.SetColumnSpan(bar, 2);
        _progress.Children.Add(bar);
        _progress.Children.Add(_fill);
        _timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _timeline.ColumnDefinitions.Add(new ColumnDefinition());
        _timeline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_progress, 1);
        Grid.SetColumn(_remaining, 2);
        _timeline.Children.Add(_elapsed);
        _timeline.Children.Add(_progress);
        _timeline.Children.Add(_remaining);
        // From the top of the body, like the approved mock (Travis, 2026-10-30).
        _player = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Children = { row, _timeline } };

        var nothing = new TextBlock { Text = "Nothing playing", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        nothing.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _asleep, nothing } };

        var body = new Grid { Height = IslandPage.BodyHeight, Children = { _player, _empty } };
        Content = new StackPanel { Children = { header, body } };
        Loaded += (_, _) =>
        {
            _media.Changed += OnChanged;
            Show();
        };
        Unloaded += (_, _) =>
        {
            _media.Changed -= OnChanged;
            _tick.Stop();
            _sources.IsOpen = false;
        };
        Show();
    }

    /// <summary>ChippiPet's color here, the user's choice in Settings › Island &amp; pet.</summary>
    internal void SetPetColor(PetColor color) => _artPet.PetColor = _asleep.PetColor = color;

    private static LucideIcon Glyph(Geometry data, double size) => new() { Data = data, Size = size, Filled = true };

    private static Button Transport(LucideIcon icon, double box, string name, Func<Task> run)
    {
        var button = new Button { Width = box, Height = box, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "IslandTransportButtonStyle");
        icon.SetResourceReference(LucideIcon.BrushProperty, "IslandTextBrush");
        button.Content = icon;
        AutomationProperties.SetName(button, name);
        button.Click += async (_, _) => await run();
        return button;
    }

    private void OnChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        var track = _media.Current;
        _player.Visibility = _source.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
        _empty.Visibility = track is null ? Visibility.Visible : Visibility.Collapsed;
        _sourceText.Text = track?.Source ?? "";
        // One app is no choice: its name shows without the chevron and doesn't open the menu.
        bool choice = _media.Sessions.Count > 1;
        _chevron.Visibility = choice ? Visibility.Visible : Visibility.Collapsed;
        _source.IsHitTestVisible = _source.Focusable = choice;
        if (track is null)
        {
            _tick.Stop();
            return;
        }
        _title.Text = track.Title.Length > 0 ? track.Title : track.Source;
        _artist.Text = track.Artist;
        _artist.Visibility = track.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Tip(_title);
        Tip(_artist);
        ShowArt(track.Artwork);

        // Without a timeline the row keeps its place: the progress hides but holds its space.
        bool timed = track.Duration.Ticks > 0;
        _timeline.Visibility = timed ? Visibility.Visible : Visibility.Hidden;
        _previous.IsEnabled = track.CanPrevious;
        _next.IsEnabled = track.CanNext;
        _playPause.IsEnabled = track.PlayPause != PlayPauseCommand.None;
        _playGlyph.Data = track.Playing ? LucideIcons.Pause : LucideIcons.Play;
        AutomationProperties.SetName(_playPause, track.Playing ? "Pause" : "Play");
        _fill.SetResourceReference(Border.BackgroundProperty, track.Playing ? "IslandAccentFillBrush" : "IslandTextTertiaryBrush");
        ShowTime();
        if (track.Playing && timed && IsLoaded)
            _tick.Start();
        else
            _tick.Stop();
    }

    // A title or artist cut off by its lines shows in full in a tooltip.
    private static void Tip(TextBlock block) =>
        block.ToolTip = block.ActualWidth > 0 && IslandGrid.Cut(block, block.Text) ? block.Text : null;

    // A cover keeps a hairline edge, so a dark one doesn't sink into the island.
    private void ShowArt(ImageSource? image)
    {
        if (image is not null)
        {
            if (_art.Background is not ImageBrush { ImageSource: var shown } || shown != image)
                _art.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            _art.BorderThickness = new Thickness(1);
            _art.Child = null;
            return;
        }
        _art.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
        _art.BorderThickness = new Thickness(0);
        _art.Child = _artPet;
    }

    private void ShowTime()
    {
        if (_media.Current is not { Duration.Ticks: > 0 } track)
            return;
        var at = track.PositionAt(DateTimeOffset.Now);
        double done = Math.Clamp(at / track.Duration, 0, 1);
        _progress.ColumnDefinitions[0].Width = new GridLength(done, GridUnitType.Star);
        _progress.ColumnDefinitions[1].Width = new GridLength(1 - done, GridUnitType.Star);
        _elapsed.Text = Clock(at);
        _remaining.Text = "−" + Clock(track.Duration - at);
    }

    internal static string Clock(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private void OpenSources()
    {
        var list = new StackPanel { Margin = new Thickness(4) };
        foreach (var session in _media.Sessions)
        {
            // The app over what it has on, so two apps are told apart by their tracks; the playing one is marked.
            var name = new TextBlock { Text = session.Source, FontSize = 12, FontWeight = session.Id == _media.Current?.Id ? FontWeights.SemiBold : FontWeights.Normal };
            var title = new TextBlock { Text = session.Title, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
            title.Visibility = session.Title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var row = new DockPanel { LastChildFill = true };
            if (session.Playing)
            {
                var playing = new LucideIcon { Data = LucideIcons.AudioLines, Size = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                playing.SetResourceReference(LucideIcon.BrushProperty, "IslandAccentBrush");
                DockPanel.SetDock(playing, Dock.Right);
                row.Children.Add(playing);
            }
            row.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, title } });
            var item = new Button { Content = row, Height = 40, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            item.SetResourceReference(StyleProperty, "IslandMenuItemStyle");
            var id = session.Id;
            item.Click += (_, _) =>
            {
                _sources.IsOpen = false;
                _media.Select(id);
            };
            list.Children.Add(item);
        }
        // Under the header's source, its right edge on the source's.
        const double width = 220;
        var menu = new Border { Width = width, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = list };
        menu.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        menu.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        _sources.Child = menu;
        _sources.HorizontalOffset = _source.ActualWidth - width;
        _sources.VerticalOffset = 4;
        _sources.IsOpen = true;
    }
}
