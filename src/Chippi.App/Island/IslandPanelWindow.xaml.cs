// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.App.Flyout;
using Chippi.App.Island.Media;
using Chippi.UI.Pet;
using Chippi.Core.Config;
using Chippi.Core.Diagnostics;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Themes;
using Windows.Win32.Foundation;

namespace Chippi.App.Island;

/// <summary>
/// The open panel (DESIGN.md §5): the home or one tile's page, in one 520 × 360 shape that the widget
/// (the puck, or the bar) becomes, on its outer corner; every page starts with a back arrow.
/// Between pages the shape stays and the new content fades in. <see cref="IslandWindow"/> opens,
/// places and closes it.
/// </summary>
public partial class IslandPanelWindow : Window
{
    /// <summary>The shell's Now Playing tile, always on the grid.</summary>
    public const string NowPlayingKey = "now-playing";

    private const double OpenRadius = 8; // the Windows 11 widget look
    private const double ShadowRoom = 40; // beside the shape, for its shadow
    private static readonly TimeSpan GrowTime = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan ShrinkTime = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan PageTime = TimeSpan.FromMilliseconds(180);
    private const double PageSlide = 12; // a page comes in from the right, the home from the left
    // Reduced motion: nothing grows or slides; the panel and its pages fade in place this quickly.
    private static readonly TimeSpan ReducedFadeTime = TimeSpan.FromMilliseconds(120);

    private enum Morph { Open, Page, Close }

    // A toggle that failed says "Failed" under its button this long.
    private static readonly TimeSpan FailedHold = TimeSpan.FromSeconds(4);

    private readonly IslandGrid _grid = new();
    private readonly Dictionary<string, long> _failed = []; // tile key → when its "Failed" goes (Stopwatch ticks)
    private readonly HashSet<string> _toggling = []; // toggles still being applied
    private readonly HashSet<string> _unreadable = []; // features whose state threw, logged once until they read again
    private readonly DispatcherTimer _failedTimer = new() { Interval = FailedHold };
    private readonly Dictionary<IslandTileContribution, FrameworkElement> _views = [];
    private readonly List<Panel> _attached = []; // the page's panels, which hold reused section views
    private readonly TextBlock _pageStatus = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private IslandLayout _layout = IslandLayout.Default;
    private IReadOnlyList<IslandTileGroup> _groups = [];
    private IReadOnlyList<IslandActivity> _activities = [];
    private IMediaSessions? _media;
    private NowPlayingView? _nowPlaying;
    private string? _page; // null is the grid
    private string? _lastPage;
    private string? _openOn;
    private IReadOnlyList<string> _shownTiles = [];
    private HWND _hwnd;
    private Rect _puck; // physical pixels
    private Rect _widget; // the widget at rest, the puck or the bar: what the shape grows out of and folds back into
    private MonitorArea _area;
    private Rect _window; // the window while open, physical pixels
    private Rect _widgetLocal; // the widget, in the window's DIPs
    private Rect _target; // where the shape is heading, likewise
    private Rect _drawn; // where it is drawn now
    private Rect _from;
    private double _radiusFrom;
    private Morph _morph;
    private bool _reduced; // this morph fades in place (reduced motion)
    private double _pageFrom; // where the new page slides from: +1 the right (into a page), -1 the left (back home)
    private TimeSpan _elapsed;
    private double _fadeFrom;
    private long _lastFrame;
    private EventHandler? _frame;
    private bool _keyboard;

    public IslandPanelWindow()
    {
        InitializeComponent();
        _grid.TileOpened += (_, key) => ShowPage(key);
        _grid.TileToggled += (_, key) => Toggle(key);
        _grid.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _failedTimer.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            foreach (var key in _failed.Where(f => f.Value <= now).Select(f => f.Key).ToList())
                _failed.Remove(key);
            if (_failed.Count == 0)
                _failedTimer.Stop();
            RefreshGrid();
        };
        Card.Width = IslandPage.Width;
        Card.Height = IslandPage.Height;
        PointSharedKeys();
        EventHandler themeChanged = (_, _) => PointSharedKeys();
        ThemeManager.ThemeChanged += themeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= themeChanged;

        // A press in the panel is the user clicking into it: from then on it takes the keyboard.
        // (The puck's window sits above this one, so a press on the puck is the puck's.)
        PreviewMouseLeftButtonDown += (_, e) => TakeKeyboard(e.OriginalSource as DependencyObject);
        PreviewMouseRightButtonDown += (_, e) => TakeKeyboard(e.OriginalSource as DependencyObject);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            if (!PageHandlesEscape())
                EscapePressed?.Invoke(this, EventArgs.Empty);
        };
        // Only the window's own scale: an Image on a page raises the same routed event, which bubbles up here.
        DpiChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, this))
                Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                {
                    if (IsOpen)
                        Place();
                });
        };
    }


    /// <summary>Esc while the island has the keyboard (the user clicked into it).</summary>
    internal event EventHandler? EscapePressed;

    /// <summary>The home's Settings button.</summary>
    internal event EventHandler? SettingsRequested;

    /// <summary>The user opened a page (a tile key) to restore next time.</summary>
    internal event EventHandler<string>? LastPageChanged;

    public bool IsOpen { get; private set; }

    internal HWND Handle => _hwnd;

    /// <summary>The page showing: a tile key, or null for the grid.</summary>
    internal string? CurrentPage => _page;

    /// <summary>Whether the island took the keyboard after the user clicked into it.</summary>
    internal bool HasKeyboard => _keyboard;

    /// <summary>The app in front, read just before the island takes the keyboard (Phase 5 notes #62).</summary>
    internal Func<Core.Clipboard.PasteTarget?>? CaptureTarget { get; set; }

    /// <summary>The app that was in front before the island took the keyboard; null until it has, and after it closes.</summary>
    internal Core.Clipboard.PasteTarget? PasteTarget { get; private set; }

    /// <summary>Lets the page use Esc first (a search to clear): true when it did, and the island stays open.</summary>
    internal bool PageHandlesEscape()
    {
        if (!IsOpen || Group(_page) is not { } group)
            return false;
        foreach (var part in group.Parts)
        {
            try
            {
                if (part.Spec.HandleEscape?.Invoke() == true)
                    return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Island: {part.ModuleId} failed to handle Esc", ex);
            }
        }
        return false;
    }

    public void Initialize() => _hwnd = IslandNative.MakeNoActivate(this);

    /// <summary>
    /// Lays the home out once while the panel is closed, so the first open costs what later ones do: its
    /// templates, text and code paths are ready (the first open after start used to skip about 5 frames).
    /// The page last opened, the likeliest next, is built and laid out first, and kept as every page is
    /// (Keep awake's first open took 16–19ms against 4 after). The app calls it once it's idle after start;
    /// it does nothing while the panel is open.
    /// </summary>
    internal void Prewarm()
    {
        if (IsOpen || IsVisible)
            return;
        if (_lastPage is { } last && last != NowPlayingKey && Exists(last))
            Lay(last);
        Lay(null);
        Detach();

        void Lay(string? key)
        {
            Attach(key);
            if (key is null)
                RefreshGrid();
            Card.Measure(new Size(IslandPage.Width, IslandPage.Height));
            Card.Arrange(new Rect(0, 0, IslandPage.Width, IslandPage.Height));
        }
    }

    // Shared Chippi.UI templates inside the island (switches, segments) and module views ask for the app's
    // accent and segment keys; here they get the island's, for the theme showing. (IslandStyles.xaml drops the
    // segment shadow: a null set from code isn't stored.)
    private static readonly (string Key, string Island)[] SharedKeys =
    [
        ("AccentBrush", "IslandAccentBrush"),
        ("OnAccentBrush", "IslandOnAccentBrush"),
        ("SegmentTroughBrush", "IslandButtonBrush"),
        ("SegmentStrokeBrush", "IslandControlBorderBrush"),
        ("SegmentActiveBgBrush", "IslandAccentFillBrush"),
        ("SegmentActiveBrush", "IslandOnAccentBrush"),
    ];

    private void PointSharedKeys()
    {
        foreach (var (key, island) in SharedKeys)
            Resources[key] = Application.Current?.TryFindResource(island);
    }

    public void SetPetColor(PetColor color)
    {
        ChippiPet.SetPetColor(this, color); // inherited by a feature page's Chippi (Keep awake's)
        _grid.Pet.PetColor = color;
        _nowPlaying?.SetPetColor(color);
    }

    /// <summary>Now Playing's media sessions.</summary>
    internal void SetMedia(IMediaSessions media)
    {
        if (_media is not null)
            _media.Changed -= OnMediaChanged;
        _media = media;
        _media.Changed += OnMediaChanged;
        _grid.SetMedia(media);
        _nowPlaying = null;
        RefreshGrid();
    }

    /// <summary>AppSettings.IslandLastPage.</summary>
    internal void SetLastPage(string? key) => _lastPage = key;

    /// <summary>The page the next open shows instead of the usual one (a notice click); null for the usual.</summary>
    internal void OpenOn(string? key) => _openOn = key;

    /// <summary>Settings › Island &amp; pet: where it opens, the tiles' order and which show.</summary>
    internal void SetLayout(IslandLayout layout)
    {
        if (layout == _layout)
            return;
        _layout = layout;
        RefreshGrid();
    }

    public void Celebrate()
    {
        if (IsOpen)
            _grid.Pet.Celebrate();
    }

    /// <summary>The activities: a feature that needs attention shows in Caution.</summary>
    public void SetActivities(IReadOnlyList<IslandActivity> activities)
    {
        _activities = activities;
        RefreshGrid();
    }

    /// <summary>The footer's Chippi wears the widget's mood (listening, wide awake…), so the two match where one becomes the other.</summary>
    internal void ShowPetMood(PetMood mood) => _grid.Pet.ShowMood(mood);

    /// <summary>The running features' island tiles, in grid order. A page whose tile went away falls back to the grid.</summary>
    public void SetTiles(IReadOnlyList<IslandTileGroup> tiles)
    {
        foreach (var gone in _views.Keys.Where(p => !tiles.SelectMany(t => t.Parts).Contains(p)).ToList())
            _views.Remove(gone);
        foreach (var group in _groups)
            foreach (var part in group.Parts)
                part.StatusChanged -= OnStatusChanged;
        _groups = tiles;
        foreach (var group in _groups)
            foreach (var part in group.Parts)
                part.StatusChanged += OnStatusChanged;
        RefreshGrid();
        if (!IsOpen)
            return;
        if (_page is not null && !Exists(_page))
            ShowPage(null, remember: false);
        else
            Attach(_page);
        Reshape(snap: _frame is null);
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshGrid);

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        RefreshGrid();
    }

    /// <summary>Grows out of the puck alone (physical pixels), or moves to it when already open.</summary>
    internal void Open(Rect puck, MonitorArea area) => Open(puck, puck, area);

    /// <summary>
    /// The widget becomes the panel: grows out of <paramref name="widget"/> (the puck, or the bar grown
    /// from <paramref name="puck"/>; physical pixels) into the panel on the puck's outer corner, or moves
    /// there when already open.
    /// </summary>
    internal void Open(Rect puck, Rect widget, MonitorArea area)
    {
        _puck = puck;
        _widget = widget;
        _area = area;
        if (IsOpen)
        {
            Place();
            return;
        }
        IsOpen = true;
        bool shrinking = IsVisible;
        if (_openOn is not null && Exists(_openOn))
            _page = _openOn;
        else if (!shrinking)
            _page = _layout.OpensOn == IslandOpensOn.LastPage && _lastPage is not null && Exists(_lastPage) ? _lastPage : null;
        _openOn = null;
        Attach(_page);
        RefreshGrid(); // the volume and the toggles are read as the home opens
        Place();
        Show();
        Begin(Morph.Open);
    }

    /// <summary>
    /// Folds back into the widget, then hides; <paramref name="immediate"/> hides at once. <paramref name="widget"/>
    /// is the widget as it is now (a song may have started or stopped while the panel was open).
    /// </summary>
    public void CloseIsland(bool immediate = false, Rect? widget = null)
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        if (widget is { } now && _hwnd != default)
        {
            _widget = now;
            _widgetLocal = Local(now);
        }
        _keyboard = false;
        PasteTarget = null;
        MarkOnScreen();
        if (immediate)
            Collapse();
        else
            Begin(Morph.Close);
    }

    /// <summary>Whether <paramref name="physical"/> is on the open island's shape.</summary>
    internal bool Covers(Point physical)
    {
        if (!IsOpen)
            return false;
        var local = new Point((physical.X - _window.X) / _area.Scale, (physical.Y - _window.Y) / _area.Scale);
        return _target.Contains(local);
    }

    /// <summary>Takes the keyboard without a click (the tray icon chosen with the keyboard, or a second launch).</summary>
    internal void TakeKeyboard() => TakeKeyboard(null);

    /// <summary>Shows <paramref name="key"/>'s page, or the grid for null; the new content fades in.</summary>
    internal void ShowPage(string? key, bool remember = true)
    {
        if (key is not null && !Exists(key))
            key = null;
        if (remember && key is not null && key != _lastPage)
        {
            _lastPage = key;
            LastPageChanged?.Invoke(this, key);
        }
        if (key == _page && IsOpen)
            return;
        _page = key;
        if (!IsOpen)
            return;
        Attach(key);
        Reshape(snap: false);
        _pageFrom = key is null ? -1 : 1;
        Begin(Morph.Page);
        if (_keyboard)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusFirst);
    }

    // ----- Pages -----

    /// <summary>Every page the grid offers, in grid order.</summary>
    internal IReadOnlyList<string> PageKeys() => [NowPlayingKey, .. _groups.Where(g => g.Key != NowPlayingKey).Select(g => g.Key)];

    private bool Exists(string key) => key == NowPlayingKey || _groups.Any(g => g.Key == key);

    private IslandTileGroup? Group(string? key) => _groups.FirstOrDefault(g => g.Key == key);

    /// <summary>The open feature page's body, below its header (496 × 304 once laid out); null on the grid and Now Playing.</summary>
    internal FrameworkElement? PageBody { get; private set; }

    private void Attach(string? key)
    {
        Detach();
        PageBody = null;
        if (key is null)
        {
            Page.Content = _grid;
            MarkOnScreen();
            return;
        }
        // The header docks on top; the sections fill the 496 × 304 body below it.
        var page = new DockPanel { LastChildFill = true };
        _attached.Add(page);
        if (key == NowPlayingKey && _media is not null)
        {
            if (_nowPlaying is null)
            {
                _nowPlaying = new NowPlayingView(_media, () => ShowPage(null, remember: false));
                _nowPlaying.SetPetColor(_grid.Pet.PetColor);
            }
            page.Children.Add(_nowPlaying); // its own header and body
        }
        else
        {
            // Construct sections first: a module may expose its header and focus through its view.
            foreach (var part in Group(key)?.Parts ?? []) _ = View(part);
            var factory = Group(key)?.Parts.Select(p => p.Spec.CreateHeader).FirstOrDefault(f => f is not null);
            var header = factory?.Invoke() as FrameworkElement ?? PageHeader(key);
            header.Margin = new Thickness(0);
            header.VerticalAlignment = VerticalAlignment.Center;
            // Every header gets the same 24 row and the 8 gap below it, so every body is 496 × 304.
            var back = BackButton(() => ShowPage(null, remember: false));
            DockPanel.SetDock(back, Dock.Left);
            var row = new DockPanel { Height = IslandPage.HeaderHeight, Margin = new Thickness(0, 0, 0, IslandPage.Gap), LastChildFill = true, Children = { back, header } };
            DockPanel.SetDock(row, Dock.Top);
            _attached.Add(row);
            page.Children.Add(row);
            page.Children.Add(Body(Group(key)?.Parts ?? []));
        }
        Page.Content = page;
        RefreshPageStatus();
        MarkOnScreen();
    }

    /// <summary>The arrow every page starts with: back to the grid.</summary>
    internal static Button BackButton(Action back)
    {
        var glyph = new LucideIcon { Data = LucideIcons.ChevronLeft, Size = 16 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var button = new Button { Content = glyph, Margin = new Thickness(-4, 0, 4, 0) };
        button.SetResourceReference(StyleProperty, "IslandIconButtonStyle");
        AutomationProperties.SetName(button, "Back");
        DockPanel.SetDock(button, Dock.Left);
        button.Click += (_, _) => back();
        return button;
    }

    /// <summary>Every page but Now Playing (which has its own) starts with its name, and the tile's status where the page doesn't show it.</summary>
    private DockPanel PageHeader(string key)
    {
        var name = new TextBlock { Text = TileName(key), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _pageStatus.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(_pageStatus, Dock.Right);
        var header = new DockPanel { Children = { _pageStatus, name } };
        _attached.Add(header);
        return header;
    }

    /// <summary>
    /// The page's 496 × 304 body. One section fills it; several stack 8 apart; sections in columns sit
    /// side by side. It never scrolls and never grows the island: what doesn't fit is cut off here, so
    /// each page fits its content to the body (DESIGN.md §5 Island, revised 2026-10-28).
    /// </summary>
    private Grid Body(IReadOnlyList<IslandTileContribution> parts)
    {
        var body = new Grid { ClipToBounds = true };
        _attached.Add(body);
        PageBody = body;
        if (parts.Any(p => p.Spec.Column is not null))
            body.Children.Add(Columns(parts));
        else if (parts.Count == 1)
        {
            if (View(parts[0]) is { } view)
            {
                view.Margin = new Thickness(0);
                body.Children.Add(view);
            }
        }
        else
        {
            var stack = new StackPanel();
            _attached.Add(stack);
            AddSections(stack, parts);
            body.Children.Add(stack);
        }
        return body;
    }

    /// <summary>
    /// Side-by-side columns, 8px apart, each under its name and framed as one card, all of one height:
    /// its sections inside, divided by hairlines, the last filling the rest. Sections that span the
    /// columns sit 8px below them, full width (DESIGN.md §5 Island pages, revised 2026-10-31).
    /// </summary>
    private Grid Columns(IReadOnlyList<IslandTileContribution> parts)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var column in parts.Where(p => !p.Spec.SpansColumns).GroupBy(p => p.Spec.Column)) // parts are in section order, so columns are too
        {
            var views = column.Select(View).OfType<FrameworkElement>().ToList();
            if (views.Count == 0)
                continue;
            if (grid.ColumnDefinitions.Count > 0)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandPage.Gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            int index = grid.ColumnDefinitions.Count - 1;
            if (column.Key is { } title)
            {
                var label = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Height = 24, Padding = new Thickness(2, 4, 0, 0) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
                Grid.SetColumn(label, index);
                grid.Children.Add(label);
            }
            var sections = new DockPanel();
            _attached.Add(sections);
            for (int i = 0; i < views.Count; i++)
            {
                if (i > 0)
                {
                    // Shown with the section under it, so a section with nothing to show leaves no line.
                    var hairline = new Border { Height = 1 };
                    hairline.SetResourceReference(Border.BackgroundProperty, "IslandHairBrush");
                    hairline.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(Visibility)) { Source = views[i] });
                    DockPanel.SetDock(hairline, Dock.Top);
                    sections.Children.Add(hairline);
                }
                views[i].Margin = new Thickness(0);
                DockPanel.SetDock(views[i], Dock.Top);
                sections.Children.Add(views[i]);
            }
            var card = new Border { Child = sections };
            card.SetResourceReference(StyleProperty, "IslandCardStyle");
            card.Padding = new Thickness(0);
            Grid.SetRow(card, 1);
            Grid.SetColumn(card, index);
            grid.Children.Add(card);
        }
        var spans = new StackPanel();
        _attached.Add(spans);
        AddSections(spans, parts.Where(p => p.Spec.SpansColumns));
        if (spans.Children.Count > 0)
        {
            // Under the cards, or at the top when no column has a section.
            bool under = grid.ColumnDefinitions.Count > 0;
            spans.Margin = new Thickness(0, under ? IslandPage.Gap : 0, 0, 0);
            Grid.SetRow(spans, under ? 2 : 0);
            Grid.SetColumnSpan(spans, Math.Max(1, grid.ColumnDefinitions.Count));
            grid.Children.Add(spans);
        }
        return grid;
    }

    private void AddSections(Panel panel, IEnumerable<IslandTileContribution> parts)
    {
        bool first = true;
        foreach (var part in parts)
        {
            if (View(part) is not { } view)
                continue;
            view.Margin = new Thickness(0, first ? 0 : 8, 0, 0);
            first = false;
            panel.Children.Add(view);
        }
    }

    /// <summary>The open page's status, when its tile puts it there: the tile's, in Caution while it needs attention.</summary>
    private void RefreshPageStatus()
    {
        if (_page is null || Group(_page) is not { } group)
            return;
        if (group.PageNote.Length > 0)
        {
            _pageStatus.Text = group.PageNote;
            _pageStatus.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
            return;
        }
        _pageStatus.Text = group.StatusOnPage ? group.Status : "";
        bool caution = LiveOn(group.Key)?.NeedsAttention == true || Muted(group.Key);
        _pageStatus.SetResourceReference(TextBlock.ForegroundProperty, caution ? "WarningTextBrush" : "IslandTextTertiaryBrush");
    }

    // The old page's sections are let go before they are reused on another page (one parent each).
    private void Detach()
    {
        foreach (var panel in _attached)
            panel.Children.Clear();
        _attached.Clear();
        Page.Content = null;
    }

    private FrameworkElement? View(IslandTileContribution part)
    {
        if (_views.TryGetValue(part, out var view))
            return view;
        try
        {
            if (part.Spec.CreateSection() is FrameworkElement created)
                return _views[part] = created;
            Log.Warn($"Island: {part.ModuleId} returned a {part.Spec.Name} island page that is not a FrameworkElement");
        }
        catch (Exception ex)
        {
            Log.Error($"Island: {part.ModuleId} failed to build its {part.Spec.Name} island page", ex);
        }
        return null;
    }

    /// <summary>The features' buttons, each with its status, in the chosen order without the hidden ones, and the volume slider. Now Playing is the card, not a button.</summary>
    private void RefreshGrid()
    {
        var tiles = new List<IslandTileView>();
        long now = Stopwatch.GetTimestamp();
        foreach (var group in _groups.Where(g => g.Key != NowPlayingKey))
        {
            var toggle = group.Toggle;
            bool muted = Muted(group.Key);
            bool attention = LiveOn(group.Key)?.NeedsAttention == true || muted;
            bool failed = _failed.TryGetValue(group.Key, out long until) && until > now;
            string name = toggle?.Name ?? group.Name;
            var icon = muted ? LucideIcons.MicOff : LucideIcons.Get(toggle?.Icon ?? group.Icon);
            // A feature whose state can't be read shows its button plain; the others still refresh.
            bool on = Read($"{group.Key} state", () => toggle?.IsOn() == true, false);
            // Needing attention for another reason than mute, the tile's status says why ("Mic stopped"), not the toggle's "Live".
            string status = failed ? "Failed"
                : attention && !muted && group.Status.Length > 0 ? group.Status
                : Read($"{group.Key} status", () => toggle?.Status?.Invoke(), null) ?? group.Status;
            // The accent says something is on and fine: a muted mic, or anything that needs attention, shows in Caution instead.
            tiles.Add(new(group.Key, name, icon, status, on && !attention, attention || failed, toggle is not null, group.Name));
        }
        var shown = _layout.Shown(tiles.Select(t => t.Key));
        _grid.SetTiles([.. shown.Select(key => tiles.First(t => t.Key == key))]);
        var volumeGroup = _groups.FirstOrDefault(g => g.Volume is not null);
        Read($"{volumeGroup?.Key} volume", () => { _grid.SetVolume(volumeGroup?.Volume, volumeGroup?.Key); return true; }, false);
        _shownTiles = shown;
        RefreshPageStatus();
        MarkOnScreen();
    }

    /// <summary>
    /// A feature's state for the home (<paramref name="what"/>, such as "keep-awake status"), read so that one that throws
    /// (a module stopping as it's read) doesn't stop the others refreshing: it gets <paramref name="fallback"/>, logged once
    /// until it reads again.
    /// </summary>
    private T Read<T>(string what, Func<T> read, T fallback)
    {
        try
        {
            var value = read();
            _unreadable.Remove(what);
            return value;
        }
        catch (Exception ex)
        {
            if (_unreadable.Add(what))
                Log.Error($"Island: reading the {what} for the home failed", ex);
            return fallback;
        }
    }

    /// <summary>
    /// A split button's toggle on the home. A press while the last one is still being applied does nothing, so a double
    /// click can't turn it on and straight off. When it fails, "Failed" shows under the button for a few seconds.
    /// </summary>
    private async void Toggle(string key)
    {
        if (!_toggling.Add(key))
            return;
        try
        {
            if (Group(key)?.Toggle is { } toggle)
                await toggle.Toggle();
        }
        catch (Exception ex)
        {
            Log.Error($"Island: toggling {key} failed", ex);
            _failed[key] = Stopwatch.GetTimestamp() + (long)(FailedHold.TotalSeconds * Stopwatch.Frequency);
            _failedTimer.Stop();
            _failedTimer.Start();
            RefreshGrid();
        }
        finally
        {
            _toggling.Remove(key);
        }
    }

    /// <summary>Tells each tile whether it's seen: the grid open, and the tile not hidden.</summary>
    private void MarkOnScreen()
    {
        bool grid = IsOpen && _page is null;
        foreach (var group in _groups)
            foreach (var part in group.Parts)
                part.SetOnScreen(grid && _shownTiles.Contains(group.Key));
    }

    private bool Muted(string key) => _activities.Any(a => a.ModuleId == "mic-mute" && a.NeedsAttention && a.TileKey == key);

    private IslandActivity? LiveOn(string key) =>
        _activities.FirstOrDefault(a => a.TileKey == key || (key == NowPlayingKey && a.Kind == IslandActivityKind.Media));

    private string TileName(string? key) => key == NowPlayingKey ? "Now Playing" : Group(key)?.Name ?? "";

    // ----- Keyboard -----

    /// <summary>
    /// The first press inside the island: it becomes the foreground window, so Tab, arrows and Enter
    /// reach it the normal way. Until then only the Esc hook watches the keyboard (no global hook takes
    /// keys from the app in front).
    /// </summary>
    private void TakeKeyboard(DependencyObject? pressed)
    {
        if (_keyboard || !IsOpen)
            return;
        _keyboard = true;
        // Before activating: once the island is in front, the app the user was in is gone from view.
        PasteTarget = CaptureTarget?.Invoke() ?? PasteTarget;
        IslandNative.Activate(_hwnd);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            for (var at = pressed; at is not null; at = at is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            {
                if (at is UIElement { Focusable: true, IsEnabled: true } target && target.Focus())
                    return;
            }
            FocusFirst();
        });
    }

    private void FocusFirst()
    {
        if (_page is null)
            _grid.Buttons().FirstOrDefault()?.Focus();
        else
        {
            var focus = Group(_page)?.Parts.Select(p => p.Spec.InitialFocus?.Invoke()).OfType<UIElement>().FirstOrDefault();
            if (focus?.Focus() != true) Card.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    // ----- Placement and the morph -----

    /// <summary>The panel's rectangle on the puck's outer corner, in physical pixels.</summary>
    private Rect PanelRect()
    {
        double s = _area.Scale;
        var size = new Size(IslandPage.Width * s, IslandPage.Height * s);
        return new Rect(IslandGeometry.PanelOrigin(_puck, size, _area.Work, s), size);
    }

    /// <summary>
    /// Covers the widget and the panel it becomes, with room for the shadow, while open, so the morph never
    /// moves the window. Kept on the monitor, and no bigger: every frame copies all of it.
    /// </summary>
    private void Place()
    {
        double s = _area.Scale;
        var cover = PanelRect();
        cover.Union(_widget);
        cover.Inflate(ShadowRoom * s, ShadowRoom * s);
        cover.Intersect(_area.Monitor);
        _window = new Rect(Math.Floor(cover.X), Math.Floor(cover.Y), Math.Ceiling(cover.Width), Math.Ceiling(cover.Height));
        IslandNative.Move(_hwnd, _window);
        Root.Width = _window.Width / s;
        Root.Height = _window.Height / s;
        _widgetLocal = Local(_widget);
        Reshape(snap: _frame is null);
    }

    private Rect Local(Rect physical) =>
        new((physical.X - _window.X) / _area.Scale, (physical.Y - _window.Y) / _area.Scale, physical.Width / _area.Scale, physical.Height / _area.Scale);

    /// <summary>
    /// Puts the 520 × 360 panel beside the puck, inside the work area.
    /// <paramref name="snap"/> also draws it there at once (no morph running).
    /// </summary>
    private void Reshape(bool snap)
    {
        _target = Local(PanelRect());
        Canvas.SetLeft(Card, _target.X);
        Canvas.SetTop(Card, _target.Y);
        // The home's Chippi sits on the puck's side of the footer. On a bottom corner that's where the puck's own Chippi is,
        // unless the work area's edge pushed the panel off the puck's corner.
        var puck = Local(_puck);
        var puckCenter = new Point(puck.X + puck.Width / 2, puck.Y + puck.Height / 2);
        bool right = puckCenter.X > _target.X + _target.Width / 2;
        _grid.SetPetSide(right);
        var pet = new Point(right ? _target.Right - IslandGrid.FooterPet : _target.X + IslandGrid.FooterPet, _target.Bottom - IslandGrid.FooterPet);
        _petAtPuck = (pet - puckCenter).Length < 1;
        if (snap && _morph != Morph.Close)
        {
            _drawn = _target;
            Draw();
        }
    }

    /// <summary>
    /// Each frame of an open or a close: whether it is opening, and how far along (0–1), so the widget
    /// fades Chippi and the bar's content in step with the shape it has become. A close ends at 1.
    /// </summary>
    internal event Action<bool, double>? Morphing;

    /// <summary>The morph's clock, in Stopwatch ticks. Tests hold it still and move it themselves, so a late frame can't skip a step.</summary>
    internal Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

    /// <summary>An open, page or close is still drawing its frames.</summary>
    internal bool IsMorphing => _frame is not null;

    private bool _petAtPuck;

    /// <summary>
    /// The home is showing and its footer's Chippi is exactly where the puck's is (a puck on a bottom corner): the widget's
    /// Chippi shrinks onto it and hands over instead of fading.
    /// </summary>
    internal bool PetMeetsPuck => _petAtPuck && _page is null;

    /// <summary>
    /// Starts a morph from what is drawn now: open (the widget to page, 220ms, content fading in over the
    /// second half), page (the shape stays; the new page slides 12 in from its side as it fades in, over 180ms)
    /// or close (back into the widget, 160ms, content fading out). Under reduced motion nothing grows or slides:
    /// the panel fades in where it opens and out where it is, and a page fades in, each over 120ms.
    /// </summary>
    private void Begin(Morph morph)
    {
        _morph = morph;
        _reduced = Motion.IsReduced;
        bool fromWidget = morph == Morph.Open && _frame is null && !_reduced;
        bool inPlace = morph == Morph.Open && _reduced;
        _from = fromWidget ? _widgetLocal : inPlace ? _target : _drawn;
        _radiusFrom = fromWidget ? WidgetRadius : inPlace ? OpenRadius : _radius;
        _fadeFrom = Card.Opacity;
        // Opening from closed starts clear; opening again mid-close carries on from where the fade is.
        _rootFrom = morph == Morph.Open && _frame is null ? 0 : Root.Opacity;
        _elapsed = TimeSpan.Zero;
        if (morph == Morph.Page)
            Card.Opacity = 0;
        if (_frame is not null)
            return;
        _lastFrame = 0;
        _frame = (_, _) =>
        {
            // The clock starts on the first frame, so a slow first show doesn't skip the start.
            long now = Clock();
            _elapsed += _lastFrame == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_lastFrame, now);
            _lastFrame = now;
            Step();
        };
        CompositionTarget.Rendering += _frame;
        Step();
    }

    private void Step()
    {
        var time = _reduced ? ReducedFadeTime : _morph switch { Morph.Close => ShrinkTime, Morph.Page => PageTime, _ => GrowTime };
        double t = Math.Min(1, _elapsed / time);
        double eased = 1 - Math.Pow(1 - t, 3);
        bool shrink = _morph == Morph.Close && !_reduced;
        var to = shrink ? _widgetLocal : _target;
        _drawn = new Rect(Lerp(_from.X, to.X, eased), Lerp(_from.Y, to.Y, eased), Lerp(_from.Width, to.Width, eased), Lerp(_from.Height, to.Height, eased));
        _radius = Lerp(_radiusFrom, shrink ? WidgetRadius : OpenRadius, eased);
        if (_reduced)
        {
            // In place: the panel fades in or out as one piece, and a page fades in.
            Root.Opacity = _morph switch { Morph.Open => Math.Max(_rootFrom, t), Morph.Close => _rootFrom * (1 - t), _ => 1 };
            if (_morph != Morph.Close)
                Card.Opacity = _morph == Morph.Page ? t : 1;
            PageShift.X = 0;
            Page.RenderTransform = Transform.Identity;
            ShapeShadow.Opacity = 1;
        }
        else
        {
            Root.Opacity = 1;
            Card.Opacity = _morph switch
            {
                Morph.Open => Math.Max(_fadeFrom, Math.Clamp(t * 2 - 1, 0, 1)),
                Morph.Page => t,
                _ => _fadeFrom * Math.Clamp(1 - t * 2, 0, 1),
            };
            // A new page comes in from its side as it fades in: into a page from the right, back home from the left.
            // The shift is on the page only while it slides, so the open, the close and a page at rest draw without it.
            PageShift.X = _morph == Morph.Page ? _pageFrom * PageSlide * (1 - eased) : 0;
            Page.RenderTransform = _morph == Morph.Page && t < 1 ? PageShift : Transform.Identity;
            // The shadow deepens from about the widget's to the panel's, and back.
            ShapeShadow.Opacity = _morph switch
            {
                Morph.Open => Lerp(WidgetShadowShare, 1, eased),
                Morph.Close => Lerp(1, WidgetShadowShare, eased),
                _ => 1,
            };
        }
        Draw();
        if (_morph != Morph.Page)
            Morphing?.Invoke(_morph == Morph.Open, t);
        if (t < 1)
            return;
        StopFrames();
        if (_morph == Morph.Close)
            Collapse();
    }

    private double _radius = OpenRadius;
    private double _rootFrom = 1;

    /// <summary>The new page's slide, on the page only while it slides.</summary>
    internal TranslateTransform PageShift { get; } = new();

    /// <summary>The puck's and the bar's round ends: half their height.</summary>
    private double WidgetRadius => Math.Min(_widgetLocal.Width, _widgetLocal.Height) / 2;

    /// <summary>The panel's shadow, scaled down to about the widget's while the shape is the widget.</summary>
    private const double WidgetShadowShare = 0.4;

    private void StopFrames()
    {
        if (_frame is null)
            return;
        CompositionTarget.Rendering -= _frame;
        _frame = null;
    }

    /// <summary>Back in the widget: hidden, with the views let go; the widget shows itself again.</summary>
    private void Collapse()
    {
        StopFrames();
        _radius = OpenRadius;
        ShapeShadow.Opacity = 1;
        Hide();
        Morphing?.Invoke(false, 1);
        Card.Opacity = 0;
        Root.Opacity = 1;
        PageShift.X = 0;
        // Unparented views get Unloaded and stop listening until the island opens again.
        Detach();
        MarkOnScreen();
    }

    /// <summary>The shape at <see cref="_drawn"/>.</summary>
    private void Draw()
    {
        Canvas.SetLeft(Shape, _drawn.X);
        Canvas.SetTop(Shape, _drawn.Y);
        Shape.Width = Math.Max(0, _drawn.Width);
        Shape.Height = Math.Max(0, _drawn.Height);
        ShapeShadow.CornerRadius = ShapeFill.CornerRadius = new CornerRadius(_radius);
        // Mid-morph the content already sits at its final place; only what's inside the shape shows.
        var inside = new Rect(_drawn.X - Canvas.GetLeft(Card), _drawn.Y - Canvas.GetTop(Card), Math.Max(0, _drawn.Width), Math.Max(0, _drawn.Height));
        Card.Clip = new RectangleGeometry(inside, _radius, _radius);
        // Nothing half-faded takes a click (a double-click on a tile would reach the new page's controls).
        Card.IsHitTestVisible = Card.Opacity >= 1;
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    /// <summary>The shape as drawn and where it is heading, in physical pixels (snapshots and tests).</summary>
    internal Rect Footprint
    {
        get
        {
            var box = _drawn;
            box.Union(_target);
            double s = _area.Scale;
            return new Rect(_window.X + box.X * s, _window.Y + box.Y * s, box.Width * s, box.Height * s);
        }
    }

#if DEBUG
    /// <summary>Debug builds: the window's own pixels, for <see cref="IslandWindow.SaveScene"/>.</summary>
    internal FrameworkElement Visual => Root;
#endif

    protected override void OnClosed(EventArgs e)
    {
        StopFrames();
        if (_media is not null)
            _media.Changed -= OnMediaChanged;
        base.OnClosed(e);
    }
}
