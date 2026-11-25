// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Actions;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>
/// The keyboard, held for a dock menu or question while it's open. The dock never takes the keyboard, and a menu in a
/// process that hasn't it can't close on Esc or on a click elsewhere, nor be worked with the arrow keys. This one clear
/// pixel takes the foreground before the menu opens, as an app's own window holds it for the app's menus, so the menu
/// behaves as any menu does: it closes when another app comes forward, and the app that was in front gets the keyboard
/// back when it closes. Never in Alt+Tab or the taskbar.
/// </summary>
internal sealed class DockFocus : Window
{
    private readonly uint _self = (uint)Environment.ProcessId;
    private nint _hwnd, _before;
    private Action? _lost;

    public DockFocus()
    {
        Title = "Chippi dock menu";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = Height = 1;
        Left = Top = 0;
        Focusable = true;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(_hwnd, Native.GwlExStyle);
            Native.SetWindowLongPtr(_hwnd, Native.GwlExStyle, (nint)((ex | Native.WsExToolWindow) & ~Native.WsExAppWindow));
        };
        Deactivated += (_, _) =>
        {
            // The menu's own window is Chippi's too; only another app coming forward ends it.
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint owner);
            if (owner != _self)
                Lose();
        };
        // Esc reaches here when the menu couldn't take the keyboard itself.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            Lose();
        };
    }

    private void Lose()
    {
        var lost = _lost;
        _lost = null;
        lost?.Invoke();
    }

    /// <summary>
    /// Takes the keyboard at <paramref name="at"/> (physical pixels). <paramref name="lost"/> runs once if another app
    /// takes it, or on Esc. False when Windows refused the foreground: the menu still opens, and works with the mouse.
    /// </summary>
    public bool Hold(Point at, Action lost)
    {
        nint front = Native.GetForegroundWindow();
        if (front != _hwnd || _before == 0)
            _before = front;
        _lost = lost;
        if (!IsVisible)
            Show();
        if (_hwnd == 0)
            return false;
        Native.SetWindowPos(_hwnd, 0, (int)at.X, (int)at.Y, 1, 1, Native.SwpNoActivate | Native.SwpNoZOrder | Native.SwpNoOwnerZOrder);
        bool held = ForegroundRight.Take(_hwnd);
        if (held)
            Keyboard.Focus(this);
        return held;
    }

    /// <summary>
    /// Lets go. The app that was in front gets the keyboard back while this still holds it (Windows picks another window
    /// otherwise), unless another app has come forward since.
    /// </summary>
    public void Release()
    {
        _lost = null;
        if (!IsVisible)
            return;
        nint before = _before;
        _before = 0;
        nint now = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(now, out uint owner);
        if (before != 0 && before != _hwnd && Native.IsWindow(before) && (now == 0 || owner == _self))
            ForegroundRight.Take(before);
        Hide();
    }
}
