// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.App.Pet;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Themes;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Island;

/// <summary>The user dropped the tab on an edge: where, and on which monitor.</summary>
internal sealed class IslandPlacementEventArgs(IslandEdge edge, double place, HMONITOR monitor) : EventArgs
{
    public IslandEdge Edge { get; } = edge;

    public double Place { get; } = place;

    /// <summary>The monitor it landed on.</summary>
    public HMONITOR Monitor { get; } = monitor;
}

/// <summary>
/// The island's docked tab (DESIGN.md §5 Island, §9), and the controller for the open island and
/// the drag. Every window here never activates (WS_EX_NOACTIVATE plus WM_MOUSEACTIVATE) and never
/// shows in Alt+Tab or the taskbar, and is placed in physical pixels on the monitor it is given.
/// Hovering only swells the tab; a click opens the island, and it stays open until a click outside
/// it, Esc or another click on the tab. A press that moves 4px drags the tab to another edge.
/// <see cref="IslandHost"/> feeds it activities, the mood, the monitor, the saved edge and place,
/// and whether to hide.
/// </summary>
public partial class IslandWindow : Window
{
    private const double DragThreshold = 4;
    private const double LineLength = 84;
    private const double LineThickness = 3;
    private const double GlyphGap = 4;
    private const uint VK_ESCAPE = 0x1B;

    private static readonly TimeSpan SwellTime = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan SlideTime = TimeSpan.FromMilliseconds(180);

    private readonly IslandPanelWindow _panel = new();
    private readonly IslandOverlay _bubble = IslandOverlay.Bubble();
    private readonly IslandOverlay _line = IslandOverlay.Line();
    private IReadOnlyList<IslandActivity> _activities = [];
    private PetMood _mood = PetMood.Sleepy;
    private IslandEdge _edge = IslandEdge.Top;
    private IslandEdge _docked = IslandEdge.Top;
    private double _place = 0.5;
    private bool _enabled = true;
    private bool _suppressed;
    private bool _hovered;
    private readonly HOOKPROC _onKeyboard; // both kept alive for as long as their hook exists
    private readonly HOOKPROC _onMouse;
    private UnhookWindowsHookExSafeHandle? _escapeHook;
    private UnhookWindowsHookExSafeHandle? _clickHook;
    private HWND _hwnd;
    private HMONITOR _monitor;
    private Point? _pressAt;
    private bool _dragging;
    private EventHandler? _slideFrame;
    private EventHandler? _swellFrame;
    private Size _tabSize; // drawn now
    private IslandEdge _tabEdge; // the edge it was drawn for
    private Size _tabTarget; // where the swell is heading

    public IslandWindow()
    {
        InitializeComponent();
        _onKeyboard = OnKeyboard;
        _onMouse = OnMouse;

        Tab.MouseEnter += (_, _) =>
        {
            _hovered = true;
            RefreshTab();
        };
        Tab.MouseLeave += (_, _) =>
        {
            _hovered = false;
            RefreshTab();
        };
        // The tab stays swollen until the island has shrunk back into it.
        _panel.IsVisibleChanged += (_, _) =>
        {
            // The island covered the tab, so the tab never saw the pointer come or go.
            if (!_panel.IsVisible && Area is { } area)
                _hovered = Grown(area).Contains(IslandNative.Cursor());
            RefreshTab();
        };
        _panel.TabPressed += OnPress;

        // The mouse is handled here, before the button sees it: a press is a click or the start of a drag.
        // Click itself comes only from Enter, Space or UI Automation's Invoke.
        Tab.PreviewMouseLeftButtonDown += OnPress;
        Tab.PreviewMouseMove += OnPointerMove;
        Tab.PreviewMouseLeftButtonUp += OnRelease;
        Tab.LostMouseCapture += OnLostCapture;
        Tab.Click += (_, _) => Toggle();
        Tab.MouseRightButtonUp += (_, e) =>
        {
            if (DebugItems.Children.Count == 0)
                return;
            DebugMenu.IsOpen = true;
            e.Handled = true;
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, Place);

        ApplyEdgeLayout(IslandEdge.Top);
    }

    /// <summary>Raised when the display layout or work area changed (WM_DISPLAYCHANGE, WM_SETTINGCHANGE).</summary>
    public event EventHandler? DisplayChanged;

    /// <summary>The user dropped the tab on an edge it can dock on. It is already there; save it.</summary>
    internal event EventHandler<IslandPlacementEventArgs>? PlacementChosen;

    /// <summary>The user dropped the tab on the taskbar's edge, the bottom or a corner; it went back.</summary>
    public event EventHandler? DropRefused;

    internal HWND Handle => _hwnd;

    /// <summary>Creates the window handles with the no-activate tool-window styles, before anything is shown.</summary>
    public void Initialize()
    {
        _hwnd = IslandNative.MakeNoActivate(this, WndProc);
        _panel.Initialize();
        _bubble.Initialize();
        _line.Initialize();
    }

    /// <summary>The monitor the island sits on, for the island display setting.</summary>
    internal void SetMonitor(HMONITOR monitor)
    {
        if (monitor == default || monitor == _monitor)
            return;
        _monitor = monitor;
        Place();
        ApplyVisibility();
    }

    /// <summary>AppSettings.IslandEdge and IslandPlace.</summary>
    public void SetPlacement(IslandEdge edge, double place)
    {
        if (edge == _edge && Math.Abs(place - _place) < 1e-6)
            return; // the drop already put it there
        _edge = edge;
        _place = place;
        Place();
    }

    /// <summary>True while a fullscreen app, game or presentation is in front: the island hides.</summary>
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
        if (_bubble.Pet is { } pet)
            pet.PetColor = color;
    }

    /// <summary>The activities in display priority order (<see cref="IslandActivityHub.Activities"/>) and the pet's mood for them.</summary>
    public void SetActivities(IReadOnlyList<IslandActivity> activities, PetMood mood)
    {
        _activities = activities;
        _mood = mood;
        RefreshTab();
        _panel.SetActivities(activities, mood);
    }

    /// <summary>The running features' panels that have an island view, in sidebar order.</summary>
    public void SetPanels(IReadOnlyList<FlyoutPanelContribution> panels) => _panel.SetPanels(panels);

    /// <summary>A module reported success: Gumdrop celebrates for about 2.5s.</summary>
    public void Celebrate()
    {
        Pet.Celebrate();
        _panel.Celebrate();
    }

    /// <summary>Debug menu entries (null is a separator). Right-clicking the tab opens them.</summary>
    public void SetDebugMenu(IEnumerable<(string Label, Action Action)?> items)
    {
        DebugItems.Children.Clear();
        foreach (var item in items)
        {
            if (item is not { } entry)
            {
                DebugItems.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4), Background = (Brush)FindResource("IslandBorderBrush") });
                continue;
            }
            var button = new Button { Content = entry.Label, Style = (Style)FindResource("IslandMenuItemStyle") };
            button.Click += (_, _) =>
            {
                DebugMenu.IsOpen = false;
                entry.Action();
            };
            DebugItems.Children.Add(button);
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
            RefreshTab();
        }
        else if (!visible && IsVisible)
        {
            DebugMenu.IsOpen = false;
            if (_pressAt is not null)
                Tab.ReleaseMouseCapture(); // ends a drag: OnLostCapture
            CloseIsland(immediate: true);
            Hide();
        }
    }

    private MonitorArea? Area => IslandNative.Area(_monitor);

    /// <summary>The tab window's rectangle at its saved edge and place: sized to the swollen live tab, flat side on the edge.</summary>
    private Rect Home(MonitorArea area) => TabAt(area, live: true);

    /// <summary>The swollen tab as it is drawn now, in physical pixels: what the island grows out of and shrinks back into.</summary>
    private Rect Grown(MonitorArea area) => TabAt(area, live: _activities.Count > 0);

    private Rect TabAt(MonitorArea area, bool live)
    {
        var edge = IslandGeometry.Docked(_edge, area.Monitor, area.Work);
        var anchor = IslandGeometry.Anchor(edge, _place, area.Monitor, area.Work, area.Scale);
        var size = IslandGeometry.TabSize(edge, live, swollen: true);
        return IslandGeometry.TabRect(edge, anchor, new Size(size.Width * area.Scale, size.Height * area.Scale));
    }

    /// <summary>Puts the tab at home on its monitor (and the open island around it), and re-asserts topmost.</summary>
    private void Place()
    {
        if (_hwnd == default || _dragging || Area is not { } area)
            return;
        StopSlide();
        ApplyEdgeLayout(IslandGeometry.Docked(_edge, area.Monitor, area.Work));
        IslandNative.Move(_hwnd, Home(area));
        if (_panel.IsOpen)
            _panel.Open(_docked, Grown(area), area);
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
    /// Esc closes the island. It never has keyboard focus (it never activates), so a low-level
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
    internal void Escape() => Dispatcher.BeginInvoke(() => CloseIsland());

    /// <summary>
    /// A click anywhere off the island closes it. It never activates, so it can't lose focus to see
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
    /// A press at <paramref name="physical"/> while the island is open: off the island and the tab it
    /// closes the island, unless a list or menu the island opened has the mouse.
    /// </summary>
    internal void PressedAt(Point physical)
    {
        if (!_panel.IsOpen || _panel.Covers(physical) || IslandNative.WindowRect(_hwnd).Contains(physical)
            || DebugMenu.IsOpen || Mouse.Captured is not null)
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

    private void OpenIsland()
    {
        if (!IsVisible || _dragging || Area is not { } area)
            return;
        _panel.Open(_docked, Grown(area), area);
        var module = PInvoke.GetModuleHandle((string?)null);
        if (_escapeHook is null || _escapeHook.IsInvalid)
            _escapeHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _onKeyboard, module, 0);
        if (_clickHook is null || _clickHook.IsInvalid)
            _clickHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _onMouse, module, 0);
        RefreshTab();
    }

    /// <summary>Shrinks the island back into the tab; <paramref name="immediate"/> hides it at once.</summary>
    private void CloseIsland(bool immediate = false)
    {
        _escapeHook?.Dispose(); // unhooks
        _escapeHook = null;
        _clickHook?.Dispose();
        _clickHook = null;
        if (!_panel.IsOpen)
            return;
        _panel.CloseIsland(immediate);
        RefreshTab();
    }

    internal bool IsIslandOpen => _panel.IsOpen;

    /// <summary>Whether the keyboard and mouse hooks are installed (only while the island is open).</summary>
    internal bool IsHooked => _escapeHook is { IsInvalid: false } || _clickHook is { IsInvalid: false };

    // ----- The tab -----

    private void ApplyEdgeLayout(IslandEdge docked)
    {
        _docked = docked;
        (Tab.HorizontalAlignment, Tab.VerticalAlignment) = docked switch
        {
            IslandEdge.Left => (HorizontalAlignment.Left, VerticalAlignment.Center),
            IslandEdge.Right => (HorizontalAlignment.Right, VerticalAlignment.Center),
            _ => (HorizontalAlignment.Center, VerticalAlignment.Top),
        };
        TabContent.Orientation = docked == IslandEdge.Top ? Orientation.Horizontal : Orientation.Vertical;
        Glyph.Margin = docked == IslandEdge.Top ? new Thickness(GlyphGap, 0, 0, 0) : new Thickness(0, GlyphGap, 0, 0);
        RefreshTab();
    }

    /// <summary>
    /// Sizes and fills the tab: 20px sleepy Gumdrop at rest; 22px with the highest activity's 14px glyph
    /// when something is live. Hovered or open it swells, and Gumdrop wakes.
    /// </summary>
    private void RefreshTab()
    {
        var top = _activities.Count > 0 ? _activities[0] : null;
        bool swollen = _hovered || _panel.IsVisible;
        SwellTo(IslandGeometry.TabSize(_docked, top is not null, swollen));

        Pet.Size = top is not null ? 22 : 20;
        Pet.ShowMood(top is not null ? _mood : swollen ? PetMood.Idle : PetMood.Sleepy);

        Glyph.Visibility = Shown(top is not null);
        Equalizer.Visibility = Shown(top?.Kind == IslandActivityKind.Media);
        Ring.Visibility = Shown(top?.Kind is IslandActivityKind.Timer or IslandActivityKind.Agent);
        bool folder = top?.Kind is IslandActivityKind.Download or IslandActivityKind.Stash;
        FolderGlyph.Visibility = Shown(folder && !top!.NeedsAttention);
        CautionDot.Visibility = Shown(folder && top!.NeedsAttention);
        Dot.Visibility = Shown(top?.Kind == IslandActivityKind.Status);
        if (top?.Kind is IslandActivityKind.Timer or IslandActivityKind.Agent)
            RingArc.Data = Arc(top.Progress ?? 1);

        AutomationProperties.SetHelpText(Tab, top is null ? "" : string.IsNullOrWhiteSpace(top.Source) ? top.Title : $"{top.Title}, {top.Source}");
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Grows or shrinks the tab to <paramref name="target"/> DIPs over 120ms, easing out, around its
    /// center. It snaps under reduced motion, while hidden, and when it moved to another edge.
    /// </summary>
    private void SwellTo(Size target)
    {
        if (target == _tabTarget && _tabEdge == _docked)
            return;
        _tabTarget = target;
        StopSwell();
        if (Motion.IsReduced || !IsVisible || _tabSize == default || _tabEdge != _docked)
        {
            DrawTab(target);
            return;
        }
        var from = _tabSize;
        long began = Stopwatch.GetTimestamp();
        _swellFrame = (_, _) =>
        {
            double t = Math.Min(1, Stopwatch.GetElapsedTime(began) / SwellTime);
            double eased = 1 - Math.Pow(1 - t, 3);
            DrawTab(new Size(from.Width + (target.Width - from.Width) * eased, from.Height + (target.Height - from.Height) * eased));
            if (t >= 1)
                StopSwell();
        };
        CompositionTarget.Rendering += _swellFrame;
    }

    private void StopSwell()
    {
        if (_swellFrame is null)
            return;
        CompositionTarget.Rendering -= _swellFrame;
        _swellFrame = null;
    }

    private void DrawTab(Size size)
    {
        _tabSize = size;
        _tabEdge = _docked;
        TabBody.Width = size.Width;
        TabBody.Height = size.Height;
        var (fill, stroke) = TabShape(_docked, size);
        TabFill.Data = fill;
        TabStroke.Data = stroke;
    }

    /// <summary>The size the tab is drawn at, or heading to while it swells.</summary>
    internal Size TabTarget => _tabTarget;

    /// <summary>
    /// The half-oval for a tab of <paramref name="size"/> DIPs: flat against the edge, fully round on
    /// the side away from it. The stroke runs half a pixel inside on every side but the flat one.
    /// </summary>
    internal static (Geometry Fill, Geometry Stroke) TabShape(IslandEdge edge, Size size)
    {
        // Built for the top edge (x along it, y away from it), then turned for the sides.
        double along = edge == IslandEdge.Top ? size.Width : size.Height;
        double across = edge == IslandEdge.Top ? size.Height : size.Width;
        var fill = HalfOval(along, across, 0, close: true);
        var stroke = HalfOval(along, across, 0.5, close: false);
        var turn = edge switch
        {
            IslandEdge.Left => new Matrix(0, 1, 1, 0, 0, 0), // (x, y) → (y, x)
            IslandEdge.Right => new Matrix(0, 1, -1, 0, across, 0), // (x, y) → (across - y, x)
            _ => Matrix.Identity,
        };
        fill.Transform = stroke.Transform = new MatrixTransform(turn);
        fill.Freeze();
        stroke.Freeze();
        return (fill, stroke);
    }

    private static PathGeometry HalfOval(double along, double across, double inset, bool close)
    {
        double r = Math.Max(0, Math.Min(across, along / 2) - inset);
        double left = inset, right = along - inset, bottom = across - inset;
        var figure = new PathFigure { StartPoint = new Point(right, 0), IsClosed = close, IsFilled = close };
        figure.Segments.Add(new LineSegment(new Point(right, bottom - r), true));
        figure.Segments.Add(new ArcSegment(new Point(right - r, bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(new Point(left + r, bottom), true));
        figure.Segments.Add(new ArcSegment(new Point(left, bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(new Point(left, 0), true));
        return new PathGeometry { Figures = { figure } };
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
        Tab.CaptureMouse();
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
        _dragging = false;
        Tab.ReleaseMouseCapture();
        if (dragged)
            DropAt(PointerAt());
        else
            Toggle();
    }

    /// <summary>Capture taken away mid-drag (another window, the island hiding): the tab goes back.</summary>
    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_pressAt is null)
            return;
        bool dragged = _dragging;
        _pressAt = null;
        _dragging = false;
        if (dragged)
        {
            EndDragVisuals();
            Return(PointerAt());
        }
    }

    private void BeginDrag()
    {
        _dragging = true;
        CloseIsland(immediate: true);
        TabBody.Opacity = 0;
    }

    /// <summary>The bubble follows the pointer; the accent line shows where the tab would land, if it can.</summary>
    private void DragTo(Point pointer)
    {
        if (IslandNative.Area(IslandNative.MonitorAt(pointer)) is not { } area)
            return;
        double bubble = IslandOverlay.BubbleWindow * area.Scale;
        _bubble.ShowAt(new Rect(pointer.X - bubble / 2, pointer.Y - bubble / 2, bubble, bubble));
        if (IslandGeometry.Snap(pointer, area.Monitor, area.Work, area.Scale) is { } target)
            _line.ShowAt(LineRect(target.Edge, IslandGeometry.Anchor(target.Edge, target.Place, area.Monitor, area.Work, area.Scale), area.Scale));
        else
            _line.Hide();
    }

    private static Rect LineRect(IslandEdge edge, Point anchor, double scale)
    {
        double length = LineLength * scale, thickness = LineThickness * scale;
        return edge switch
        {
            IslandEdge.Left => new Rect(anchor.X, anchor.Y - length / 2, thickness, length),
            IslandEdge.Right => new Rect(anchor.X - thickness, anchor.Y - length / 2, thickness, length),
            _ => new Rect(anchor.X - length / 2, anchor.Y, length, thickness),
        };
    }

    private void EndDragVisuals()
    {
        _bubble.Hide();
        _line.Hide();
        TabBody.Opacity = 1;
    }

    /// <summary>
    /// Docks the tab where <paramref name="pointer"/> (physical pixels) lets go, sliding it into place,
    /// and reports it. A drop on the taskbar's edge, the bottom or in a corner is refused: the tab
    /// slides back home. Returns whether the drop was taken.
    /// </summary>
    internal bool DropAt(Point pointer)
    {
        EndDragVisuals();
        var monitor = IslandNative.MonitorAt(pointer);
        if (IslandNative.Area(monitor) is not { } area || IslandGeometry.Snap(pointer, area.Monitor, area.Work, area.Scale) is not { } target)
        {
            Return(pointer);
            DropRefused?.Invoke(this, EventArgs.Empty);
            return false;
        }
        _edge = target.Edge;
        _place = target.Place;
        _monitor = monitor;
        ApplyEdgeLayout(target.Edge);
        SlideTo(pointer, Home(area));
        PlacementChosen?.Invoke(this, new IslandPlacementEventArgs(target.Edge, target.Place, monitor));
        return true;
    }

    /// <summary>Slides the tab from the pointer back to where it was.</summary>
    private void Return(Point pointer)
    {
        if (Area is not { } area)
            return;
        ApplyEdgeLayout(IslandGeometry.Docked(_edge, area.Monitor, area.Work));
        SlideTo(pointer, Home(area));
    }

    /// <summary>Moves the tab window from centered on <paramref name="from"/> to <paramref name="to"/> over 180ms (at once under reduced motion).</summary>
    private void SlideTo(Point from, Rect to)
    {
        StopSlide();
        if (Motion.IsReduced)
        {
            IslandNative.Move(_hwnd, to);
            return;
        }
        var start = new Rect(from.X - to.Width / 2, from.Y - to.Height / 2, to.Width, to.Height);
        long began = Stopwatch.GetTimestamp();
        IslandNative.Move(_hwnd, start);
        _slideFrame = (_, _) =>
        {
            double t = Math.Min(1, Stopwatch.GetElapsedTime(began) / SlideTime);
            double eased = 1 - Math.Pow(1 - t, 3);
            IslandNative.Move(_hwnd, new Rect(start.X + (to.X - start.X) * eased, start.Y + (to.Y - start.Y) * eased, to.Width, to.Height));
            if (t >= 1)
                StopSlide();
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

#if DEBUG
    /// <summary>Debug builds: open or close the island without a pointer, for snapshots.</summary>
    internal void ForceOpen(bool open)
    {
        if (open)
            OpenIsland();
        else
            CloseIsland();
    }

    /// <summary>Debug builds: swell the tab as if hovered, for snapshots.</summary>
    internal void ForceHover(bool hovered)
    {
        _hovered = hovered;
        RefreshTab();
    }

    internal MonitorArea? CurrentArea => Area;

    /// <summary>
    /// Debug builds: the tab and the open island as they sit on the monitor, over a backdrop color,
    /// in physical pixels: the region around both, with the work area's edge drawn so the dock shows.
    /// </summary>
    internal void SaveScene(string path, Color backdrop)
    {
        if (Area is not { } area)
            return;
        var tab = IslandNative.WindowRect(_hwnd);
        var region = tab;
        if (_panel.IsVisible)
            region.Union(IslandNative.WindowRect(_panel.Handle));
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
            Draw(dc, Root, tab, region, area.Scale);
            if (_panel.IsVisible)
                Draw(dc, _panel.Visual, IslandNative.WindowRect(_panel.Handle), region, area.Scale);
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

    protected override void OnClosed(EventArgs e)
    {
        StopSlide();
        StopSwell();
        CloseIsland();
        _panel.Close();
        _bubble.Close();
        _line.Close();
        base.OnClosed(e);
    }
}
