// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.App.Island.Media;
using Chippi.UI.Pet;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>
/// The home's Now Playing card: 68 tall, the cover, the app, the title and the artist, then previous,
/// play or pause and next. Pressing the card anywhere but its buttons opens the Now Playing page. Nothing
/// playing shows the pet asleep beside "Nothing playing" alone, with no buttons.
/// </summary>
internal sealed class IslandHomeMedia : UserControl
{
    // A refused press says "Failed" on the app's line this long.
    private static readonly TimeSpan FailedHold = TimeSpan.FromSeconds(4);

    private readonly Border _art = new() { Width = 52, Height = 52, CornerRadius = new CornerRadius(4), ClipToBounds = true };
    private readonly ChippiPet _artPet = new() { Size = 28, Mood = PetMood.Sleepy, IsDecorative = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly LucideIcon _note = new() { Data = LucideIcons.Music, Size = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _appIcon = new() { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _source = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly LucideIcon _playGlyph = new() { Data = LucideIcons.Play, Size = 15, Filled = true };
    private readonly Button _card = new() { Height = 68 };
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;
    private readonly StackPanel _transport;
    private readonly StackPanel _text;
    private readonly DockPanel _app;
    private readonly Image _artWas = new(), _textWas = new(); // what was there, fading out as a new track arrives
    private readonly DispatcherTimer _failed = new() { Interval = FailedHold };
    private IMediaSessions? _media;
    private string? _shownTrack;
    private bool _shownPlaying;

    public IslandHomeMedia()
    {
        _note.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        _artist.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        // A dock, not a row: the app's name gets the line's width, so a long one ends in an ellipsis rather than cut mid-letter.
        _app = new DockPanel { Children = { _note, _appIcon, _source } };
        _text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _app, _title, _artist },
        };
        var art = new Grid { Children = { _art, _artWas } };
        var text = new Grid { Margin = new Thickness(12, 0, 0, 0), Children = { _text, _textWas } };
        _title.SizeChanged += (_, _) => Tip();
        _artist.SizeChanged += (_, _) => Tip();
        _failed.Tick += (_, _) =>
        {
            _failed.Stop();
            Show();
        };

        _previous = Transport(Glyph(LucideIcons.SkipBack, 14), 32, "Previous track", () => _media!.PreviousAsync(), "IslandTextBrush");
        _next = Transport(Glyph(LucideIcons.SkipForward, 14), 32, "Next track", () => _media!.NextAsync(), "IslandTextBrush");
        _playPause = Transport(_playGlyph, 36, "Play", () => _media!.PlayPauseAsync(), "IslandOnAccentBrush");
        _playPause.SetResourceReference(StyleProperty, "IslandPlayDiscStyle");
        _transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Children = { _previous, _playPause, _next } };
        DockPanel.SetDock(_transport, Dock.Right);

        // The whole card is the button that opens Now Playing (its hover and press are the card's shape); the transport's
        // buttons sit on it and keep their own. 68 tall: inside the 1px edge, 7 around the 52 art, so it sits 8 from the
        // card's outside and nothing clips it.
        _card.Content = new DockPanel { LastChildFill = true, Children = { _transport, art, text } };
        _card.SetResourceReference(StyleProperty, "IslandCardButtonStyle");
        _card.Padding = new Thickness(7, 7, 10, 7);
        AutomationProperties.SetName(_card, "Now Playing");
        // Only the card's own click: a transport button's bubbles up through it.
        _card.Click += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, _card))
                OpenRequested?.Invoke(this, EventArgs.Empty);
        };
        Content = _card;

        Loaded += (_, _) =>
        {
            if (_media is not null)
                _media.Changed += OnChanged;
            _shownTrack = null; // opening shows what's playing now; only a change while it's out arrives
            Show();
        };
        Unloaded += (_, _) =>
        {
            if (_media is not null)
                _media.Changed -= OnChanged;
        };
        Show();
    }

    /// <summary>The card was pressed (not its transport): open Now Playing.</summary>
    public event EventHandler? OpenRequested;

    internal void SetMedia(IMediaSessions? media)
    {
        if (_media is not null && IsLoaded)
            _media.Changed -= OnChanged;
        _media = media;
        if (_media is not null && IsLoaded)
            _media.Changed += OnChanged;
        Show();
    }

    private static LucideIcon Glyph(System.Windows.Media.Geometry data, double size) => new() { Data = data, Size = size, Filled = true };

    private Button Transport(LucideIcon icon, double box, string name, Func<Task> run, string brush)
    {
        var button = new Button { Width = box, Height = box, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "IslandTransportButtonStyle");
        icon.SetResourceReference(LucideIcon.BrushProperty, brush);
        button.Content = icon;
        AutomationProperties.SetName(button, name);
        button.ToolTip = name;
        button.Click += async (_, e) =>
        {
            e.Handled = true; // the card around it opens Now Playing; a transport press doesn't
            try { await run(); }
            catch (Exception)
            {
                // The player refused (the session logged why): the app's line says so for a moment.
                _failed.Stop();
                _failed.Start();
                Show();
            }
        };
        return button;
    }

    // A cut-off title or artist shows in full in the card's tooltip.
    private void Tip()
    {
        bool cut = IslandGrid.Cut(_title, _title.Text) || (_artist.Visibility == Visibility.Visible && IslandGrid.Cut(_artist, _artist.Text));
        _card.ToolTip = !cut ? null : _artist.Text.Length == 0 ? _title.Text : $"{_title.Text} · {_artist.Text}";
    }

    private void OnChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        var track = _media?.Current;
        // A new track (or none) on a card that's on screen arrives: the old one fades where it was (Arrival).
        string shown = track is null ? "" : $"{track.Id}\n{track.Title}\n{track.Artist}";
        bool arriving = _shownTrack is not null && shown != _shownTrack && Arrival.Showing(_text);
        bool flipped = _shownTrack is not null && shown == _shownTrack && track?.Playing != _shownPlaying && Arrival.Showing(_playGlyph);
        if (shown != _shownTrack)
            _failed.Stop(); // "Failed" was about the track that went
        _shownTrack = shown;
        _shownPlaying = track?.Playing == true;
        if (arriving)
        {
            Arrival.Leave(_art, _artWas);
            Arrival.Leave(_text, _textWas);
        }
        // The app's line names the app (with its icon when it has one) over the title. A track with no title shows the app
        // as its title instead, so the line goes rather than say the app twice. A refused press says "Failed" there.
        bool failed = _failed.IsEnabled && track is not null;
        _source.Text = failed ? "Failed" : track?.Source ?? "";
        _source.SetResourceReference(TextBlock.ForegroundProperty, failed ? "WarningTextBrush" : "IslandTextTertiaryBrush");
        _app.Visibility = failed || (track is { Title.Length: > 0, Source.Length: > 0 }) ? Visibility.Visible : Visibility.Collapsed;
        _appIcon.Source = track?.Icon;
        _appIcon.Visibility = track?.Icon is null ? Visibility.Collapsed : Visibility.Visible;
        _note.Visibility = track?.Icon is null ? Visibility.Visible : Visibility.Collapsed;
        _title.Text = track is null ? "Nothing playing" : track.Title.Length > 0 ? track.Title : track.Source;
        // Nothing playing is a state, not a song: one regular secondary line, centered beside the sleeping pet.
        _title.FontWeight = track is null ? FontWeights.Normal : FontWeights.SemiBold;
        _title.SetResourceReference(TextBlock.ForegroundProperty, track is null ? "IslandTextSecondaryBrush" : "IslandTextBrush");
        _artist.Text = track?.Artist ?? "";
        _artist.Visibility = _artist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Nothing playing has no buttons, as on the page; their room stays, so nothing moves when a song starts.
        _transport.Visibility = track is null ? Visibility.Hidden : Visibility.Visible;
        _previous.IsEnabled = track?.CanPrevious == true;
        _next.IsEnabled = track?.CanNext == true;
        _playPause.IsEnabled = track is not null && track.PlayPause != PlayPauseCommand.None;
        _playGlyph.Data = track?.Playing == true ? LucideIcons.Pause : LucideIcons.Play;
        AutomationProperties.SetName(_playPause, track?.Playing == true ? "Pause" : "Play");
        _playPause.ToolTip = AutomationProperties.GetName(_playPause);
        _artPet.Mood = track is null ? PetMood.Sleepy : PetMood.Listening;
        ShowArt(track?.Artwork);
        // The lines keep their width when the text changes (a new title cut off like the last), so the tooltip is
        // worked out again here, not only when they change size.
        Tip();
        if (arriving)
        {
            Arrival.Enter(_art, rise: false);
            Arrival.Enter(_text, rise: true);
        }
        else if (flipped)
            Arrival.Pop(_playGlyph); // play to pause and back
    }

    private void ShowArt(ImageSource? image)
    {
        if (image is not null)
        {
            if (_art.Background is not ImageBrush { ImageSource: var shown } || shown != image)
                _art.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            _art.BorderThickness = new Thickness(1);
            _art.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
            _art.Child = null;
            return;
        }
        _art.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        _art.BorderThickness = new Thickness(0);
        _art.Child = _artPet;
    }
}
