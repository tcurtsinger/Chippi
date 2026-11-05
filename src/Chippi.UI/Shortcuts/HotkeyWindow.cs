// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Chippi.Core.Shortcuts;

namespace Chippi.UI.Shortcuts;

/// <summary>
/// <c>RegisterHotKey</c> on one message-only window, not a keyboard hook, so shortcuts work over admin windows. Made on
/// the UI thread, where its presses arrive; the window opens with the first shortcut.
/// </summary>
public sealed class HotkeyWindow(Dispatcher dispatcher) : IHotkeyRegistrar, IDisposable
{
    private HwndSource? _window;

    public event Action<int>? Pressed;

    public bool Register(int id, KeyShortcut shortcut)
    {
        if (!dispatcher.CheckAccess()) return dispatcher.Invoke(() => Register(id, shortcut));
        if (_window is null)
        {
            _window = new HwndSource(new HwndSourceParameters("Chippi shortcuts") { ParentWindow = new IntPtr(-3) });
            _window.AddHook(Message);
        }
        return RegisterHotKey(_window.Handle, id, (uint)shortcut.Modifiers | 0x4000, shortcut.Key); // MOD_NOREPEAT
    }

    public void Unregister(int id)
    {
        if (!dispatcher.CheckAccess()) { dispatcher.Invoke(() => Unregister(id)); return; }
        if (_window is not null) UnregisterHotKey(_window.Handle, id);
    }

    private IntPtr Message(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312) // WM_HOTKEY
        {
            handled = true;
            Pressed?.Invoke(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!dispatcher.CheckAccess()) { dispatcher.Invoke(Dispose); return; }
        _window?.Dispose(); // Windows frees a destroyed window's hotkeys
        _window = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

/// <summary>Reading a shortcut from a key press while one is being recorded.</summary>
public static class ShortcutKeys
{
    /// <summary>The shortcut a key press makes, or null while only modifiers are down.</summary>
    public static KeyShortcut? From(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return null;
        var modifiers = Keyboard.Modifiers;
        var mapped = (modifiers.HasFlag(ModifierKeys.Control) ? ShortcutModifiers.Control : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? ShortcutModifiers.Shift : 0)
            | (modifiers.HasFlag(ModifierKeys.Alt) ? ShortcutModifiers.Alt : 0)
            | (modifiers.HasFlag(ModifierKeys.Windows) ? ShortcutModifiers.Windows : 0);
        return new(mapped, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
}
