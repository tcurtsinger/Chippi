// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Windows.Threading;
using Chippi.Features.RadialMenu.Pc;
using Chippi.Features.RadialMenu.Slices;

namespace Chippi.Features.RadialMenu.Input;

/// <summary>Records a Key shortcut slice's keys. The real one hooks the keyboard; tests use a fake.</summary>
public interface IKeyRecorder
{
    /// <summary>
    /// Takes the keyboard until a key is pressed with whatever modifiers are down, then calls <paramref name="recorded"/>
    /// with them on the calling (UI) thread, or with null for Esc alone, and lets go once those keys are up. Dispose the
    /// handle to stop early.
    /// </summary>
    IDisposable Record(Action<KeyCombo?> recorded);
}

/// <summary>
/// A low-level keyboard hook while Record shows "Press keys": every key is kept from the apps and Windows, so
/// Win+Shift+S is recorded instead of opening the Snipping Tool. It lets go at the first key that isn't a modifier, once
/// every key it kept is up, or after 10 seconds.
/// </summary>
internal sealed class KeyRecorder : IKeyRecorder
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public IDisposable Record(Action<KeyCombo?> recorded) => new Recording(recorded);

    private sealed class Recording : IDisposable
    {
        private readonly Native.HookProc _proc;
        private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
        private readonly Action<KeyCombo?> _recorded;
        private readonly HashSet<uint> _down = [];
        private readonly Thread _thread;
        private readonly Timer _limit;
        private uint _threadId;
        private KeyModifiers _modifiers;
        private bool _done;

        public Recording(Action<KeyCombo?> recorded)
        {
            _recorded = recorded;
            _proc = OnKey;
            var ready = new ManualResetEventSlim();
            Exception? failed = null;
            _thread = new Thread(() =>
            {
                _threadId = Native.GetCurrentThreadId();
                nint hook = Native.SetWindowsHookEx(Native.HookKeyboard, _proc, Native.GetModuleHandle(null), 0);
                if (hook == 0)
                {
                    failed = new Win32Exception();
                    ready.Set();
                    return;
                }
                ready.Set();
                while (Native.GetMessage(out _, 0, 0, 0) > 0)
                {
                }
                Native.UnhookWindowsHookEx(hook);
            })
            { IsBackground = true, Name = "Radial menu key recorder", Priority = ThreadPriority.Highest };
            _thread.Start();
            ready.Wait();
            ready.Dispose();
            if (failed is not null)
                throw failed;
            _limit = new Timer(_ => Finish(null), null, Limit, Timeout.InfiniteTimeSpan);
        }

        private unsafe nint OnKey(int code, nint wParam, nint lParam)
        {
            if (code < 0)
                return Native.CallNextHookEx(0, code, wParam, lParam);
            var info = (Native.KeyHookInfo*)lParam;
            if ((info->Flags & Native.InjectedKey) != 0 && info->ExtraInfo == Native.Ours)
                return Native.CallNextHookEx(0, code, wParam, lParam);
            bool down = (uint)wParam is Native.WmKeyDown or Native.WmSysKeyDown;
            uint vk = info->Vk;
            lock (_down)
            {
                if (down)
                {
                    // A key held from before (a repeat) is the app's.
                    bool before = !_down.Contains(vk) && (Native.GetAsyncKeyState((int)vk) & 0x8000) != 0;
                    if (before)
                        return Native.CallNextHookEx(0, code, wParam, lParam);
                    _down.Add(vk);
                    if (!_done)
                    {
                        var modifier = KeyCombo.ModifierOf(vk);
                        if (modifier != KeyModifiers.None)
                            _modifiers |= modifier;
                        else
                            Finish(vk == 0x1B && _modifiers == KeyModifiers.None ? null : new KeyCombo(_modifiers, vk));
                    }
                    return 1;
                }
                if (!_down.Remove(vk))
                    return Native.CallNextHookEx(0, code, wParam, lParam);
                _modifiers &= ~KeyCombo.ModifierOf(vk);
                if (_done && _down.Count == 0)
                    Native.PostThreadMessage(_threadId, Native.WmQuit, 0, 0);
                return 1;
            }
        }

        private void Finish(KeyCombo? keys)
        {
            lock (_down)
            {
                if (_done)
                    return;
                _done = true;
                // The keys still down stay kept until they're up, so Windows never sees an up without its down.
                if (_down.Count == 0)
                    Native.PostThreadMessage(_threadId, Native.WmQuit, 0, 0);
            }
            _limit?.Dispose();
            _ui.BeginInvoke(() => _recorded(keys));
        }

        public void Dispose()
        {
            _limit.Dispose();
            lock (_down)
                _done = true;
            Native.PostThreadMessage(_threadId, Native.WmQuit, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
