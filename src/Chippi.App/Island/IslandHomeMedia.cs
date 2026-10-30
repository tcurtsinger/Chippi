// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.App.Island.Media;
using Chippi.App.Pet;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>
/// The home's Now Playing card: 68 tall, the cover, the app, the title and the artist, then previous,
/// play or pause and next. Pressing the card opens the Now Playing page. Nothing playing shows the pet
/// asleep and the transport dimmed.
/// </summary>
internal sealed class IslandHomeMedia : UserControl
{
    private readonly Border _art = new() { Width = 52, Height = 52, CornerRadius = new CornerRadius(4), ClipToBounds = true };
    private readonly Gumdrop _artPet = new() { Size = 28, Mood = PetMood.Sleepy, IsDecorative = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _source = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly LucideIcon _playGlyph = new() { Data = LucideIcons.Play, Size = 15, Filled = true };
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;
    private IMediaSessions? _media;

    public IslandHomeMedia()
    {
        var note = new LucideIcon { Data = LucideIcons.Music, Size = 11, VerticalAlignment = VerticalAlignment.Center };
        note.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        _source.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _title.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
        _artist.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { note, _source } }, _title, _artist },
        };
        var open = new Button { Content = new DockPanel { LastChildFill = true, Children = { _art, text } }, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        open.SetResourceReference(StyleProperty, "IslandBareButtonStyle");
        AutomationProperties.SetName(open, "Now Playing");
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        _previous = Transport(Glyph(LucideIcons.SkipBack, 14), 32, "Previous track", () => _media!.PreviousAsync(), "IslandTextBrush");
        _next = Transport(Glyph(LucideIcons.SkipForward, 14), 32, "Next track", () => _media!.NextAsync(), "IslandTextBrush");
        _playPause = Transport(_playGlyph, 36, "Play", () => _media!.PlayPauseAsync(), "IslandOnAccentBrush");
        _playPause.SetResourceReference(StyleProperty, "IslandPlayDiscStyle");
        var transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Children = { _previous, _playPause, _next } };
        DockPanel.SetDock(transport, Dock.Right);

        var card = new Border { Height = 68, Padding = new Thickness(8, 8, 10, 8), Child = new DockPanel { LastChildFill = true, Children = { transport, open } } };
        card.SetResourceReference(StyleProperty, "IslandCardStyle");
        card.Padding = new Thickness(8, 8, 10, 8);
        Content = card;

        Loaded += (_, _) =>
        {
            if (_media is not null)
                _media.Changed += OnChanged;
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

    private static Button Transport(LucideIcon icon, double box, string name, Func<Task> run, string brush)
    {
        var button = new Button { Width = box, Height = box, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "IslandTransportButtonStyle");
        icon.SetResourceReference(LucideIcon.BrushProperty, brush);
        button.Content = icon;
        AutomationProperties.SetName(button, name);
        button.Click += async (_, _) =>
        {
            try { await run(); }
            catch { /* the player refused; the card shows what it reports next */ }
        };
        return button;
    }

    private void OnChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        var track = _media?.Current;
        _source.Text = track?.Source ?? "";
        _title.Text = track is null ? "Nothing playing" : track.Title.Length > 0 ? track.Title : track.Source;
        _artist.Text = track?.Artist ?? "";
        _artist.Visibility = _artist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _previous.IsEnabled = track?.CanPrevious == true;
        _next.IsEnabled = track?.CanNext == true;
        _playPause.IsEnabled = track is not null && track.PlayPause != PlayPauseCommand.None;
        _playGlyph.Data = track?.Playing == true ? LucideIcons.Pause : LucideIcons.Play;
        AutomationProperties.SetName(_playPause, track?.Playing == true ? "Pause" : "Play");
        ShowArt(track?.Artwork);
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
