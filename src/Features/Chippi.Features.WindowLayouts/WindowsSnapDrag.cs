// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Chippi.Core.Actions;
using Chippi.Core.Input;

namespace Chippi.Features.WindowLayouts;

/// <summary>WinEvents observe native title-bar drags. No keyboard or mouse hook is installed by Snap.</summary>
public sealed class WindowsSnapDrag : IDisposable
{
    private readonly WindowLayoutsModule _module;
    private readonly IInputHub? _input;
    private readonly Func<nint, ActionWindow?> _capture;
    private readonly SnapDragController _drag;
    private readonly WinEvent _callback;
    private readonly DispatcherTimer _timer;
    private nint _hook, _desktopHook;
    private bool _disposed;
    private nint _window;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    public event Action<string>? Failed;

    public WindowsSnapDrag(WindowLayoutsModule module, IInputHub? input, Func<nint, ActionWindow?> capture, SnapDragController drag)
    {
        _module = module;
        _input = input;
        _capture = capture;
        _drag = drag;
        _callback = OnEvent;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, Tick, Dispatcher.CurrentDispatcher);
        _timer.Stop();
        _hook = SetWinEventHook(0xA, 0xB, 0, _callback, 0, 0, 0); // EVENT_SYSTEM_MOVESIZESTART/END
        _desktopHook = SetWinEventHook(0x20, 0x20, 0, _callback, 0, 0, 0);
        if (_hook == 0 || _desktopHook == 0)
        {
            Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        _module.Changed += SettingsChanged;
        if (_input is not null) _input.PauseChanged += PauseChanged;
    }

    private void SettingsChanged() { _drag.Cancel(); _timer.Stop(); }
    private void PauseChanged() => _ui.BeginInvoke(() =>
    {
        if (!_disposed && _input?.Paused == true) SettingsChanged();
    });

    private async void OnEvent(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (_disposed) return;
        try
        {
            if (kind == 0x20) { SettingsChanged(); return; }
            if (kind == 0xA)
            {
                if (_module.Layouts?.Settings.ShiftDragEnabled != true || _input?.Paused == true
                    || !Down(0x10) || !Down(1) || !Caption(window)) return;
                if (_capture(window) is not { Elevated: false } target) return;
                _window = window;
                _drag.Begin(target);
                Tick(this, EventArgs.Empty);
                if (_drag.Active) _timer.Start();
            }
            else if (kind == 0xB && window == _window)
            {
                _timer.Stop();
                Tick(this, EventArgs.Empty);
                await _drag.EndAsync(Down(0x10), Down(0x1B), _input?.Paused == true);
            }
        }
        catch (ActionFailure)
        {
            _drag.Cancel();
            _timer.Stop();
            Failed?.Invoke("Snap failed");
        }
    }

    private void Tick(object? sender, EventArgs args)
    {
        if (!IsWindow(_window) || GetForegroundWindow() != _window) { SettingsChanged(); return; }
        if (GetCursorPos(out var p)) _drag.Move(p.X, p.Y, Down(0x10), Down(0x1B), _input?.Paused == true);
        else _drag.Cancel();
        if (!_drag.Active) _timer.Stop();
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    private static bool Caption(nint window)
    {
        if (!GetCursorPos(out var p)) return false;
        nint packed = unchecked((nint)((p.X & 0xFFFF) | ((p.Y & 0xFFFF) << 16)));
        return SendMessageTimeout(window, 0x84, 0, packed, 2, 50, out nuint result) != 0 && result == 2; // HTCAPTION
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _module.Changed -= SettingsChanged;
        if (_input is not null) _input.PauseChanged -= PauseChanged;
        if (_hook != 0) UnhookWinEvent(_hook);
        if (_desktopHook != 0) UnhookWinEvent(_desktopHook);
        _hook = _desktopHook = 0;
        _drag.Cancel();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate void WinEvent(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}


