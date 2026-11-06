// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>The keys the overlay acts on itself (capture-layer.md, Keys on the overlay); everything else is <see cref="Other"/>.</summary>
public enum SelectorKey { Other, Escape, Enter, Left, Up, Right, Down, A }

/// <summary>What the overlay does after a key or a click.</summary>
public enum SelectorOutcome
{
    /// <summary>Nothing more: the selector took it (or ignored it).</summary>
    None,

    /// <summary>The selection was cancelled (Esc while selecting, a right click while picking): close, keep nothing.</summary>
    Cancel,

    /// <summary>A pixel was picked (<see cref="Selector.Picked"/>).</summary>
    Picked,

    /// <summary>The key belongs to the feature: the selection was already released.</summary>
    PassToFeature,
}

/// <summary>
/// The selector's state over a frozen screen, without windows (capture-layer.md, Modes, After release, Keys on the
/// overlay): the overlay feeds it the pointer in physical virtual-screen pixels and its keys, and draws what it says.
/// Called on one thread.
/// </summary>
public sealed class Selector
{
    private readonly int _dragWidth, _dragHeight, _reach;
    private PixelPoint _down;
    private PixelRect _startRect;
    private bool _pressed;
    private readonly List<PixelPoint> _outline = [];

    /// <param name="last">The last region, for <see cref="CaptureMode.LastRegion"/>.</param>
    /// <param name="pointer">Where the pointer is at the freeze.</param>
    /// <param name="drag">Windows' drag distance (<c>SM_CXDRAG</c>, <c>SM_CYDRAG</c>).</param>
    /// <param name="reach">How near a handle the pointer has to be, in physical pixels.</param>
    public Selector(Freeze freeze, CaptureMode mode, PixelRect? last, PixelPoint pointer, PixelSize drag, int reach)
    {
        Freeze = freeze;
        Mode = mode;
        _dragWidth = Math.Max(1, drag.Width);
        _dragHeight = Math.Max(1, drag.Height);
        _reach = Math.Max(1, reach);
        Pointer = SelectionMath.OnDisplays(freeze.Displays, pointer);
        switch (mode)
        {
            case CaptureMode.Screen:
                Release(Freeze.NearestScreen(Pointer).Bounds, null, null, false);
                break;
            case CaptureMode.AllDisplays:
                Release(Freeze.VirtualBounds, null, null, false);
                break;
            case CaptureMode.LastRegion:
                if (SelectionMath.LastRegion(freeze.Displays, last) is { } rect)
                    Release(rect, null, null, false);
                else
                    Mode = CaptureMode.Region;
                break;
        }
        if (!IsReleased)
            Hover = Snaps ? Freeze.WindowAt(Pointer) : null;
    }

    /// <summary>
    /// A finished capture reopened at <paramref name="rect"/> (<see cref="Reopening.Place"/>): released at once and fixed,
    /// with no handles, moves or Ctrl+A; with <see cref="Tooling"/> on, presses inside it are the feature's.
    /// </summary>
    public Selector(Freeze freeze, PixelRect rect, CapturedScreen display, CapturedImage source, double scale, PixelPoint pointer)
    {
        Freeze = freeze;
        Mode = CaptureMode.Region;
        _dragWidth = _dragHeight = _reach = 1;
        Pointer = SelectionMath.OnDisplays(freeze.Displays, pointer);
        Fixed = true;
        Rect = rect;
        Selection = new Selection(rect, freeze, Mode, null, null, false, display) { Source = source, Scale = scale };
    }

    public Freeze Freeze { get; }

    /// <summary>A reopened capture: the selection can't be moved, resized or replaced.</summary>
    public bool Fixed { get; }

    /// <summary>The mode; <see cref="CaptureMode.LastRegion"/> with none becomes <see cref="CaptureMode.Region"/>.</summary>
    public CaptureMode Mode { get; private set; }

    public PixelPoint Pointer { get; private set; }

    /// <summary>The window under the pointer while choosing (Region and Window modes), highlighted; a click takes it.</summary>
    public FrozenWindow? Hover { get; private set; }

    /// <summary>The rectangle being dragged, or the released selection's.</summary>
    public PixelRect? Rect { get; private set; }

    /// <summary>A freeform outline as it's drawn.</summary>
    public IReadOnlyList<PixelPoint> Outline => _outline;

    /// <summary>The selection once released; null while choosing.</summary>
    public Selection? Selection { get; private set; }

    public bool IsReleased => Selection is not null;

    /// <summary>A drag or a handle is held: the bars hide.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>The handle being dragged after release.</summary>
    public SelectionHandle Held { get; private set; }

    /// <summary>The pixel picked in <see cref="CaptureMode.PickPoint"/>.</summary>
    public PixelPoint? Picked { get; private set; }

    /// <summary>
    /// The feature has a markup tool on: a press inside the released selection is the feature's, not a move. The
    /// handles still resize it.
    /// </summary>
    public bool Tooling { get; set; }

    private bool Snaps => Mode is CaptureMode.Region or CaptureMode.Window;

    /// <summary>The handle or inside under the pointer after release, for the cursor; none while choosing.</summary>
    public SelectionHandle HandleUnderPointer
    {
        get
        {
            // A freeform outline keeps its shape, and a reopened capture its place: no handles.
            if (Fixed || Selection is not { Mask: null } s)
                return SelectionHandle.None;
            var handle = SelectionMath.HandleAt(s.Rect, Pointer, _reach);
            return handle == SelectionHandle.Inside && Tooling ? SelectionHandle.None : handle;
        }
    }

    /// <summary>The pointer moved (also while a button is held).</summary>
    public void Move(PixelPoint p)
    {
        Pointer = p;
        if (Selection is { } selection)
        {
            if (Held != SelectionHandle.None)
            {
                var rect = SelectionMath.Drag(Freeze.Displays, _startRect, Held, p.X - _down.X, p.Y - _down.Y);
                if (rect != selection.Rect)
                {
                    Rect = rect;
                    Selection = selection with { Rect = rect, Window = null, Display = Freeze.NearestScreen(p) };
                }
            }
            return;
        }
        if (!_pressed)
        {
            Hover = Snaps ? Freeze.WindowAt(SelectionMath.OnDisplays(Freeze.Displays, p)) : null;
            return;
        }
        switch (Mode)
        {
            case CaptureMode.Region:
                if (IsDragging || SelectionMath.IsDrag(_down, p, _dragWidth, _dragHeight))
                {
                    IsDragging = true;
                    Hover = null;
                    Rect = PixelRect.Spanning(_down, SelectionMath.OnDisplays(Freeze.Displays, p)).Intersect(SelectionMath.Around(Freeze.Displays));
                }
                break;
            case CaptureMode.Freeform:
                IsDragging = true;
                var q = SelectionMath.OnDisplays(Freeze.Displays, p);
                if (_outline.Count == 0 || _outline[^1] != q)
                    _outline.Add(q);
                Rect = _outline.Aggregate(default(PixelRect), (r, o) => r.Union(new PixelRect(o.X, o.Y, 1, 1)));
                break;
        }
    }

    private bool _fastPath;

    /// <summary>A drag that started with Ctrl is under way: its release copies and closes (the size pill shows the copy glyph).</summary>
    public bool CopyDrag => _pressed && _fastPath;

    /// <summary>The left button went down.</summary>
    public SelectorOutcome Down(PixelPoint p, bool ctrl)
    {
        Pointer = p;
        _down = p;
        if (Mode == CaptureMode.PickPoint)
        {
            Picked = SelectionMath.OnDisplays(Freeze.Displays, p);
            return SelectorOutcome.Picked;
        }
        if (Selection is { } selection)
        {
            Held = HandleUnderPointer;
            _startRect = selection.Rect;
            IsDragging = Held != SelectionHandle.None;
            return SelectorOutcome.None;
        }
        _pressed = true;
        _fastPath = ctrl;
        if (Mode == CaptureMode.Freeform)
        {
            _outline.Clear();
            _outline.Add(SelectionMath.OnDisplays(Freeze.Displays, p));
        }
        return SelectorOutcome.None;
    }

    /// <summary>The left button came up: a drag releases its rectangle or outline, a click takes the window under it.</summary>
    public void Up(PixelPoint p)
    {
        Move(p);
        if (Selection is not null)
        {
            Held = SelectionHandle.None;
            IsDragging = false;
            return;
        }
        if (!_pressed)
            return;
        _pressed = false;
        bool dragged = IsDragging;
        IsDragging = false;
        var display = Freeze.NearestScreen(p);
        switch (Mode)
        {
            case CaptureMode.Region when dragged && Rect is { IsEmpty: false } rect:
                Release(rect, null, null, _fastPath, display);
                break;
            case CaptureMode.Region when !dragged:
            case CaptureMode.Window when !SelectionMath.IsDrag(_down, p, _dragWidth, _dragHeight): // a drag does nothing
                TakeWindow(Freeze.WindowAt(SelectionMath.OnDisplays(Freeze.Displays, p)), display);
                break;
            case CaptureMode.Freeform:
                if (Freeform.Close(_outline) is var (bounds, mask))
                    Release(bounds, mask, null, _fastPath, display);
                else
                {
                    _outline.Clear();
                    Rect = null;
                }
                break;
        }
    }

    /// <summary>A right click: cancels a pick; otherwise nothing.</summary>
    public SelectorOutcome RightClick() => Mode == CaptureMode.PickPoint ? SelectorOutcome.Cancel : SelectorOutcome.None;

    /// <summary>
    /// A key went down. Arrows move <see cref="Pointer"/> while choosing (the overlay moves the real pointer to match)
    /// or the selection after release.
    /// </summary>
    public SelectorOutcome Key(SelectorKey key, bool shift, bool ctrl)
    {
        if (Fixed)
            return key is SelectorKey.Left or SelectorKey.Right or SelectorKey.Up or SelectorKey.Down && !ctrl || key == SelectorKey.A && ctrl
                ? SelectorOutcome.None : SelectorOutcome.PassToFeature;
        if (key == SelectorKey.A && ctrl)
        {
            if (Mode == CaptureMode.PickPoint)
                return SelectorOutcome.None;
            var display = Freeze.NearestScreen(Pointer);
            _pressed = false;
            IsDragging = false;
            Held = SelectionHandle.None;
            Release(display.Bounds, null, null, false, display);
            return SelectorOutcome.None;
        }
        (int dx, int dy) = key switch
        {
            SelectorKey.Left => (-1, 0),
            SelectorKey.Right => (1, 0),
            SelectorKey.Up => (0, -1),
            SelectorKey.Down => (0, 1),
            _ => (0, 0),
        };
        if (Selection is { } selection)
        {
            if (dx == 0 && dy == 0 || ctrl)
                return SelectorOutcome.PassToFeature;
            if (selection.Mask is not null)
                return SelectorOutcome.None; // a freeform outline keeps its shape
            var rect = shift ? SelectionMath.Grow(Freeze.Displays, selection.Rect, dx, dy) : SelectionMath.Move(Freeze.Displays, selection.Rect, dx, dy);
            Rect = rect;
            Selection = selection with { Rect = rect, Window = null };
            return SelectorOutcome.None;
        }
        switch (key)
        {
            case SelectorKey.Escape:
                return SelectorOutcome.Cancel;
            case SelectorKey.Enter when Mode == CaptureMode.PickPoint:
                Picked = Pointer;
                return SelectorOutcome.Picked;
            case SelectorKey.Enter when Snaps && !_pressed:
                TakeWindow(Hover, Freeze.NearestScreen(Pointer));
                return SelectorOutcome.None;
        }
        if (dx != 0 || dy != 0)
        {
            int step = shift && Mode == CaptureMode.PickPoint ? 10 : 1;
            Move(SelectionMath.OnDisplays(Freeze.Displays, Pointer.Offset(dx * step, dy * step)));
        }
        return SelectorOutcome.None;
    }

    private void TakeWindow(FrozenWindow? window, CapturedScreen display)
    {
        if (window is null)
            return;
        var rect = SelectionMath.WindowRect(Freeze.Displays, window);
        if (!rect.IsEmpty)
            Release(rect, null, window, false, display);
    }

    private void Release(PixelRect rect, SelectionMask? mask, FrozenWindow? window, bool fastPath, CapturedScreen? display = null)
    {
        Hover = null;
        Rect = rect;
        Selection = new Selection(rect, Freeze, Mode, mask, window, fastPath, display ?? Freeze.NearestScreen(Pointer));
    }
}
