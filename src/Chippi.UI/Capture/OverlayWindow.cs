// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Chippi.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.UI.Capture;

/// <summary>
/// One display's overlay window (capture-layer.md, The freeze): topmost, covering the whole display, taskbar included, at
/// that display's DPI. Made once and kept hidden; a capture shows it with nothing drawn (a layered window at alpha 0, so
/// the screen read sees through it), draws the frozen image, then turns it opaque. Not in Alt+Tab or the taskbar.
/// </summary>
internal sealed partial class OverlayWindow : Window
{
    private bool _disposed;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Chippi capture";
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;
        Content = View;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(Handle, GwlExStyle, GetWindowLongPtr(Handle, GwlExStyle) | WsExToolWindow | WsExLayered);
            SetAlpha(0);
        };
    }

    public OverlayView View { get; } = new();

    public nint Handle { get; private set; }

    public PixelRect Bounds => View.Bounds;

    public int Dpi { get; private set; }

    /// <summary>Raised for the overlay's own pointer and keys; the layer reads the pointer in physical pixels itself.</summary>
    public event Action<OverlayWindow, MouseButtonEventArgs>? ButtonDown, ButtonUp;
    public event Action<OverlayWindow>? PointerMoved;
    public event Action<OverlayWindow, KeyEventArgs>? Key;

    /// <summary>Alt+F4, or anything else asking the window to close: the layer cancels the capture.</summary>
    public event Action<OverlayWindow>? CloseAsked;

    /// <summary>The window lost the foreground (a lock, Ctrl+Alt+Del, a UAC prompt, another app).</summary>
    public event Action<OverlayWindow>? LostForeground;

    /// <summary>Covers a display in its own pixels: WPF's position is in DIPs of whichever display the window was last on.</summary>
    public void Place(PixelRect bounds, int dpi)
    {
        Dpi = dpi;
        View.Place(bounds, dpi);
        if (Handle == 0)
            new WindowInteropHelper(this).EnsureHandle();
        // Twice: moving onto a display at another scale makes WPF resize for it (WM_DPICHANGED) after the first.
        for (int i = 0; i < 2; i++)
            SetWindowPos(Handle, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoActivate);
    }

    /// <summary>On screen with nothing drawn: a layered window at alpha 0 adds nothing to a screen read.</summary>
    public void ShowEmpty()
    {
        SetAlpha(0);
        if (!IsVisible)
            Show();
        SetWindowPos(Handle, HwndTopmost, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, SwpNoActivate);
    }

    /// <summary>The frozen image has rendered: show it.</summary>
    public void Reveal() => SetAlpha(255);

    /// <summary>Hidden again, its image and the feature's drawing let go.</summary>
    public void HideEmpty()
    {
        SetAlpha(0);
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Hide();
        View.SetImage(null);
        View.SetBars(null, null);
        View.Layer.Children.Clear();
    }

    /// <summary>Closes it for good (the last capture feature stopped, or the displays changed).</summary>
    public void Dispose()
    {
        _disposed = true;
        Close();
    }

    private void SetAlpha(byte alpha)
    {
        if (Handle != 0)
            SetLayeredWindowAttributes(Handle, 0, alpha, LwaAlpha);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        ButtonDown?.Invoke(this, e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        ButtonUp?.Invoke(this, e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointerMoved?.Invoke(this);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
            Key?.Invoke(this, e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsVisible)
            LostForeground?.Invoke(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_disposed)
            return;
        e.Cancel = true;
        CloseAsked?.Invoke(this);
    }

    private const int GwlExStyle = -20;
    private const nint WsExToolWindow = 0x80, WsExLayered = 0x80000;
    private const uint LwaAlpha = 0x2, SwpNoActivate = 0x0010;
    private static readonly nint HwndTopmost = -1;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
