// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;
using Chippi.UI.Actions;
using Chippi.UI.Displays;

namespace Chippi.UI.Capture;

/// <summary>
/// The shared capture layer (docs/phase-7/capture-layer.md): one overlay window per display, made when the first capture
/// feature starts and kept hidden; a starter runs the freeze's steps and hands the feature a <see cref="Selection"/>.
/// The shell builds one and passes it to every capture feature. Called on the UI thread.
/// </summary>
public sealed partial class CaptureLayer : ICaptureLayer
{
    private readonly ICaptureShell _shell;
    private readonly IScreenReader _reader;
    private readonly TimeProvider _time;
    private readonly List<OverlayWindow> _overlays = [];
    private readonly string? _timings = Environment.GetEnvironmentVariable("CHIPPI_CAPTURE_TIMINGS");
    private int _uses;
    private Session? _current;

    /// <param name="reader">The screen read; tests pass a fake with known pixels.</param>
    public CaptureLayer(ICaptureShell shell, IScreenReader? reader = null, TimeProvider? time = null)
    {
        _shell = shell;
        _reader = reader ?? new ScreenReader();
        _time = time ?? TimeProvider.System;
    }

    public ICaptureSession? Current => _current;

    public PixelRect? LastRegion { get; set; }

    public IDisposable Use()
    {
        if (_uses++ == 0)
        {
            DisplayChanges.Changed += OnDisplaysChanged;
            Build(_reader.Monitors());
        }
        return new Lease(this);
    }

    private void Unuse()
    {
        if (--_uses > 0)
            return;
        DisplayChanges.Changed -= OnDisplaysChanged;
        _current?.Close(CaptureEnd.Interrupted, handBack: false);
        foreach (var o in _overlays)
            o.Dispose();
        _overlays.Clear();
    }

    private sealed class Lease(CaptureLayer layer) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                layer.Unuse();
            }
        }
    }

    public ICaptureSession? Start(CaptureRequest request)
    {
        if (_current is { } running)
        {
            running.CancelCountdown(); // the same starter again cancels a delay; anything else is ignored
            return null;
        }
        nint back = request.Door == CaptureDoor.Island ? _shell.IslandReturnWindow() : GetForegroundWindow();
        var session = _current = new Session(this, request, back, Stopwatch.GetTimestamp());
        session.Begin();
        return session;
    }

    /// <summary>The displays changed: a capture on screen closes with nothing kept, and the overlays are made again.</summary>
    private void OnDisplaysChanged()
    {
        _current?.Close(CaptureEnd.Interrupted, handBack: false);
        if (_uses > 0)
            Build(_reader.Monitors());
    }

    /// <summary>One overlay per display, placed at its bounds and DPI; kept when nothing changed.</summary>
    private void Build(IReadOnlyList<MonitorBounds> monitors)
    {
        if (_overlays.Count == monitors.Count && _overlays.Zip(monitors).All(p => p.First.Bounds == p.Second.Bounds && p.First.Dpi == p.Second.Dpi))
            return;
        foreach (var o in _overlays)
            o.Dispose();
        _overlays.Clear();
        foreach (var m in monitors)
        {
            var overlay = new OverlayWindow();
            overlay.Place(m.Bounds, m.Dpi);
            overlay.ButtonDown += (o, e) => _current?.OnButtonDown(o, e);
            overlay.ButtonUp += (o, e) => _current?.OnButtonUp(o, e);
            overlay.PointerMoved += o => _current?.OnPointerMoved(o);
            overlay.Key += (o, e) => _current?.OnKey(e);
            overlay.CloseAsked += _ => _current?.Close(CaptureEnd.Cancelled);
            overlay.LostForeground += _ => _current?.OnLostForeground();
            _overlays.Add(overlay);
        }
    }

    private void Report(string line)
    {
        if (string.IsNullOrEmpty(_timings))
            return;
        try { File.AppendAllText(_timings, line + Environment.NewLine); }
        catch (IOException) { }
    }

    private static double Ms(long from) => Stopwatch.GetElapsedTime(from).TotalMilliseconds;

    /// <summary>The next frame WPF renders.</summary>
    private static Task NextFrame()
    {
        var done = new TaskCompletionSource();
        void OnFrame(object? s, EventArgs e)
        {
            CompositionTarget.Rendering -= OnFrame;
            done.TrySetResult();
        }
        CompositionTarget.Rendering += OnFrame;
        return done.Task;
    }

    private sealed class Session(CaptureLayer layer, CaptureRequest request, nint back, long started) : ICaptureSession
    {
        private Selector? _selector;
        private Countdown? _countdown;
        private bool _closed;
        private FrameworkElement? _tools, _output;
        private long _released;

        public CaptureRequest Request { get; } = request;
        public Freeze? Freeze { get; private set; }
        public Selection? Selection => _selector?.Selection;
        public IReadOnlyList<CaptureSurface> Surfaces { get; private set; } = [];

        public bool Tooling
        {
            get => _selector?.Tooling ?? false;
            set
            {
                if (_selector is null)
                    return;
                _selector.Tooling = value;
                Render();
            }
        }

        public event Action<Selection>? Selected;
        public event Action<PixelPoint, uint>? Picked;
        public event Action<KeyEventArgs>? KeyDown;
        public event Action? Frozen;
        public event Action<CaptureEnd>? Closed;

        public async void Begin()
        {
            try
            {
                if (Request.Delay > 0)
                {
                    if (Request.Door == CaptureDoor.Island)
                        await layer._shell.CloseIslandAsync();
                    if (_closed)
                        return;
                    var ui = Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    // Nothing of Chippi's holds the keyboard while counting, so menus can be opened; a click on the
                    // line or the same starter again cancels it.
                    _countdown = new Countdown(Request.Delay, layer._time,
                        n => ui.BeginInvoke(() =>
                        {
                            if (!_closed)
                                layer._shell.Bar(new CaptureBarLine($"Capturing in {n}", CaptureBarLine.ModeName(Request.Mode))
                                    { Progress = n / (double)Request.Delay, Clicked = CancelCountdown });
                        }),
                        () => ui.BeginInvoke(async () =>
                        {
                            if (_closed)
                                return;
                            layer._shell.Bar(null);
                            started = Stopwatch.GetTimestamp();
                            await FreezeAsync(CaptureDoor.Swallowed); // no input reached Chippi during the count
                        }));
                    return;
                }
                await FreezeAsync(Request.Door);
            }
            catch (Exception ex)
            {
                Core.Diagnostics.Log.Warn("Capture: the freeze failed", ex);
                Fail();
            }
        }

        public void CancelCountdown()
        {
            if (_countdown?.Cancel() == true)
                Close(CaptureEnd.Cancelled, handBack: false);
        }

        /// <summary>The freeze's steps 2, 3, 5, 6 and 7.</summary>
        private async Task FreezeAsync(CaptureDoor door)
        {
            var monitors = layer._reader.Monitors();
            layer.Build(monitors);
            var overlays = layer._overlays;
            // 2. The overlays take the keyboard before anything is read, still empty.
            foreach (var o in overlays)
                o.ShowEmpty();
            var pointer = Cursor();
            var target = overlays.FirstOrDefault(o => o.Bounds.Contains(pointer)) ?? overlays[0];
            bool keyboard = door == CaptureDoor.Shortcut && SetForegroundWindow(target.Handle) && GetForegroundWindow() == target.Handle
                || ForegroundRight.Take(target.Handle);
            if (!keyboard)
                Core.Diagnostics.Log.Info("Capture: the overlay couldn't take the keyboard; a click on it will");
            target.Focus();
            // 3. From the island: it closes now, and the read waits for its last frame to leave the screen.
            long islandClosed = 0;
            if (door == CaptureDoor.Island)
            {
                long closing = Stopwatch.GetTimestamp();
                await layer._shell.CloseIslandAsync();
                await Task.Run(DwmFlush);
                islandClosed = (long)Ms(closing);
            }
            if (_closed)
                return;
            // 5 and 6. Every display, then the window list at the same moment.
            var exclude = overlays.Select(o => o.Handle).ToHashSet();
            var ids = DisplayCatalog.Connected().ToDictionary(d => d.Handle, d => d.Id);
            var freezer = new Freezer(layer._reader, () => FrozenWindows.Read(exclude), m => ids.GetValueOrDefault(m, ""), layer._time);
            long reading = Stopwatch.GetTimestamp();
            var freeze = await Task.Run(() => freezer.Take(monitors));
            double readMs = Ms(reading);
            if (_closed)
                return;
            if (freeze is null)
            {
                Fail();
                return;
            }
            Freeze = freeze;
            _selector = new Selector(freeze, Request.Mode, layer.LastRegion, Cursor(), DragSize(), (int)Math.Round(6 * target.Dpi / 96.0));
            // 7. The frozen image, drawn into the overlays already on screen, then shown.
            Surfaces = [.. overlays.Select(o => new CaptureSurface(ScreenOf(o)?.DisplayId ?? "", o.Bounds, o.Dpi, o.View.Layer))];
            foreach (var o in overlays)
                o.View.SetImage(ScreenOf(o));
            Render();
            await NextFrame();
            await NextFrame();
            if (_closed)
                return;
            foreach (var o in overlays)
                o.Reveal();
            await Task.Run(DwmFlush);
            layer.Report(string.Create(CultureInfo.InvariantCulture,
                $"freeze door={door} mode={Request.Mode} displays={monitors.Count} key-to-shown={Ms(started):F1} read={readMs:F1} island-close={islandClosed} keyboard={keyboard}"));
            Frozen?.Invoke();
            if (_selector.Selection is { } selection)
                Released(selection);
        }

        private CapturedScreen? ScreenOf(OverlayWindow o) => Freeze?.Screens.FirstOrDefault(s => s.Bounds == o.Bounds);

        private void Fail()
        {
            Close(CaptureEnd.Failed, handBack: true);
            layer._shell.Bar(new CaptureBarLine("Capture failed", CaptureBarLine.ModeName(Request.Mode)) { Failed = true });
        }

        public void SetBars(FrameworkElement? tools, FrameworkElement? output)
        {
            _tools = tools;
            _output = output;
            var display = Selection?.Display.Bounds;
            foreach (var o in layer._overlays)
                if (o.Bounds == display)
                    o.View.SetBars(tools, output);
                else
                    o.View.SetBars(null, null);
            Render();
        }

        private void Render()
        {
            if (_selector is null || _closed)
                return;
            foreach (var o in layer._overlays)
            {
                o.View.Render(_selector, Request, ScreenOf(o));
                o.Cursor = _selector.HandleUnderPointer switch
                {
                    SelectionHandle.TopLeft or SelectionHandle.BottomRight => Cursors.SizeNWSE,
                    SelectionHandle.TopRight or SelectionHandle.BottomLeft => Cursors.SizeNESW,
                    SelectionHandle.Left or SelectionHandle.Right => Cursors.SizeWE,
                    SelectionHandle.Top or SelectionHandle.Bottom => Cursors.SizeNS,
                    SelectionHandle.Inside => Cursors.SizeAll,
                    _ => _selector.IsReleased ? Cursors.Arrow : Cursors.Cross,
                };
            }
        }

        public void OnPointerMoved(OverlayWindow o)
        {
            if (_selector is null)
                return;
            _selector.Move(Cursor());
            Render(); // a handle drag reports its change on release
        }

        private Selection? _last;

        public void OnButtonDown(OverlayWindow o, MouseButtonEventArgs e)
        {
            if (_selector is null)
            {
                // Before the read lands, or after every try for the keyboard was refused: a click earns it.
                o.Activate();
                return;
            }
            if (!o.IsActive)
                o.Activate();
            if (e.ChangedButton != MouseButton.Left)
                return;
            o.CaptureMouse();
            _last = _selector.Selection;
            var outcome = _selector.Down(Cursor(), Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            if (outcome == SelectorOutcome.Picked && _selector.Picked is { } picked)
                Picked?.Invoke(picked, Freeze?.PixelAt(picked) ?? 0);
            Render();
        }

        public void OnButtonUp(OverlayWindow o, MouseButtonEventArgs e)
        {
            if (_selector is null)
                return;
            if (e.ChangedButton == MouseButton.Right)
            {
                if (_selector.RightClick() == SelectorOutcome.Cancel)
                    Close(CaptureEnd.Cancelled);
                return;
            }
            if (e.ChangedButton != MouseButton.Left)
                return;
            if (o.IsMouseCaptured)
                o.ReleaseMouseCapture();
            bool wasReleased = _selector.IsReleased;
            _selector.Up(Cursor());
            if (_selector.Selection is { } s && (!wasReleased || !ReferenceEquals(s, _last)))
            {
                _released = Stopwatch.GetTimestamp();
                if (!wasReleased)
                    Released(s);
                else
                    Changed(s);
            }
            Render();
            if (_released != 0)
                _ = ReportBars();
        }

        private async Task ReportBars()
        {
            long from = _released;
            _released = 0;
            await NextFrame();
            await Task.Run(DwmFlush);
            layer.Report(string.Create(CultureInfo.InvariantCulture, $"release-to-bars={Ms(from):F1}"));
        }

        /// <summary>The first release: the feature puts up its bars from <see cref="Selected"/>.</summary>
        private void Released(Selection s)
        {
            if (s.Mask is null)
                layer.LastRegion = s.Rect;
            // The bars move to the display the selection was released on.
            SetBars(_tools, _output);
            Selected?.Invoke(s);
        }

        private void Changed(Selection s)
        {
            if (s.Mask is null)
                layer.LastRegion = s.Rect;
            if (!_selector!.IsDragging)
                SetBars(_tools, _output);
            Selected?.Invoke(s);
        }

        public void OnKey(KeyEventArgs e)
        {
            if (_selector is null)
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    e.Handled = true;
                    Close(CaptureEnd.Cancelled);
                }
                return;
            }
            var key = (e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key) switch
            {
                System.Windows.Input.Key.Escape => SelectorKey.Escape,
                System.Windows.Input.Key.Enter => SelectorKey.Enter,
                System.Windows.Input.Key.Left => SelectorKey.Left,
                System.Windows.Input.Key.Right => SelectorKey.Right,
                System.Windows.Input.Key.Up => SelectorKey.Up,
                System.Windows.Input.Key.Down => SelectorKey.Down,
                System.Windows.Input.Key.A => SelectorKey.A,
                _ => SelectorKey.Other,
            };
            var before = _selector.Selection;
            bool wasReleased = _selector.IsReleased;
            var mods = Keyboard.Modifiers;
            var outcome = _selector.Key(key, mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Control));
            switch (outcome)
            {
                case SelectorOutcome.Cancel:
                    e.Handled = true;
                    Close(CaptureEnd.Cancelled);
                    return;
                case SelectorOutcome.Picked when _selector.Picked is { } picked:
                    e.Handled = true;
                    Picked?.Invoke(picked, Freeze?.PixelAt(picked) ?? 0);
                    return;
                case SelectorOutcome.PassToFeature:
                    KeyDown?.Invoke(e);
                    return;
            }
            if (key != SelectorKey.Other || !wasReleased)
                e.Handled = true;
            // Fine aim while choosing moves the real pointer with the selector's.
            if (!_selector.IsReleased && key is SelectorKey.Left or SelectorKey.Right or SelectorKey.Up or SelectorKey.Down)
                SetCursorPos(_selector.Pointer.X, _selector.Pointer.Y);
            if (_selector.Selection is { } s && !ReferenceEquals(s, before))
            {
                if (!wasReleased)
                    Released(s);
                else
                    Changed(s);
            }
            Render();
        }

        /// <summary>A lock, Ctrl+Alt+Del or a UAC prompt switched to another desktop: close, keeping nothing.</summary>
        public void OnLostForeground()
        {
            if (!InputDesktopIsOurs())
                Close(CaptureEnd.Interrupted, handBack: false);
        }

        public void Close(CaptureEnd end = CaptureEnd.Finished, bool handBack = true)
        {
            if (_closed)
                return;
            _closed = true;
            if (_countdown is { } countdown)
            {
                countdown.Cancel();
                layer._shell.Bar(null);
            }
            // Hand the keyboard on while the overlay still holds the foreground, then hide: hiding first lets Windows pick.
            if (handBack && back != 0 && IsWindow(back) && !IsIconic(back))
                SetForegroundWindow(back);
            foreach (var o in layer._overlays)
                o.HideEmpty();
            Freeze = null;
            _selector = null;
            Surfaces = [];
            if (ReferenceEquals(layer._current, this))
                layer._current = null;
            Closed?.Invoke(end);
        }
    }

    private static PixelPoint Cursor() => GetCursorPos(out var p) ? new PixelPoint(p.X, p.Y) : default;

    private static PixelSize DragSize() => new(GetSystemMetrics(SmCxDrag), GetSystemMetrics(SmCyDrag));

    /// <summary>False on the secure desktop (lock, Ctrl+Alt+Del, UAC), which Chippi can't open.</summary>
    private static bool InputDesktopIsOurs()
    {
        nint desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == 0)
            return false;
        CloseDesktop(desktop);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    private const int SmCxDrag = 68, SmCyDrag = 69;
    private const uint DesktopReadObjects = 0x0001;

    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out Point point);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetCursorPos(int x, int y);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint hwnd);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseDesktop(nint desktop);
    [LibraryImport("dwmapi.dll")] private static partial int DwmFlush();
}
