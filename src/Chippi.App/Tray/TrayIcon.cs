// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Interop;
using Chippi.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Tray;

/// <summary>Where a tray interaction happened, in physical screen pixels.</summary>
internal sealed class TrayEventArgs(int x, int y, bool keyboard) : EventArgs
{
    public int X { get; } = x;

    public int Y { get; } = y;

    /// <summary>Enter or Space on the focused icon, or the menu key, rather than the mouse.</summary>
    public bool Keyboard { get; } = keyboard;
}

/// <summary>
/// Chippi's notification-area icon, owned directly through <c>Shell_NotifyIcon</c> on a hidden
/// top-level window (not WinForms <c>NotifyIcon</c>), so the docked pages can anchor to it with
/// <c>Shell_NotifyIconGetRect</c>. Uses NOTIFYICON_VERSION_4 callbacks, re-adds itself when
/// Explorer restarts (<c>TaskbarCreated</c>) and reloads its image when the display DPI changes.
/// Create and use on the UI thread.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint NIN_KEYSELECT = PInvoke.NIN_SELECT | 0x1; // NINF_KEY; not in the Win32 metadata

    private readonly string _tooltip;
    private readonly HwndSource _window;
    private readonly uint _taskbarCreated;
    private HICON _icon;
    private int _iconSize;
    private long _lastKeySelect;
    private bool _visible = true;
    private bool _disposed;

    public TrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");

        // A hidden top-level window rather than a message-only one: TaskbarCreated and the
        // display-change broadcasts only reach top-level windows. Tool window, never shown.
        _window = new HwndSource(new HwndSourceParameters("Chippi tray")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);

        LoadIcon();
        Add();
    }

    /// <summary>Left click, or Enter/Space on the focused icon.</summary>
    public event EventHandler<TrayEventArgs>? Selected;

    /// <summary>Right click, Shift+F10 or the menu key on the icon.</summary>
    public event EventHandler<TrayEventArgs>? ContextMenuRequested;

    public HWND Handle => new(_window.Handle);

    /// <summary>Whether the icon is in the notification area. Hiding removes it; showing adds it back.</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (value == _visible || _disposed)
                return;
            _visible = value;
            if (value)
            {
                Add();
                return;
            }
            var data = NewData();
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        }
    }

    /// <summary>
    /// The icon's rectangle in physical screen pixels, or null when the shell cannot say (for
    /// example while the icon sits in the closed overflow area or Explorer is restarting).
    /// </summary>
    public RECT? GetRect()
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = Handle,
            uID = IconId,
        };
        var hr = PInvoke.Shell_NotifyIconGetRect(in id, out var rect);
        return hr.Succeeded && rect.right > rect.left && rect.bottom > rect.top ? rect : null;
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = Handle,
        uID = IconId,
    };

    private void Add()
    {
        var data = NewData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON
            | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = _tooltip;

        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
        {
            // Already there (a re-add raced Explorer) or the shell is not up yet; TaskbarCreated
            // will call us again once it is.
            if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data))
            {
                Log.Warn("Tray: Shell_NotifyIcon(NIM_ADD) failed; waiting for TaskbarCreated");
                return;
            }
        }

        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data))
            Log.Warn("Tray: NIM_SETVERSION 4 was refused");
    }

    private void LoadIcon()
    {
        // The notification area lives on the primary monitor's taskbar; size the image for its DPI
        // (16 px at 100%, 24 at 150%, 32 at 200%) so the shell never rescales it.
        var primary = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        int size = MonitorDpi.SmallIconSize(MonitorDpi.Of(primary));
        if (size == _iconSize && !_icon.IsNull)
            return;

        var old = _icon;
        _icon = TrayIconImage.Create(size);
        _iconSize = size;
        if (!old.IsNull)
            PInvoke.DestroyIcon(old);
    }

    private void RefreshIcon()
    {
        int before = _iconSize;
        LoadIcon();
        if (_iconSize == before)
            return;
        var data = NewData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_ICON;
        data.hIcon = _icon;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            // Version 4: LOWORD(lParam) is the event, wParam holds the anchor x/y in screen pixels.
            uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
            int x = unchecked((short)(wParam.ToInt64() & 0xFFFF));
            int y = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
            switch (evt)
            {
                case PInvoke.NIN_SELECT:
                    Selected?.Invoke(this, new TrayEventArgs(x, y, keyboard: false));
                    break;
                case NIN_KEYSELECT:
                    // Enter can arrive as two NIN_KEYSELECTs; one press is one toggle.
                    long now = Environment.TickCount64;
                    if (now - _lastKeySelect > 300)
                        Selected?.Invoke(this, new TrayEventArgs(x, y, keyboard: true));
                    _lastKeySelect = now;
                    break;
                case PInvoke.WM_CONTEXTMENU:
                    ContextMenuRequested?.Invoke(this, new TrayEventArgs(x, y, keyboard: false));
                    break;
            }
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted (or the taskbar DPI changed): the shell forgot every icon.
            Log.Info("Tray: TaskbarCreated, re-adding the icon");
            _iconSize = 0;
            LoadIcon();
            if (_visible)
                Add();
        }
        else if (msg is (int)PInvoke.WM_DISPLAYCHANGE or (int)PInvoke.WM_SETTINGCHANGE or (int)PInvoke.WM_DPICHANGED)
        {
            RefreshIcon();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var data = NewData();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        _window.RemoveHook(WndProc);
        _window.Dispose();
        if (!_icon.IsNull)
            PInvoke.DestroyIcon(_icon);
        _icon = default;
    }
}
