// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.Displays.Views;

/// <summary>
/// The island's Displays page (designs/displays v3): the displays drawn where Windows arranges them, the picked one's
/// Brightness and Contrast below, and All displays in the header, which picks every display and moves them together,
/// keeping their difference. It opens on the display under the pointer. A display that doesn't answer shows "Brightness
/// control failed" and Try again in place of its sliders.
/// </summary>
public sealed class DisplaysIslandView : UserControl
{
    internal const string AllDisplays = "All displays";
    internal const string NoDisplays = "No displays found";
    private const double MapHeight = 132, MapInset = 16, TileGap = 3;

    private readonly IDisplaysPanelModel _model;
    private readonly ToggleButton _all = new() { Height = 24, Padding = new Thickness(8, 0, 8, 0) };
    private readonly Canvas _map = new() { ClipToBounds = true };
    private readonly Border _mapCard = new() { Height = MapHeight, Padding = new Thickness(0) };
    private readonly Dictionary<string, (ToggleButton Tile, TextBlock Name, TextBlock Line)> _tiles = [];
    private readonly LucideIcon _pickedGlyph = new() { Size = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pickedName = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _pickedNote = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
    private readonly TextBlock _pickedError = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Text = DisplaysSettingsPage.FailedText };
    private readonly DockPanel _picked = new() { Height = 28, Margin = new Thickness(2, 10, 2, 4), LastChildFill = true };
    private readonly ControlRow _brightness, _contrast;
    private readonly Border _failed = new();
    private readonly Button _tryAgain = new() { Content = "Try again", Height = 24, MinWidth = 0, Padding = new Thickness(8, 0, 8, 0), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _body = new();
    private readonly StackPanel _none = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private string? _pick;
    private bool _allPicked;

    public DisplaysIslandView(IDisplaysPanelModel model)
    {
        _model = model;
        var title = new TextBlock { Text = "Displays", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _all.SetResourceReference(StyleProperty, "IslandPickTileStyle");
        _all.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 8, 0),
            Children = { Glyph(LucideIcons.Screens, 14, "IslandTextSecondaryBrush", new Thickness(0, 0, 6, 0)), new TextBlock { Text = AllDisplays, FontSize = 11, VerticalAlignment = VerticalAlignment.Center } },
        };
        AutomationProperties.SetName(_all, AllDisplays);
        _all.Click += (_, _) => { _allPicked = _all.IsChecked == true; Refresh(); };
        DockPanel.SetDock(_all, Dock.Right);
        Header = new DockPanel { LastChildFill = true, Children = { _all, title } };

        _mapCard.SetResourceReference(StyleProperty, "IslandCardStyle");
        _mapCard.Padding = new Thickness(0);
        _mapCard.Child = _map;
        AutomationProperties.SetName(_mapCard, "Your displays, as Windows arranges them");

        _pickedGlyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        _pickedGlyph.Margin = new Thickness(0, 0, 8, 0);
        _pickedNote.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _pickedError.SetResourceReference(TextBlock.ForegroundProperty, "IslandCautionBrush");
        DockPanel.SetDock(_pickedGlyph, Dock.Left);
        DockPanel.SetDock(_pickedError, Dock.Right);
        _picked.Children.Add(_pickedGlyph);
        _picked.Children.Add(_pickedError);
        _picked.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _pickedName, _pickedNote } });

        _brightness = new ControlRow(this, DisplayControl.Brightness, "Brightness", LucideIcons.Sun);
        _contrast = new ControlRow(this, DisplayControl.Contrast, "Contrast", LucideIcons.Contrast);

        _failed.SetResourceReference(StyleProperty, "IslandCardStyle");
        _failed.Padding = new Thickness(9, 12, 9, 12);
        _tryAgain.SetResourceReference(StyleProperty, "IslandButtonStyle");
        _tryAgain.Click += (_, _) => { if (_pick is { } id) _model.TryAgain(id); };
        var failedText = new TextBlock { Text = DisplaysSettingsPage.FailedText, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        failedText.SetResourceReference(TextBlock.ForegroundProperty, "IslandCautionBrush");
        var alert = Glyph(LucideIcons.TriangleAlert, 18, "IslandCautionBrush", new Thickness(0));
        alert.HorizontalAlignment = HorizontalAlignment.Center;
        _tryAgain.HorizontalAlignment = HorizontalAlignment.Center;
        _failed.Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { alert, failedText, _tryAgain } };

        var noneText = new TextBlock { Text = NoDisplays, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        noneText.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        var noneGlyph = Glyph(LucideIcons.MonitorOff, 18, "IslandTextTertiaryBrush", new Thickness(0));
        noneGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        _none.Children.Add(noneGlyph);
        _none.Children.Add(noneText);

        _body.Children.Add(_mapCard);
        _body.Children.Add(_picked);
        _body.Children.Add(_brightness.Card);
        _body.Children.Add(_contrast.Card);
        _body.Children.Add(_failed);
        Content = new Grid { Width = IslandPage.BodyWidth, Height = IslandPage.BodyHeight, Children = { _body, _none } };

        // Subscribed from construction, not Loaded, so the page stays current while it's built but not yet shown.
        _model.Changed += () => { if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(Refresh); };
        Loaded += (_, _) => Opened();
        Refresh();
    }

    /// <summary>The page's header: its title, and All displays on the right.</summary>
    public DockPanel Header { get; }

    /// <summary>Milliseconds now, for how long a slider the user just moved keeps its value over a stale reading.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    internal ToggleButton AllButton => _all;

    internal IReadOnlyDictionary<string, ToggleButton> Tiles => _tiles.ToDictionary(t => t.Key, t => t.Value.Tile);

    internal string? Picked => _allPicked ? null : _pick;

    internal Slider BrightnessSlider => _brightness.Slider;

    internal Slider ContrastSlider => _contrast.Slider;

    internal FrameworkElement BrightnessCard => _brightness.Card;

    internal FrameworkElement ContrastCard => _contrast.Card;

    internal FrameworkElement FailedCard => _failed;

    internal Button TryAgainButton => _tryAgain;

    internal FrameworkElement NoneMessage => _none;

    /// <summary>The page shows: read every display again, and pick the one under the pointer.</summary>
    internal void Opened()
    {
        _model.Read();
        _allPicked = false;
        _pick = _model.UnderPointer();
        Refresh();
    }

    private static LucideIcon Glyph(System.Windows.Media.Geometry data, double size, string brush, Thickness margin)
    {
        var glyph = new LucideIcon { Data = data, Size = size, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }

    private static string Percent(int level) => $"{level.ToString(CultureInfo.CurrentCulture)}%";

    private static int? Level(DisplayStatus d, DisplayControl control) => control == DisplayControl.Brightness ? d.Brightness : d.Contrast ? d.ContrastLevel : null;

    // All displays shows the main display's level, or the first that answers (displays.md, All displays).
    private static int? AllLevel(IReadOnlyList<DisplayStatus> answering, DisplayControl control) =>
        answering.Where(d => d.IsMain).Select(d => Level(d, control)).FirstOrDefault(l => l is not null)
        ?? answering.Select(d => Level(d, control)).FirstOrDefault(l => l is not null);

    private void Refresh()
    {
        var displays = _model.PanelDisplays;
        var answering = displays.Where(d => !d.Failed).ToList();
        bool any = displays.Count > 0;
        _body.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _none.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _all.Visibility = answering.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (answering.Count < 2) _allPicked = false;
        if (_pick is null || displays.All(d => d.Id != _pick))
            _pick = (displays.FirstOrDefault(d => d.IsMain) ?? displays.FirstOrDefault())?.Id;
        _all.IsChecked = _allPicked;
        DrawMap(displays);
        if (!any) return;

        if (_allPicked)
        {
            _pickedGlyph.Data = LucideIcons.Screens;
            _pickedName.Text = AllDisplays;
            _pickedNote.Text = "";
            _pickedError.Visibility = answering.Any(d => d.WriteFailed) ? Visibility.Visible : Visibility.Collapsed;
            _failed.Visibility = Visibility.Collapsed;
            _brightness.Show(AllDisplays, AllLevel(answering, DisplayControl.Brightness), visible: true);
            _contrast.Show(AllDisplays, AllLevel(answering, DisplayControl.Contrast), visible: answering.Any(d => d.Contrast));
            return;
        }

        var picked = displays.First(d => d.Id == _pick);
        _pickedGlyph.Data = LucideIcons.Monitor;
        _pickedName.Text = picked.Name;
        _pickedNote.Text = picked.IsMain ? "· Main" : "";
        _pickedError.Visibility = picked.WriteFailed && !picked.Failed ? Visibility.Visible : Visibility.Collapsed;
        _failed.Visibility = picked.Failed ? Visibility.Visible : Visibility.Collapsed;
        _tryAgain.IsEnabled = !picked.Retrying;
        AutomationProperties.SetName(_tryAgain, $"Try again on {picked.Name}");
        _brightness.Show(picked.Name, picked.Brightness, visible: !picked.Failed);
        _contrast.Show(picked.Name, picked.ContrastLevel, visible: !picked.Failed && picked.Contrast);
    }

    /// <summary>Each display as a tile where Windows puts it, scaled to fit the map with the arrangement centered.</summary>
    private void DrawMap(IReadOnlyList<DisplayStatus> displays)
    {
        foreach (string gone in _tiles.Keys.Except(displays.Select(d => d.Id)).ToList())
        {
            _map.Children.Remove(_tiles[gone].Tile);
            _tiles.Remove(gone);
        }
        if (displays.Count == 0) return;
        // A display Windows gave no bounds for goes to the right of the rest, as a 16:9 screen.
        var bounds = new List<(DisplayStatus Display, Rect Bounds)>();
        foreach (var d in displays)
        {
            var known = _model.Bounds(d.Id);
            double right = bounds.Count == 0 ? 0 : bounds.Max(b => b.Bounds.Right);
            bounds.Add((d, known is { IsEmpty: false, Width: > 0, Height: > 0 } r ? r : new Rect(right, 0, 1920, 1080)));
        }
        var all = bounds.Select(b => b.Bounds).Aggregate(Rect.Union);
        double width = IslandPage.BodyWidth - 2, height = MapHeight - 2;
        double scale = Math.Min((width - 2 * MapInset) / all.Width, (height - 2 * MapInset) / all.Height);
        double left = (width - all.Width * scale) / 2, top = (height - all.Height * scale) / 2;
        int order = 0;
        foreach (var (d, r) in bounds)
        {
            if (!_tiles.TryGetValue(d.Id, out var tile))
            {
                tile = NewTile(d.Id);
                _tiles[d.Id] = tile;
                _map.Children.Add(tile.Tile);
            }
            _map.Children.Remove(tile.Tile);
            _map.Children.Insert(order++, tile.Tile); // Tab follows display order.
            tile.Tile.Width = Math.Max(24, r.Width * scale - 2 * TileGap);
            tile.Tile.Height = Math.Max(24, r.Height * scale - 2 * TileGap);
            Canvas.SetLeft(tile.Tile, left + (r.X - all.X) * scale + TileGap);
            Canvas.SetTop(tile.Tile, top + (r.Y - all.Y) * scale + TileGap);
            tile.Tile.IsChecked = _allPicked ? !d.Failed : d.Id == _pick;
            tile.Name.Text = d.Name;
            string line = d.Failed ? DisplaysSettingsPage.FailedText
                : d.Brightness is { } level ? (d.IsMain ? "Main · " : "") + Percent(level)
                : d.IsMain ? "Main" : "";
            tile.Line.Text = line;
            tile.Line.Visibility = line.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            tile.Line.SetResourceReference(TextBlock.ForegroundProperty, d.Failed ? "IslandCautionBrush" : "IslandTextSecondaryBrush");
            tile.Tile.ToolTip = d.Name;
            AutomationProperties.SetName(tile.Tile, line.Length > 0 ? $"{d.Name}, {line.Replace(" · ", ", ", StringComparison.Ordinal)}" : d.Name);
        }
    }

    private (ToggleButton Tile, TextBlock Name, TextBlock Line) NewTile(string id)
    {
        var name = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        var line = new TextBlock { FontSize = 11, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 28, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        var tile = new ToggleButton
        {
            Content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Children = { name, line } },
        };
        tile.SetResourceReference(StyleProperty, "IslandPickTileStyle");
        tile.Click += (_, _) =>
        {
            _allPicked = false;
            _pick = id;
            Refresh();
        };
        return (tile, name, line);
    }

    /// <summary>A 44-tall card: the glyph, the control's name, its slider and the level.</summary>
    private sealed class ControlRow
    {
        private readonly DisplaysIslandView _view;
        private readonly DisplayControl _control;
        private readonly string _label;
        private readonly TextBlock _percent = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        private bool _refreshing, _dragging;
        private long _touched = long.MinValue / 2;
        private string? _target;

        public ControlRow(DisplaysIslandView view, DisplayControl control, string label, System.Windows.Media.Geometry glyph)
        {
            _view = view;
            _control = control;
            _label = label;
            Slider = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Slider.SetResourceReference(StyleProperty, "IslandSliderStyle");
            Slider.ValueChanged += (_, e) => Moved(e.NewValue);
            Slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
            {
                _dragging = true;
                if (_view._allPicked) _view._model.BeginAll(_control);
            }));
            Slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
            {
                _dragging = false;
                _touched = _view.Clock();
                if (_view._allPicked) _view._model.EndAll();
            }));
            _percent.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
            var name = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var icon = Glyph(glyph, 16, "IslandTextSecondaryBrush", new Thickness(0));
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(32) },
                    new ColumnDefinition { Width = new GridLength(84) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(40) },
                },
                Children = { icon, name, Slider, _percent },
            };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(Slider, 2);
            Grid.SetColumn(_percent, 3);
            Card = new Border { Child = grid, Margin = new Thickness(0, 0, 0, 6) };
            Card.SetResourceReference(StyleProperty, "IslandControlCardStyle");
        }

        public Border Card { get; }

        public Slider Slider { get; }

        public void Show(string target, int? level, bool visible)
        {
            Card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (target != _target)
            {
                _target = target; // Another display picked: its own level shows at once.
                _touched = long.MinValue / 2;
            }
            AutomationProperties.SetName(Slider, $"{target} {_label.ToLowerInvariant()}");
            Slider.IsEnabled = level is not null;
            // A drag, or a value the user set in the last second, wins over a reading that hasn't caught up yet.
            bool held = _dragging || _view.Clock() - _touched < 1000;
            _percent.Text = held ? Percent((int)Math.Round(Slider.Value)) : level is { } l ? Percent(l) : "";
            if (level is not { } shown || held) return;
            _refreshing = true;
            try { Slider.Value = shown; }
            finally { _refreshing = false; }
        }

        private void Moved(double value)
        {
            if (_refreshing) return;
            int percent = (int)Math.Round(value);
            _percent.Text = Percent(percent);
            if (!_dragging) _touched = _view.Clock();
            var model = _view._model;
            if (_view._allPicked)
            {
                if (_dragging)
                {
                    model.MoveAll(_control, percent);
                    return;
                }
                model.BeginAll(_control);
                model.MoveAll(_control, percent);
                model.EndAll();
            }
            else if (_view._pick is { } id)
                model.Set(id, _control, percent);
        }
    }
}
