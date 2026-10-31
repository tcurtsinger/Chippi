// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Chippi.Features.Sound.Mute;

[Flags]
public enum ShortcutModifiers : uint { Alt = 1, Control = 2, Shift = 4, Windows = 8 }

public sealed record MicShortcut(ShortcutModifiers Modifiers = ShortcutModifiers.Control | ShortcutModifiers.Shift, uint Key = 0x4D)
{
    // Require Ctrl or Alt so Shift-only shortcuts cannot consume normal typing. Windows combinations and F12 stay reserved.
    public bool IsValid => (Modifiers & ShortcutModifiers.Windows) == 0
        && (Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt)) != 0 && ((uint)Modifiers & ~7u) == 0
        && (Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A);

    public override string ToString() => string.Join("+", new[]
    {
        Modifiers.HasFlag(ShortcutModifiers.Control) ? "Ctrl" : null,
        Modifiers.HasFlag(ShortcutModifiers.Alt) ? "Alt" : null,
        Modifiers.HasFlag(ShortcutModifiers.Shift) ? "Shift" : null,
        Key is >= 0x30 and <= 0x5A ? ((char)Key).ToString() : KeyInterop.KeyFromVirtualKey((int)Key).ToString(), // "1", not "D1"
    }.Where(x => x is not null));

    /// <summary>The shortcut a key press makes while one is being recorded, or null while only modifiers are down.</summary>
    public static MicShortcut? From(KeyEventArgs e)
    {
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)
            return null;
        var modifiers = Keyboard.Modifiers;
        var mapped = (modifiers.HasFlag(ModifierKeys.Control) ? ShortcutModifiers.Control : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? ShortcutModifiers.Shift : 0)
            | (modifiers.HasFlag(ModifierKeys.Alt) ? ShortcutModifiers.Alt : 0)
            | (modifiers.HasFlag(ModifierKeys.Windows) ? ShortcutModifiers.Windows : 0);
        return new(mapped, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
}

public interface IMicHotkey : IDisposable
{
    bool Register(MicShortcut shortcut, Action pressed);
}

/// <summary>A message-only window, not a keyboard hook. Constructed and disposed on the UI thread.</summary>
public sealed class GlobalMicHotkey : IMicHotkey
{
    private readonly HwndSource _window = new(new HwndSourceParameters("Chippi mic shortcut") { ParentWindow = new IntPtr(-3) });
    private Action? _pressed;
    private int _id;
    private MicShortcut? _shortcut;

    public GlobalMicHotkey() => _window.AddHook(Message);

    public static IMicHotkey Create()
    {
        var dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("Shortcut dispatcher unavailable");
        return dispatcher.CheckAccess() ? new GlobalMicHotkey() : dispatcher.Invoke(() => new GlobalMicHotkey());
    }

    public bool Register(MicShortcut shortcut, Action pressed)
    {
        if (!_window.Dispatcher.CheckAccess()) return _window.Dispatcher.Invoke(() => Register(shortcut, pressed));
        if (!shortcut.IsValid) return false;
        if (_id != 0 && _shortcut == shortcut) { _pressed = pressed; return true; }
        int next = _id == 1 ? 2 : 1;
        if (!RegisterHotKey(_window.Handle, next, (uint)shortcut.Modifiers | 0x4000, shortcut.Key)) return false;
        if (_id != 0) UnregisterHotKey(_window.Handle, _id);
        _id = next;
        _shortcut = shortcut;
        _pressed = pressed;
        return true;
    }

    private IntPtr Message(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == _id) { handled = true; _pressed?.Invoke(); }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_window.Dispatcher.CheckAccess()) { _window.Dispatcher.Invoke(Dispose); return; }
        if (_id != 0) { UnregisterHotKey(_window.Handle, _id); _id = 0; }
        _pressed = null;
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
