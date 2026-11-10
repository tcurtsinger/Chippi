// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Capture;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.UI.Capture;

/// <summary>
/// The Capture tile's page on the island (designs/capture-panel v1): six modes as cards, the installed tools on the row
/// under them, then the last four captures. Delay sits in the header and applies to the next mode. Nothing scrolls: the
/// rows fit the 496 × 304 body with room to spare.
/// </summary>
public sealed class CapturePanelView : UserControl
{
    internal const string NoCaptures = "No captures yet";
    internal const int MaxTools = IslandPage.Columns;
    internal const int RecentShown = 4;
    private const double CardHeight = 64, ThumbHeight = 66;

    /// <summary>The modes in the card row's order, with each card's label and the starter's name (screen-capture.md, Shortcuts).</summary>
    internal static readonly IReadOnlyList<(CaptureMode Mode, string Name, string Verb, Geometry Icon)> Modes =
    [
        (CaptureMode.Region, "Region", "Capture region", LucideIcons.SquareDashed),
        (CaptureMode.Window, "Window", "Capture window", LucideIcons.AppWindow),
        (CaptureMode.Screen, "Screen", "Capture full screen", LucideIcons.Monitor),
        (CaptureMode.AllDisplays, "All displays", "Capture all displays", LucideIcons.Screens),
        (CaptureMode.Freeform, "Freeform", "Capture freeform", LucideIcons.Lasso),
        (CaptureMode.LastRegion, "Last region", "Capture last region", LucideIcons.RotateCcwClock),
    ];

    private readonly ICapturePanelModel _model;
    private readonly Func<IDisposable>? _holdOpen;
    private readonly Grid _modes = Row();
    private readonly Grid _tools = Row();
    private readonly Dictionary<CaptureMode, Button> _modeCards = [];
    private readonly DockPanel _recentHeading = new() { Height = 28, Margin = new Thickness(0, 12, 0, 0), LastChildFill = true };
    private readonly Button _openFolder = new() { Content = "Open folder", Height = 24, MinWidth = 0, Padding = new Thickness(8, 0, 8, 0), FontSize = 11 };
    private readonly Grid _recent = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly Border _empty = new() { Height = 104, Margin = new Thickness(0, 4, 0, 0), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
    private readonly Button _delay = new() { Height = 24, MinWidth = 0, Padding = new Thickness(8, 0, 6, 0), FontSize = 11 };
    private readonly TextBlock _delayText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
    private readonly Popup _delayMenu = new() { StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, Placement = PlacementMode.Custom };
    private readonly StackPanel _delayList = new();
    private readonly Dictionary<Button, int> _delaySeconds = [];
    private IReadOnlyList<CapturePanelTool> _shownTools = [];
    private IReadOnlyList<CapturePanelRecent> _shownRecent = [];
    private IDisposable? _hold;

    /// <param name="holdOpen">Keeps the island open while the Delay menu is (ModuleContext.HoldIslandOpen).</param>
    public CapturePanelView(ICapturePanelModel model, Func<IDisposable>? holdOpen = null)
    {
        _model = model;
        _holdOpen = holdOpen;
        Header = BuildHeader();

        foreach (var (mode, name, verb, icon) in Modes)
        {
            var card = Card(name, verb, icon);
            card.Click += (_, _) => _model.Start(mode);
            Place(_modes, card, _modeCards.Count);
            _modeCards[mode] = card;
        }
        _tools.Margin = new Thickness(0, IslandPage.Gap, 0, 0);

        var recentTitle = new TextBlock { Text = "Recent", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _openFolder.SetResourceReference(StyleProperty, "IslandButtonStyle");
        _openFolder.Click += (_, _) => _model.OpenFolder();
        DockPanel.SetDock(_openFolder, Dock.Right);
        _recentHeading.Children.Add(_openFolder);
        _recentHeading.Children.Add(recentTitle);
        for (int i = 0; i < RecentShown; i++)
        {
            if (i > 0) _recent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandPage.Gap) });
            _recent.ColumnDefinitions.Add(new ColumnDefinition());
        }

        _empty.SetResourceReference(Border.BackgroundProperty, "IslandGalleryFaceBrush");
        _empty.SetResourceReference(Border.BorderBrushProperty, "IslandCardBorderBrush");
        var emptyText = new TextBlock { Text = NoCaptures, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        emptyText.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        _empty.Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Glyph(LucideIcons.Image, 24, "IslandTextTertiaryBrush", HorizontalAlignment.Center), emptyText } };

        Content = new StackPanel
        {
            Width = IslandPage.BodyWidth,
            Height = IslandPage.BodyHeight,
            Children = { _modes, _tools, _recentHeading, _recent, _empty },
        };

        // Subscribed from construction, not Loaded, so a page built before it shows stays current.
        _model.Changed += () => { if (Dispatcher.CheckAccess()) Refresh(); else Dispatcher.BeginInvoke(Refresh); };
        Unloaded += (_, _) => _delayMenu.IsOpen = false;
        Refresh();
    }

    /// <summary>The page's header: "Capture", and Delay on the right. The island puts Back before it.</summary>
    public DockPanel Header { get; }

    /// <summary>The first mode card, for the island's keyboard focus when the page takes it.</summary>
    public Button InitialFocus => _model.CanCapture ? _modeCards[CaptureMode.Region] : _tools.Children.OfType<Button>().FirstOrDefault() ?? _modeCards[CaptureMode.Region];

    internal IReadOnlyDictionary<CaptureMode, Button> ModeCards => _modeCards;

    internal IEnumerable<Button> ToolCards => _tools.Children.OfType<Button>();

    internal IEnumerable<Button> RecentCards => _recent.Children.OfType<Button>();

    internal Button OpenFolderButton => _openFolder;

    internal Button DelayButton => _delay;

    internal FrameworkElement EmptyCard => _empty;

    internal FrameworkElement ToolsRow => _tools;

    internal FrameworkElement ModesRow => _modes;

    internal FrameworkElement RecentHeading => _recentHeading;

    /// <summary>The Delay menu is open (for shots and tests).</summary>
    internal bool DelayMenuOpen { get => _delayMenu.IsOpen; set => _delayMenu.IsOpen = value; }

    internal IEnumerable<Button> DelayItems => _delayList.Children.OfType<Button>();

    /// <summary>"No delay", or "3 seconds".</summary>
    internal static string DelayName(int seconds) => seconds <= 0 ? "No delay" : string.Create(CultureInfo.CurrentCulture, $"{seconds} seconds");

    private DockPanel BuildHeader()
    {
        var title = new TextBlock { Text = "Capture", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var chevron = Glyph(LucideIcons.ChevronDown, 12, "IslandTextSecondaryBrush");
        _delay.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { Glyph(LucideIcons.Timer, 12, "IslandTextSecondaryBrush"), _delayText, chevron } };
        _delay.SetResourceReference(StyleProperty, "IslandButtonStyle");
        _delay.Click += (_, _) => OpenDelayMenu();

        foreach (int seconds in (int[])[0, .. Countdown.Delays])
        {
            var check = Glyph(LucideIcons.Check, 14, "IslandAccentBrush");
            check.Width = 14;
            check.Margin = new Thickness(0, 0, 8, 0);
            // The island's list row: 30 tall, 12 text, and the picked one in the accent tint (Tag "Selected").
            var item = new Button
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { check, new TextBlock { Text = DelayName(seconds), VerticalAlignment = VerticalAlignment.Center } } },
            };
            item.SetResourceReference(StyleProperty, "IslandListRowStyle");
            _delaySeconds[item] = seconds;
            AutomationProperties.SetName(item, DelayName(seconds));
            item.Click += (_, _) =>
            {
                bool returnFocus = _delayList.IsKeyboardFocusWithin;
                _delayMenu.IsOpen = false;
                _model.Delay = seconds;
                ShowDelay();
                if (returnFocus) _delay.Focus();
            };
            _delayList.Children.Add(item);
        }
        var surface = new Border { Child = _delayList, MinWidth = 132, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        // Solid, as the design's flyout: the island's own fill lets the cards under the menu show through.
        surface.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "IslandBorderBrush");
        KeyboardNavigation.SetTabNavigation(surface, KeyboardNavigationMode.Cycle);
        surface.PreviewKeyDown += DelayMenuKeys;
        _delayMenu.Child = surface;
        _delayMenu.PlacementTarget = _delay;
        // Under Delay, its right edge on Delay's, so the menu stays inside the panel.
        _delayMenu.CustomPopupPlacementCallback = (popup, target, _) => [new CustomPopupPlacement(new Point(target.Width - popup.Width, target.Height + 4), PopupPrimaryAxis.Horizontal)];
        _delayMenu.Opened += (_, _) =>
        {
            _hold = _holdOpen?.Invoke();
            (DelayItems.FirstOrDefault(i => _delaySeconds[i] == _model.Delay) ?? DelayItems.First()).Focus();
        };
        _delayMenu.Closed += (_, _) => { _hold?.Dispose(); _hold = null; };

        DockPanel.SetDock(_delay, Dock.Right);
        DockPanel.SetDock(_delayMenu, Dock.Right);
        // The menu sits in the header's tree, so it finds the island's styles.
        return new DockPanel { LastChildFill = true, Children = { _delay, _delayMenu, title } };
    }

    public void OpenDelayMenu()
    {
        ShowDelay();
        _delayMenu.IsOpen = true;
    }

    private void DelayMenuKeys(object sender, KeyEventArgs e)
    {
        var items = DelayItems.ToList();
        switch (e.Key)
        {
            case Key.Escape:
                _delayMenu.IsOpen = false;
                _delay.Focus();
                break;
            case Key.Up or Key.Down:
                int at = items.FindIndex(i => i.IsKeyboardFocusWithin);
                items[(at + (e.Key == Key.Up ? items.Count - 1 : 1)) % items.Count].Focus();
                break;
            case Key.Home: items[0].Focus(); break;
            case Key.End: items[^1].Focus(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void ShowDelay()
    {
        string name = DelayName(_model.Delay);
        _delayText.Text = name;
        AutomationProperties.SetName(_delay, "Delay: " + name);
        _delay.ToolTip = "Delay";
        foreach (var item in DelayItems)
        {
            bool picked = _delaySeconds[item] == _model.Delay;
            item.Tag = picked ? "Selected" : null;
            ((StackPanel)item.Content).Children[0].Visibility = picked ? Visibility.Visible : Visibility.Hidden;
            AutomationProperties.SetItemStatus(item, picked ? "Selected" : "");
        }
    }

    private void Refresh()
    {
        bool capture = _model.CanCapture;
        var mode = capture ? Visibility.Visible : Visibility.Collapsed;
        _modes.Visibility = mode;
        _modes.IsEnabled = _delay.IsEnabled = !_model.IsRecording;
        _delay.Visibility = mode;
        if (!capture) _delayMenu.IsOpen = false;
        ShowDelay();
        foreach (var (m, _, verb, _) in Modes)
            Tip(_modeCards[m], verb, _model.Keys(m));

        var tools = _model.Tools.Take(MaxTools).ToList();
        if (!tools.SequenceEqual(_shownTools))
        {
            _shownTools = tools;
            _tools.Children.Clear();
            for (int i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                var card = Card(tool.Name, tool.Verb, tool.Icon);
                card.IsEnabled = tool.Enabled;
                card.Click += (_, _) => tool.Start();
                Tip(card, tool.Verb, tool.Keys);
                Place(_tools, card, i);
            }
        }
        _tools.Visibility = tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Without the modes the tools lead the page.
        _tools.Margin = new Thickness(0, capture ? IslandPage.Gap : 0, 0, 0);

        var recent = _model.HasRecent ? _model.Recent.Take(RecentShown).ToList() : [];
        if (!recent.SequenceEqual(_shownRecent))
        {
            _shownRecent = recent;
            _recent.Children.Clear();
            for (int i = 0; i < recent.Count; i++)
            {
                var thumb = Thumb(recent[i]);
                Grid.SetColumn(thumb, i * 2);
                _recent.Children.Add(thumb);
            }
        }
        _recentHeading.Visibility = _model.HasRecent ? Visibility.Visible : Visibility.Collapsed;
        _openFolder.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _recent.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = _model.HasRecent && recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A mode or tool card, as the home's tool tiles: the glyph over its label, a lift on hover and focus.</summary>
    private static Button Card(string name, string verb, Geometry icon)
    {
        var label = new TextBlock { Text = name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 5, 2, 0) };
        var card = new Button
        {
            Height = CardHeight,
            Content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Glyph(icon, 20, "IslandTextSecondaryBrush", HorizontalAlignment.Center), label } },
        };
        card.SetResourceReference(StyleProperty, "IslandTileStyle");
        AutomationProperties.SetName(card, verb);
        return card;
    }

    /// <summary>The verb, then its keys once set ("Capture region · Print Screen").</summary>
    private static void Tip(Button card, string verb, string? keys)
    {
        string tip = string.IsNullOrWhiteSpace(keys) ? verb : $"{verb} · {keys}";
        card.ToolTip = tip;
        AutomationProperties.SetAcceleratorKey(card, keys ?? "");
    }

    /// <summary>A recent capture: the whole thumbnail fitted and centred on the card's face, its name and age under it.</summary>
    private Button Thumb(CapturePanelRecent item)
    {
        var face = new Border { Height = ThumbHeight, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        face.SetResourceReference(Border.BackgroundProperty, "IslandGalleryFaceBrush");
        face.SetResourceReference(Border.BorderBrushProperty, "IslandHairBrush");
        face.Child = item.Thumbnail is { } source
            ? new Image { Source = source, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : Glyph(LucideIcons.Image, 20, "IslandTextTertiaryBrush", HorizontalAlignment.Center);
        if (face.Child is Image image) RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        else ((FrameworkElement)face.Child).VerticalAlignment = VerticalAlignment.Center;
        var name = new TextBlock { Text = item.Name, FontSize = 11, LineHeight = 14, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextSecondaryBrush");
        var when = new TextBlock { Text = item.When, FontSize = 11, LineHeight = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        when.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        var thumb = new Button
        {
            Content = new StackPanel { Children = { face, name, when } },
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = item.Name,
        };
        thumb.SetResourceReference(StyleProperty, "IslandBareButtonStyle");
        AutomationProperties.SetName(thumb, $"{item.Name}, {item.When}");
        thumb.Click += (_, _) => _model.OpenRecent(item.Id);
        return thumb;
    }

    /// <summary>A row of six 76 columns with 8 gaps, the island's body grid.</summary>
    private static Grid Row()
    {
        var grid = new Grid();
        for (int i = 0; i < IslandPage.Columns; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandPage.Gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandPage.ColumnWidth) });
        }
        return grid;
    }

    private static void Place(Grid row, UIElement card, int column)
    {
        Grid.SetColumn(card, column * 2);
        row.Children.Add(card);
    }

    private static LucideIcon Glyph(Geometry data, double size, string brush, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var glyph = new LucideIcon { Data = data, Size = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align };
        glyph.SetResourceReference(LucideIcon.BrushProperty, brush);
        return glyph;
    }
}
