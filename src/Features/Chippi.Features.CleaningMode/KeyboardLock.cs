// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.Features.CleaningMode;

/// <summary>A key the lock took: its virtual-key code, going down or coming up.</summary>
public readonly record struct LockedKey(uint Vk, bool Down);

/// <summary>Takes the whole keyboard. The real one is a low-level hook; tests use a fake, so no test locks the owner's keyboard.</summary>
public interface IKeyboardLock
{
    /// <summary>Takes every key until the handle is disposed; <paramref name="key"/> hears each one on the UI thread.</summary>
    IDisposable Lock(Action<LockedKey> key);
}

/// <summary>
/// A low-level keyboard hook on the UI thread: every key goes down and up here and nowhere else, the Windows key and the
/// media and volume keys included. Ctrl+Alt+Del isn't a key Windows lets anyone take.
/// </summary>
internal sealed class KeyboardLock : IKeyboardLock
{
    public IDisposable Lock(Action<LockedKey> key) => new Hook(key);

    private sealed class Hook : IDisposable
    {
        private readonly HOOKPROC _proc; // kept alive while hooked
        private readonly UnhookWindowsHookExSafeHandle _hook;
        private readonly KeyFilter _filter = new();
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly Action<LockedKey> _key;

        public Hook(Action<LockedKey> key)
        {
            _key = key;
            _proc = OnKey;
            _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _proc, PInvoke.GetModuleHandle((string?)null), 0);
            if (_hook.IsInvalid)
                throw new Win32Exception();
        }

        private unsafe LRESULT OnKey(int code, WPARAM wParam, LPARAM lParam)
        {
            if (code >= 0)
            {
                uint message = (uint)wParam.Value;
                bool down = message is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
                bool up = message is PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP;
                uint vk = ((KBDLLHOOKSTRUCT*)lParam.Value)->vkCode;
                if ((down || up) && _filter.Swallow(vk, down))
                {
                    // Heard after the hook returns: Windows drops a hook that takes too long.
                    _dispatcher.BeginInvoke(_key, new LockedKey(vk, down));
                    return new LRESULT(1);
                }
            }
            return PInvoke.CallNextHookEx(_hook, code, wParam, lParam);
        }

        public void Dispose() => _hook.Dispose(); // unhooks
    }
}
