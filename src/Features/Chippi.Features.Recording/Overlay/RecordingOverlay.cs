// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Chippi.Capture;

namespace Chippi.Features.Recording.Overlay;

/// <summary>
/// The red frame and the pill while a recording runs (screen-recording.md, Frame and pill): two top-level windows,
/// topmost, out of Alt+Tab and the taskbar, never activated, and left out of every capture
/// (<c>WDA_EXCLUDEFROMCAPTURE</c>), so neither is in the video. The frame lets every click through; the pill takes
/// clicks without taking the keyboard. Record window's frame and pill follow the window as it moves.
/// </summary>
internal sealed partial class RecordingOverlay : IRecordingOverlay
{
    /// <summary>The frame's width in DIPs (2 px at 100%).</summary>
    public const double FrameWidth = 2;

    /// <summary>Between the frame and the pill, in DIPs.</summary>
    public const double PillGap = 8;

    private readonly RecordingTarget _target;
    private readonly Window _frame;
    private readonly Window? _pill;
    private readonly WinEventProc? _follow;
    private nint _hook;
    private PixelRect _recorded, _display;
    private int _dpi;

    public RecordingOverlay(RecordingTarget target, FrameworkElement? pillFace)
    {
        _target = target;
        _recorded = target.Rect;
        _display = target.Display;
        _dpi = target.Dpi;
        var border = new Border { BorderThickness = new Thickness(FrameWidth) };
        border.SetResourceReference(Border.BorderBrushProperty, "DangerBrush");
        if (border.BorderBrush is null) border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
        _frame = Create("Recording frame", border, clickThrough: true);
        if (pillFace is not null)
        {
            _pill = Create("Recording", pillFace, clickThrough: false);
            _pill.SizeToContent = SizeToContent.WidthAndHeight;
            _pill.SizeChanged += (_, _) => Place();
        }
        Place();
        _frame.Show();
        _pill?.Show();
        Place();
        if (target.Window != 0)
        {
            _follow = OnLocationChanged;
            uint thread = GetWindowThreadProcessId(target.Window, out uint process);
            _hook = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, 0, _follow, process, thread, WinEventOutOfContext);
        }
    }

    internal nint FrameHandle => new WindowInteropHelper(_frame).Handle;

    internal nint PillHandle => _pill is null ? 0 : new WindowInteropHelper(_pill).Handle;

    private static Window Create(string title, FrameworkElement content, bool clickThrough)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Focusable = false,
            Title = title,
            Content = content,
            Left = -32000,
            Top = -32000,
            Width = 1,
            Height = 1,
        };
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            nint style = GetWindowLongPtr(hwnd, GwlExStyle) | WsExToolWindow | WsExNoActivate | (clickThrough ? WsExTransparent | WsExLayered : 0);
            SetWindowLongPtr(hwnd, GwlExStyle, style);
            // Out of the video, and out of Travis's screen shares and stills too.
            SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
        };
        return window;
    }

    private int Px(double dips) => (int)Math.Round(dips * _dpi / 96.0);

    private void Place()
    {
        var helper = new WindowInteropHelper(_frame);
        if (helper.Handle == 0) helper.EnsureHandle();
        var frame = OverlayPlacement.Frame(_recorded, _display, Px(FrameWidth));
        SetWindowPos(helper.Handle, HwndTopmost, frame.X, frame.Y, frame.Width, frame.Height, SwpNoActivate);
        if (_pill is null) return;
        var pill = new WindowInteropHelper(_pill);
        if (pill.Handle == 0) pill.EnsureHandle();
        if (!GetWindowRect(pill.Handle, out var r)) return;
        var size = new PixelSize(r.Right - r.Left, r.Bottom - r.Top);
        if (size.Width <= 1 && _pill.Content is FrameworkElement face)
        {
            face.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            size = new PixelSize(Px(face.DesiredSize.Width), Px(face.DesiredSize.Height));
        }
        var at = OverlayPlacement.Pill(_recorded, _display, size, Px(FrameWidth + PillGap));
        SetWindowPos(pill.Handle, HwndTopmost, at.X, at.Y, 0, 0, SwpNoActivate | SwpNoSize);
    }

    // Record window: the frame and pill follow the window, on whichever display it's now on.
    private void OnLocationChanged(nint hook, uint e, nint hwnd, int obj, int child, uint thread, uint time)
    {
        if (hwnd != _target.Window || obj != 0) return;
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var r, Marshal.SizeOf<Rect>()) != 0) return;
        var rect = PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
        if (rect.IsEmpty || IsIconic(hwnd)) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (GetMonitorInfo(monitor, ref info))
        {
            _display = PixelRect.FromEdges(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
            if (GetDpiForMonitor(monitor, 0, out uint dx, out _) == 0) _dpi = (int)dx;
        }
        _recorded = rect.Intersect(_display);
        if (!_recorded.IsEmpty) Place();
    }

    public void Dispose()
    {
        if (_hook != 0) UnhookWinEvent(_hook);
        _hook = 0;
        _frame.Close();
        _pill?.Close();
        GC.KeepAlive(_follow);
    }

    private delegate void WinEventProc(nint hook, uint e, nint hwnd, int obj, int child, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExLayered = 0x80000, WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x10, SwpNoSize = 0x1, WdaExcludeFromCapture = 0x11;
    private const uint EventObjectLocationChange = 0x800B, WinEventOutOfContext = 0;
    private const uint MonitorDefaultToNearest = 2;
    private const int DwmwaExtendedFrameBounds = 9;
    private static readonly nint HwndTopmost = -1;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRect(nint hwnd, out Rect rect);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [LibraryImport("shcore.dll")] private static partial int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint process, uint thread, uint flags);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWinEvent(nint hook);
}
