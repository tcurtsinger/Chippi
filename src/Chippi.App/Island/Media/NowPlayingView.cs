// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.UI.Controls;

namespace Chippi.App.Island.Media;

/// <summary>
/// The Now Playing page (DESIGN.md §5 Island): source picker, 112px artwork (a neutral placeholder
/// without one), title and source, the progress bar with elapsed and remaining time, and previous /
/// play-pause / next, each disabled when the app doesn't offer it. Nothing playing shows an empty state.
/// </summary>
internal sealed class NowPlayingView : UserControl
{
    private const double Art = 112;

    private readonly IMediaSessions _media;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Button _source = new();
    private readonly TextBlock _sourceText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Popup _sources = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private readonly Border _art = new() { Width = Art, Height = Art, CornerRadius = new CornerRadius(8), ClipToBounds = true };
    private readonly TextBlock _title = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Grid _progress = new() { Height = 4, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _elapsed = new() { FontSize = 12 };
    private readonly TextBlock _remaining = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid _times = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly LucideIcon _playGlyph = new() { Data = LucideIcons.Play, Size = 16, Brush = Brushes.Black };
    private readonly Button _next;

    public NowPlayingView(IMediaSessions media)
    {
        _media = media;
        _tick.Tick += (_, _) => ShowTime();

        var heading = new TextBlock { Text = "Now Playing", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _source.SetResourceReference(StyleProperty, "IslandTransportButtonStyle");
        _source.Width = double.NaN;
        _source.Height = 24;
        var chevron = new LucideIcon { Data = LucideIcons.ChevronDown, Size = 12, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        _sourceText.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _source.Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 6, 0), Children = { _sourceText, chevron } };
        AutomationProperties.SetName(_source, "Media source");
        _source.Click += (_, _) => OpenSources();
        _sources.PlacementTarget = _source;
        DockPanel.SetDock(_source, Dock.Right);
        var header = new DockPanel { Height = 28, LastChildFill = true, Children = { _source, heading } };

        _artist.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _elapsed.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _remaining.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _progress.ColumnDefinitions.Add(new ColumnDefinition());
        _progress.ColumnDefinitions.Add(new ColumnDefinition());
        var track = new Border { CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, "IslandTrackBrush");
        Grid.SetColumnSpan(track, 2);
        var fill = new Border { CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, "IslandAccentBrush");
        _progress.Children.Add(track);
        _progress.Children.Add(fill);
        _times.Children.Add(_elapsed);
        _times.Children.Add(_remaining);

        _previous = Transport(LucideIcons.SkipBack, "Previous track", () => _media.PreviousAsync());
        _next = Transport(LucideIcons.SkipForward, "Next track", () => _media.NextAsync());
        _playPause = new Button { Content = _playGlyph, Margin = new Thickness(16, 0, 16, 0) };
        _playPause.SetResourceReference(StyleProperty, "IslandPlayLargeButtonStyle");
        _playPause.Click += async (_, _) => await _media.PlayPauseAsync();
        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0),
            Children = { _previous, _playPause, _next } };

        var info = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children = { _title, _artist, _progress, _times, transport } };
        Grid.SetColumn(info, 1);
        var body = new Grid { Margin = new Thickness(0, 8, 0, 0), ColumnDefinitions = { new() { Width = new GridLength(Art) }, new() }, Children = { _art, info } };

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

    private Button Transport(Geometry glyph, string name, Func<Task> run)
    {
        var button = new Button { VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "IslandTransportButtonStyle");
        var icon = new LucideIcon { Data = glyph, Size = 16 };
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
        _source.Visibility = _media.Sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _sourceText.Text = track?.Source ?? "";
        _title.Text = track is null ? "Nothing playing" : track.Title.Length > 0 ? track.Title : track.Source;
        _artist.Text = track is null ? "" : track.Artist.Length > 0 ? track.Artist : track.Source;
        _artist.Visibility = _artist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowArt(track?.Artwork);

        bool timed = track is { Duration.Ticks: > 0 };
        _progress.Visibility = _times.Visibility = timed ? Visibility.Visible : Visibility.Collapsed;
        _previous.IsEnabled = track?.CanPrevious == true;
        _next.IsEnabled = track?.CanNext == true;
        _playPause.IsEnabled = track is not null && track.PlayPause != PlayPauseCommand.None;
        bool playing = track?.Playing == true;
        _playGlyph.Data = playing ? LucideIcons.Pause : LucideIcons.Play;
        AutomationProperties.SetName(_playPause, playing ? "Pause" : "Play");
        ShowTime();
        if (playing && timed && IsLoaded)
            _tick.Start();
        else
            _tick.Stop();
    }

    private void ShowArt(ImageSource? image)
    {
        if (image is not null)
        {
            _art.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            _art.Child = null;
            return;
        }
        _art.SetResourceReference(Border.BackgroundProperty, "IslandPanelBrush");
        var glyph = new LucideIcon { Data = LucideIcons.Music, Size = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        _art.Child = glyph;
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
            var item = new Button { Content = session.Source };
            item.SetResourceReference(StyleProperty, "IslandMenuItemStyle");
            if (session.Id == _media.Current?.Id)
                item.FontWeight = FontWeights.SemiBold;
            var id = session.Id;
            item.Click += (_, _) =>
            {
                _sources.IsOpen = false;
                _media.Select(id);
            };
            list.Children.Add(item);
        }
        var menu = new Border { MinWidth = 160, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = list };
        menu.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        menu.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        _sources.Child = menu;
        _sources.IsOpen = true;
    }
}
