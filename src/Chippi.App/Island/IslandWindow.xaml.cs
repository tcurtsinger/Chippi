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
using Chippi.App.Pet;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Themes;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
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
/// Two sizes only: the 50 puck and the 256 × 50 bar. Every window here never activates (WS_EX_NOACTIVATE
/// plus WM_MOUSEACTIVATE) and never shows in Alt+Tab or the taskbar, and is placed in physical pixels on
/// the monitor it is given. Hovering brightens the rim and wakes the blob; a click becomes the panel,
/// which stays open until a click outside it or Esc. A press that moves 4px lifts the puck (the bar
/// folds) and drags it; it drops where it is let go, kept inside the work area. The tray icon opens
/// Settings. <see cref="IslandHost"/> feeds it activities, the mood, the monitor, the saved place and
/// whether to hide.
/// </summary>
public partial class IslandWindow : Window
{
    private const double DragThreshold = 4;
    private const double Room = 62; // the puck's center, in from the window's top and its near side: room for the lift and its shadow
    private const double SongGap = 40; // between one pass and the next
    private const double SongSpeed = 30; // DIPs a second
    private const double LiftScale = 1.12;
    private const double LiftTilt = -6;
    private const uint VK_ESCAPE = 0x1B;

    private static readonly TimeSpan GrowTime = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan UnfoldTime = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan FoldTime = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan SongHold = TimeSpan.FromSeconds(2);

    /// <summary>A paused song keeps the bar this long before it folds, so a quick pause doesn't make it flicker.</summary>
    internal static readonly TimeSpan PauseHold = TimeSpan.FromSeconds(10);
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly IslandPanelWindow _panel = new();
    private IReadOnlyList<IslandActivity> _activities = [];
    private IReadOnlyList<IslandTileGroup> _tiles = [];
    private PetMood _mood = PetMood.Sleepy;
    private double? _x, _y; // the saved place; null is the start, bottom right
    private bool _fallback; // the chosen monitor is gone: the main display's start instead
    private bool _enabled = true;
    private bool _suppressed;
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
    private Vector _grab; // from the puck's center to the pointer, physical pixels
    private bool _pressedBar;
    private bool _dragging;
    private EventHandler? _slideFrame;
    private double _followScale = double.NaN; // the scale of the last full move during a drag or slide
    private double _scaleTarget = 1, _tiltTarget;
    private Media.IMediaSessions? _media;
    private bool _singing; // the bar names a song, playing or just paused
    private bool _barShown; // the widget is the bar (or unfolding into it), not the puck
    private string? _heldSong; // the song last shown playing, which keeps the bar for PauseHold after it pauses
    private bool _holdingPause;
    private double _lineRoom; // the title's room on the bar; longer scrolls
    private string? _chipKey; // the page the bar opens
    private string? _lastPage; // the page last opened, saved once the panel has gone
    private string _lineText = "";
    private string _chipText = ""; // the line as plain text, for the chip's name
    private string _lineState = "";
    private TabLevel? _level;
    private EqualizerBars _bars = new();

    public IslandWindow()
    {
        InitializeComponent();
        _onKeyboard = OnKeyboard;
        _onMouse = OnMouse;

        PuckButton.MouseEnter += (_, _) =>
        {
            _hovered = true;
            RefreshPuck();
        };
        PuckButton.MouseLeave += (_, _) =>
        {
            _hovered = false;
            RefreshPuck();
        };
        // Hidden (a fullscreen app in front), the song stops scrolling; shown again, it starts over.
        IsVisibleChanged += (_, _) => RefreshPuck();
        // Reduced motion turned on or off: the song holds or scrolls, and the bars stop or follow, at once.
        Motion.Changed += OnMotionChanged;
        _panel.IsVisibleChanged += (_, _) =>
        {
            // The panel covered the widget, so the widget never saw the pointer come or go.
            if (!_panel.IsVisible)
                _hovered = IsVisible && WidgetRect.Contains(IslandNative.Cursor());
            RefreshPuck();
            if (!_panel.IsVisible && _lastPage is { } page)
            {
                _lastPage = null;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => LastPageChanged?.Invoke(this, page));
            }
        };
        // While it is out, the panel is the widget's shape: the widget draws only Chippi and the bar's content, above it,
        // fading as the shape grows and coming back as it folds. Opening, the bar's content goes over the first 25%, and
        // Chippi shrinks to 70% and fades over the first 45%; closing, they come back over the end.
        _panel.Morphing += (opening, t) =>
        {
            // How much of something is showing: gone by `until` opening, back from `from` closing (exactly 1 at the end).
            double Shown(double until, double from) => opening ? 1 - Math.Clamp(t / until, 0, 1) : t >= 1 ? 1 : Math.Clamp((t - from) / (1 - from), 0, 1);
            double pet = Shown(0.45, 0.45);
            PuckEnd.Opacity = pet;
            PetScale.ScaleX = PetScale.ScaleY = 0.7 + 0.3 * pet;
            BarLayer.Opacity = Shown(0.25, 0.55);
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
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, Place);

        ApplySide(left: true);
    }

    /// <summary>Raised when the display layout or work area changed (WM_DISPLAYCHANGE, WM_SETTINGCHANGE).</summary>
    public event EventHandler? DisplayChanged;

    /// <summary>The user dropped the widget. It is already there; save it.</summary>
    internal event EventHandler<IslandPlacementEventArgs>? PlacementChosen;

    /// <summary>The home's Settings button: the panel has closed; open Settings.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>The user opened a page (a tile key): save it as AppSettings.IslandLastPage.</summary>
    public event EventHandler<string>? LastPageChanged;

    internal HWND Handle => _hwnd;

    /// <summary>The open panel's window.</summary>
    internal HWND PanelHandle => _panel.Handle;

    /// <summary>AppSettings.IslandLastPage: the page the next open restores while its tile exists.</summary>
    public void SetLastPage(string? key) => _panel.SetLastPage(key);

    /// <summary>Settings › Island &amp; pet: where the panel opens, and its buttons.</summary>
    internal void SetLayout(IslandLayout layout) => _panel.SetLayout(layout);

    /// <summary>The output's live level for the equalizer while the bar names a song playing.</summary>
    internal void SetLevel(TabLevel level)
    {
        _level = level;
        level.Level += (_, peak) => ShowLevel(peak);
    }

    /// <summary>Now Playing's media sessions.</summary>
    internal void SetMedia(Media.IMediaSessions media)
    {
        _media = media;
        media.Changed += (_, _) => RefreshPuck(); // a new track, its artist or its cover arriving
        _panel.SetMedia(media);
    }

    /// <summary>Creates the window handles with the no-activate tool-window styles, before anything is shown.</summary>
    public void Initialize()
    {
        _hwnd = IslandNative.MakeNoActivate(this, WndProc);
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
        RefreshPuck();
        _panel.SetActivities(activities.Where(a => a.ModuleId != Chippi.Features.PlainPaste.PlainPasteModule.Id).ToArray(), mood);
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
        bool visible = _enabled && !_suppressed && _monitor != default && _hwnd != default;
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
            CloseIsland(immediate: true);
            Hide();
        }
    }

    private MonitorArea? Area => IslandNative.Area(_monitor);

    /// <summary>The puck at rest, in physical pixels: the bar grows from it, and the panel sits on its outer corner.</summary>
    internal Rect PuckRect => _puck;

    /// <summary>The widget at rest, in physical pixels: the bar while it shows, otherwise the puck. The panel grows out of it and folds back into it.</summary>
    internal Rect WidgetRect => _barShown && Area is { } area ? IslandGeometry.BarRect(_puck, _left, area.Scale) : _puck;

    /// <summary>Whether the widget is the bar (or unfolding into it).</summary>
    internal bool BarShown => _barShown;

    /// <summary>Whether the bar and the panel grow left from the puck (it sits right of the middle).</summary>
    internal bool OpensLeft => _left;

    /// <summary>The window around a puck centered on <paramref name="center"/>, with the bar's room on its side.</summary>
    private Rect WindowAt(Point center, double scale) =>
        new(center.X - (_left ? Root.Width - Room : Room) * scale, center.Y - Room * scale, Root.Width * scale, Root.Height * scale);

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
            _panel.Open(_puck, WidgetRect, area);
    }

    /// <summary>
    /// The puck on the window's right with the bar growing left from it, or the other way round: the widget is
    /// held at the puck's outer side, and Chippi sits at that end.
    /// </summary>
    private void ApplySide(bool left)
    {
        _left = left;
        double outer = Room - IslandGeometry.Puck / 2;
        Canvas.SetLeft(PuckButton, left ? double.NaN : outer);
        Canvas.SetRight(PuckButton, left ? outer : double.NaN);
        Bar.HorizontalAlignment = PuckEnd.HorizontalAlignment = left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        LayOutBar(left);
    }

    /// <summary>
    /// The bar's columns from its round far end to Chippi's: 8, the lead (34), 10, the lines, 10, the equalizer, 10 and
    /// Chippi's 46. With Chippi at the left it's the same mirrored, so the two circles always sit in the two round
    /// ends and the bars always sit by Chippi; the lines still read left to right. They get 123 either way.
    /// </summary>
    private void LayOutBar(bool chippiRight)
    {
        GridLength[] widths = [new(8), new(34), new(10), new(1, GridUnitType.Star), new(10), GridLength.Auto, new(10), new(46)];
        int Column(int fromFarEnd) => chippiRight ? fromFarEnd : widths.Length - 1 - fromFarEnd;
        for (int i = 0; i < widths.Length; i++)
            Bar.ColumnDefinitions[Column(i)].Width = widths[i];
        Grid.SetColumn(Cover, Column(1));
        Grid.SetColumn(Glyph, Column(1));
        Grid.SetColumn(Lines, Column(3));
        Grid.SetColumn(Equalizer, Column(5));
        Grid.SetColumn(PausedMark, Column(5));
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch ((uint)msg)
        {
            case PInvoke.WM_DISPLAYCHANGE:
            case PInvoke.WM_SETTINGCHANGE:
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => DisplayChanged?.Invoke(this, EventArgs.Empty));
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
        if (code >= 0 && wParam.Value == PInvoke.WM_KEYDOWN && ((KBDLLHOOKSTRUCT*)lParam.Value)->vkCode == VK_ESCAPE)
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
    /// A click anywhere off the panel closes it. It never activates, so it can't lose focus to see
    /// that; a low-level mouse hook watches for presses while it is open, and always passes them
    /// on: the click still reaches the app under the pointer.
    /// </summary>
    private unsafe LRESULT OnMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        uint message = (uint)wParam.Value;
        if (code >= 0 && message is PInvoke.WM_LBUTTONDOWN or PInvoke.WM_RBUTTONDOWN or PInvoke.WM_MBUTTONDOWN or PInvoke.WM_XBUTTONDOWN)
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
        if (!_panel.IsOpen || _panel.Covers(physical) || (IsVisible && WidgetRect.Contains(physical))
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

    private void OpenIsland()
    {
        if (!IsVisible || _dragging || Area is not { } area)
            return;
        _panel.Open(_puck, WidgetRect, area);
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
        if (!_panel.IsOpen)
            return;
        _panel.CloseIsland(immediate, WidgetRect);
        RefreshPuck();
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
        if (_panel.Covers(at) || (IsVisible && WidgetRect.Contains(at)))
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

    /// <summary>Whether the keyboard and mouse hooks are installed (only while the panel is open).</summary>
    internal bool IsHooked => _escapeHook is { IsInvalid: false } || _clickHook is { IsInvalid: false };

    // ----- The puck and the bar -----

    /// <summary>
    /// The widget: the blob sleeps at rest and wakes on hover or while anything is live; hover brightens the
    /// rim, never the size, and the puck lifts while dragged. The widget is the bar while there is something
    /// to show, in order: a drag from another app (the page it opens), Plain paste's feedback, the song
    /// playing (or paused, for <see cref="PauseHold"/>), or the highest activity. Otherwise, and while
    /// dragged, it is the puck. While the panel is out, the panel is its shape.
    /// </summary>
    private void RefreshPuck()
    {
        var feedback = _activities.FirstOrDefault(a => a.ModuleId == Chippi.Features.PlainPaste.PlainPasteModule.Id);
        var mic = _activities.FirstOrDefault(a => a.ModuleId == "mic-mute" && a.NeedsAttention);
        var top = _activities.FirstOrDefault(a => a.ModuleId is not Chippi.Features.PlainPaste.PlainPasteModule.Id and not "mic-mute");
        bool dropping = _dragKey is not null;
        bool awake = dropping || _hovered || _lifted || _panel.IsVisible;
        Grow(_lifted ? LiftScale : 1, _lifted ? LiftTilt : 0);
        BodyShadow.Effect = (System.Windows.Media.Effects.Effect)FindResource(_lifted ? "IslandLiftShadow" : "IslandPuckShadow");
        Body.SetResourceReference(Border.BorderBrushProperty, _hovered && !_lifted ? "IslandWidgetHoverBorderBrush" : "IslandWidgetBorderBrush");
        Body.Visibility = BodyShadow.Visibility = _panel.IsVisible ? Visibility.Hidden : Visibility.Visible;
        MicBadge.Visibility = Shown(mic is not null);

        var current = _media?.Current;
        var playing = top?.Kind == IslandActivityKind.Media && current is { Playing: true } ? current : null;
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
        var tile = top?.TileKey is { } key ? _tiles.FirstOrDefault(t => t.Key == key) : null;
        var dropTile = dropping ? _tiles.FirstOrDefault(t => t.Key == _dragKey) : null;
        bool bar = !_lifted && (dropTile is not null || feedback is not null || song is not null || top is not null);
        _singing = bar && dropTile is null && feedback is null && song is not null;
        bool paused = _singing && playing is null;
        // Listening (headphones) while a song plays, the panel out or not; otherwise the mood for the activities, awake on hover or on the bar.
        Pet.ShowMood(playing is not null && !_lifted ? PetMood.Listening : _mood != PetMood.Sleepy ? _mood : awake || bar ? PetMood.Idle : PetMood.Sleepy);
        _chipKey = dropTile is not null || feedback is not null ? null : _singing ? IslandPanelWindow.NowPlayingKey : top?.TileKey;
        ShowBar(bar);
        if (bar)
        {
            // Folding, the bar keeps what it showed while it fades.
            Cover.Visibility = Shown(_singing);
            Glyph.Visibility = Shown(!_singing);
            Equalizer.Visibility = Shown(_singing && !paused);
            PausedMark.Visibility = Shown(paused);
            // The lines' column: what the rest takes, with the bars three 3s 1 apart and 1 in (15) or the pause mark (14).
            _lineRoom = IslandGeometry.BarWidth - 8 - 34 - 10 - 10 - 10 - 46 - (_singing ? paused ? 14 : 15 : 0);
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
            ShowGlyph(LucideIcons.ClipboardType, feedback.NeedsAttention);
            ShowLine(feedback.Key, feedback.Title, "");
        }
        else if (song is not null)
        {
            ShowCover(song.Artwork);
            ShowLine(song.Id, song.Title.Length > 0 ? song.Title : song.Source, song.Artist);
        }
        else if (top is not null)
        {
            if (tile is not null)
                ShowGlyph(LucideIcons.Get(tile.Toggle?.Icon ?? tile.Icon), top.NeedsAttention);
            else
                ShowKindGlyph(top);
            // The status over the feature: "1 hr left" over "Keep awake".
            string status = tile is null ? "" : tile.Toggle?.Status?.Invoke() ?? tile.Status;
            string title = status.Length > 0 ? status : top.Title;
            string detail = tile?.Name ?? top.Source;
            ShowLine(top.Key, title, detail == title ? "" : detail);
        }
        // The equalizer follows the output's real level while a song plays on the bar and can be seen: not while
        // the panel is out, where the bar keeps its state only for the fold back.
        if (_singing && !paused && IsVisible && !_panel.IsVisible && !Motion.IsReduced)
            _level?.Start();
        else
            StopLevel();
        AutomationProperties.SetName(PuckButton, bar ? $"Chippi, {_chipText}" : "Chippi");
        AutomationProperties.SetHelpText(PuckButton, mic is not null ? (mic.Title == "Muted" ? "Microphone muted" : mic.Title) : "");
        AutomationProperties.SetLiveSetting(PuckButton, AutomationLiveSetting.Polite);
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

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
        if (shown == _barShown)
            return;
        _barShown = shown;
        double width = shown ? IslandGeometry.BarWidth : IslandGeometry.Puck;
        if (Motion.IsReduced || !IsVisible || _lifted || _panel.IsVisible)
        {
            PuckButton.BeginAnimation(WidthProperty, null);
            PuckButton.Width = width;
            Bar.BeginAnimation(OpacityProperty, null);
            Bar.Opacity = shown ? 1 : 0;
            return;
        }
        PuckButton.BeginAnimation(WidthProperty, new DoubleAnimation(width, shown ? UnfoldTime : FoldTime) { EasingFunction = EaseOut }, HandoffBehavior.SnapshotAndReplace);
        var fade = shown
            ? new DoubleAnimation(1, UnfoldTime / 2) { BeginTime = UnfoldTime / 2 }
            : new DoubleAnimation(0, FoldTime / 2);
        Bar.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>The puck's size and tilt, eased over 120ms (at once under reduced motion or while hidden).</summary>
    private void Grow(double scale, double tilt)
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
                target.BeginAnimation(property, new DoubleAnimation(to, GrowTime) { EasingFunction = EaseOut }, HandoffBehavior.SnapshotAndReplace);
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
        GlyphIcon.Data = icon;
        GlyphIcon.SetResourceReference(LucideIcon.BrushProperty, caution ? "IslandCautionBrush" : "IslandAccentBrush");
        GlyphIcon.Visibility = Visibility.Visible;
        Ring.Visibility = Dot.Visibility = Visibility.Collapsed;
    }

    /// <summary>An activity without a page of its own: the ring for a timer or an agent, a folder for a download or a stash, a note for music, a dot for a status.</summary>
    private void ShowKindGlyph(IslandActivity activity)
    {
        switch (activity.Kind)
        {
            case IslandActivityKind.Timer or IslandActivityKind.Agent:
                RingArc.Data = Arc(activity.Progress ?? 1);
                GlyphIcon.Visibility = Dot.Visibility = Visibility.Collapsed;
                Ring.Visibility = Visibility.Visible;
                break;
            case IslandActivityKind.Download or IslandActivityKind.Stash:
                ShowGlyph(LucideIcons.Folder, activity.NeedsAttention);
                break;
            case IslandActivityKind.Media: // music Now Playing can't read: its title, without the bars
                ShowGlyph(LucideIcons.Music, activity.NeedsAttention);
                break;
            default:
                Dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, activity.NeedsAttention ? "IslandCautionBrush" : "IslandAccentBrush");
                GlyphIcon.Visibility = Ring.Visibility = Visibility.Collapsed;
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
        SongText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double full = Math.Ceiling(SongText.DesiredSize.Width);
        double room = _lineRoom;
        bool fits = full <= room;
        bool scroll = !fits && !Motion.IsReduced && IsVisible && !_panel.IsVisible;
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
        var heights = _bars.Next(peak);
        for (int i = 0; i < heights.Count; i++)
            ((FrameworkElement)Equalizer.Children[i]).Height = 2 + Math.Round(12 * heights[i]);
    }

    // Still, the bars take their resting shape again (8, 14, 10).
    private void StopLevel()
    {
        _level?.Stop();
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

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _pressAt = PointerAt();
        _pressedBar = _barShown;
        _grab = _pressAt.Value - Center(_puck);
        ((UIElement)sender).CaptureMouse();
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (_pressAt is not { } start)
            return;
        var pointer = PointerAt();
        if (!_dragging)
        {
            double threshold = DragThreshold * (Area?.Scale ?? 1);
            if ((pointer - start).Length < threshold)
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
            DropAt(PointerAt());
        else if (_pressedBar)
            OpenChip();
        else
            Toggle();
    }

    /// <summary>Capture taken away mid-drag (another window, the widget hiding): it drops where it is.</summary>
    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_pressAt is null)
            return;
        _pressAt = null;
        if (_dragging)
            DropAt(PointerAt());
    }

    /// <summary>The puck lifts (bigger, tilted, a deeper shadow): the panel goes and the bar folds at once.</summary>
    private void BeginDrag()
    {
        _dragging = true;
        Menu.IsOpen = false;
        CloseIsland(immediate: true);
        StopSlide();
        _lifted = true;
        _followScale = double.NaN;
        RefreshPuck();
    }

    /// <summary>The puck follows the pointer, held where it was grabbed, onto any display.</summary>
    internal void DragTo(Point pointer)
    {
        var center = pointer - _grab;
        if (IslandNative.Area(IslandNative.MonitorAt(center)) is not { } area)
            return;
        Follow(center, area.Scale);
    }

    /// <summary>
    /// Drops the puck where <paramref name="pointer"/> (physical pixels) lets go: on the display under
    /// its center, slid the least distance that keeps all of it inside the work area, then reports it.
    /// Returns the puck's rectangle there.
    /// </summary>
    internal Rect DropAt(Point pointer)
    {
        _dragging = false;
        _lifted = false;
        var center = pointer - _grab;
        var monitor = IslandNative.MonitorAt(center);
        if (IslandNative.Area(monitor) is not { } area)
        {
            Place();
            RefreshPuck();
            return _puck;
        }
        double size = IslandGeometry.Puck * area.Scale;
        var puck = IslandGeometry.Inside(new Rect(center.X - size / 2, center.Y - size / 2, size, size), area.Work);
        (_x, _y) = IslandGeometry.Fractions(puck, area.Work);
        _monitor = monitor;
        _fallback = false;
        _puck = puck;
        _hovered = puck.Contains(pointer);
        // Saved once it has landed: a save writes settings.json, which mid-slide costs frames.
        var chosen = new IslandPlacementEventArgs(_x.Value, _y.Value, monitor);
        SlideTo(center, Center(puck), area.Scale, IslandGeometry.OpensLeft(puck, area.Work), () => PlacementChosen?.Invoke(this, chosen));
        RefreshPuck();
        return puck;
    }

    /// <summary>
    /// Slides the puck from <paramref name="from"/> to <paramref name="to"/> (its center) over 180ms, at
    /// once under reduced motion; then the bar takes the side toward the middle, and <paramref name="arrived"/> runs.
    /// </summary>
    private void SlideTo(Point from, Point to, double scale, bool left, Action arrived)
    {
        StopSlide();
        void Arrive()
        {
            ApplySide(left);
            IslandNative.Move(_hwnd, WindowAt(to, scale));
            arrived();
        }
        if (Motion.IsReduced || (to - from).Length < 0.5)
        {
            Arrive();
            return;
        }
        long began = Stopwatch.GetTimestamp();
        _slideFrame = (_, _) =>
        {
            double t = Math.Min(1, Stopwatch.GetElapsedTime(began) / SlideTime);
            double eased = 1 - Math.Pow(1 - t, 3);
            if (t >= 1)
            {
                StopSlide();
                Arrive();
                return;
            }
            Follow(from + (to - from) * eased, scale);
        };
        CompositionTarget.Rendering += _slideFrame;
    }

    private void StopSlide()
    {
        if (_slideFrame is null)
            return;
        CompositionTarget.Rendering -= _slideFrame;
        _slideFrame = null;
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
        _lifted = lifted;
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
        Motion.Changed -= OnMotionChanged;
        foreach (var part in _tiles.SelectMany(t => t.Parts))
            part.StatusChanged -= OnStatusChanged;
        StopSlide();
        _level?.Stop();
        CloseIsland();
        _panel.Close();
        base.OnClosed(e);
    }
}
