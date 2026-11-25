// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Chippi.UI.Actions;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>
/// The keyboard, held for a menu or question of the menu bar or the dock while it's open. Neither surface ever takes the
/// keyboard, and a menu in a process that hasn't it can't close on Esc or on a click elsewhere, nor be worked with the
/// arrow keys. This one clear pixel takes the foreground before the menu opens, as an app's own window holds it for the
/// app's menus, so the menu behaves as any menu does: it closes when another app comes forward, and the app that was in
/// front gets the keyboard back when it closes. Never in Alt+Tab or the taskbar.
/// </summary>
internal sealed class MenuFocus : Window
{
    private static MenuFocus? _shared;

    private readonly uint _self = (uint)Environment.ProcessId;
    private readonly FocusLease _lease = new();
    private nint _hwnd, _before;
    private Action? _lost;

    /// <summary>The one holder the menu bar and the dock share, made on first use on the UI thread.</summary>
    public static MenuFocus Shared => _shared ??= new MenuFocus();

    /// <summary>Closes the shared holder (the mode went off); the next menu makes another.</summary>
    public static void CloseShared()
    {
        _shared?.Close();
        _shared = null;
    }

    private MenuFocus()
    {
        Title = "Chippi menu";
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
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_shared, this))
                _shared = null;
        };
    }

    private void Lose()
    {
        var lost = _lost;
        _lost = null;
        lost?.Invoke();
    }

    /// <summary>
    /// Takes the keyboard for <paramref name="holder"/> at <paramref name="at"/> (physical pixels). <paramref name="lost"/>
    /// runs once if another app takes it, or on Esc. False when Windows refused the foreground: the menu still opens, and
    /// works with the mouse.
    /// </summary>
    public bool Hold(object holder, Point at, Action lost)
    {
        _lease.Take(holder);
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
    /// <paramref name="holder"/> lets go, if the keyboard is still its own: a menu reports that it closed only once it has
    /// faded out, by when the question its item opened may hold the keyboard. The app that was in front gets the keyboard
    /// back while this window still has it (Windows picks another window otherwise); not when anything else has come
    /// forward since, another app or one of Chippi's own windows (Settings, opened from a menu), and not a window that has
    /// gone or been minimized meanwhile.
    /// </summary>
    public void Release(object holder)
    {
        if (!_lease.Leave(holder))
            return;
        _lost = null;
        if (!IsVisible)
            return;
        nint before = _before;
        _before = 0;
        nint now = Native.GetForegroundWindow();
        if (before != 0 && before != _hwnd && Native.IsWindow(before) && Native.IsWindowVisible(before) && !Native.IsIconic(before)
            && (now == 0 || now == _hwnd))
            ForegroundRight.Take(before);
        Hide();
    }
}

/// <summary>
/// Whose the held keyboard is. The last to take it holds it, and only its holder can let it go: one that was replaced
/// (a menu still fading out behind the question it opened) lets go of nothing.
/// </summary>
internal sealed class FocusLease
{
    private object? _holder;

    public bool Held => _holder is not null;

    public void Take(object holder) => _holder = holder;

    /// <summary>True when <paramref name="holder"/> held it, and it's now free.</summary>
    public bool Leave(object holder)
    {
        if (!ReferenceEquals(_holder, holder))
            return false;
        _holder = null;
        return true;
    }
}
