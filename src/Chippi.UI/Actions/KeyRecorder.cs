// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Core.Actions;
using Chippi.Core.Input;

namespace Chippi.UI.Actions;

/// <summary>
/// The input hub's last filter while Record shows "Press keys": every key the filters before it passed is kept from the
/// apps and Windows, so Win+Shift+S is recorded instead of opening the Snipping Tool. It lets go at the first key that
/// isn't a modifier, or after 10 seconds; the hub keeps the ups of the keys it kept.
/// </summary>
public sealed class KeyRecorder(Func<IInputHub> hub) : IKeyRecorder
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public IDisposable Record(Action<KeyCombo?> recorded) => new Recording(hub(), recorded);

    private sealed class Recording : IDisposable, IInputFilter
    {
        private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
        private readonly Action<KeyCombo?> _recorded;
        private readonly HashSet<uint> _down = [];
        private readonly HashSet<uint> _heldBefore = [];
        private readonly IInputRegistration _registration;
        private readonly Timer _limit;
        private KeyModifiers _modifiers;
        private bool _done;

        public Recording(IInputHub hub, Action<KeyCombo?> recorded)
        {
            _recorded = recorded;
            _registration = hub.Add(this);
            _limit = new Timer(_ => Finish(null), null, Limit, Timeout.InfiniteTimeSpan);
        }

        public InputOrder Order => InputOrder.Recorder;

        public InputDevices Devices => InputDevices.Keyboard;

        public bool Pauses => false;

        public bool Mouse(in MouseInput mouse) => false;

        public bool Key(in KeyInput key)
        {
            uint vk = key.Vk;
            lock (_down)
            {
                if (key.Down)
                {
                    // A key held from before (a repeat) is the app's. A modifier held from before still counts
                    // toward the combo, but its up stays the app's too.
                    if (key.WasDown && !_down.Contains(vk))
                    {
                        var held = KeyCombo.ModifierOf(vk);
                        if (held != KeyModifiers.None && !_done && _heldBefore.Add(vk))
                            _modifiers |= held;
                        return false;
                    }
                    _down.Add(vk);
                    if (!_done)
                    {
                        var modifier = KeyCombo.ModifierOf(vk);
                        if (modifier != KeyModifiers.None)
                            _modifiers |= modifier;
                        else
                            Finish(vk == 0x1B && !PressedModifier() ? null : new KeyCombo(_modifiers, vk));
                    }
                    return true;
                }
                if (_heldBefore.Remove(vk))
                {
                    _modifiers &= ~KeyCombo.ModifierOf(vk);
                    return false;
                }
                if (!_down.Remove(vk))
                    return false;
                _modifiers &= ~KeyCombo.ModifierOf(vk);
                return true;
            }
        }

        // Esc cancels unless the user pressed a modifier after recording began (one held from before doesn't count).
        private bool PressedModifier()
        {
            foreach (uint down in _down)
                if (KeyCombo.ModifierOf(down) != KeyModifiers.None)
                    return true;
            return false;
        }

        private void Finish(KeyCombo? keys)
        {
            lock (_down)
            {
                if (_done)
                    return;
                _done = true;
            }
            // Out of the hub: the ups of the keys still down stay kept, so Windows never sees an up without its down.
            // This can run on the timer's pool thread, where the hub may already be gone: nothing to leave then.
            try { _registration.Dispose(); }
            catch (ObjectDisposedException) { }
            _limit?.Dispose();
            _ui.BeginInvoke(() => _recorded(keys));
        }

        public void Dispose()
        {
            _limit.Dispose();
            lock (_down)
                _done = true;
            try { _registration.Dispose(); }
            catch (ObjectDisposedException) { }
        }
    }
}
