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
/// The open island (DESIGN.md §5 Island, revised 2026-10-28): the tile grid or one tile's page, in
/// one 520 × 360 shape that grows out of the tab, with the orbit buttons around it. Between pages the
/// shape stays and the content crossfades. <see cref="IslandWindow"/> opens, places and closes it.
/// </summary>
public partial class IslandPanelWindow : Window
{
    /// <summary>The shell's Now Playing tile, always on the grid.</summary>
    public const string NowPlayingKey = "now-playing";

    private const double OpenRadius = 20;
    private const double ShadowRoom = 40; // beside the shape, for its shadow
    private static readonly TimeSpan GrowTime = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan ShrinkTime = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan PageFadeOutTime = TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan PageFadeTime = TimeSpan.FromMilliseconds(180);

    private enum Morph { Open, Page, Close }

    private readonly DispatcherTimer _clock = new();
    private readonly IslandGrid _grid = new();
    private readonly Dictionary<IslandTileContribution, FrameworkElement> _views = [];
    private readonly Dictionary<OrbitSlot, Button> _orbit = [];
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
    private IslandEdge _edge;
    private Rect _tab; // physical pixels
    private MonitorArea _area;
    private Rect _window; // the window while open, physical pixels
    private Rect _tabLocal; // the tab, in the window's DIPs
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
    private List<OrbitSlot> _placedSlots = []; // the buttons the current placement made room for

    public IslandPanelWindow()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => UpdateClock();
        _grid.TileOpened += (_, key) => ShowPage(key);
        foreach (var slot in Enum.GetValues<OrbitSlot>())
            Orbit.Children.Add(_orbit[slot] = OrbitButton(slot));
        Card.Width = IslandPage.Width;
        Card.Height = IslandPage.Height;

        // The island grows over the tab: a press where the tab sits is the tab's (click or drag).
        // Any other press is the user clicking into the island: from then on it takes the keyboard.
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_tabLocal.Contains(e.GetPosition(Root)))
                TabPressed?.Invoke(this, e);
            else
                TakeKeyboard(e.OriginalSource as DependencyObject);
        };
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

    /// <summary>A left press inside the tab's footprint, which the island covers while it is visible.</summary>
    internal event MouseButtonEventHandler? TabPressed;

    /// <summary>Esc while the island has the keyboard (the user clicked into it).</summary>
    internal event EventHandler? EscapePressed;

    /// <summary>The Settings orbit button.</summary>
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

    public void SetPetColor(PetColor color) => _grid.Pet.PetColor = color;

    /// <summary>Now Playing's media sessions.</summary>
    internal void SetMedia(IMediaSessions media)
    {
        if (_media is not null)
            _media.Changed -= OnMediaChanged;
        _media = media;
        _media.Changed += OnMediaChanged;
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
        RefreshOrbit();
    }

    public void Celebrate()
    {
        if (IsOpen)
            _grid.Pet.Celebrate();
    }

    /// <summary>The activities give tiles their dots and pick the live orbit button.</summary>
    public void SetActivities(IReadOnlyList<IslandActivity> activities, PetMood mood)
    {
        _activities = activities;
        _grid.Pet.ShowMood(mood == PetMood.WideAwake ? PetMood.WideAwake : PetMood.Idle);
        RefreshGrid();
        RefreshOrbit();
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
        RefreshOrbit(place: false);
        Reshape(snap: _frame is null);
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshGrid);

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        RefreshGrid();
        RefreshOrbit(); // playing or not decides the live button
    }

    /// <summary>Grows out of <paramref name="tab"/> (physical pixels) on <paramref name="edge"/>, or moves there when already open.</summary>
    internal void Open(IslandEdge edge, Rect tab, MonitorArea area)
    {
        _edge = edge;
        _tab = tab;
        _area = area;
        if (IsOpen)
        {
            Place();
            return;
        }
        IsOpen = true;
        // The island's local Dark.xaml carries the default accent; module views ask for AccentBrush,
        // so it is pointed at the island accent (the dark shade of the user's choice) on every open.
        if (Application.Current?.TryFindResource("IslandAccentBrush") is { } accent)
            Resources["AccentBrush"] = accent;
        bool shrinking = IsVisible;
        if (_openOn is not null && Exists(_openOn))
            _page = _openOn;
        else if (!shrinking)
            _page = _layout.OpensOn == IslandOpensOn.LastPage && _lastPage is not null && Exists(_lastPage) ? _lastPage : null;
        _openOn = null;
        Attach(_page);
        RefreshOrbit(place: false);
        UpdateClock();
        Place();
        Show();
        Begin(Morph.Open);
    }

    /// <summary>Shrinks back into the tab, then hides; <paramref name="immediate"/> hides at once.</summary>
    public void CloseIsland(bool immediate = false)
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        _keyboard = false;
        PasteTarget = null;
        _clock.Stop();
        MarkOnScreen();
        if (immediate)
            Collapse();
        else
            Begin(Morph.Close);
    }

    /// <summary>Whether <paramref name="physical"/> is on the open island: its shape or an orbit button.</summary>
    internal bool Covers(Point physical)
    {
        if (!IsOpen)
            return false;
        var local = new Point((physical.X - _window.X) / _area.Scale, (physical.Y - _window.Y) / _area.Scale);
        return _target.Contains(local) || IslandGeometry.OrbitLayout(_edge, _target, Slots()).Any(o => o.Rect.Contains(local));
    }

    /// <summary>Takes the keyboard without a click (the tray icon chosen with the keyboard, or a second launch).</summary>
    internal void TakeKeyboard() => TakeKeyboard(null);

    /// <summary>Shows <paramref name="key"/>'s page, or the grid for null; the old content fades out as the new one fades in.</summary>
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
        HoldLeaving();
        Attach(key);
        RefreshOrbit(place: false);
        Reshape(snap: false); // the same shape; a changed set of orbit buttons may still move it along the edge
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
            page.Children.Add(_nowPlaying ??= new NowPlayingView(_media)); // its own header and body
        else
        {
            // Construct sections first: a module may expose its header and focus through its view.
            foreach (var part in Group(key)?.Parts ?? []) _ = View(part);
            var factory = Group(key)?.Parts.Select(p => p.Spec.CreateHeader).FirstOrDefault(f => f is not null);
            var header = factory?.Invoke() as FrameworkElement ?? PageHeader(key);
            header.Margin = new Thickness(0);
            header.VerticalAlignment = VerticalAlignment.Center;
            // Every header gets the same 24 row and the 8 gap below it, so every body is 496 × 304.
            var row = new Grid { Height = IslandPage.HeaderHeight, Margin = new Thickness(0, 0, 0, IslandPage.Gap), Children = { header } };
            DockPanel.SetDock(row, Dock.Top);
            _attached.Add(row);
            page.Children.Add(row);
            page.Children.Add(Body(Group(key)?.Parts ?? []));
        }
        Page.Content = page;
        RefreshPageStatus();
        MarkOnScreen();
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

    /// <summary>Now Playing, then the features' tiles, each with its status and dot, in the chosen order without the hidden ones.</summary>
    private void RefreshGrid()
    {
        var track = _media?.Current;
        var tiles = new List<IslandTileView>
        {
            new(NowPlayingKey, "Now Playing", LucideIcons.Music, track?.Title ?? "", track?.Playing == true || LiveOn(NowPlayingKey) is not null),
        };
        foreach (var group in _groups.Where(g => g.Key != NowPlayingKey))
        {
            var live = LiveOn(group.Key);
            bool muted = Muted(group.Key);
            tiles.Add(new(group.Key, group.Name, muted ? LucideIcons.MicOff : LucideIcons.Get(group.Icon), group.Status, live is not null, live?.NeedsAttention == true || muted));
        }
        var shown = _layout.Shown(tiles.Select(t => t.Key));
        _grid.SetTiles([.. shown.Select(key => tiles.First(t => t.Key == key))]);
        _shownTiles = shown;
        RefreshPageStatus();
        MarkOnScreen();
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

    /// <summary>
    /// The live button's tile: Now Playing while media plays, otherwise the tile of an activity that
    /// needs attention (a muted mic), or none.
    /// </summary>
    private string? LiveKey()
    {
        if (_activities.Any(a => a.Kind == IslandActivityKind.Media) || _media?.Current?.Playing == true)
            return NowPlayingKey;
        return _activities.FirstOrDefault(a => a.NeedsAttention && a.TileKey is { } key && Exists(key))?.TileKey;
    }

    // ----- Orbit buttons -----

    private Button OrbitButton(OrbitSlot slot)
    {
        var glyph = new LucideIcon { Size = 14 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextBrush");
        var button = new Button { Content = glyph, Visibility = Visibility.Collapsed };
        button.SetResourceReference(StyleProperty, "IslandOrbitButtonStyle");
        button.Click += (_, _) => OnOrbit(slot);
        return button;
    }

    private void OnOrbit(OrbitSlot slot)
    {
        switch (slot)
        {
            case OrbitSlot.Back:
                ShowPage(_page is null ? _lastPage : null, remember: false);
                break;
            case OrbitSlot.Settings:
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
            default:
                if (OrbitTarget(slot) is { } key)
                    ShowPage(key);
                break;
        }
    }

    private string? OrbitTarget(OrbitSlot slot) => slot switch
    {
        OrbitSlot.Left => IslandLayout.Button(_layout.Left, Exists, _groups.FirstOrDefault(g => g.Orbit == IslandOrbit.Left)?.Key),
        OrbitSlot.Right => IslandLayout.Button(_layout.Right, Exists, _groups.FirstOrDefault(g => g.Orbit == IslandOrbit.Right)?.Key),
        OrbitSlot.Live => _layout.Live ? LiveKey() : null,
        _ => null,
    };

    private List<OrbitSlot> Slots()
    {
        var slots = new List<OrbitSlot>();
        if (_page is not null || (_lastPage is not null && Exists(_lastPage)))
            slots.Add(OrbitSlot.Back);
        if (OrbitTarget(OrbitSlot.Left) is not null)
            slots.Add(OrbitSlot.Left);
        slots.Add(OrbitSlot.Settings);
        if (OrbitTarget(OrbitSlot.Right) is not null)
            slots.Add(OrbitSlot.Right);
        if (OrbitTarget(OrbitSlot.Live) is not null)
            slots.Add(OrbitSlot.Live);
        return slots;
    }

    /// <summary>
    /// Which buttons show, with their glyphs, names and the active page's ring. Unless
    /// <paramref name="place"/> is false (the caller places the island next), a changed set of buttons
    /// re-places the open island.
    /// </summary>
    private void RefreshOrbit(bool place = true)
    {
        var slots = Slots();
        foreach (var (slot, button) in _orbit)
        {
            button.Visibility = slots.Contains(slot) ? Visibility.Visible : Visibility.Collapsed;
            string? target = slot == OrbitSlot.Back ? (_page is null ? _lastPage : null) : OrbitTarget(slot);
            var (glyph, name) = slot switch
            {
                OrbitSlot.Back when _page is null => (LucideIcons.Undo, $"Back to {TileName(target)}"),
                OrbitSlot.Back => (LucideIcons.LayoutGrid, "All features"),
                OrbitSlot.Settings => (LucideIcons.Settings, "Settings"),
                _ when target == NowPlayingKey => (LucideIcons.Music, "Now Playing"),
                _ => (LucideIcons.Get(Group(target)?.Icon), TileName(target)),
            };
            ((LucideIcon)button.Content).Data = glyph;
            AutomationProperties.SetName(button, name);
            button.Tag = slot is not (OrbitSlot.Back or OrbitSlot.Settings) && target is not null && target == _page ? "Active" : null;
        }
        if (!IsOpen || !place)
            return;
        // A button that appeared or went (the live one, once music starts) changes how far the island
        // reaches, so it is placed again to keep every button in the work area.
        if (!slots.SequenceEqual(_placedSlots))
            Reshape(snap: _frame is null);
        else
            Draw();
    }

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

    /// <summary>
    /// Covers a fixed area around the tab while open, so the morph never moves the window: on the top
    /// edge the island plus both orbit pairs, centered on the tab; on a side edge the island plus the
    /// live button beside it; the work area's full height either way.
    /// </summary>
    private void Place()
    {
        double s = _area.Scale;
        var work = _area.Work;
        double widest = IslandPage.Width;
        double reach = IslandGeometry.OrbitGap + IslandGeometry.OrbitSize;
        double width = (_edge == IslandEdge.Top ? widest + 2 * (reach + ShadowRoom) : widest + reach + ShadowRoom) * s;
        double left = _edge switch
        {
            IslandEdge.Left => work.Left,
            IslandEdge.Right => work.Right - width,
            _ => Math.Clamp(_tab.Left + _tab.Width / 2 - width / 2, work.Left, Math.Max(work.Left, work.Right - width)),
        };
        _window = new Rect(left, work.Top, width, work.Height);
        IslandNative.Move(_hwnd, _window);
        Root.Width = _window.Width / s;
        Root.Height = _window.Height / s;
        _tabLocal = Local(_tab);
        ShapeFill.BorderThickness = _edge switch
        {
            IslandEdge.Left => new Thickness(0, 1, 1, 1),
            IslandEdge.Right => new Thickness(1, 1, 0, 1),
            _ => new Thickness(1, 0, 1, 1),
        };
        Reshape(snap: _frame is null);
    }

    private Rect Local(Rect physical) =>
        new((physical.X - _window.X) / _area.Scale, (physical.Y - _window.Y) / _area.Scale, physical.Width / _area.Scale, physical.Height / _area.Scale);

    /// <summary>
    /// Centers the 520 × 360 island on the tab, keeping it and its orbit buttons in the work area.
    /// <paramref name="snap"/> also draws it there at once (no morph running).
    /// </summary>
    private void Reshape(bool snap)
    {
        double s = _area.Scale;
        const double width = IslandPage.Width, height = IslandPage.Height;
        _placedSlots = Slots();
        var reach = IslandGeometry.OrbitReach(_edge, new Size(width, height), _placedSlots);
        var origin = IslandGeometry.PanelOrigin(_edge, _tab, new Size(width * s, height * s), _area.Work, s, reach);
        _target = Local(new Rect(origin.X, origin.Y, width * s, height * s));
        Canvas.SetLeft(Card, _target.X);
        Canvas.SetTop(Card, _target.Y);
        if (snap && _morph != Morph.Close)
        {
            _drawn = _target;
            Draw();
        }
    }

    /// <summary>
    /// Starts a morph from what is drawn now: open (tab to page, 220ms, content fading in over the
    /// second half), page (the shape stays; the old content fades out over 90ms while the new one
    /// fades in over 180ms, with no blank gap) or close (back to the swollen tab, 160ms, content
    /// fading out). Reduced motion snaps.
    /// </summary>
    private void Begin(Morph morph)
    {
        _morph = morph;
        bool fromTab = morph == Morph.Open && _frame is null;
        _from = fromTab ? _tabLocal : _drawn;
        _radiusFrom = fromTab ? TabRadius() : _radius;
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
            Card.Opacity = 0; // Leaving shows the old content meanwhile
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
        var to = _morph == Morph.Close ? _tabLocal : _target;
        _drawn = new Rect(Lerp(_from.X, to.X, eased), Lerp(_from.Y, to.Y, eased), Lerp(_from.Width, to.Width, eased), Lerp(_from.Height, to.Height, eased));
        _radius = Lerp(_radiusFrom, _morph == Morph.Close ? TabRadius() : OpenRadius, eased);
        Card.Opacity = _morph switch
        {
            Morph.Open => Math.Max(_fadeFrom, Math.Clamp(t * 2 - 1, 0, 1)),
            Morph.Page => Math.Clamp(_elapsed / PageFadeTime, 0, 1),
            _ => _fadeFrom * Math.Clamp(1 - t * 2, 0, 1),
        };
        Leaving.Opacity = _morph == Morph.Page ? _leavingFrom * (1 - Math.Clamp(_elapsed / PageFadeOutTime, 0, 1)) : 0;
        Draw();
        bool done = _morph == Morph.Page ? _elapsed >= PageFadeTime && t >= 1 : t >= 1;
        if (!done)
            return;
        StopFrames();
        Leaving.Source = null;
        if (_morph == Morph.Close)
            Collapse();
    }

    private double _radius = OpenRadius;

    // The tab's corners are as round as it is deep.
    private double TabRadius() =>
        _edge == IslandEdge.Top ? Math.Min(_tabLocal.Height, _tabLocal.Width / 2) : Math.Min(_tabLocal.Width, _tabLocal.Height / 2);

    private void StopFrames()
    {
        if (_frame is null)
            return;
        CompositionTarget.Rendering -= _frame;
        _frame = null;
    }

    /// <summary>Back in the tab: hidden, with the views let go.</summary>
    private void Collapse()
    {
        StopFrames();
        _radius = OpenRadius;
        Hide();
        Card.Opacity = 0;
        Leaving.Source = null;
        // Unparented views get Unloaded and stop listening until the island opens again.
        Detach();
        MarkOnScreen();
    }

    /// <summary>The shape at <see cref="_drawn"/>, flat on its edge, and the orbit buttons following it.</summary>
    private void Draw()
    {
        Canvas.SetLeft(Shape, _drawn.X);
        Canvas.SetTop(Shape, _drawn.Y);
        Shape.Width = Math.Max(0, _drawn.Width);
        Shape.Height = Math.Max(0, _drawn.Height);
        ShapeShadow.CornerRadius = ShapeFill.CornerRadius = _edge switch
        {
            IslandEdge.Left => new CornerRadius(0, _radius, _radius, 0),
            IslandEdge.Right => new CornerRadius(_radius, 0, 0, _radius),
            _ => new CornerRadius(0, 0, _radius, _radius),
        };
        // Mid-morph the content already sits at its final place; only what's inside the shape shows.
        var inside = new Rect(_drawn.X - Canvas.GetLeft(Card), _drawn.Y - Canvas.GetTop(Card), Math.Max(0, _drawn.Width), Math.Max(0, _drawn.Height));
        Card.Clip = new RectangleGeometry(inside, _radius, _radius);
        bool settled = Card.Opacity >= 1;
        Card.IsHitTestVisible = settled; // nothing half-faded takes a click
        Leaving.Clip = Card.Clip;
        // Opening and closing fade the buttons with the content; between pages they stay.
        Orbit.Opacity = _morph == Morph.Page ? 1 : Card.Opacity;
        Orbit.IsHitTestVisible = IsOpen && Orbit.Opacity >= 1;
        foreach (var (slot, rect) in IslandGeometry.OrbitLayout(_edge, _drawn, Slots()))
        {
            Canvas.SetLeft(_orbit[slot], rect.X);
            Canvas.SetTop(_orbit[slot], rect.Y);
        }
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private double _leavingFrom;

    /// <summary>
    /// Before a page change: a picture of the content showing now, laid over the new page, which
    /// <see cref="Step"/> fades out over 90ms while the new content fades in. Nothing while hidden.
    /// </summary>
    private void HoldLeaving()
    {
        Leaving.Source = null;
        Leaving.Opacity = _leavingFrom = 0;
        if (!IsVisible || Card.Opacity <= 0 || Card.ActualWidth <= 0 || Motion.IsReduced)
            return;
        double s = Math.Max(1, _area.Scale);
        var picture = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(Card.ActualWidth * s), (int)Math.Ceiling(Card.ActualHeight * s),
            96 * s, 96 * s, PixelFormats.Pbgra32);
        var copy = new DrawingVisual();
        using (var dc = copy.RenderOpen())
            dc.DrawRectangle(new VisualBrush(Card), null, new Rect(0, 0, Card.ActualWidth, Card.ActualHeight));
        picture.Render(copy);
        picture.Freeze();
        Leaving.Source = picture;
        Leaving.Width = Card.ActualWidth;
        Leaving.Height = Card.ActualHeight;
        Canvas.SetLeft(Leaving, Canvas.GetLeft(Card));
        Canvas.SetTop(Leaving, Canvas.GetTop(Card));
        Leaving.Opacity = _leavingFrom = Card.Opacity;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _grid.SetClock(now);
        _clock.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        if (IsOpen)
            _clock.Start();
    }

#if DEBUG
    /// <summary>Debug builds: the window's own pixels, for <see cref="IslandWindow.SaveScene"/>.</summary>
    internal FrameworkElement Visual => Root;

    /// <summary>Debug builds: the shape as drawn and where it is heading, with its orbit buttons, in physical pixels.</summary>
    internal Rect Footprint
    {
        get
        {
            var box = _drawn;
            box.Union(_target);
            foreach (var (_, rect) in IslandGeometry.OrbitLayout(_edge, _drawn, Slots()))
                box.Union(rect);
            double s = _area.Scale;
            return new Rect(_window.X + box.X * s, _window.Y + box.Y * s, box.Width * s, box.Height * s);
        }
    }
#endif

    protected override void OnClosed(EventArgs e)
    {
        _clock.Stop();
        StopFrames();
        if (_media is not null)
            _media.Changed -= OnMediaChanged;
        base.OnClosed(e);
    }
}
