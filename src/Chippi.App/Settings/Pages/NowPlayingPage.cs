// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Chippi.App.Island.Media;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

/// <summary>
/// Settings › Now Playing (2026-10-28): the island's Now Playing in one card (96 artwork, title,
/// artist, progress with times, previous / play-pause / next), then the source. It shares the
/// island's sessions, so a source picked in one shows in the other.
/// </summary>
internal sealed class NowPlayingPage : UserControl
{
    private const double Art = 96;

    private readonly IMediaSessions? _media;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Border _art = new() { Width = Art, Height = Art, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 16, 0) };
    private readonly TextBlock _title = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Grid _progress = new() { Height = 4, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _elapsed = new();
    private readonly TextBlock _remaining = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid _times = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly IconButton _previous = new() { Icon = LucideIcons.SkipBack };
    private readonly IconButton _next = new() { Icon = LucideIcons.SkipForward };
    private readonly Button _playPause = PlayButton();
    private readonly LucideIcon _playGlyph = new() { Data = LucideIcons.Play, Size = 16 };
    private readonly ComboBox _source = SettingsLayout.Combo([]);
    private bool _showing;

    public NowPlayingPage(IMediaSessions? media)
    {
        _media = media;
        _tick.Tick += (_, _) => ShowTime();
        foreach (var text in new[] { _artist, _elapsed, _remaining })
        {
            text.SetResourceReference(StyleProperty, "CaptionTextStyle");
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        }
        _artist.SetResourceReference(StyleProperty, "BodyTextStyle");
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        _progress.ColumnDefinitions.Add(new ColumnDefinition());
        _progress.ColumnDefinitions.Add(new ColumnDefinition());
        var track = new Border { CornerRadius = new CornerRadius(2) };
        track.SetResourceReference(Border.BackgroundProperty, "TrackRemainderBrush");
        Grid.SetColumnSpan(track, 2);
        var fill = new Border { CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _progress.Children.Add(track);
        _progress.Children.Add(fill);
        _times.Children.Add(_elapsed);
        _times.Children.Add(_remaining);

        AutomationProperties.SetName(_previous, "Previous track");
        AutomationProperties.SetName(_next, "Next track");
        _previous.Click += async (_, _) => { if (_media is not null) await _media.PreviousAsync(); };
        _next.Click += async (_, _) => { if (_media is not null) await _media.NextAsync(); };
        _playGlyph.SetResourceReference(LucideIcon.BrushProperty, "OnAccentBrush");
        _playPause.Content = _playGlyph;
        _playPause.Margin = new Thickness(12, 0, 12, 0);
        _playPause.Click += async (_, _) => { if (_media is not null) await _media.PlayPauseAsync(); };
        var transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
            Children = { _previous, _playPause, _next } };

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _title, _artist, _progress, _times } };
        var player = new DockPanel { Margin = new Thickness(16), Children = { _art, transport, info } };
        DockPanel.SetDock(_art, Dock.Left);
        DockPanel.SetDock(transport, Dock.Right);

        AutomationProperties.SetName(_source, "Media source");
        _source.SelectionChanged += (_, _) =>
        {
            if (!_showing && _media is not null && _source.SelectedIndex >= 0 && _source.SelectedIndex < _media.Sessions.Count)
                _media.Select(_media.Sessions[_source.SelectedIndex].Id);
        };

        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Now Playing", ""));
        page.Children.Add(SettingsLayout.Card(player));
        var source = SettingsLayout.Card(SettingsLayout.Row("Source", _source));
        source.Margin = new Thickness(0, 8, 0, 0);
        page.Children.Add(source);
        Content = page;
        Loaded += (_, _) =>
        {
            if (_media is not null) _media.Changed += OnChanged;
            Show();
        };
        Unloaded += (_, _) =>
        {
            if (_media is not null) _media.Changed -= OnChanged;
            _tick.Stop();
        };
        Show();
    }

    /// <summary>A 40 accent circle, the island's play button in Settings colors.</summary>
    private static Button PlayButton()
    {
        var fill = new FrameworkElementFactory(typeof(Ellipse), "Fill");
        fill.SetResourceReference(Shape.FillProperty, "AccentBrush");
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var root = new FrameworkElementFactory(typeof(Grid), "Root");
        root.AppendChild(fill);
        root.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
        template.Triggers.Add(new Trigger { Property = IsMouseOverProperty, Value = true, Setters = { new Setter(OpacityProperty, 0.88, "Fill") } });
        template.Triggers.Add(new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(OpacityProperty, 0.76, "Fill") } });
        template.Triggers.Add(new Trigger { Property = IsEnabledProperty, Value = false, Setters = { new Setter(OpacityProperty, 0.36, "Root") } });
        return new Button { Width = 40, Height = 40, Template = template, Cursor = System.Windows.Input.Cursors.Hand };
    }

    private void OnChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        _showing = true;
        try
        {
            var track = _media?.Current;
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

            var sessions = _media?.Sessions ?? [];
            var names = sessions.Select(s => (object)s.Source).ToList();
            if (_source.ItemsSource is not IList<object> shown || !shown.SequenceEqual(names))
                _source.ItemsSource = names;
            _source.SelectedIndex = track is null ? -1 : sessions.ToList().FindIndex(s => s.Id == track.Id);
            _source.IsEnabled = sessions.Count > 0;

            ShowTime();
            if (playing && timed && IsLoaded)
                _tick.Start();
            else
                _tick.Stop();
        }
        finally { _showing = false; }
    }

    private void ShowArt(ImageSource? image)
    {
        if (image is not null)
        {
            _art.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            _art.Child = null;
            return;
        }
        _art.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
        var glyph = new LucideIcon { Data = LucideIcons.Music, Size = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "TextTertiaryBrush");
        _art.Child = glyph;
    }

    private void ShowTime()
    {
        if (_media?.Current is not { Duration.Ticks: > 0 } track)
            return;
        var at = track.PositionAt(DateTimeOffset.Now);
        double done = Math.Clamp(at / track.Duration, 0, 1);
        _progress.ColumnDefinitions[0].Width = new GridLength(done, GridUnitType.Star);
        _progress.ColumnDefinitions[1].Width = new GridLength(1 - done, GridUnitType.Star);
        _elapsed.Text = NowPlayingView.Clock(at);
        _remaining.Text = "−" + NowPlayingView.Clock(track.Duration - at);
    }
}
