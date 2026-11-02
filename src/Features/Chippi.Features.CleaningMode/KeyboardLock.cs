// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.Features.CleaningMode;

/// <summary>A key the lock took: its virtual-key code, going down or coming up, and when (Windows' millisecond tick).</summary>
public readonly record struct LockedKey(uint Vk, bool Down, uint Time);

/// <summary>Takes the whole keyboard. The real one is a low-level hook; tests use a fake, so no test locks the owner's keyboard.</summary>
public interface IKeyboardLock
{
    /// <summary>Takes every key until the handle is disposed; <paramref name="key"/> hears each one on the calling (UI) thread.</summary>
    IDisposable Lock(Action<LockedKey> key);
}

/// <summary>
/// A low-level keyboard hook: every key goes down and up here and nowhere else, the Windows key and the media and volume
/// keys included. Ctrl+Alt+Del isn't a key Windows lets anyone take. The hook has a thread of its own that does nothing
/// else, so it always answers in time: Windows passes a key on, and may drop the hook unannounced, when the thread that
/// owns it is busy (the UI thread with Chippi's animations, say).
/// </summary>
internal sealed class KeyboardLock : IKeyboardLock
{
    public IDisposable Lock(Action<LockedKey> key) => new Hook(key);

    private sealed class Hook : IDisposable
    {
        private readonly HOOKPROC _proc; // kept alive while hooked
        private readonly KeyFilter _filter = new();
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly Action<LockedKey> _key;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private UnhookWindowsHookExSafeHandle? _hook;
        private uint _threadId;
        private Exception? _failed;

        public Hook(Action<LockedKey> key)
        {
            _key = key;
            _proc = OnKey;
            _thread = new Thread(Run) { IsBackground = true, Name = "Cleaning mode keyboard", Priority = ThreadPriority.Highest };
            _thread.Start();
            _ready.Wait();
            _ready.Dispose();
            if (_failed is not null)
                throw _failed;
        }

        private void Run()
        {
            _threadId = PInvoke.GetCurrentThreadId();
            _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _proc, PInvoke.GetModuleHandle((string?)null), 0);
            if (_hook.IsInvalid)
            {
                _failed = new Win32Exception();
                _ready.Set();
                return;
            }
            _ready.Set();
            // Windows calls the hook from inside GetMessage; the loop ends on WM_QUIT.
            while (PInvoke.GetMessage(out _, HWND.Null, 0, 0).Value > 0)
            {
            }
            _hook.Dispose(); // unhooks, on the thread that hooked
        }

        private unsafe LRESULT OnKey(int code, WPARAM wParam, LPARAM lParam)
        {
            if (code >= 0)
            {
                uint message = (uint)wParam.Value;
                bool down = message is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
                bool up = message is PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP;
                var info = (KBDLLHOOKSTRUCT*)lParam.Value;
                // Windows' own state still has the key down when it was down before the lock (its repeat comes here).
                bool heldBefore = down && (PInvoke.GetAsyncKeyState((int)info->vkCode) & 0x8000) != 0;
                if ((down || up) && _filter.Swallow(info->vkCode, down, heldBefore))
                {
                    _dispatcher.BeginInvoke(_key, new LockedKey(info->vkCode, down, info->time));
                    return new LRESULT(1);
                }
            }
            return PInvoke.CallNextHookEx(_hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_threadId != 0)
                PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
