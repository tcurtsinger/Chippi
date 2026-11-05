// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Chippi.Features.AppSwitcher.Views;

/// <summary>
/// The overlay's plumbing: a topmost window out of Alt+Tab that shows without activating, sends keys to the switcher
/// and closes when it loses the foreground. Its look is a plain list until Lumen's design (#257) replaces this
/// content; the keys and the foreground rules here stay.
/// </summary>
public sealed partial class SwitcherOverlay : Window, ISwitcherView
{
    private readonly Switcher _switcher;
    private readonly TextBox _search = new() { Margin = new Thickness(8) };
    private readonly ListBox _list = new() { Margin = new Thickness(8, 0, 8, 8), Focusable = false };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 0, 8, 8) };
    private SwitcherSession? _session;
    private nint _hwnd;

    public SwitcherOverlay(Switcher switcher)
    {
        _switcher = switcher;
        Title = "App switcher";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 640;
        Height = 420;
        var panel = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        panel.Children.Add(_search);
        panel.Children.Add(_status);
        panel.Children.Add(_list);
        Content = panel;
        _search.TextChanged += (_, _) => _session?.Type(_search.Text);
        _list.MouseLeftButtonUp += (_, _) =>
        {
            if (_session is null || _list.SelectedIndex < 0)
                return;
            _session.Select(_list.SelectedIndex);
            _switcher.Commit();
        };
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => _switcher.Dismiss();
    }

    public nint Show(SwitcherSession session, nint monitor)
    {
        if (_session is not null)
            _session.Changed -= Draw;
        _session = session;
        session.Changed += Draw;
        _status.Text = "";
        _search.Text = "";
        Draw();
        if (_hwnd == 0)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            SetWindowLong(_hwnd, GwlExStyle, GetWindowLong(_hwnd, GwlExStyle) | WsExToolWindow);
        }
        Center(monitor);
        Show();
        _search.Focus();
        return _hwnd;
    }

    public void ShowFailed(SwitcherWindow window) => _status.Text = AppSwitcherModule.SwitchFailed;

    void ISwitcherView.Hide()
    {
        if (_session is not null)
            _session.Changed -= Draw;
        _session = null;
        Hide();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_session is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                _switcher.Commit();
                break;
            case Key.Escape:
                _switcher.Cancel();
                break;
            case Key.Down or Key.Right:
                _session.Move(1);
                break;
            case Key.Up or Key.Left:
                _session.Move(-1);
                break;
            case Key.Tab:
                _session.Move(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                break;
            default:
                return; // letters go to the search box
        }
        e.Handled = true;
    }

    private void Draw()
    {
        if (_session is null)
            return;
        _list.ItemsSource = _session.Shown.Select(w => $"{w.App}  {w.Title}").ToList();
        _list.SelectedIndex = _session.Selected;
        if (_session.Selected >= 0)
            _list.ScrollIntoView(_list.SelectedItem);
    }

    // Centered in the display's work area, in physical pixels, before it shows.
    private void Center(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            return;
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        int width = (int)(Width * scale), height = (int)(Height * scale);
        int left = info.Work.Left + (info.Work.Right - info.Work.Left - width) / 2;
        int top = info.Work.Top + (info.Work.Bottom - info.Work.Top - height) / 2;
        SetWindowPos(_hwnd, 0, left, top, width, height, SwpNoZOrder | SwpNoActivate);
    }

    private const int GwlExStyle = -20, WsExToolWindow = 0x80;
    private const uint SwpNoZOrder = 0x4, SwpNoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")] private static partial int GetWindowLong(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")] private static partial int SetWindowLong(nint hwnd, int index, int value);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
