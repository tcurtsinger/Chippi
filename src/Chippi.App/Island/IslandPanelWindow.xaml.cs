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
using Chippi.Features.Sound;
using Chippi.Features.Sound.Volume;
using Chippi.UI.Controls;
using Chippi.UI.Themes;
using Windows.Win32.Foundation;

namespace Chippi.App.Island;

/// <summary>
/// The open panel (DESIGN.md §5): the tool gallery or one tile's page, in one anchored shape that the widget
/// (the puck, or the bar) becomes, on its outer corner; every page starts with a back arrow.
/// Between pages the shape stays and the new content fades in. <see cref="IslandWindow"/> opens,
/// places and closes it.
/// </summary>
public partial class IslandPanelWindow : Window
{
    /// <summary>The shell's Now Playing destination, always in the gallery footer.</summary>
    public const string NowPlayingKey = "now-playing";

    private const double OpenRadius = 8; // the Windows 11 widget look
    private const double ShadowRoom = 40; // beside the shape, for its shadow
    internal static readonly TimeSpan GrowTime = TimeSpan.FromMilliseconds(260);
    internal static readonly TimeSpan ShrinkTime = TimeSpan.FromMilliseconds(190);
    private static readonly TimeSpan PageTime = TimeSpan.FromMilliseconds(180);
    private const double PageSlide = 12; // a page comes in from the right, the home from the left
    // Reduced motion: nothing grows or slides; the panel and its pages fade in place this quickly.
    private static readonly TimeSpan ReducedFadeTime = TimeSpan.FromMilliseconds(120);

    private enum Morph { Open, Page, Close }

    // How long a failed quick switch shows its error.
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
    private IReadOnlyList<IslandTool> _tools = [];
    private readonly IslandAudioTabs _audioTabs = new();
    private IReadOnlyList<string> _audioPageKeys = [];
    private bool _syncAudioTabs;
    internal bool? AudioShowsMixer { get; private set; }
    internal string? AudioPage { get; private set; }
    private IReadOnlyList<IslandActivity> _activities = [];
    private IMediaSessions? _media;
    private NowPlayingView? _nowPlaying;
    private string? _page; // null is the grid
    private string? _returnFocusKey; // the gallery destination to focus when returning from a page
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
    private double _pageScale = 1; // the authored page's DIPs to window DIPs on a small work area
    private Rect _drawn; // where it is drawn now
    private Rect _from;
    private double _openPhase, _phaseFrom; // 0 is the widget, 1 the full panel; a reversal keeps its place
    private readonly SolidColorBrush _rim = new();
    private Color _widgetEdge, _panelEdge;
    private Morph _morph;
    private bool _reduced; // this morph fades in place (reduced motion)
    private double _pageFrom; // where the new page slides from: +1 the right (into a page), -1 the left (back home)
    private TimeSpan _elapsed;
    private double _fadeFrom;
    private long? _lastFrame;
    private EventHandler? _frame;
    private readonly IslandKeyboardHandoff _keyboard = new();

    public IslandPanelWindow()
    {
        InitializeComponent();
        _audioTabs.SetResourceReference(StyleProperty, "IslandAudioTabsStyle");
        AutomationProperties.SetName(_audioTabs, "Audio view");
        _audioTabs.SelectionChanged += (_, _) =>
        {
            if (_syncAudioTabs || _audioTabs.SelectedIndex < 0 || _audioTabs.SelectedIndex >= _audioPageKeys.Count) return;
            string selected = _audioPageKeys[_audioTabs.SelectedIndex];
            if (IsOpen && IslandTool.IsAudioPage(_page) && selected != _page)
                ShowPage(selected);
        };
        _grid.TileOpened += (_, key) => ShowPage(key);
        _grid.TileToggled += (_, key) => Toggle(key);
        _grid.SearchRequested += (_, _) => TakeKeyboard();
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
            if (e.Key != Key.Escape || PopupOwnsEscape(e.OriginalSource as DependencyObject))
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

    /// <summary>The page to restore next time: a tile key, or null for Home.</summary>
    internal event EventHandler<string?>? LastPageChanged;

    public bool IsOpen { get; private set; }

    internal HWND Handle => _hwnd;

    /// <summary>The page showing: a tile key, or null for the grid.</summary>
    internal string? CurrentPage => _page;

    /// <summary>Whether the user requested the keyboard and the panel still owns the foreground.</summary>
    internal bool HasKeyboard => _keyboard.Requested && IsForegroundWindow(_hwnd);

    /// <summary>The app in front, read just before the island takes the keyboard (Phase 5 notes #62).</summary>
    internal Func<Core.Clipboard.PasteTarget?>? CaptureTarget { get; set; }

    /// <summary>Brings the island in front to take the keyboard; tests stand in for it, so none takes the owner's foreground.</summary>
    internal Action<HWND> ActivateWindow { get; set; } = IslandNative.Activate;

    /// <summary>Checks the real foreground instead of treating an activation attempt as success.</summary>
    internal Func<HWND, bool> IsForegroundWindow { get; set; } = IslandNative.IsForeground;

    /// <summary>The app that was in front before the island took the keyboard; null until it has, and after it closes.</summary>
    internal Core.Clipboard.PasteTarget? PasteTarget => _keyboard.PasteTarget;

    /// <summary>Lets the page use Esc first (a search to clear): true when it did, and the island stays open.</summary>
    internal bool PageHandlesEscape()
    {
        if (IsOpen && _page == SoundTile.Key)
        {
            var picker = Mouse.Captured as ComboBox ?? Keyboard.FocusedElement as ComboBox;
            if (picker is null && Keyboard.FocusedElement is ComboBoxItem item)
                picker = ItemsControl.ItemsControlFromItemContainer(item) as ComboBox;
            if (picker is { IsDropDownOpen: true } && GetWindow(picker) == this)
            {
                picker.IsDropDownOpen = false;
                return true;
            }
        }
        return IsOpen && _page is null ? _grid.CloseSearch() : PageHandles(s => s.HandleEscape ?? s.HandleBack, "Esc");
    }

    private static bool PopupOwnsEscape(DependencyObject? source)
    {
        for (var at = source; at is not null; at = LogicalTreeHelper.GetParent(at)
            ?? (at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : null))
        {
            if (at is System.Windows.Controls.Primitives.Popup { IsOpen: true } or ComboBox { IsDropDownOpen: true }) return true;
            if (at is ComboBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is ComboBox { IsDropDownOpen: true }) return true;
        }
        return false;
    }

    /// <summary>Lets the page use its Back arrow first (a detail page back to its overview): true when it did, and the page stays.</summary>
    internal bool PageHandlesBack() => PageHandles(s => s.HandleBack, "Back");

    private bool PageHandles(Func<IslandTileSpec, Func<bool>?> handler, string what)
    {
        if (!IsOpen || Group(_page) is not { } group)
            return false;
        foreach (var part in group.Parts)
        {
            try
            {
                if (handler(part.Spec)?.Invoke() == true)
                    return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Island: {part.ModuleId} failed to handle {what}", ex);
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
        if (TryFindResource("IslandWidgetBorderBrush") is SolidColorBrush widget && TryFindResource("IslandBorderBrush") is SolidColorBrush panel)
        {
            _widgetEdge = widget.Color;
            _panelEdge = panel.Color;
            _rim.Color = IsVisible ? _panelEdge : _widgetEdge;
            ShapeFill.BorderBrush = _rim;
        }
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
    internal void SetLastPage(string? key, bool? audioShowsMixer = null, string? audioPage = null)
    {
        _lastPage = key;
        AudioPage = IslandTool.RememberedAudioPage(audioPage, key, audioShowsMixer);
        AudioShowsMixer = AudioPage is null ? null : AudioPage == AppVolumeModule.TileKey;
    }

    /// <summary>The page the next open shows instead of the usual one (a notice click); null for the usual.</summary>
    internal void OpenOn(string? key) => _openOn = key;

    /// <summary>Settings › Island &amp; pet: where it opens, the tiles' order and which show.</summary>
    internal void SetLayout(IslandLayout layout)
    {
        if (layout == _layout)
            return;
        _layout = layout;
        _tools = _layout.Tools(_groups);
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

    /// <summary>A beat in the music: every Chippi in the panel that's listening bobs to it (each checks for itself).</summary>
    internal void Beat(double strength)
    {
        if (!IsOpen)
            return;
        _grid.Pet.Beat(strength);
        _nowPlaying?.Beat(strength);
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
        _tools = _layout.Tools(tiles);
        foreach (var group in _groups)
            foreach (var part in group.Parts)
                part.StatusChanged += OnStatusChanged;
        RefreshGrid();
        if (!IsOpen)
            return;
        if (_page is not null && !Exists(_page))
            ShowPage(ResolvePage(_page), remember: false);
        else if (_page is not null)
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
    internal void Open(Rect puck, Rect widget, MonitorArea area, SolidColorBrush? sourceRim = null,
        double tabExtension = 0, bool opensLeft = false)
    {
        _puck = puck;
        _widget = widget;
        SetWidgetTab(tabExtension, opensLeft);
        _area = area;
        if (IsOpen)
        {
            Place();
            return;
        }
        _cameFrom = null;
        IsOpen = true;
        bool shrinking = IsVisible;
        if (!shrinking && (sourceRim ?? TryFindResource("IslandWidgetBorderBrush") as SolidColorBrush) is { } rim)
            _widgetEdge = rim.Color;
        if (ResolvePage(_openOn) is { } requested)
            _page = requested;
        else if (shrinking)
            _page = ResolvePage(_page); // a module may have stopped while the widget was folding
        else
            _page = _layout.OpensOn == IslandOpensOn.LastPage ? ResolvePage(_lastPage) : null;
        _openOn = null;
        Attach(_page);
        RefreshGrid(); // the volume and the toggles are read as the home opens
        Place();
        Begin(Morph.Open);
        Show(); // the first visible surface already has the source outline and source ink
        if (TakesKeyboard(_page))
            TakeKeyboard();
    }

    /// <summary>
    /// Folds back into the widget, then hides; <paramref name="immediate"/> hides at once. <paramref name="widget"/>
    /// is the widget as it is now (a song may have started or stopped while the panel was open).
    /// </summary>
    public void CloseIsland(bool immediate = false, Rect? widget = null, double tabExtension = 0, bool opensLeft = false)
    {
        if (!IsOpen)
        {
            if (immediate && IsVisible) Collapse(); // a pickup can interrupt the closing morph too
            return;
        }
        IsOpen = false;
        if (widget is { } now && _hwnd != default)
        {
            _widget = now;
            _widgetLocal = Local(now);
            SetWidgetTab(tabExtension, opensLeft);
        }
        _keyboard.Reset();
        MarkOnScreen();
        if (immediate)
            Collapse();
        else
            Begin(Morph.Close);
    }

    private void SetWidgetTab(double extension, bool opensLeft)
    {
        ShapeFill.SetTab(extension, opensLeft);
        ShapeShadow.SetTab(extension, opensLeft);
    }

    /// <summary>Whether <paramref name="physical"/> is on the open island's shape.</summary>
    internal bool Covers(Point physical)
    {
        if (!IsOpen)
            return false;
        var local = new Point((physical.X - _window.X) / _area.Scale, (physical.Y - _window.Y) / _area.Scale);
        return (_frame is not null && _morph == Morph.Page ? _drawn : _target).Contains(local);
    }

    /// <summary>Takes the keyboard without a click (the tray icon chosen with the keyboard, or a second launch).</summary>
    internal void TakeKeyboard() => TakeKeyboard(null);

    private string? _cameFrom;

    internal bool GoBackToPage(string key)
    {
        if (!IsOpen || !Exists(key)) return false;
        ShowPage(key, remember: false, back: true);
        return true;
    }

    private void Back()
    {
        if (!PageHandlesBack()) ShowPage(_cameFrom, back: true);
    }

    /// <summary>Shows <paramref name="key"/>'s page, or the grid for null; Back slides from the left.</summary>
    internal void ShowPage(string? key, bool remember = true, bool back = false)
    {
        key = ResolvePage(key);
        if (remember)
            RememberPage(key);
        if (key == _page && IsOpen)
            return;
        bool changeTab = IslandTool.IsAudioPage(_page) && IslandTool.IsAudioPage(key);
        if (!changeTab || back)
            _cameFrom = IsOpen && !back && key is not null ? _page : null;
        if (_page is null && key is not null)
            _returnFocusKey = IslandTool.GalleryKey(key);
        if (key is null && _page is not null)
            _returnFocusKey ??= IslandTool.GalleryKey(_page);
        bool focusTab = changeTab && _audioTabs.IsKeyboardFocusWithin;
        _page = key;
        if (!IsOpen)
            return;
        Attach(key);
        if (changeTab)
        {
            if (focusTab && _audioTabs.IsVisible)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (IsOpen && IsForegroundWindow(_hwnd))
                        (_audioTabs.ItemContainerGenerator.ContainerFromIndex(_audioTabs.SelectedIndex) as ListBoxItem)?.Focus();
                });
            return;
        }
        Reshape(snap: false);
        _pageFrom = back || key is null ? -1 : 1;
        if (_frame is null || _morph != Morph.Open)
            Begin(Morph.Page); // a shortcut during the open reveals the requested page through the same growing outline
        if (_keyboard.Requested && IsForegroundWindow(_hwnd))
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (IsOpen && IsForegroundWindow(_hwnd))
                    FocusFirst();
            });
        else if (TakesKeyboard(key))
            TakeKeyboard();
    }

    /// <summary>Whether showing <paramref name="key"/>'s page takes the keyboard at once (the Command bar).</summary>
    private bool TakesKeyboard(string? key) => Group(key)?.Parts.Any(p => p.Spec.TakesKeyboard) == true;

    // ----- Pages -----

    /// <summary>Every page the grid offers, in grid order.</summary>
    internal IReadOnlyList<string> PageKeys() => [NowPlayingKey, .. _groups.Where(g => g.Key != NowPlayingKey).Select(g => g.Key)];

    private bool Exists(string key) => key == NowPlayingKey || _groups.Any(g => g.Key == key);

    private string? ResolvePage(string? key)
    {
        if (key == IslandTool.Audio)
            key = IslandTool.ResolveAudioPage(AudioPage, _groups.Select(g => g.Key));
        if (key is null || Exists(key)) return key;
        if (!IslandTool.IsAudioPage(key)) return null;
        return IslandTool.ResolveAudioPage(key, _groups.Select(g => g.Key));
    }

    private void RememberPage(string? key)
    {
        bool? mixer = IslandTool.IsAudioPage(key) ? key == AppVolumeModule.TileKey : AudioShowsMixer;
        string? audioPage = IslandTool.IsAudioPage(key) ? key : AudioPage;
        if (_lastPage == key && AudioPage == audioPage && AudioShowsMixer == mixer) return;
        AudioShowsMixer = mixer;
        AudioPage = audioPage;
        _lastPage = key;
        LastPageChanged?.Invoke(this, key);
    }

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
                _nowPlaying = new NowPlayingView(_media, Back);
                _nowPlaying.SetPetColor(_grid.Pet.PetColor);
            }
            page.Children.Add(_nowPlaying); // its own header and body
        }
        else
        {
            // Construct sections first: a module may expose its header and focus through its view.
            foreach (var part in Group(key)?.Parts ?? []) _ = View(part);
            var factory = Group(key)?.Parts.Select(p => p.Spec.CreateHeader).FirstOrDefault(f => f is not null);
            var header = IslandTool.IsAudioPage(key) ? AudioHeader(key) : factory?.Invoke() as FrameworkElement ?? PageHeader(key);
            header.Margin = new Thickness(0);
            header.VerticalAlignment = VerticalAlignment.Center;
            // Every header gets the same 24 row and the 8 gap below it, so every body is 496 × 304; a page that asks for a
            // taller row (the Command bar's 32 field) gets it, and its body is that much shorter.
            var back = BackButton(Back);
            DockPanel.SetDock(back, Dock.Left);
            double height = IslandTool.IsAudioPage(key) && (IslandTool.AudioPages.Count(Exists) > 1 || key == "sound.board") ? 32
                : Group(key)?.Parts.Max(p => p.Spec.HeaderHeight) is > 0 and var taller ? taller : IslandPage.HeaderHeight;
            var row = new DockPanel { Height = height, Margin = new Thickness(0, 0, 0, IslandPage.Gap), LastChildFill = true, Children = { back, header } };
            DockPanel.SetDock(row, Dock.Top);
            _attached.Add(row);
            page.Children.Add(row);
            page.Children.Add(Body(Group(key)?.Parts ?? [], devices: key == SoundTile.Key));
        }
        Page.Content = page;
        RefreshPageStatus();
        MarkOnScreen();
    }

    /// <summary>The Back arrow every page starts with.</summary>
    internal static Button BackButton(Action back)
    {
        var glyph = new LucideIcon { Data = LucideIcons.ChevronLeft, Size = 16 };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "IslandTextSecondaryBrush");
        var button = new Button { Content = glyph, Margin = new Thickness(-4, 0, 4, 0), ToolTip = "Back" };
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
        _pageStatus.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(name, Dock.Left);
        var header = new DockPanel { Children = { name, _pageStatus } };
        _attached.Add(header);
        return header;
    }

    private DockPanel AudioHeader(string key)
    {
        var row = new DockPanel();
        var available = IslandTool.AudioPages.Where(Exists).ToList();
        if (available.Count > 1)
        {
            _syncAudioTabs = true;
            try
            {
                if (!_audioPageKeys.SequenceEqual(available))
                {
                    _audioTabs.Items.Clear();
                    _audioPageKeys = available;
                    foreach (string page in available) _audioTabs.Items.Add(IslandTool.AudioPageName(page));
                }
                _audioTabs.SelectedIndex = available.IndexOf(key);
            }
            finally { _syncAudioTabs = false; }
            DockPanel.SetDock(_audioTabs, Dock.Right);
            row.Children.Add(_audioTabs);
        }
        row.Children.Add(new TextBlock { Text = "Audio", FontSize = 16, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center });
        _attached.Add(row);
        return row;
    }

    /// <summary>
    /// The page's 496 × 304 body. One section fills it; several stack 8 apart; sections in columns sit
    /// side by side. It never scrolls and never grows the island: what doesn't fit is cut off here, so
    /// each page fits its content to the body (DESIGN.md §5 Island, revised 2026-10-28). The cut is above
    /// and below; at the sides it reaches into the card's padding, so a Chippi set out to the cards' edge
    /// (Keep awake's) keeps his sparks, and the panel's own edge cuts there.
    /// </summary>
    private Grid Body(IReadOnlyList<IslandTileContribution> parts, bool devices)
    {
        var body = new Grid();
        body.SizeChanged += (_, e) =>
        {
            double side = Card.Padding.Left;
            body.Clip = new RectangleGeometry(new Rect(-side, 0, e.NewSize.Width + 2 * side, e.NewSize.Height));
        };
        _attached.Add(body);
        PageBody = body;
        if (devices)
        {
            var layout = SoundTile.DevicesBody(parts.Select(View).OfType<FrameworkElement>().ToList());
            _attached.Add(layout);
            body.Children.Add(layout);
        }
        else if (parts.Any(p => p.Spec.Column is not null))
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
        bool caution = group.NeedsAttention || LiveOn(group.Key)?.NeedsAttention == true || Muted(group.Key);
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
        double previousHeight = _grid.HomeHeight;
        var tiles = new List<IslandTileView>();
        long now = Stopwatch.GetTimestamp();
        foreach (var tool in _tools.Where(t => t.Visible))
        {
            var group = tool.Source;
            var toggle = group.Toggle;
            bool muted = Muted(group.Key);
            bool attention = group.NeedsAttention || LiveOn(group.Key)?.NeedsAttention == true || muted;
            bool failed = _failed.TryGetValue(group.Key, out long until) && until > now;
            // Sound opens both Output and Microphone. Its quick mute switch has its own identity.
            var (name, icon) = tool.Key == IslandTool.Audio
                ? (tool.Name, LucideIcons.Get(tool.Icon))
                : (toggle?.Name ?? group.Name, muted ? LucideIcons.MicOff : LucideIcons.Get(toggle?.Icon ?? group.Icon));
            // A feature whose state can't be read shows its button plain; the others still refresh.
            bool on = Read($"{group.Key} state", () => toggle?.IsOn() == true, false);
            // Needing attention for another reason than mute, the tile's status says why ("Mic stopped"), not the toggle's "Live".
            string status = failed ? "Failed"
                : attention && !muted && group.Status.Length > 0 ? group.Status
                : Read($"{group.Key} status", () => toggle?.Status?.Invoke(), null) ?? group.Status;
            bool waiting = _activities.Any(a => a.Kind == IslandActivityKind.Agent && a.NeedsAttention && a.OpensTile == group.Key);
            tiles.Add(new(tool.Key, name, icon, status, on, attention || failed, toggle is not null, tool.Name, Waiting: waiting));
        }
        _grid.SetTiles(tiles);
        var volumeGroup = _groups.FirstOrDefault(g => g.Volume is not null);
        Read($"{volumeGroup?.Key} volume", () => { _grid.SetVolume(volumeGroup?.Volume, volumeGroup?.Key); return true; }, false);
        _shownTiles = [.. tiles.Select(t => t.Key)];
        RefreshPageStatus();
        MarkOnScreen();
        if (IsOpen && IsVisible && _page is null && previousHeight != _grid.HomeHeight)
            Reshape(snap: _frame is null);
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
    /// A quick switch on the home. While the feature applies a change, a second press is ignored.
    /// </summary>
    private async void Toggle(string key)
    {
        if (key == IslandTool.Audio) key = SoundTile.Key;
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
        }
        finally
        {
            _toggling.Remove(key);
            RefreshGrid();
        }
    }

    /// <summary>Tells each tile whether it's seen: the grid open, and the tile not hidden.</summary>
    private void MarkOnScreen()
    {
        bool grid = IsOpen && _page is null;
        foreach (var group in _groups)
            foreach (var part in group.Parts)
                part.SetOnScreen(grid && _shownTiles.Contains(IslandTool.GalleryKey(group.Key)));
    }

    private bool Muted(string key) => _activities.Any(a => a.ModuleId == "mic-mute" && a.NeedsAttention && a.TileKey == key);

    private IslandActivity? LiveOn(string key) =>
        _activities.FirstOrDefault(a => a.TileKey == key || (key == NowPlayingKey && a.Kind == IslandActivityKind.Media));

    private string TileName(string? key) => key == NowPlayingKey ? "Now Playing" : Group(key)?.Name ?? "";

    // ----- Keyboard -----

    /// <summary>
    /// A press inside the island requests the foreground, so Tab, arrows and Enter reach it normally.
    /// Retry on a later press if Windows refused activation or another app reclaimed the foreground.
    /// Until activation only the Esc hook watches the keyboard (no global hook takes other apps' keys).
    /// </summary>
    private void TakeKeyboard(DependencyObject? pressed)
    {
        if (!IsOpen || !_keyboard.Take(_hwnd, IsForegroundWindow, CaptureTarget, ActivateWindow))
            return;
        var focusAtActivation = Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            // Focus() itself can activate a WPF window. Do not use it to fight a game that kept
            // or reclaimed the foreground; the next deliberate press can retry the handoff.
            if (!IsOpen || !IsForegroundWindow(_hwnd))
                return;
            // A composite control can focus its slider during the bubbling press, after our preview.
            if (pressed is not null && IsKeyboardFocusWithin && Keyboard.FocusedElement != focusAtActivation)
                return;
            if (pressed is null && _page is null && _grid.IsSearching && _grid.FocusTile(null))
                return;
            for (var at = pressed; at is not null; at = at is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            {
                if (at is UIElement { Focusable: true, IsEnabled: true } target && (target.IsKeyboardFocusWithin || target.Focus()))
                    return;
            }
            if (pressed is null || !IsKeyboardFocusWithin)
                FocusFirst();
        });
    }

    private void FocusFirst()
    {
        if (_page is null)
        {
            if (!_grid.FocusTile(_returnFocusKey))
                _grid.FocusTile(null);
        }
        else
        {
            var focus = Group(_page)?.Parts.Select(p => p.Spec.InitialFocus?.Invoke()).OfType<UIElement>().FirstOrDefault();
            if (focus?.Focus() != true) Card.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    // ----- Placement and the morph -----

    /// <summary>
    /// Covers the widget and the panel it becomes, with room for the shadow, while open, so the morph never
    /// moves the window. Kept on the monitor, and no bigger: every frame copies all of it.
    /// </summary>
    private void Place()
    {
        double s = _area.Scale;
        // The host reserves the full page so the HWND stays still when a shorter gallery opens a tool.
        var cover = IslandGeometry.PanelRect(_puck, _area.Work, s);
        cover.Union(_widget);
        cover.Inflate(ShadowRoom * s, ShadowRoom * s);
        cover.Intersect(_area.Monitor);
        _window = new Rect(Math.Floor(cover.X), Math.Floor(cover.Y), Math.Ceiling(cover.Width), Math.Ceiling(cover.Height));
        IslandNative.Move(_hwnd, _window);
        Root.Width = _window.Width / s;
        Root.Height = _window.Height / s;
        _widgetLocal = Local(_widget);
        Reshape(snap: IsVisible && (_frame is null || _morph == Morph.Page));
    }

    private Rect Local(Rect physical) =>
        new((physical.X - _window.X) / _area.Scale, (physical.Y - _window.Y) / _area.Scale, physical.Width / _area.Scale, physical.Height / _area.Scale);

    /// <summary>
    /// Places the 520 × 360 page on the puck's corner, fitting it uniformly inside the work area.
    /// <paramref name="snap"/> also draws it there at once (no morph running).
    /// </summary>
    private void Reshape(bool snap)
    {
        Card.Height = _page is null ? _grid.HomeHeight : IslandPage.Height;
        _target = Local(IslandGeometry.PanelRect(_puck, _area.Work, _area.Scale, Card.Height));
        double fit = _target.Width / IslandPage.Width;
        if (fit != _pageScale)
        {
            _pageScale = fit;
            Card.RenderTransform = fit == 1 ? Transform.Identity : new ScaleTransform(fit, fit);
        }
        Canvas.SetLeft(Card, _target.X);
        Canvas.SetTop(Card, _target.Y);
        // The home's Chippi sits on the puck's side of the footer. On a bottom corner that's where the puck's own Chippi is,
        // unless the work area's edge pushed the panel off the puck's corner.
        var puck = Local(_puck);
        var puckCenter = new Point(puck.X + puck.Width / 2, puck.Y + puck.Height / 2);
        bool right = puckCenter.X > _target.X + _target.Width / 2;
        _grid.SetPetSide(right);
        var pet = new Point(right ? _target.Right - IslandGrid.FooterPet : _target.X + IslandGrid.FooterPet, _target.Bottom - IslandGrid.FooterPet);
        // The seamless handoff needs the same size as well as the same center. A fitted footer fades in.
        _petAtPuck = _pageScale == 1 && (pet - puckCenter).Length < 1;
        if (snap && _morph != Morph.Close)
        {
            _drawn = _target;
            _radius = OpenRadius * _pageScale;
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
    /// Starts from the surface already on screen. Opening and closing share one reversible path.
    /// Page changes keep the corner anchored. Reduced motion dissolves the panel in place.
    /// </summary>
    private void Begin(Morph morph)
    {
        _morph = morph;
        _reduced = Motion.IsReduced;
        _phaseFrom = _openPhase;
        _from = morph == Morph.Open && !IsVisible ? _widgetLocal : _drawn;
        _fadeFrom = Card.Opacity;
        _elapsed = TimeSpan.Zero;
        if (morph == Morph.Page)
            Card.Opacity = 0;
        if (_frame is null)
        {
            _frame = OnFrame;
            CompositionTarget.Rendering += _frame;
        }
        _lastFrame = null;
        Step();
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        // Let the first composition present frame zero, even on a cold open.
        long now = Clock();
        if (_lastFrame is { } previous)
            _elapsed += Stopwatch.GetElapsedTime(previous, now);
        _lastFrame = now;
        Step();
    }

    private void Step()
    {
        var time = _reduced ? ReducedFadeTime : _morph switch { Morph.Close => ShrinkTime, Morph.Page => PageTime, _ => GrowTime };
        double t = Math.Min(1, _elapsed / time);
        if (_morph == Morph.Page)
        {
            if (_drawn != _target)
            {
                double at = _reduced ? 1 : SmoothStep(t);
                _drawn = new Rect(Lerp(_from.X, _target.X, at), Lerp(_from.Y, _target.Y, at),
                    Lerp(_from.Width, _target.Width, at), Lerp(_from.Height, _target.Height, at));
                Draw();
            }
            Card.Opacity = t;
            PageShift.X = _reduced ? 0 : _pageFrom * PageSlide * Math.Pow(1 - t, 3);
            Page.RenderTransform = PageShift.X == 0 ? Transform.Identity : PageShift;
            Card.IsHitTestVisible = t >= 1;
            if (t >= 1) StopFrames();
            return;
        }

        bool opening = _morph == Morph.Open;
        double end = opening ? 1 : 0;
        _openPhase = Math.Clamp(_phaseFrom + (opening ? t : -t), 0, 1);
        double height = SmoothStep(_openPhase);
        double width = WidthProgress(height);
        var to = opening ? _target : _widgetLocal;
        double startHeight = SmoothStep(_phaseFrom);
        double x = Travel(WidthProgress(startHeight), width, end), y = Travel(startHeight, height, end);
        _drawn = _reduced ? _target : new Rect(Lerp(_from.X, to.X, x), Lerp(_from.Y, to.Y, y),
            Lerp(_from.Width, to.Width, x), Lerp(_from.Height, to.Height, y));
        // The round ends stretch with the small shape before resolving into the panel's normal corners.
        double softEnd = Math.Min(36, Math.Min(_drawn.Height, _drawn.Width) / 2);
        _radius = _reduced ? OpenRadius * _pageScale : Lerp(softEnd, OpenRadius * _pageScale, SmoothStep((_openPhase - 0.16) / 0.74));
        Root.Opacity = _reduced ? _openPhase : 1;
        double startInk = Ink(_phaseFrom);
        double inkTravel = startInk == end ? Travel(_phaseFrom, _openPhase, end) : Travel(startInk, Ink(_openPhase), end);
        Card.Opacity = _reduced ? (opening ? 1 : _fadeFrom) : Lerp(_fadeFrom, end, inkTravel);
        PageShift.X = 0;
        Page.RenderTransform = Transform.Identity;
        ShapeShadow.Opacity = _reduced ? 1 : Lerp(WidgetShadowShare, 1, height);
        _rim.Color = _reduced ? _panelEdge : _widgetEdge * (float)(1 - height) + _panelEdge * (float)height;
        _grid.Pet.Opacity = PetMeetsPuck && !_reduced && (!opening || _openPhase < 1) ? 0 : 1;
        Draw();
        Morphing?.Invoke(opening, opening ? _openPhase : 1 - _openPhase);
        if (_openPhase != end)
            return;
        StopFrames();
        if (_morph == Morph.Close)
            Collapse();
    }

    internal static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double WidthProgress(double height) => height + 0.64 * height * (1 - height);
    private static double Ink(double phase) => SmoothStep((phase - 0.24) / 0.60);
    // Portion of the remaining curve travelled since the last direction change.
    private static double Travel(double from, double now, double to) => from == to ? 1 : (now - from) / (to - from);

    private double _radius = OpenRadius;

    /// <summary>The new page's slide, on the page only while it slides.</summary>
    internal TranslateTransform PageShift { get; } = new();

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
        _openPhase = 0;
        _grid.Pet.Opacity = 1;
        _radius = OpenRadius;
        ShapeShadow.Opacity = 1;
        Hide();
        Morphing?.Invoke(false, 1);
        Card.Opacity = 0;
        Root.Opacity = 1;
        PageShift.X = 0;
        // Unparented views get Unloaded and stop listening until the island opens again.
        Detach();
        _grid.CloseSearch();
        _returnFocusKey = null;
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
        // The clip belongs to the card, before its transform into the shape's coordinate system.
        var inside = new Rect((_drawn.X - _target.X) / _pageScale, (_drawn.Y - _target.Y) / _pageScale,
            _drawn.Width / _pageScale, _drawn.Height / _pageScale);
        Card.Clip = new RectangleGeometry(inside, _radius / _pageScale, _radius / _pageScale);
        // The reveal can finish before the moving edge. Accept a press only on the settled page.
        Card.IsHitTestVisible = IsOpen && _openPhase >= 1 && Card.Opacity >= 1;
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
