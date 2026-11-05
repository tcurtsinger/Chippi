// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chippi.UI.Pet;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.AgentUsage;
using Chippi.UI.Controls;
using Chippi.UI.Themes;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Island;

/// <summary>The user dropped the widget: where on its display, and which display.</summary>
internal sealed class IslandPlacementEventArgs(double x, double y, HMONITOR monitor) : EventArgs
{
    /// <summary>0–1 across the room the puck has in the work area (AppSettings.IslandX).</summary>
    public double X { get; } = x;

    /// <summary>Likewise, top to bottom (AppSettings.IslandY).</summary>
    public double Y { get; } = y;

    /// <summary>The monitor it landed on.</summary>
    public HMONITOR Monitor { get; } = monitor;
}

/// <summary>
/// The widget on the desktop (DESIGN.md §5, §9), and the controller for the open panel and the drag:
/// one shape that is the puck with Chippi, or the bar it stretches into toward the screen's middle while
/// there is something to show (the song, or the highest activity), with the red badge for a muted mic.
/// Status icons extend the compact puck into a side tab. Every window here never activates (WS_EX_NOACTIVATE
/// plus WM_MOUSEACTIVATE) and never shows in Alt+Tab or the taskbar, and is placed in physical pixels on
/// the monitor it is given. Hovering brightens the rim and wakes the blob; a click becomes the panel,
/// which stays open until a click outside it or Esc. Moving 4 DIPs picks up only the compact puck.
/// Its tab turns inward while held and it lands inside the work area. The tray icon opens
/// Settings. <see cref="IslandHost"/> feeds it activities, the mood, the monitor, the saved place and
/// whether to hide.
/// </summary>
public partial class IslandWindow : Window
{
    private const double DragThreshold = 4;
    private const double SongGap = 40; // between one pass and the next
    private const double SongSpeed = 30; // DIPs a second
    private const double LiftScale = 1.12;
    private const double LiftTilt = -6;
    private const double PressScale = 0.96; // pressed, before it opens or lifts
    private const uint VK_ESCAPE = 0x1B;

    private static readonly TimeSpan GrowTime = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan BadgeIn = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan BadgeOut = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReducedFade = TimeSpan.FromMilliseconds(120); // the bar's content, under reduced motion
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan UnfoldTime = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan FoldTime = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan SongHold = TimeSpan.FromSeconds(2);

    /// <summary>A paused song keeps the bar this long before it folds, so a quick pause doesn't make it flicker.</summary>
    internal static readonly TimeSpan PauseHold = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan AgentPeek = TimeSpan.FromSeconds(6);
    internal static readonly TimeSpan AgentHoverDelay = TimeSpan.FromMilliseconds(350);
    internal static readonly TimeSpan RestDelay = TimeSpan.FromMinutes(1);
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly IslandPanelWindow _panel = new();
    private IReadOnlyList<IslandActivity> _activities = [];
    private (string Module, string Key)? _agentState;
    private IIslandTimer? _agentPeekTimer, _agentHoverTimer;
    private bool _agentPeeking, _agentHovered, _agentDwelling;
    private Point _agentPeekPointer;
    private IIslandTimer? _restTimer;
    private bool _petEngaged, _windingDown;
    private IReadOnlyList<IslandTileGroup> _tiles = [];
    private PetMood _mood = PetMood.Sleepy;

    // What the puck's Chippi shrinks to as the puck becomes the panel: the footer's, 70% of his size here.
    private const double PetShrunk = IslandGrid.FooterPetSize / PetSize.Puck;
    private double? _x, _y; // the saved place; null is the start, bottom right
    private bool _fallback; // the chosen monitor is gone: the main display's start instead
    private bool _enabled = true;
    private bool _suppressed;
    private bool _summoned; // shown over a fullscreen app for the Command bar, until the panel closes
    private bool _hovered;
    private bool _lifted;
    private bool _left; // the bar and the panel grow left from the puck
    private readonly HOOKPROC _onKeyboard; // both kept alive for as long as their hook exists
    private readonly HOOKPROC _onMouse;
    private UnhookWindowsHookExSafeHandle? _escapeHook;
    private UnhookWindowsHookExSafeHandle? _clickHook;
    private HWND _hwnd;
    private HMONITOR _monitor;
    private Rect _puck; // the puck at rest, physical pixels
    private Point? _pressAt;
    private Vector _grab; // press offset in widget DIPs; becomes pet-local once the drag begins
    private double? _heldWidth; // freezes a press for its click, or the compact puck for its landing slide
    private bool _resumeBar; // a fold, unfold or content fade was interrupted by the press
    private bool _pressedBar;
    private bool _dragging;
    private MonitorArea? _dragArea; // reuse the work area for pointer moves on the same display
    private EventHandler? _slideFrame;
    private long _slideTick;
    private Action? _landing; // a drop's slide ending: Chippi set down and the place saved, even when it's cut short
    private HPOWERNOTIFY _displayNotice; // the display turning off or on
    private double _followScale = double.NaN; // the scale of the last full move during a drag or slide
    private double _scaleTarget = 1, _tiltTarget;
    private Media.IMediaSessions? _media;
    private bool _nowPlayingAlwaysShow = true;
    private bool _musicHovered; // entered through Chippi; keep the reveal over the whole bar until the pointer leaves
    private bool _singing; // the bar names a song, playing or just paused
    private bool _barShown; // the widget is the bar (or unfolding into it), not the puck
    private double _compactWidth = IslandGeometry.Puck;
    private double _widthTarget = IslandGeometry.Puck;
    private double _tabExtension; // held while an existing tab retracts, so its shoulders fold into the circle
    private double PetColumnWidth => _compactWidth == IslandGeometry.Puck ? 46 : _compactWidth;
    private string? _heldSong; // the song last shown playing, which keeps the bar for PauseHold after it pauses
    private bool _holdingPause;
    private double _lineRoom; // the title's room on the bar; longer scrolls
    private string? _chipKey; // the page the bar opens
    private string? _lastPage; // the page last opened, saved once the panel has gone
    private string _lineText = "";
    private string? _shownContent; // what the bar shows, by identity: a new one arrives (Arrival)
    private string? _rim; // the rim's brush key, rest or hovered
    private string _chipText = ""; // the line as plain text, for the chip's name
    private string _lineState = "";
    private TabLevel? _level;
    private EqualizerBars _bars = new();
    private readonly BeatDetector _beats = new(); // the same level, for Chippi to bob to while he listens
    private DateOnly? _morning; // the day Chippi last woke for the first unlock
    private bool _stretching; // awake for a moment after his morning yawn
    private readonly DispatcherTimer _stretch = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <summary>After his morning yawn he stays awake this long (3s), then dozes off as usual. Tests shorten it.</summary>
    internal TimeSpan MorningAwake
    {
        get => _stretch.Interval;
        set => _stretch.Interval = value;
    }

    public IslandWindow()
    {
        InitializeComponent();
        _onKeyboard = OnKeyboard;
        _onMouse = OnMouse;

        PuckButton.MouseEnter += (_, _) => SetHover(true);
        PuckButton.MouseLeave += (_, _) => SetHover(false);
        // Hidden (a fullscreen app in front), the song stops scrolling; shown again, it starts over.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) RefreshPuck();
            else SetHover(false);
        };
        _stretch.Tick += (_, _) =>
        {
            _stretch.Stop();
            _stretching = false;
            RefreshPuck();
        };
        // Reduced motion turned on or off: the song holds or scrolls, and the bars stop or follow, at once.
        Motion.Changed += OnMotionChanged;
        _panel.IsVisibleChanged += (_, _) =>
        {
            // The panel covered the widget, so the widget never saw the pointer come or go.
            SetHover(!_panel.IsVisible && IsVisible && CoversWidget(PointerAt()));
            if (!_panel.IsVisible && _lastPage is not null)
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
                {
                    if (!_panel.IsVisible) SaveLastPage();
                });
        };
        // Windows presents the two layered windows separately. Keep the source surface lit at frame zero
        // and pass it to the panel as the edges first separate (the same small overlap on return).
        // Their visibility follows the same physical phase in both directions, even on a reversal.
        _panel.Morphing += (opening, t) =>
        {
            double phase = opening ? t : 1 - t;
            double Fade(double until) => Motion.IsReduced ? 1 - phase : 1 - IslandPanelWindow.SmoothStep(phase / until);
            bool hadBarInk = BarLayer.Opacity > 0;
            double surface = Fade(0.11);
            // Carry the fill across native presents, but let the moving outline own the rim and shadow
            // as soon as it separates. A second circular stroke inside the new surface reads as a puck.
            double edge = Fade(0.04);
            Body.Opacity = surface;
            Body.BorderThickness = new Thickness(edge);
            BodyShadow.Opacity = edge;
            Body.Visibility = BodyShadow.Visibility = surface > 0 ? Visibility.Visible : Visibility.Hidden;
            bool handOver = _panel.PetMeetsPuck && !Motion.IsReduced;
            // Where the footer meets the puck, only one pet draws: this one up to the final open frame,
            // then the footer's. Closing takes ownership back on the first frame at the same size.
            PuckEnd.Opacity = handOver ? (opening && phase >= 1 ? 0 : 1) : Fade(0.50);
            BadgeLayer.Opacity = Fade(0.50);
            PetScale.ScaleX = PetScale.ScaleY = Motion.IsReduced ? 1 : 1 - (1 - PetShrunk) * IslandPanelWindow.SmoothStep(phase);
            BarLayer.Opacity = Fade(0.38);
            if (opening && hadBarInk && BarLayer.Opacity == 0)
                RefreshPuck(); // now a hover-only bar may fold, or new activity replace the original ink
        };
        // Clicked into, the panel comes to the front; the widget, faded out over it, stays above it for the close.
        _panel.Activated += (_, _) => IslandNative.Raise(_hwnd);
        _panel.EscapePressed += (_, _) => CloseIsland();
        _panel.SettingsRequested += (_, _) =>
        {
            CloseIsland(immediate: true);
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        };
        // Saved once the panel has gone: a save writes settings.json, which mid-switch costs frames.
        _panel.LastPageChanged += (_, key) => _lastPage = key;

        // The mouse is handled here, before the button sees it: a press is a click or the start of a drag.
        // Click itself comes only from Enter, Space or UI Automation's Invoke.
        PuckButton.PreviewMouseLeftButtonDown += OnPress;
        PuckButton.PreviewMouseMove += OnPointerMove;
        PuckButton.PreviewMouseLeftButtonUp += OnRelease;
        PuckButton.LostMouseCapture += OnLostCapture;
        PuckButton.MouseRightButtonUp += (_, e) =>
        {
            if (MenuItems.Children.Count == 0)
                return;
            Menu.IsOpen = true;
            e.Handled = true;
        };
        PuckButton.Click += (_, _) =>
        {
            if (_barShown)
                OpenChip();
            else
                Toggle();
        };
        // A drag from another app over the closed puck (Phase 5 notes #48). The puck never takes the drop itself.
        PuckButton.AllowDrop = true;
        PuckButton.DragEnter += OnPuckDrag;
        PuckButton.DragOver += OnPuckDrag;
        PuckButton.DragLeave += (_, _) => DragLeft();
        PuckButton.Drop += (_, e) =>
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            DragLeft();
        };
        // The open panel: a page that takes drops (the Shelf) handles them; elsewhere a drop does nothing.
        // Seen even when the page handled them, which the Shelf's drop zone does.
        _panel.AllowDrop = true;
        _panel.AddHandler(DragEnterEvent, new DragEventHandler((_, e) => DragOverPanel(e)), handledEventsToo: true);
        _panel.AddHandler(DragOverEvent, new DragEventHandler((_, e) => DragOverPanel(e)), handledEventsToo: true);
        _panel.AddHandler(DragLeaveEvent, new DragEventHandler((_, _) => DragLeft()), handledEventsToo: true);
        _panel.AddHandler(DropEvent, new DragEventHandler((_, e) =>
        {
            if (!e.Handled)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
            _openedByDrag = false;
            DragCloseTimer.Stop();
        }), handledEventsToo: true);
        // A press or a key on the panel ends the drag-opened mode: normal closing resumes.
        _panel.PreviewMouseDown += (_, _) => _openedByDrag = false;
        _panel.PreviewKeyDown += (_, _) => _openedByDrag = false;
        // Only the window's own scale: an Image inside raises the same routed event, which bubbles up here, and a
        // re-place from the saved fractions would move a just-dropped puck by their rounding.
        DpiChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, this))
                Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                {
                    if (_dragging) DragTo(PointerAt());
                    else Place();
                });
        };

        ApplySide(left: true);
    }

    /// <summary>Raised when the display layout or work area changed (WM_DISPLAYCHANGE, WM_SETTINGCHANGE).</summary>
    public event EventHandler? DisplayChanged;

    /// <summary>Raised when the display turns off (false) or on again (true, dimmed too), and once with its state at the start.</summary>
    internal event EventHandler<bool>? DisplayPowerChanged;

    /// <summary>The user dropped the widget. It is already there; save it.</summary>
    internal event EventHandler<IslandPlacementEventArgs>? PlacementChosen;

    /// <summary>The home's Settings button: the panel has closed; open Settings.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>The user opened a page (a tile key): save it as AppSettings.IslandLastPage.</summary>
    public event EventHandler<string>? LastPageChanged;

    private void SaveLastPage()
    {
        if (_lastPage is not { } page) return;
        _lastPage = null;
        LastPageChanged?.Invoke(this, page);
    }

    internal HWND Handle => _hwnd;

    /// <summary>The open panel's window.</summary>
    internal HWND PanelHandle => _panel.Handle;

    /// <summary>AppSettings.IslandLastPage: the page the next open restores while its tile exists.</summary>
    public void SetLastPage(string? key, bool? audioShowsMixer = null, string? audioPage = null)
    {
        // A config write from another feature may arrive before the pending navigation is saved on close.
        if (_lastPage is null)
            _panel.SetLastPage(key, audioShowsMixer, audioPage);
    }

    /// <summary>Settings › Island &amp; pet: where the panel opens, and its buttons.</summary>
    internal void SetLayout(IslandLayout layout) => _panel.SetLayout(layout);

    /// <summary>
    /// The output's live level for the equalizer while the bar names a song playing, and its beats for Chippi to bob to,
    /// on the bar and in the panel.
    /// </summary>
    internal void SetLevel(TabLevel level)
    {
        _level = level;
        level.Level += (_, peak) => ShowLevel(peak);
    }

    /// <summary>
    /// The session was unlocked: the first time each day, Chippi wakes with a yawn and a stretch, stays awake a moment,
    /// then dozes off as usual.
    /// </summary>
    internal void Unlocked(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (_morning == today)
            return;
        _morning = today;
        Morning();
    }

    /// <summary>Chippi's morning: a yawn and a stretch, awake a moment, then dozing off (the debug menu's Morning too).</summary>
    internal void Morning()
    {
        if (!IsVisible || _lifted)
            return;
        _stretching = true;
        _stretch.Stop();
        _stretch.Start();
        RefreshPuck();
        Pet.Yawn();
    }

    /// <summary>Now Playing's media sessions.</summary>
    internal void SetMedia(Media.IMediaSessions media)
    {
        _media = media;
        media.Changed += (_, _) => RefreshPuck(); // a new track, its artist or its cover arriving
        _panel.SetMedia(media);
    }

    internal void SetNowPlayingAlwaysShow(bool alwaysShow)
    {
        if (_nowPlayingAlwaysShow == alwaysShow) return;
        _nowPlayingAlwaysShow = alwaysShow;
        RefreshPuck();
    }

    /// <summary>Creates the window handles with the no-activate tool-window styles, before anything is shown.</summary>
    public void Initialize()
    {
        _hwnd = IslandNative.MakeNoActivate(this, WndProc);
        _displayNotice = IslandNative.WatchDisplayPower(_hwnd);
        _panel.Initialize();
    }

    /// <summary>
    /// The monitor the widget sits on, for the display setting. <paramref name="fallback"/>: the chosen
    /// monitor is disconnected and this is the main display in its place, so the widget starts at its
    /// bottom right rather than at a place saved for another screen.
    /// </summary>
    internal void SetMonitor(HMONITOR monitor, bool fallback = false)
    {
        if (monitor == default || (monitor == _monitor && fallback == _fallback))
            return;
        _monitor = monitor;
        _fallback = fallback;
        Place();
        ApplyVisibility();
    }

    /// <summary>AppSettings.IslandX and IslandY: null for the start, 24 in from the bottom-right corner.</summary>
    public void SetPlacement(double? x, double? y)
    {
        if (x == _x && y == _y)
            return; // the drop already put it there
        _x = x;
        _y = y;
        Place();
    }

    /// <summary>True while a fullscreen app, game or presentation is in front: the widget hides.</summary>
    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        ApplyVisibility();
    }

    /// <summary>AppSettings.IslandEnabled.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        ApplyVisibility();
    }

    public void SetPetColor(PetColor color)
    {
        Pet.PetColor = color;
        _panel.SetPetColor(color);
    }

    /// <summary>The activities in display priority order (<see cref="IslandActivityHub.Activities"/>) and the pet's mood for them.</summary>
    public void SetActivities(IReadOnlyList<IslandActivity> activities, PetMood mood)
    {
        _activities = activities;
        _mood = mood;
        UpdateAgentPeek(activities.FirstOrDefault(a => a.Kind == IslandActivityKind.Agent));
        RefreshPuck();
        _panel.SetActivities(activities.Where(a => a.ModuleId != Chippi.Features.PlainPaste.PlainPasteModule.Id).ToArray());
    }

    /// <summary>The running features' tiles, in the home's order. The bar shows a tile's status, so it follows them.</summary>
    public void SetTiles(IReadOnlyList<IslandTileGroup> tiles)
    {
        foreach (var part in _tiles.SelectMany(t => t.Parts))
            part.StatusChanged -= OnStatusChanged;
        _tiles = tiles;
        foreach (var part in _tiles.SelectMany(t => t.Parts))
            part.StatusChanged += OnStatusChanged;
        _panel.SetTiles(tiles);
        RefreshPuck();
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshPuck);

    /// <summary>A module reported success: the blob celebrates for about 2.5s.</summary>
    public void Celebrate()
    {
        Pet.Celebrate();
        _panel.Celebrate();
    }

    /// <summary>The puck's right-click menu (null is a separator): Open settings and Quit, after the debug entries with --debug.</summary>
    public void SetMenu(IEnumerable<(string Label, Action Action)?> items)
    {
        MenuItems.Children.Clear();
        foreach (var item in items)
        {
            if (item is not { } entry)
            {
                var line = new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4) };
                line.SetResourceReference(Border.BackgroundProperty, "IslandBorderBrush");
                MenuItems.Children.Add(line);
                continue;
            }
            var button = new Button { Content = entry.Label, Style = (Style)FindResource("IslandMenuItemStyle") };
            button.Click += (_, _) =>
            {
                Menu.IsOpen = false;
                entry.Action();
            };
            MenuItems.Children.Add(button);
        }
    }

    // ----- Visibility and placement -----

    private void ApplyVisibility()
    {
        bool visible = _enabled && (!_suppressed || _summoned) && _monitor != default && _hwnd != default;
        if (visible && !IsVisible)
        {
            Show();
            Place();
            RefreshPuck();
        }
        else if (!visible && IsVisible)
        {
            Menu.IsOpen = false;
            if (_pressAt is not null)
                Mouse.Captured?.ReleaseMouseCapture(); // ends a drag where it is: OnLostCapture
            // A hidden window can stop receiving the rendering frames that finish an edge slide.
            StopSlide();
            Land();
            CloseIsland(immediate: true);
            Hide();
        }
    }

    private MonitorArea? Area => IslandNative.Area(_monitor);

    /// <summary>The puck at rest, in physical pixels: the bar grows from it, and the panel sits on its outer corner.</summary>
    internal Rect PuckRect => _puck;

    /// <summary>The widget at rest, in physical pixels: the bar while it shows, otherwise the puck. The panel grows out of it and folds back into it.</summary>
    internal Rect WidgetRect => IslandGeometry.WidgetRect(_puck, _left,
        (_heldWidth ?? (_barShown ? IslandGeometry.BarWidth : _compactWidth)) * _puck.Height / IslandGeometry.Puck);

    /// <summary>Tests the drawn surface in its current position, including a drag or an interrupted unfold.</summary>
    internal bool CoversWidget(Point physical) =>
        Body.RenderSize.Width > 0 && PresentationSource.FromVisual(Body) is not null
            ? Body.ContainsLocal(Body.PointFromScreen(physical))
            : WidgetRect.Contains(physical);

    /// <summary>Whether the widget is the bar (or unfolding into it).</summary>
    internal bool BarShown => _barShown;

    /// <summary>Whether the bar and the panel grow left from the puck (it sits right of the middle).</summary>
    internal bool OpensLeft => _left;

    // A layered HWND can present its previous pixels just after a move. Keep the pet at the same local
    // center in both directions, with room for a full bar (256 − 25) and its shadow (37) on either side.
    private Rect WindowAt(Point center, double scale) =>
        new(center.X - Root.Width * scale / 2, center.Y - Root.Height * scale / 2, Root.Width * scale, Root.Height * scale);

    private static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    /// <summary>A move during a drag or slide: only the position, unless the display's scale changes the size.</summary>
    private void Follow(Point center, double scale)
    {
        var rect = WindowAt(center, scale);
        if (scale == _followScale)
        {
            IslandNative.MoveTo(_hwnd, rect.TopLeft);
            return;
        }
        _followScale = scale;
        IslandNative.Move(_hwnd, rect);
    }

    /// <summary>Puts the widget at its saved place on its monitor (and the open panel on its corner), and re-asserts topmost.</summary>
    private void Place()
    {
        if (_hwnd == default || _dragging || Area is not { } area)
            return;
        StopSlide();
        _puck = _fallback ? IslandGeometry.Start(area.Work, area.Scale) : IslandGeometry.PuckRect(_x, _y, area.Work, area.Scale);
        ApplySide(IslandGeometry.OpensLeft(_puck, area.Work));
        IslandNative.Move(_hwnd, WindowAt(Center(_puck), area.Scale));
        if (_panel.IsOpen)
            _panel.Open(_puck, WidgetRect, area, tabExtension: Body.TabExtension, opensLeft: _left);
        Land(); // a drop's slide this cut short still sets him down
    }

    /// <summary>
    /// The puck on the window's right with the bar growing left from it, or the other way round: the widget is
    /// held at the puck's outer side, and Chippi sits at that end.
    /// </summary>
    private void ApplySide(bool left)
    {
        _left = left;
        double outer = (Root.Width - IslandGeometry.Puck) / 2;
        Canvas.SetLeft(PuckButton, left ? double.NaN : outer);
        Canvas.SetRight(PuckButton, left ? outer : double.NaN);
        Bar.HorizontalAlignment = PuckEnd.HorizontalAlignment = left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        LayOutBar(left);
        LayoutBadgeTab();
    }

    /// <summary>
    /// The bar's columns from its round far end to Chippi's: 8, the lead (34), 10, the lines, 10, the equalizer, 10 and
    /// Chippi's 46. With Chippi at the left it's the same mirrored, so the two circles always sit in the two round
    /// ends and the bars always sit by Chippi. The two lines read left to right as a group beside their cover or glyph.
    /// </summary>
    private void LayOutBar(bool chippiRight)
    {
        GridLength[] widths = [new(8), new(34), new(10), new(1, GridUnitType.Star), GridLength.Auto, new(10), new(PetColumnWidth)];
        int Column(int fromFarEnd) => chippiRight ? fromFarEnd : widths.Length - 1 - fromFarEnd;
        for (int i = 0; i < widths.Length; i++)
            Bar.ColumnDefinitions[Column(i)].Width = widths[i];
        Grid.SetColumn(Cover, Column(1));
        Grid.SetColumn(Glyph, Column(1));
        Grid.SetColumn(Lines, Column(3));
        Lines.HorizontalAlignment = chippiRight ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        Grid.SetColumn(PlaybackMark, Column(4));
        PlaybackMark.Margin = chippiRight ? new Thickness(10, 0, 0, 0) : new Thickness(0, 0, 10, 0);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch ((uint)msg)
        {
            case PInvoke.WM_NCHITTEST:
                long position = lParam.ToInt64();
                if (!CoversWidget(new Point((short)position, (short)(position >> 16))))
                {
                    handled = true;
                    return -1; // HTTRANSPARENT: shadows and empty shoulders belong to the window below.
                }
                break;
            case PInvoke.WM_DISPLAYCHANGE:
            case PInvoke.WM_SETTINGCHANGE:
                _dragArea = null;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => DisplayChanged?.Invoke(this, EventArgs.Empty));
                break;
            case PInvoke.WM_POWERBROADCAST:
                if (IslandNative.DisplayOn(wParam, lParam) is { } on)
                    Dispatcher.BeginInvoke(() => DisplayPowerChanged?.Invoke(this, on));
                break;
        }
        return 0;
    }

    /// <summary>
    /// Esc closes the panel. It never has keyboard focus (it never activates), so a low-level
    /// keyboard hook watches for Esc while it is open, and always passes the key on: the app in
    /// front still gets its Esc (a dialog it closes, a selection it cancels).
    /// </summary>
    private unsafe LRESULT OnKeyboard(int code, WPARAM wParam, LPARAM lParam)
    {
        if (WatchesInput && code >= 0 && wParam.Value == PInvoke.WM_KEYDOWN && ((KBDLLHOOKSTRUCT*)lParam.Value)->vkCode == VK_ESCAPE)
            Escape();
        return PInvoke.CallNextHookEx(_escapeHook, code, wParam, lParam);
    }

    // A lambda: CloseIsland's optional parameter turns a method group into Action<bool>, which throws.
    // With the keyboard, the panel sees this Esc itself; the page may use it first (a search to clear).
    internal void Escape()
    {
        if (_openHolds.IsHeld)
            return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_panel.HasKeyboard || _panel.PageHandlesEscape())
                return;
            CloseIsland();
        });
    }

    /// <summary>
    /// Whether the hooks act on the real keyboard and mouse. The island tests turn it off for the whole run,
    /// so a click or an Esc on the PC while they run can't close a test's panel; they press through
    /// <see cref="PressedAt"/> and <see cref="Escape"/> instead.
    /// </summary>
    internal static bool WatchesInput { get; set; } = true;

    /// <summary>
    /// A click anywhere off the panel closes it. It never activates, so it can't lose focus to see
    /// that; a low-level mouse hook watches for presses while it is open, and always passes them
    /// on: the click still reaches the app under the pointer.
    /// </summary>
    private unsafe LRESULT OnMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        uint message = (uint)wParam.Value;
        if (WatchesInput && code >= 0 && message is PInvoke.WM_LBUTTONDOWN or PInvoke.WM_RBUTTONDOWN or PInvoke.WM_MBUTTONDOWN or PInvoke.WM_XBUTTONDOWN)
        {
            var at = ((MSLLHOOKSTRUCT*)lParam.Value)->pt;
            PressedAt(new Point(at.X, at.Y));
        }
        return PInvoke.CallNextHookEx(_clickHook, code, wParam, lParam);
    }

    /// <summary>
    /// A press at <paramref name="physical"/> while the panel is open: off the panel and the widget it
    /// closes the panel, unless a list or menu the panel opened has the mouse.
    /// </summary>
    internal void PressedAt(Point physical)
    {
        if (!_panel.IsOpen || _panel.Covers(physical) || (IsVisible && CoversWidget(physical))
            || Menu.IsOpen || Mouse.Captured is not null)
            return;
        Dispatcher.BeginInvoke(() => CloseIsland());
    }

    // ----- Open and close -----

    private void Toggle()
    {
        if (_panel.IsOpen)
            CloseIsland();
        else
            OpenIsland();
    }

    /// <summary>The bar's page: a song's opens Now Playing, an activity's its feature's page; without one it acts as the puck.</summary>
    private void OpenChip()
    {
        if (_chipKey is { } key && !_panel.IsOpen)
            ShowPage(key);
        else
            Toggle();
    }

    /// <summary>
    /// A second launch: opens the panel while the widget shows; from the keyboard it takes the keyboard.
    /// False when the widget is off or hidden for a fullscreen app, so nothing opened.
    /// </summary>
    internal bool ShowPages(bool keyboard)
    {
        if (!_panel.IsOpen)
            OpenIsland();
        if (keyboard && _panel.IsOpen)
            _panel.TakeKeyboard();
        return _panel.IsOpen;
    }

    /// <summary>
    /// The panel on <paramref name="key"/>'s page (a notice click): moved there when open, otherwise
    /// opened on it. False when the panel can't show.
    /// </summary>
    internal bool ShowPage(string key)
    {
        if (_panel.IsOpen)
        {
            _panel.ShowPage(key);
            return true;
        }
        _panel.OpenOn(key);
        bool opened = ShowPages(keyboard: false);
        _panel.OpenOn(null); // unused when nothing could open
        return opened;
    }

    /// <summary>
    /// The Command bar's shortcut: the panel on <paramref name="key"/>'s page with the keyboard, over a fullscreen app or
    /// game too, on the widget's screen; the widget hides again once the panel closes. False while the widget is off.
    /// </summary>
    internal bool SummonPage(string key)
    {
        if (!_enabled)
            return false;
        if (_suppressed && !_panel.IsOpen)
        {
            _summoned = true;
            ApplyVisibility();
        }
        bool shown = ShowPage(key);
        if (shown)
            _panel.TakeKeyboard();
        else if (_summoned)
        {
            _summoned = false;
            ApplyVisibility();
        }
        return shown;
    }

    // The saved puck anchors the destination, but the visible button can be dipping about its
    // off-center grip, partly unfolded, or still sliding. Project the actual outline at handoff.
    private Rect RenderedWidgetRect()
    {
        if (PuckButton.RenderSize.Width <= 0 || PuckButton.RenderSize.Height <= 0 || PresentationSource.FromVisual(Root) is null)
            return WidgetRect; // a summon can arrive before the widget's first arrange
        var bounds = PuckButton.TransformToAncestor(Root).TransformBounds(new Rect(PuckButton.RenderSize));
        return new Rect(Root.PointToScreen(bounds.TopLeft), Root.PointToScreen(bounds.BottomRight));
    }

    private void OpenIsland()
    {
        if (!IsVisible || _dragging || Area is not { } area)
            return;
        if (!_heldWidth.HasValue && !_panel.IsVisible)
            HoldBarMotion(); // summoned without a press: keep an interrupted unfold at its visible width too
        var sourceRim = Body.BorderBrush as SolidColorBrush ?? TryFindResource(_rim ?? "IslandWidgetBorderBrush") as SolidColorBrush;
        _panel.Open(_puck, RenderedWidgetRect(), area, sourceRim, Body.TabExtension, _left);
        IslandNative.Raise(_hwnd); // Chippi and the bar's content stay above the shape the widget became, fading
        Hook();
    }

    /// <summary>Watches for Esc and clicks away while the panel is open.</summary>
    private void Hook()
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        if (_escapeHook is null || _escapeHook.IsInvalid)
            _escapeHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _onKeyboard, module, 0);
        if (_clickHook is null || _clickHook.IsInvalid)
            _clickHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _onMouse, module, 0);
        RefreshPuck();
    }

    /// <summary>Folds the panel back into the widget, the puck or the bar; <paramref name="immediate"/> hides it at once.</summary>
    private void CloseIsland(bool immediate = false)
    {
        if (!immediate && _openHolds.IsHeld) return;
        _escapeHook?.Dispose(); // unhooks
        _escapeHook = null;
        _clickHook?.Dispose();
        _clickHook = null;
        _openedByDrag = false;
        DragCloseTimer.Stop();
        if (!_panel.IsOpen && !(immediate && _panel.IsVisible))
            return;
        // A reversal can still have a half-unfolded bar underneath it. Land there first;
        // the remaining unfold or a change of activity takes over after the panel hides.
        var widget = _resumeBar || _heldWidth.HasValue ? RenderedWidgetRect() : WidgetRect;
        _panel.CloseIsland(immediate, widget, Body.TabExtension, _left);
        RefreshPuck();
        if (_summoned)
        {
            _summoned = false;
            ApplyVisibility(); // hidden again while the fullscreen app is still in front
        }
    }

    // ----- A drag from another app (Phase 5 notes #48) -----

    internal static readonly TimeSpan DragOpenDelay = TimeSpan.FromMilliseconds(250), DragCloseDelay = TimeSpan.FromMilliseconds(500);
    private IIslandTimer? _dragOpen, _dragClose;

    /// <summary>Makes the drag timers on first use: a dispatcher timer, or a timer a test fires itself, so no test waits on wall time.</summary>
    internal Func<TimeSpan, Action, IIslandTimer> CreateTimer { get; set; } = (interval, tick) => new DispatcherIslandTimer(interval, tick);

    private IIslandTimer DragOpenTimer => _dragOpen ??= CreateTimer(DragOpenDelay, OpenForDrag);

    private IIslandTimer DragCloseTimer => _dragClose ??= CreateTimer(DragCloseDelay, CloseAfterDrag);
    private string? _dragKey; // the tile a drag over the closed puck is about to open
    private bool _openedByDrag; // open because of a drag that hasn't dropped, been clicked into or typed into

    /// <summary>The pointer in physical pixels; tests stand in for it.</summary>
    internal Func<Point> CursorAt { get; set; } = IslandNative.Cursor;

    /// <summary>Whether the panel is open because of a drag that hasn't ended that mode (tests).</summary>
    internal bool OpenedByDrag => _openedByDrag;

    /// <summary>The tile a drag over the puck opens, if any feature set one.</summary>
    private IslandTileGroup? DragTile => _tiles.FirstOrDefault(t => t.Parts.Any(p => p.Spec.OpensOnDrag));

    private void OnPuckDrag(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None; // the puck never takes the drop
        e.Handled = true;
        DragCloseTimer.Stop();
        if (_panel.IsOpen || _dragKey is not null || _dragging || DragTile is not { } tile)
            return;
        _dragKey = tile.Key;
        RefreshPuck();
        DragOpenTimer.Start();
    }

    /// <summary>250 ms over the puck: the panel opens on that page, without the keyboard.</summary>
    private void OpenForDrag()
    {
        DragOpenTimer.Stop();
        if (_dragKey is not { } key)
            return;
        _dragKey = null;
        if (!_panel.IsOpen)
        {
            ShowPage(key);
            _openedByDrag = _panel.IsOpen;
        }
        RefreshPuck();
    }

    private void DragOverPanel(DragEventArgs e)
    {
        DragCloseTimer.Stop();
        if (e.Handled)
            return;
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// The drag left the puck or the panel (or moved between their parts). Before 250 ms it doesn't open
    /// anything; once a drag opened the panel, it closes 500 ms after the drag is off both.
    /// </summary>
    private void DragLeft()
    {
        if (_dragKey is not null)
        {
            DragOpenTimer.Stop();
            _dragKey = null;
            RefreshPuck();
        }
        if (_openedByDrag)
            DragCloseTimer.Start();
    }

    private void CloseAfterDrag()
    {
        DragCloseTimer.Stop();
        if (!_openedByDrag)
            return;
        var at = CursorAt();
        if (_panel.Covers(at) || (IsVisible && CoversWidget(at)))
            return; // still over the widget: a later leave starts this again
        CloseIsland();
    }

    private readonly IslandOpenHold _openHolds = new();
    internal IDisposable HoldIslandOpen() => _openHolds.Hold();

    internal bool IsIslandOpen => _panel.IsOpen;

    /// <summary>Reads the app in front before the panel takes the keyboard (Phase 5 notes #62).</summary>
    internal Func<Core.Clipboard.PasteTarget?>? CaptureTarget { set => _panel.CaptureTarget = value; }

    /// <summary>The app that was in front before the panel took the keyboard, for a page's Paste.</summary>
    internal Core.Clipboard.PasteTarget? PasteTarget => _panel.PasteTarget;

    /// <summary>Closes the panel the usual way, after a page's paste was sent.</summary>
    internal void ClosePages() => CloseIsland();

    /// <summary>The open panel itself, for tests and snapshots.</summary>
    internal IslandPanelWindow Panel => _panel;

    /// <summary>Lays the panel's home out ahead of the first open (<see cref="IslandPanelWindow.Prewarm"/>).</summary>
    internal void Prewarm() => _panel.Prewarm();

    /// <summary>Whether the keyboard and mouse hooks are installed (only while the panel is open).</summary>
    internal bool IsHooked => _escapeHook is { IsInvalid: false } || _clickHook is { IsInvalid: false };

    // ----- The puck and the bar -----

    /// <summary>
    /// The widget: the blob sleeps at rest and wakes on hover or while anything is live; hover brightens the
    /// rim, never the size, and the puck lifts while dragged. The widget is the bar while there is something
    /// to show, in order: a drag from another app (the page it opens), Plain paste's feedback, the song
    /// playing (or paused, for <see cref="PauseHold"/>), or the highest activity. Otherwise it is the puck.
    /// During a drag only the compact puck and its status icons follow the pointer.
    /// </summary>
    private void RefreshPuck()
    {
        // Plain paste's feedback, or else another short line like it (an agent's "80% used"), which waits while Plain paste's shows.
        var feedback = _activities.FirstOrDefault(a => a.ModuleId == Chippi.Features.PlainPaste.PlainPasteModule.Id)
            ?? _activities.FirstOrDefault(a => a.IsFeedback);
        var mic = _activities.FirstOrDefault(a => a.ModuleId == "mic-mute" && a.NeedsAttention);
        bool dropping = _dragKey is not null;
        if (_lifted)
            Pet.IsHeld = true; // set down once the drop has slid into place (DropAt)
        bool pressed = _pressAt is not null && !_dragging && !Motion.IsReduced;
        // A long bar lifts by its shadow alone. A tilt about its center would move the end out of the hand.
        bool heldBar = _heldWidth is { } held && (_pressedBar || held > IslandGeometry.Puck + Body.TabExtension);
        Grow(heldBar ? 1 : _lifted ? LiftScale : pressed ? PressScale : 1, _lifted && !heldBar ? LiftTilt : 0, pressed ? FillFade.Time : GrowTime);
        ShowRim(_hovered && !_lifted ? "IslandWidgetHoverBorderBrush" : "IslandWidgetBorderBrush");
        bool keepSource = _panel.IsVisible && (!_panel.IsOpen || (_panel.IsMorphing && BarLayer.Opacity > 0));
        if (_heldWidth is not null || keepSource)
        {
            if (_panel.IsVisible && SongCopy.Visibility == Visibility.Visible)
                FitTitle(); // hold the current title as soon as the panel opens, even while its ink is fading
            StayAwake(engaged: true);
            return; // keep the captured or closing shape and its ink until release, handoff, or a reopen
        }
        ShowBadge(MicBadge, mic is not null);

        var current = _media?.Current;
        var playing = current is { Playing: true } && _activities.Any(a => a.Kind == IslandActivityKind.Media) ? current : null;
        if (playing is not null)
        {
            _heldSong = playing.Id;
            if (_holdingPause)
            {
                _holdingPause = false;
                PauseTimer.Stop();
            }
        }
        var song = playing ?? HeldSong(current);
        _musicHovered = !_nowPlayingAlwaysShow && song is not null && CanHoverMusic && (_musicHovered || _puck.Contains(PointerAt()));
        bool showMusic = _nowPlayingAlwaysShow || _musicHovered;
        if (!showMusic) song = null;
        var top = _activities.FirstOrDefault(a => a.ModuleId is not Chippi.Features.PlainPaste.PlainPasteModule.Id and not "mic-mute" && !a.IsFeedback
            && (showMusic || a.Kind != IslandActivityKind.Media)
            && (a.Kind != IslandActivityKind.Agent || _agentPeeking || _agentHovered));
        var tile = top?.TileKey is { } key ? _tiles.FirstOrDefault(t => t.Key == key) : null;
        var dropTile = dropping ? _tiles.FirstOrDefault(t => t.Key == _dragKey) : null;
        bool bar = !_lifted && (dropTile is not null || feedback is not null || song is not null || top is not null);
        var puckAgent = bar ? null : _activities.FirstOrDefault(a => a.Kind == IslandActivityKind.Agent && a.NeedsAttention)
            ?? _activities.FirstOrDefault(a => a.Kind == IslandActivityKind.Agent);
        var puckProvider = AgentNames.FromId(puckAgent?.ProviderId);
        if (puckAgent is not null)
        {
            AgentMarkPlate.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, puckAgent.NeedsAttention ? "PetSparkBrush" : "IslandAccentFillBrush");
            AgentIcons.SetSource(AgentBadgeProvider, puckProvider);
            AgentBadgeFace.Visibility = Shown(puckProvider.HasValue);
            AgentMarkIcon.Visibility = Shown(!puckProvider.HasValue);
        }
        ShowBadge(AgentMark, puckAgent is not null);
        _singing = bar && dropTile is null && feedback is null && song is not null;
        // Keep awake stays in sight while something else holds the bar (a song, a drag, another activity) or the puck is lifted.
        var keepAwake = _activities.FirstOrDefault(a => a.Kind == IslandActivityKind.Status && a.ModuleId == IslandHost.KeepAwakeModuleId);
        bool awakeOnBar = bar && dropTile is null && feedback is null && song is null && ReferenceEquals(top, keepAwake);
        bool awakeBadge = keepAwake is not null && !awakeOnBar;
        ShowBadge(AwakeBadge, awakeBadge);
        LayoutBadgeTab();
        bool paused = _singing && playing is null;
        // A background session can still report Idle after its notice folds. Only the visible activity
        // or an interaction keeps the pet engaged. Progress updates leave his quiet minute alone.
        bool awake = StayAwake(playing is not null || bar || _hovered || _lifted || _panel.IsVisible || _mood == PetMood.WideAwake);
        var mood = playing is not null && !_lifted ? PetMood.Listening : _mood switch
        {
            PetMood.Idle or PetMood.Sleepy => awake || _stretching ? PetMood.Idle : PetMood.Sleepy,
            _ => _mood,
        };
        Pet.ShowMood(mood);
        _panel.ShowPetMood(mood);
        _chipKey = dropTile is not null ? null : feedback is not null ? feedback.OpensTile
            : _singing ? IslandPanelWindow.NowPlayingKey : top?.TileKey ?? top?.OpensTile;
        // What the bar shows, by identity rather than its values ("59 min left" after "1 hr left" is the same activity):
        // a new song or a new activity on a bar that's out and seen arrives (Arrival), the old fading where it was.
        string? content = !bar ? null
            : dropTile is not null ? "drop " + dropTile.Key
            : feedback is not null ? "feedback " + feedback.Key
            : song is not null ? $"song {song.Id}\n{song.Title}\n{song.Artist}"
            : "activity " + top!.Key;
        bool settled = _barShown && Bar.Opacity >= 1 && IsVisible && !_panel.IsVisible;
        bool arriving = bar && settled && _shownContent is not null && content != _shownContent;
        // Something new beside him (the bar unfolding, or a new song or activity on it): Chippi looks toward it.
        if (content is not null && content != _shownContent && IsVisible && !_panel.IsVisible)
            Pet.Glance(_left ? -1 : 1);
        _shownContent = content;
        if (arriving)
        {
            Arrival.Leave(Cover.Visibility == Visibility.Visible ? Cover : Glyph, LeadWas);
            Arrival.Leave(Lines, LinesWas);
        }
        ShowBar(bar);
        if (_dragging) SetDragOrigin();
        if (bar)
        {
            // Folding, the bar keeps what it showed while it fades.
            Cover.Visibility = Shown(_singing);
            Glyph.Visibility = Shown(!_singing);
            bool bars = _singing && !paused;
            // Playing to paused and back on a bar that's out: the mark that takes over scales in.
            if (settled && !arriving && bars && Equalizer.Visibility != Visibility.Visible)
                Arrival.Pop(Equalizer);
            if (settled && !arriving && paused && PausedMark.Visibility != Visibility.Visible)
                Arrival.Pop(PausedMark);
            Equalizer.Visibility = Shown(bars);
            PausedMark.Visibility = Shown(paused);
            PlaybackMark.Visibility = Shown(_singing);
            // Only music uses the 15-DIP indicator and its 10-DIP gap.
            _lineRoom = IslandGeometry.BarWidth - 8 - 34 - 10 - 10 - PetColumnWidth - (_singing ? 25 : 0);
        }
        if (!bar)
            ShowLine(null, "", "");
        else if (dropTile is not null)
        {
            ShowGlyph(LucideIcons.Get(dropTile.Icon), caution: false);
            ShowLine(dropTile.Key, dropTile.Name, "");
        }
        else if (feedback is not null)
        {
            if (AgentNames.FromId(feedback.ProviderId) is { } provider)
                ShowProvider(provider, feedback.NeedsAttention);
            else
            {
                var opens = feedback.OpensTile is { } page ? _tiles.FirstOrDefault(t => t.Key == page) : null;
                ShowGlyph(feedback.Icon is { } icon ? LucideIcons.Get(icon) : opens is null ? LucideIcons.ClipboardType : LucideIcons.Get(opens.Icon), feedback.NeedsAttention);
            }
            ShowLine(feedback.Key, feedback.Title, feedback.Source);
        }
        else if (song is not null)
        {
            ShowCover(song.Artwork);
            ShowLine(song.Id, song.Title.Length > 0 ? song.Title : song.Source, song.Artist);
        }
        else if (top is not null)
        {
            if (AgentNames.FromId(top.ProviderId) is { } provider)
                ShowProvider(provider, top.NeedsAttention);
            else if (tile is not null)
                ShowGlyph(LucideIcons.Get(tile.Toggle?.Icon ?? tile.Icon), top.NeedsAttention);
            else
                ShowKindGlyph(top);
            // The status over the feature: "1 hr left" over "Keep awake".
            string status = tile is null ? "" : tile.Toggle?.Status?.Invoke() ?? tile.Status;
            string title = status.Length > 0 ? status : top.Title;
            string detail = tile?.Name ?? top.Source;
            ShowLine(top.Key, title, detail == title ? "" : detail);
        }
        if (arriving)
        {
            Arrival.Enter(Cover, rise: false);
            Arrival.Enter(Glyph, rise: false);
            Arrival.Enter(Lines, rise: true);
        }
        // The equalizer follows the output's real level while a song plays on the bar and can be seen, and Chippi bobs
        // to its beats there; with the panel out the bars keep still, but the panel's listening Chippis still bob.
        if (playing is not null && !_lifted && (_singing || !bar) && (IsVisible || _panel.IsVisible) && !Motion.IsReduced && !Motion.IsUnseen)
            _level?.Start();
        else
            StopLevel();
        AutomationProperties.SetName(PuckButton, bar ? $"Chippi, {_chipText}" : "Chippi");
        List<string> help = [];
        if (paused) help.Add("Paused");
        if (!bar && playing is not null && !_nowPlayingAlwaysShow)
            help.Add($"Now Playing · {(playing.Title.Length > 0 ? playing.Title : playing.Source)}");
        if (mic is not null) help.Add(mic.Title == "Muted" ? "Microphone muted" : mic.Title);
        if (awakeBadge) help.Add("Keep awake is on");
        if (puckAgent is not null)
            help.Add($"{puckProvider?.Name() ?? "Agent"} {(puckAgent.NeedsAttention ? "waiting on you" : "working")}");
        AutomationProperties.SetHelpText(PuckButton, string.Join(". ", help));
        AutomationProperties.SetLiveSetting(PuckButton, AutomationLiveSetting.Polite);
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    // Arm once when the last interaction ends. A quiet first launch starts asleep.
    private bool StayAwake(bool engaged)
    {
        if (engaged != _petEngaged)
        {
            _petEngaged = engaged;
            _windingDown = !engaged;
            if (engaged)
                _restTimer?.Stop();
            else
                (_restTimer ??= CreateTimer(RestDelay, () =>
                {
                    _windingDown = false;
                    RefreshPuck();
                    if (IsVisible && Pet.Mood == PetMood.Sleepy && !Motion.IsUnseen)
                        Pet.Yawn();
                })).Start();
        }
        return engaged || _windingDown;
    }

    /// <summary>The rim brightens on hover and dims after, over 83ms, like Windows' own controls.</summary>
    private void ShowRim(string key)
    {
        if (key == _rim)
            return;
        bool first = _rim is null;
        _rim = key;
        if (first)
            Body.SetResourceReference(Border.BorderBrushProperty, key);
        else
            FillFade.Blend(Body, Border.BorderBrushProperty, Body.BorderBrush, (Brush)FindResource(key), () => Body.SetResourceReference(Border.BorderBrushProperty, key));
    }

    // Slots grow from Chippi toward the middle of the display. Keep a fading slot until its ink is gone.
    private void LayoutBadgeTab()
    {
        int count = 0;
        void PlaceBadge(FrameworkElement badge)
        {
            if (badge.Visibility != Visibility.Visible) return;
            double offset = IslandGeometry.Puck + 1.5 + 21 * count++;
            Canvas.SetLeft(badge, _left ? double.NaN : offset);
            Canvas.SetRight(badge, _left ? offset : double.NaN);
        }
        PlaceBadge(AgentMark);
        PlaceBadge(MicBadge);
        PlaceBadge(AwakeBadge);

        double extension = count == 0 ? 0 : 25 + 21 * (count - 1);
        _compactWidth = IslandGeometry.Puck + extension;
        if (_lifted || extension >= _tabExtension || PuckButton.Width <= _compactWidth || PuckButton.Width >= IslandGeometry.BarWidth)
            _tabExtension = extension;
        Body.SetTab(_tabExtension, _left);
        BodyShadow.SetTab(_tabExtension, _left);
        Bar.ColumnDefinitions[_left ? 6 : 0].Width = new GridLength(PetColumnWidth);
    }

    /// <summary>
    /// A badge on the puck (microphone, Keep awake or Agents) scales in from 0.6 as it fades in over 160ms, and fades out over
    /// 100ms when it goes; under reduced motion it only fades. Its Tag is whether it is requested.
    /// </summary>
    private void ShowBadge(FrameworkElement badge, bool shown)
    {
        if (shown == (badge.Tag is true))
            return;
        badge.Tag = shown;
        bool seen = IsVisible && !_panel.IsVisible;
        if (shown)
        {
            badge.Visibility = Visibility.Visible;
            if (seen)
                Arrival.Pop(badge, 0.6, BadgeIn);
            return;
        }
        if (!seen)
        {
            badge.BeginAnimation(OpacityProperty, null);
            badge.Visibility = Visibility.Collapsed;
            return;
        }
        var fade = new DoubleAnimation(0, BadgeOut);
        fade.Completed += (_, _) =>
        {
            if (badge.Tag is true)
                return; // up again meanwhile: its pop has taken over
            badge.Visibility = Visibility.Collapsed;
            badge.BeginAnimation(OpacityProperty, null);
            RefreshPuck(); // release this slot only after the exit animation
        };
        badge.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    // The working/waiting key changes with the session state. Percentages, names and counts update the lines in place.
    private void UpdateAgentPeek(IslandActivity? session)
    {
        (string Module, string Key)? state = session is null ? null : (session.ModuleId, session.Key);
        if (_agentState == state)
            return;
        _agentState = state;
        _agentPeeking = state.HasValue;
        _agentHovered = false;
        _agentDwelling = false;
        _agentHoverTimer?.Stop();
        if (!_agentPeeking)
        {
            _agentPeekTimer?.Stop();
            return;
        }
        _agentPeekPointer = PointerAt();
        (_agentPeekTimer ??= CreateTimer(AgentPeek, () =>
        {
            _agentPeeking = false;
            var pointer = PointerAt();
            // An unfolding bar can grow underneath a stationary mouse. That alone doesn't hold it open.
            _agentHovered = CanHoverAgent && CoversWidget(pointer)
                && (_puck.Contains(pointer) || pointer != _agentPeekPointer);
            RefreshPuck();
        })).Start();
    }

    private bool CanHoverAgent => _agentState.HasValue && _hovered && IsVisible && !_panel.IsVisible
        && !Menu.IsOpen && !_lifted && _pressAt is null && _dragKey is null && !Motion.IsUnseen;

    private bool CanHoverMusic => _hovered && !_panel.IsVisible && !_lifted && _dragKey is null && !Menu.IsOpen;

    private void SetHover(bool hovered)
    {
        _hovered = hovered;
        if (!hovered) _musicHovered = false;
        _agentHovered = false;
        TrackAgentDwell();
        RefreshPuck();
    }

    // The compact tab belongs to the puck. On a full bar, entering through its text still requires moving onto Chippi.
    private bool CanDwellAt(Point pointer) => _barShown ? _puck.Contains(pointer) : CoversWidget(pointer);

    private void TrackAgentDwell()
    {
        bool dwell = CanHoverAgent && !_agentPeeking && !_agentHovered && CanDwellAt(PointerAt());
        if (dwell == _agentDwelling)
            return;
        _agentDwelling = dwell;
        if (dwell)
            (_agentHoverTimer ??= CreateTimer(AgentHoverDelay, () =>
            {
                _agentDwelling = false;
                _agentHovered = CanHoverAgent && CanDwellAt(PointerAt());
                RefreshPuck();
            })).Start();
        else
            _agentHoverTimer?.Stop();
    }

    private IIslandTimer? _pauseTimer;

    private IIslandTimer PauseTimer => _pauseTimer ??= CreateTimer(PauseHold, () =>
    {
        _heldSong = null;
        _holdingPause = false;
        RefreshPuck();
    });

    /// <summary>
    /// The song the bar keeps after it pauses: the one last shown playing, still the session in use and paused,
    /// for <see cref="PauseHold"/> from the pause. Anything else lets it go.
    /// </summary>
    private Media.MediaTrack? HeldSong(Media.MediaTrack? current)
    {
        if (_heldSong is not null && current is { Playing: false } && current.Id == _heldSong)
        {
            if (!_holdingPause)
            {
                _holdingPause = true;
                PauseTimer.Start();
            }
            return current;
        }
        if (_holdingPause)
            PauseTimer.Stop();
        _heldSong = null;
        _holdingPause = false;
        return null;
    }

    /// <summary>
    /// The puck unfolds into the bar (240ms ease-out from Chippi's end, its content fading in over the second
    /// half) or folds back (180ms, the content fading over the first half). At once under reduced motion, while
    /// hidden, lifted, or while the panel is its shape.
    /// </summary>
    private void ShowBar(bool shown)
    {
        double width = shown ? IslandGeometry.BarWidth : _compactWidth;
        if (shown == _barShown && width == _widthTarget && !_resumeBar)
            return;
        bool fadeBar = _barShown != shown || _resumeBar;
        _resumeBar = false;
        _barShown = shown;
        _widthTarget = width;
        if (Motion.IsReduced || !IsVisible || _lifted || _panel.IsVisible)
        {
            PuckButton.BeginAnimation(WidthProperty, null);
            PuckButton.Width = width;
            if (!fadeBar) return;
            Bar.BeginAnimation(OpacityProperty, null);
            Bar.Opacity = shown ? 1 : 0;
            // Under reduced motion the bar is there at once and what it shows fades in, so it still reads as new.
            if (shown && Motion.IsReduced && IsVisible && !_lifted && !_panel.IsVisible)
                Bar.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, ReducedFade) { FillBehavior = FillBehavior.Stop });
            return;
        }
        PuckButton.BeginAnimation(WidthProperty, new DoubleAnimation(width, shown ? UnfoldTime : FoldTime) { EasingFunction = EaseOut }, HandoffBehavior.SnapshotAndReplace);
        if (!fadeBar) return;
        var fade = shown
            ? new DoubleAnimation(1, UnfoldTime / 2) { BeginTime = UnfoldTime / 2 }
            : new DoubleAnimation(0, FoldTime / 2);
        Bar.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>The puck's size and tilt, eased over <paramref name="time"/> (120ms; 83ms for a press), at once under reduced motion or while hidden.</summary>
    private void Grow(double scale, double tilt, TimeSpan? time = null)
    {
        if (scale == _scaleTarget && tilt == _tiltTarget)
            return;
        _scaleTarget = scale;
        _tiltTarget = tilt;
        foreach (var (target, property, to) in new (Animatable, DependencyProperty, double)[]
        {
            (PuckScale, ScaleTransform.ScaleXProperty, scale),
            (PuckScale, ScaleTransform.ScaleYProperty, scale),
            (PuckTilt, RotateTransform.AngleProperty, tilt),
        })
        {
            if (Motion.IsReduced || !IsVisible)
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, to);
            }
            else
                target.BeginAnimation(property, new DoubleAnimation(to, time ?? GrowTime) { EasingFunction = EaseOut }, HandoffBehavior.SnapshotAndReplace);
        }
    }

    /// <summary>The bar's lines as plain text ("title · detail"), in its name.</summary>
    internal string ChipText => _chipText;

    /// <summary>The size the puck is drawn at, or heading to: 1, except lifted (hover never changes it).</summary>
    internal double PuckScaleTarget => _scaleTarget;

    /// <summary>The puck's tilt, or where it is heading (0 unless lifted).</summary>
    internal double PuckTiltTarget => _tiltTarget;

    private void ShowGlyph(Geometry icon, bool caution)
    {
        GlyphPlate.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "IslandAccentTintBrush");
        GlyphIcon.Data = icon;
        GlyphIcon.SetResourceReference(LucideIcon.BrushProperty, caution ? "IslandCautionBrush" : "IslandAccentBrush");
        GlyphIcon.Visibility = Visibility.Visible;
        Ring.Visibility = Dot.Visibility = ProviderMark.Visibility = Visibility.Collapsed;
    }

    private void ShowProvider(AgentKind provider, bool waiting)
    {
        AgentIcons.SetSource(ProviderMark, provider);
        GlyphPlate.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, waiting ? "IslandWaitingTintBrush" : "IslandAccentTintBrush");
        ProviderMark.Visibility = Visibility.Visible;
        GlyphIcon.Visibility = Ring.Visibility = Dot.Visibility = Visibility.Collapsed;
    }

    /// <summary>An activity without a page of its own: the ring for a timer or an agent, a folder for a download or a stash, a note for music, a dot for a status.</summary>
    private void ShowKindGlyph(IslandActivity activity)
    {
        switch (activity.Kind)
        {
            case IslandActivityKind.Timer or IslandActivityKind.Agent:
                GlyphPlate.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "IslandAccentTintBrush");
                RingArc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, activity.NeedsAttention ? "IslandCautionBrush" : "IslandAccentBrush");
                RingArc.Data = Arc(activity.Progress ?? 1);
                GlyphIcon.Visibility = Dot.Visibility = ProviderMark.Visibility = Visibility.Collapsed;
                Ring.Visibility = Visibility.Visible;
                break;
            case IslandActivityKind.Download or IslandActivityKind.Stash:
                ShowGlyph(LucideIcons.Folder, activity.NeedsAttention);
                break;
            case IslandActivityKind.Media: // music Now Playing can't read: its title, without the bars
                ShowGlyph(LucideIcons.Music, activity.NeedsAttention);
                break;
            default:
                GlyphPlate.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "IslandAccentTintBrush");
                Dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, activity.NeedsAttention ? "IslandCautionBrush" : "IslandAccentBrush");
                GlyphIcon.Visibility = Ring.Visibility = ProviderMark.Visibility = Visibility.Collapsed;
                Dot.Visibility = Visibility.Visible;
                break;
        }
    }

    // The cover fills the 34 circle; without one, a Music glyph on the button fill.
    private void ShowCover(ImageSource? art)
    {
        if (art is null)
        {
            CoverArt.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "IslandButtonBrush");
            CoverGlyph.Visibility = Visibility.Visible;
            return;
        }
        if (CoverArt.Fill is not ImageBrush { ImageSource: var shown } || shown != art)
            CoverArt.Fill = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        CoverGlyph.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The bar's lines: <paramref name="title"/> (13, semibold) over <paramref name="detail"/> (12, secondary, cut
    /// with an ellipsis). The title holds still when it fits its room (123 beside the equalizer); otherwise it
    /// scrolls 30 a second after a 2s hold, a copy following 40 behind so it loops seamlessly. It stops while the
    /// widget is hidden, the panel is out, or motion is reduced. <paramref name="id"/> tells one song or activity from the next, so an
    /// update doesn't start it over.
    /// </summary>
    private void ShowLine(string? id, string title, string detail)
    {
        if (id is null)
        {
            _lineText = _lineState = _chipText = "";
            SongMove.BeginAnimation(TranslateTransform.XProperty, null);
            return;
        }
        string text = id + "\n" + title + "\n" + detail;
        if (_lineText != text)
        {
            _lineText = text;
            _chipText = detail.Length > 0 ? $"{title} · {detail}" : title;
            SongText.Text = SongCopy.Text = title;
            Detail.Text = detail;
            Detail.Visibility = Shown(detail.Length > 0);
        }
        FitTitle();
    }

    private void FitTitle()
    {
        SongText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double full = Math.Ceiling(SongText.DesiredSize.Width);
        double room = _lineRoom;
        bool fits = full <= room;
        bool scroll = !fits && !Motion.IsReduced && !Motion.IsUnseen && IsVisible && !_panel.IsVisible;
        string state = $"{_lineText}\n{full}\n{room}\n{scroll}";
        if (state == _lineState)
            return; // still on its way: a timeline or play-state update doesn't start it over
        _lineState = state;
        double cycle = full + SongGap;
        var run = TimeSpan.FromSeconds(cycle / SongSpeed);
        Song.Width = fits ? full : room;
        Song.OpacityMask = fits ? null : Fade(room, scroll ? run : null);
        SongCopy.Visibility = Shown(scroll);
        if (!scroll)
        {
            SongMove.BeginAnimation(TranslateTransform.XProperty, null);
            return;
        }
        var move = new DoubleAnimationUsingKeyFrames { Duration = SongHold + run, RepeatBehavior = RepeatBehavior.Forever };
        move.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(SongHold)));
        move.KeyFrames.Add(new LinearDoubleKeyFrame(-cycle, KeyTime.FromTimeSpan(SongHold + run)));
        Timeline.SetDesiredFrameRate(move, 60); // half a DIP a frame is smooth; no need to draw at the screen's 165
        SongMove.BeginAnimation(TranslateTransform.XProperty, move);
    }

    private void ShowLevel(float peak)
    {
        if (_singing)
        {
            var heights = _bars.Next(peak);
            for (int i = 0; i < heights.Count; i++)
                ((FrameworkElement)Equalizer.Children[i]).Height = 2 + Math.Round(12 * heights[i]);
        }
        if (_beats.Next(peak, TimeSpan.FromMilliseconds(Environment.TickCount64)) is { } beat)
        {
            Pet.Beat(beat);
            _panel.Beat(beat);
        }
    }

    // Still, the bars take their resting shape again (8, 14, 10).
    private void StopLevel()
    {
        _level?.Stop();
        _beats.Reset();
        _bars = new EqualizerBars();
        double[] rest = [8, 14, 10];
        for (int i = 0; i < rest.Length; i++)
            ((FrameworkElement)Equalizer.Children[i]).Height = rest[i];
    }

    // The line fades over its last 8. Scrolling, it fades over its first 8 too, but only while it moves: held at the
    // start of each loop, its first letter is whole. The fade opens as the text crosses it, in step with the scroll.
    // In the line's own DIPs: relative units would follow the whole strip, which runs far past it.
    private static LinearGradientBrush Fade(double width, TimeSpan? run)
    {
        double f = 8 / width;
        var fade = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(width, 0) };
        var first = new GradientStop(Colors.Black, 0);
        fade.GradientStops.Add(first);
        fade.GradientStops.Add(new GradientStop(Colors.Black, f));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 1 - f));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        if (run is not { } moving)
        {
            fade.Freeze();
            return fade;
        }
        var cross = TimeSpan.FromSeconds(8 / SongSpeed);
        var open = new ColorAnimationUsingKeyFrames { Duration = SongHold + moving, RepeatBehavior = RepeatBehavior.Forever };
        open.KeyFrames.Add(new LinearColorKeyFrame(Colors.Black, KeyTime.FromTimeSpan(SongHold)));
        open.KeyFrames.Add(new LinearColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(SongHold + cross)));
        open.KeyFrames.Add(new LinearColorKeyFrame(Colors.Transparent, KeyTime.FromTimeSpan(SongHold + moving - cross)));
        open.KeyFrames.Add(new LinearColorKeyFrame(Colors.Black, KeyTime.FromTimeSpan(SongHold + moving)));
        Timeline.SetDesiredFrameRate(open, 60);
        first.BeginAnimation(GradientStop.ColorProperty, open);
        return fade;
    }

    /// <summary>The progress ring's arc on the 14px glyph, clockwise from 12 o'clock.</summary>
    private static Geometry Arc(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        const double c = 7, r = 6;
        if (progress >= 0.999)
            return new EllipseGeometry(new Point(c, c), r, r);
        double angle = progress * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, progress > 0.5, SweepDirection.Clockwise, true));
        var arc = new PathGeometry { Figures = { figure } };
        arc.Freeze();
        return arc;
    }

    // ----- Drag -----

    /// <summary>The pointer in physical pixels; tests stand in their own.</summary>
    internal Func<Point> PointerAt { get; set; } = IslandNative.Cursor;

    /// <summary>Stopwatch ticks for the edge slide. Tests can hold a drawn frame while picking it up.</summary>
    internal Func<long> SlideClock { get; set; } = Stopwatch.GetTimestamp;

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var pointer = PointerAt();
        _pressAt = pointer;
        _pressedBar = _barShown;
        double width = HoldBarMotion();
        _heldWidth = width;
        double scale = Area?.Scale ?? 1;
        var at = IslandGeometry.WidgetRect(_puck, _left, width * scale).TopLeft;
        // During an edge slide the saved puck is already at the destination; grab the moving shape itself.
        _grab = _slideFrame is null ? (pointer - at) / scale : (Vector)PuckButton.PointFromScreen(pointer);
        if (_slideFrame is not null)
            CompositionTarget.Rendering -= _slideFrame; // stop at the picked-up frame, even before the drag threshold
        PuckButton.RenderTransformOrigin = new Point(Math.Clamp(_grab.X / width, 0, 1), Math.Clamp(_grab.Y / IslandGeometry.Puck, 0, 1));
        if (!((UIElement)sender).CaptureMouse())
        {
            _pressAt = null;
            _heldWidth = null;
            ResumeSlide();
        }
        RefreshPuck();
    }

    // Capture the animated values, including a fold still in progress. An activity update or an
    // animation must not take the object away between pressing and crossing the drag threshold.
    private double HoldBarMotion()
    {
        double width = PuckButton.Width, ink = Bar.Opacity;
        _resumeBar = width != (_barShown ? IslandGeometry.BarWidth : _compactWidth) || ink != (_barShown ? 1 : 0);
        if (!_resumeBar)
            return width;
        PuckButton.BeginAnimation(WidthProperty, null);
        PuckButton.Width = width;
        Bar.BeginAnimation(OpacityProperty, null);
        Bar.Opacity = ink;
        return width;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (_pressAt is not { } start)
        {
            TrackAgentDwell();
            if (!_nowPlayingAlwaysShow && !_musicHovered && (_media?.Current?.Playing == true || _holdingPause)
                && CanHoverMusic && _puck.Contains(PointerAt()))
                RefreshPuck();
            return;
        }
        var pointer = PointerAt();
        if (!_dragging)
        {
            double threshold = DragThreshold * (Area?.Scale ?? 1);
            if ((pointer - start).Length < threshold)
                return;
            // A Command bar summoned over a fullscreen app can hide the whole island as it closes, releasing capture.
            CloseIsland(immediate: true);
            if (_pressAt is null)
                return;
            BeginDrag();
        }
        DragTo(pointer);
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (_pressAt is null)
            return;
        e.Handled = true;
        bool dragged = _dragging;
        _pressAt = null;
        ((UIElement)sender).ReleaseMouseCapture();
        if (dragged)
        {
            DropAt(PointerAt());
            return;
        }
        // Open from the shape and action at the press, before delayed activity changes take over.
        ResumeSlide();
        if (_pressedBar) OpenChip();
        else Toggle();
        _heldWidth = null;
        RefreshPuck();
    }

    /// <summary>Capture taken away mid-drag (another window, the widget hiding): it drops where it is.</summary>
    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_pressAt is null)
            return;
        _pressAt = null;
        if (_dragging)
            DropAt(PointerAt());
        else
        {
            ResumeSlide();
            _heldWidth = null;
            RefreshPuck();
        }
    }

    private void SetLifted(bool lifted)
    {
        _lifted = lifted;
        BodyShadow.SetResourceReference(EffectProperty, lifted ? "IslandLiftShadow" : "IslandPuckShadow");
    }

    /// <summary>A click retains its target until the pointer crosses the threshold. Then only Chippi travels.</summary>
    private void BeginDrag()
    {
        double size = IslandGeometry.Puck, half = size / 2;
        if (_left) _grab.X -= (_heldWidth ?? size) - size;
        var middle = new Vector(half, half);
        if ((_grab - middle).Length > half)
            _grab = middle; // the tab or notice under the pointer is giving way to the pet
        _heldWidth = null;
        _resumeBar = true; // discard any frozen width or opacity, even mid-unfold
        _dragArea = null;
        _dragging = true;
        Menu.IsOpen = false;
        StopSlide();
        _landing = null; // picked up again mid-slide: this drop lands instead
        SetLifted(true);
        _followScale = double.NaN;
        RefreshPuck();
    }

    private void SetDragOrigin()
    {
        double tab = _left ? _compactWidth - IslandGeometry.Puck : 0;
        PuckButton.RenderTransformOrigin = new Point((tab + _grab.X) / _compactWidth, _grab.Y / IslandGeometry.Puck);
    }

    /// <summary>Keep Chippi under the pointer while the tab turns toward the middle of the pointer's display.</summary>
    internal void DragTo(Point pointer)
    {
        var monitor = IslandNative.MonitorAt(pointer);
        if (_dragArea?.Handle != monitor)
            _dragArea = IslandNative.Area(monitor);
        if (_dragArea is not { } area) return;
        // The pointer determines the side. The layered window keeps its actual scale until WPF's DPI change.
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _puck = IslandGeometry.HeldPuck(pointer, _grab, scale);
        bool left = IslandGeometry.OpensLeft(_puck, area.Work);
        if (left != _left)
        {
            ApplySide(left);
            SetDragOrigin();
        }
        Follow(Center(_puck), scale);
    }

    /// <summary>
    /// Lands the same compact preview on the pointer's display, sliding only enough to clear the edges.
    /// </summary>
    internal Rect DropAt(Point pointer)
    {
        bool wasDragging = _dragging;
        if (wasDragging) _heldWidth = _compactWidth;
        _dragging = false;
        SetLifted(false);
        var monitor = IslandNative.MonitorAt(pointer);
        if (IslandNative.Area(monitor) is not { } area)
        {
            _heldWidth = null;
            Place();
            Pet.IsHeld = false;
            RefreshPuck();
            return _puck;
        }
        double half = IslandGeometry.Puck / 2;
        var grip = wasDragging ? _grab : new Vector(half, half); // snapshots can also drop a puck directly
        var held = IslandGeometry.HeldPuck(pointer, grip, area.Scale);
        var (landed, left) = IslandGeometry.LandWidget(held, area.Work, _compactWidth * area.Scale);
        var puck = IslandGeometry.PuckEnd(landed, left);
        (_x, _y) = IslandGeometry.Fractions(puck, area.Work);
        _monitor = monitor;
        _fallback = false;
        _puck = puck;
        _hovered = landed.Contains(pointer);
        // Saved once it has landed: a save writes settings.json, which mid-slide costs frames.
        var chosen = new IslandPlacementEventArgs(_x.Value, _y.Value, monitor);
        // Chippi's eyes stay round until the puck has slid into place; then he lands.
        var from = Center(held);
        ApplySide(left);
        SlideTo(from, Center(puck), area.Scale, () =>
        {
            Pet.IsHeld = false;
            if (_pressAt is null) _heldWidth = null;
            RefreshPuck();
            PlacementChosen?.Invoke(this, chosen);
        });
        RefreshPuck();
        return puck;
    }

    /// <summary>
    /// Slides the puck from <paramref name="from"/> to <paramref name="to"/> (its center) over 180ms, at
    /// once under reduced motion; then <paramref name="arrived"/> runs. A
    /// placement that cuts it short (a display change) runs it too.
    /// </summary>
    private void SlideTo(Point from, Point to, double scale, Action arrived)
    {
        StopSlide();
        _landing = arrived;
        void Arrive()
        {
            IslandNative.Move(_hwnd, WindowAt(to, scale));
            Land();
        }
        if (Motion.IsReduced || (to - from).Length < 0.5)
        {
            Arrive();
            return;
        }
        Follow(from, scale);
        var elapsed = TimeSpan.Zero;
        _slideFrame = (_, _) =>
        {
            long now = SlideClock();
            elapsed += Stopwatch.GetElapsedTime(_slideTick, now);
            _slideTick = now;
            double t = Math.Min(1, elapsed / SlideTime);
            double eased = 1 - Math.Pow(1 - t, 3);
            if (t >= 1)
            {
                StopSlide();
                Arrive();
                return;
            }
            Follow(from + (to - from) * eased, scale);
        };
        ResumeSlide();
    }

    // A tap or a lost capture before the drag threshold lets the original drop finish from its held frame.
    private void ResumeSlide()
    {
        if (_slideFrame is null)
            return;
        _slideTick = SlideClock();
        CompositionTarget.Rendering += _slideFrame;
    }

    private void StopSlide()
    {
        if (_slideFrame is null)
            return;
        CompositionTarget.Rendering -= _slideFrame;
        _slideFrame = null;
    }

    /// <summary>The drop has landed, or its slide was cut short: Chippi is set down and the place saved, once.</summary>
    private void Land()
    {
        var landing = _landing;
        _landing = null;
        landing?.Invoke();
    }

    /// <summary>The widget's monitor, as placed (snapshots and tests).</summary>
    internal MonitorArea? CurrentArea => Area;

#if DEBUG
    /// <summary>Debug builds: open or close the panel without a pointer, for snapshots.</summary>
    internal void ForceOpen(bool open)
    {
        if (open)
            OpenIsland();
        else
            CloseIsland();
    }

    /// <summary>Debug builds: grow the puck as if hovered, for snapshots.</summary>
    internal void ForceHover(bool hovered)
    {
        _hovered = hovered;
        RefreshPuck();
    }

    /// <summary>Debug builds: lift the puck as if dragged, for snapshots.</summary>
    internal void ForceLift(bool lifted)
    {
        SetLifted(lifted);
        Pet.IsHeld = lifted;
        RefreshPuck();
    }

    /// <summary>
    /// Debug builds: the widget and the open panel as they sit on the monitor, over a backdrop color,
    /// in physical pixels: the region around both, with the work area's edge drawn.
    /// </summary>
    internal void SaveScene(string path, Color backdrop)
    {
        if (Area is not { } area)
            return;
        var widget = IslandNative.WindowRect(_hwnd);
        var region = widget;
        if (_panel.IsVisible)
            region.Union(_panel.Footprint);
        region.Inflate(48 * area.Scale, 48 * area.Scale);
        region.Intersect(area.Monitor);
        region = new Rect(Math.Floor(region.X), Math.Floor(region.Y), Math.Ceiling(region.Width), Math.Ceiling(region.Height));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(backdrop), null, new Rect(region.Size));
            var work = area.Work;
            work.Offset(-region.X, -region.Y);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)), 1), work);
            if (_panel.IsVisible)
                Draw(dc, _panel.Visual, IslandNative.WindowRect(_panel.Handle), region, area.Scale);
            Draw(dc, Root, widget, region, area.Scale);
        }
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)region.Width, (int)region.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private static void Draw(DrawingContext dc, FrameworkElement root, Rect window, Rect region, double scale)
    {
        var pixels = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        pixels.Render(root);
        dc.DrawImage(pixels, new Rect(window.X - region.X, window.Y - region.Y, pixels.PixelWidth, pixels.PixelHeight));
    }
#endif

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        _scaleTarget = -1; // re-applied at once or eased, for the new setting
        RefreshPuck();
    }

    protected override void OnClosed(EventArgs e)
    {
        _agentPeekTimer?.Stop();
        _agentHoverTimer?.Stop();
        Motion.Changed -= OnMotionChanged;
        foreach (var part in _tiles.SelectMany(t => t.Parts))
            part.StatusChanged -= OnStatusChanged;
        IslandNative.StopWatchingDisplayPower(_displayNotice);
        StopSlide();
        _level?.Stop();
        CloseIsland();
        _panel.Close();
        SaveLastPage(); // quitting with Audio open may leave no idle dispatcher turn
        base.OnClosed(e);
        _restTimer?.Stop();
    }
}
