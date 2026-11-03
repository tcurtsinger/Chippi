// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Chippi.App.Island.Media;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>The gallery footer opens Now Playing and shows the current track and artist on one line.</summary>
internal sealed class IslandHomeMedia : UserControl
{
    private readonly Button _link = new() { Height = 32, Padding = new Thickness(6, 0, 6, 0) };
    private readonly LucideIcon _glyph = new() { Size = 16 };
    private readonly Border _cover = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(4),
        BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
    private readonly Run _title = new() { FontWeight = FontWeights.SemiBold };
    private readonly Run _detail = new();
    private readonly TextBlock _line = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _previousLine = new() { IsHitTestVisible = false };
    private IMediaSessions? _media;

    public IslandHomeMedia()
    {
        _link.SetResourceReference(StyleProperty, "IslandListRowStyle");
        _glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        _cover.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        _title.SetResourceReference(TextElement.ForegroundProperty, "IslandTextBrush");
        _detail.SetResourceReference(TextElement.ForegroundProperty, "IslandTextSecondaryBrush");
        _line.Inlines.Add(_title);
        _line.Inlines.Add(_detail);

        var arrow = new LucideIcon { Data = LucideIcons.ChevronRight, Size = 12, Margin = new Thickness(8, 0, 0, 0) };
        arrow.SetResourceReference(LucideIcon.BrushProperty, "IslandTextTertiaryBrush");
        var overlay = new Canvas { IsHitTestVisible = false, Children = { _previousLine } };
        var text = new Grid { Margin = new Thickness(8, 0, 0, 0), ClipToBounds = true, Children = { _line, overlay } };
        DockPanel.SetDock(arrow, Dock.Right);
        var art = new Grid { Width = 22, Height = 22, Children = { _glyph, _cover } };
        _link.Content = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Children = { art, arrow, text } };
        _link.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        AutomationProperties.SetName(_link, "Now Playing");
        Content = _link;

        Loaded += (_, _) =>
        {
            if (_media is not null)
                _media.Changed += OnChanged;
            Show(animate: false);
        };
        Unloaded += (_, _) =>
        {
            if (_media is not null)
                _media.Changed -= OnChanged;
        };
        Show(animate: false);
    }

    public event EventHandler? OpenRequested;

    internal void SetMedia(IMediaSessions? media)
    {
        if (_media is not null && IsLoaded)
            _media.Changed -= OnChanged;
        _media = media;
        if (_media is not null && IsLoaded)
            _media.Changed += OnChanged;
        Show(animate: false);
    }

    private void OnChanged(object? sender, EventArgs e) => Show(animate: true);

    private void Show(bool animate)
    {
        var track = _media?.Current;
        _glyph.Data = track?.Playing == true ? LucideIcons.AudioLines : LucideIcons.Music;
        var artwork = track?.Artwork;
        if ((_cover.Background as ImageBrush)?.ImageSource != artwork)
            _cover.Background = artwork is null ? null : new ImageBrush(artwork) { Stretch = Stretch.UniformToFill };
        _cover.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        _glyph.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
        string title = track is null ? "Now Playing" : track.Title.Length > 0 ? track.Title : track.Source;
        string detail = track is null ? "Nothing playing" : track.Artist;
        string second = detail.Length == 0 ? "" : (track is null ? "\u2002\u2002" : " · ") + detail;
        if (_title.Text == title && _detail.Text == second)
            return;

        bool arriving = animate && Arrival.Showing(_line);
        if (arriving)
            Arrival.Leave(_line, _previousLine);
        _title.Text = title;
        _detail.Text = second;
        string full = track is null ? detail : detail.Length > 0 ? $"{title} · {detail}" : title;
        _link.ToolTip = full;
        AutomationProperties.SetHelpText(_link, full);
        if (arriving)
            Arrival.Enter(_line, rise: true);
    }
}
