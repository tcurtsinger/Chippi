// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Features.AppSwitcher.Windows;

/// <summary>
/// Windows' foreground and window-closed events, out of context (no DLL in other apps, no keyboard or mouse hook).
/// Create it on the UI thread: Windows delivers the events through that thread's messages. The closed event is
/// hooked only while something listens, as every window in the session raises it.
/// </summary>
public sealed partial class WindowsWindowEvents : IWindowEvents
{
    private const uint EventSystemForeground = 0x0003, EventObjectDestroy = 0x8001, OutOfContext = 0x0000;
    private const int ObjidWindow = 0;

    private readonly WinEventProc _proc;
    private readonly nint _foregroundHook;
    private nint _destroyHook;
    private Action<nint>? _destroyed;

    public WindowsWindowEvents()
    {
        _proc = OnEvent;
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _proc, 0, 0, OutOfContext);
    }

    public event Action<nint>? Foreground;

    public event Action<nint>? Destroyed
    {
        add
        {
            _destroyed += value;
            if (_destroyHook == 0 && _destroyed is not null)
                _destroyHook = SetWinEventHook(EventObjectDestroy, EventObjectDestroy, 0, _proc, 0, 0, OutOfContext);
        }
        remove
        {
            _destroyed -= value;
            if (_destroyed is null && _destroyHook != 0)
            {
                UnhookWinEvent(_destroyHook);
                _destroyHook = 0;
            }
        }
    }

    public void Dispose()
    {
        if (_foregroundHook != 0)
            UnhookWinEvent(_foregroundHook);
        if (_destroyHook != 0)
            UnhookWinEvent(_destroyHook);
        _destroyHook = 0;
        _destroyed = null;
    }

    private void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == 0 || idObject != ObjidWindow || idChild != 0)
            return;
        if (ev == EventSystemForeground)
            Foreground?.Invoke(hwnd);
        else if (ev == EventObjectDestroy)
            _destroyed?.Invoke(hwnd);
    }

    private delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
}
