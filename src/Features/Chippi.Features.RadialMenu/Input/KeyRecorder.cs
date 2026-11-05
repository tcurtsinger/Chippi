// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Core.Actions;
using Chippi.Core.Input;

namespace Chippi.Features.RadialMenu.Input;

/// <summary>
/// The input hub's last filter while Record shows "Press keys": every key the filters before it passed is kept from the
/// apps and Windows, so Win+Shift+S is recorded instead of opening the Snipping Tool. It lets go at the first key that
/// isn't a modifier, or after 10 seconds; the hub keeps the ups of the keys it kept.
/// </summary>
internal sealed class KeyRecorder(Func<IInputHub> hub) : IKeyRecorder
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public IDisposable Record(Action<KeyCombo?> recorded) => new Recording(hub(), recorded);

    private sealed class Recording : IDisposable, IInputFilter
    {
        private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
        private readonly Action<KeyCombo?> _recorded;
        private readonly HashSet<uint> _down = [];
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
                    // A key held from before (a repeat) is the app's.
                    if (key.WasDown && !_down.Contains(vk))
                        return false;
                    _down.Add(vk);
                    if (!_done)
                    {
                        var modifier = KeyCombo.ModifierOf(vk);
                        if (modifier != KeyModifiers.None)
                            _modifiers |= modifier;
                        else
                            Finish(vk == 0x1B && _modifiers == KeyModifiers.None ? null : new KeyCombo(_modifiers, vk));
                    }
                    return true;
                }
                if (!_down.Remove(vk))
                    return false;
                _modifiers &= ~KeyCombo.ModifierOf(vk);
                return true;
            }
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
            _registration.Dispose();
            _limit?.Dispose();
            _ui.BeginInvoke(() => _recorded(keys));
        }

        public void Dispose()
        {
            _limit.Dispose();
            lock (_down)
                _done = true;
            _registration.Dispose();
        }
    }
}
