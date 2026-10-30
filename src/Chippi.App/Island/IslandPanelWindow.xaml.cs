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
using Chippi.App.Pet;
using Chippi.Core.Config;
using Chippi.Core.Diagnostics;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Themes;
using Windows.Win32.Foundation;

namespace Chippi.App.Island;

/// <summary>
/// The open panel (DESIGN.md §5): the home or one tile's page, in one 520 × 360 shape that grows out
/// of the widget's puck, beside it toward the screen's middle; every page starts with a back arrow.
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
    private static readonly TimeSpan PageFadeTime = TimeSpan.FromMilliseconds(150);

    private enum Morph { Open, Page, Close }

    private readonly IslandGrid _grid = new();
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
    private MonitorArea _area;
    private Rect _window; // the window while open, physical pixels
    private Rect _puckLocal; // the puck, in the window's DIPs
    private Rect _target; // where the shape is heading, likewise
    private Rect _drawn; // where it is drawn now
    private Rect _from;
    private double _radiusFrom;
    private Morph _morph;
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
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (IsOpen)
                Place();
        });
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

    /// <summary>Settings › Island &amp; pet: where it opens, the tiles' order and which show, and the round buttons.</summary>
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

    /// <summary>The activities give tiles their dots.</summary>
    public void SetActivities(IReadOnlyList<IslandActivity> activities, PetMood mood)
    {
        _activities = activities;
        _grid.Pet.ShowMood(mood == PetMood.WideAwake ? PetMood.WideAwake : PetMood.Idle);
        RefreshGrid();
    }

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

    /// <summary>Grows out of the <paramref name="puck"/> (physical pixels), or moves beside it when already open.</summary>
    internal void Open(Rect puck, MonitorArea area)
    {
        _puck = puck;
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

    /// <summary>Shrinks back into the puck, then hides; <paramref name="immediate"/> hides at once.</summary>
    public void CloseIsland(bool immediate = false)
    {
        if (!IsOpen)
            return;
        IsOpen = false;
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
        var columns = parts.GroupBy(p => p.Spec.Column).ToList(); // parts are in section order, so columns are too
        if (columns.Any(c => c.Key is not null))
            body.Children.Add(Columns(columns));
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

    /// <summary>Side-by-side columns, 8px apart, each under its name.</summary>
    private Grid Columns(IReadOnlyList<IGrouping<string?, IslandTileContribution>> columns)
    {
        var grid = new Grid();
        foreach (var column in columns)
        {
            if (grid.ColumnDefinitions.Count > 0)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandPage.Gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var stack = new StackPanel();
            _attached.Add(stack);
            if (column.Key is { } title)
            {
                var label = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Height = 24, Padding = new Thickness(2, 4, 0, 0) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
                stack.Children.Add(label);
            }
            AddSections(stack, column);
            Grid.SetColumn(stack, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(stack);
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
        foreach (var group in _groups.Where(g => g.Key != NowPlayingKey))
        {
            var toggle = group.Toggle;
            bool muted = Muted(group.Key);
            string name = toggle?.Name ?? group.Name;
            string status = toggle?.Status?.Invoke() ?? group.Status;
            var icon = muted ? LucideIcons.MicOff : LucideIcons.Get(toggle?.Icon ?? group.Icon);
            tiles.Add(new(group.Key, name, icon, status, toggle?.IsOn() == true, LiveOn(group.Key)?.NeedsAttention == true || muted, toggle is not null));
        }
        var shown = _layout.Shown(tiles.Select(t => t.Key));
        _grid.SetTiles([.. shown.Select(key => tiles.First(t => t.Key == key))]);
        var volumeGroup = _groups.FirstOrDefault(g => g.Volume is not null);
        _grid.SetVolume(volumeGroup?.Volume, volumeGroup?.Key);
        _shownTiles = shown;
        RefreshPageStatus();
        MarkOnScreen();
    }

    /// <summary>A split button's toggle on the home.</summary>
    private void Toggle(string key)
    {
        try
        {
            Group(key)?.Toggle?.Toggle();
        }
        catch (Exception ex)
        {
            Log.Error($"Island: toggling {key} failed", ex);
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

    /// <summary>The panel's rectangle beside the puck, in physical pixels.</summary>
    private Rect PanelRect()
    {
        double s = _area.Scale;
        var size = new Size(IslandPage.Width * s, IslandPage.Height * s);
        return new Rect(IslandGeometry.PanelOrigin(_puck, size, _area.Work, s), size);
    }

    /// <summary>
    /// Covers the puck and the panel beside it, with room for the shadow, while open, so the morph never
    /// moves the window. Kept on the monitor, and no bigger: every frame copies all of it.
    /// </summary>
    private void Place()
    {
        double s = _area.Scale;
        var cover = PanelRect();
        cover.Union(_puck);
        cover.Inflate(ShadowRoom * s, ShadowRoom * s);
        cover.Intersect(_area.Monitor);
        _window = new Rect(Math.Floor(cover.X), Math.Floor(cover.Y), Math.Ceiling(cover.Width), Math.Ceiling(cover.Height));
        IslandNative.Move(_hwnd, _window);
        Root.Width = _window.Width / s;
        Root.Height = _window.Height / s;
        _puckLocal = Local(_puck);
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
        if (snap && _morph != Morph.Close)
        {
            _drawn = _target;
            Draw();
        }
    }

    /// <summary>
    /// Starts a morph from what is drawn now: open (puck to page, 220ms, content fading in over the
    /// second half), page (the shape stays; the new page fades in over 150ms) or close (back into the
    /// puck, 160ms, content fading out). Reduced motion snaps.
    /// </summary>
    private void Begin(Morph morph)
    {
        _morph = morph;
        bool fromPuck = morph == Morph.Open && _frame is null;
        _from = fromPuck ? _puckLocal : _drawn;
        _radiusFrom = fromPuck ? PuckRadius : _radius;
        _fadeFrom = Card.Opacity;
        _elapsed = TimeSpan.Zero;
        if (Motion.IsReduced)
        {
            StopFrames();
            if (morph == Morph.Close)
            {
                Collapse();
                return;
            }
            _elapsed = TimeSpan.FromSeconds(1);
            Step();
            return;
        }
        if (morph == Morph.Page)
            Card.Opacity = 0;
        if (_frame is not null)
            return;
        _lastFrame = 0;
        _frame = (_, _) =>
        {
            // The clock starts on the first frame, so a slow first show doesn't skip the start.
            long now = Stopwatch.GetTimestamp();
            _elapsed += _lastFrame == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_lastFrame, now);
            _lastFrame = now;
            Step();
        };
        CompositionTarget.Rendering += _frame;
        Step();
    }

    private void Step()
    {
        var time = _morph == Morph.Close ? ShrinkTime : GrowTime;
        double t = Math.Min(1, _elapsed / time);
        double eased = 1 - Math.Pow(1 - t, 3);
        var to = _morph == Morph.Close ? _puckLocal : _target;
        _drawn = new Rect(Lerp(_from.X, to.X, eased), Lerp(_from.Y, to.Y, eased), Lerp(_from.Width, to.Width, eased), Lerp(_from.Height, to.Height, eased));
        _radius = Lerp(_radiusFrom, _morph == Morph.Close ? PuckRadius : OpenRadius, eased);
        double fade = Math.Clamp(_elapsed / PageFadeTime, 0, 1);
        Card.Opacity = _morph switch
        {
            Morph.Open => Math.Max(_fadeFrom, Math.Clamp(t * 2 - 1, 0, 1)),
            Morph.Page => fade,
            _ => _fadeFrom * Math.Clamp(1 - t * 2, 0, 1),
        };
        Draw();
        bool done = _morph == Morph.Page ? fade >= 1 && t >= 1 : t >= 1;
        if (!done)
            return;
        StopFrames();
        if (_morph == Morph.Close)
            Collapse();
    }

    private double _radius = OpenRadius;

    private double PuckRadius => Math.Min(_puckLocal.Width, _puckLocal.Height) / 2;

    private void StopFrames()
    {
        if (_frame is null)
            return;
        CompositionTarget.Rendering -= _frame;
        _frame = null;
    }

    /// <summary>Back in the puck: hidden, with the views let go.</summary>
    private void Collapse()
    {
        StopFrames();
        _radius = OpenRadius;
        Hide();
        Card.Opacity = 0;
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
