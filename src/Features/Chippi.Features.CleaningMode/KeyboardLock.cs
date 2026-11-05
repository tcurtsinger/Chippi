// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows.Threading;
using Chippi.Core.Input;

namespace Chippi.Features.CleaningMode;

/// <summary>A key the lock took: its virtual-key code, going down or coming up, and when (milliseconds on the input hub's clock).</summary>
public readonly record struct LockedKey(uint Vk, bool Down, uint Time);

/// <summary>Takes the whole keyboard. The real one is the input hub's first filter; tests use a fake, so no test locks the owner's keyboard.</summary>
public interface IKeyboardLock
{
    /// <summary>Takes every key until the handle is disposed; <paramref name="key"/> hears each one on the calling (UI) thread.</summary>
    IDisposable Lock(Action<LockedKey> key);
}

/// <summary>
/// The input hub's first filter (architecture §2): every key goes down and up here and nowhere else, the Windows key and
/// the media and volume keys included. Ctrl+Alt+Del isn't a key Windows lets anyone take. The hub's hook has a thread of
/// its own that does nothing else, so it always answers in time.
/// </summary>
internal sealed class KeyboardLock(Func<IInputHub> hub) : IKeyboardLock
{
    public IDisposable Lock(Action<LockedKey> key) => new Filter(hub(), key);

    private sealed class Filter : IInputFilter, IDisposable
    {
        private readonly KeyFilter _filter = new();
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly Action<LockedKey> _key;
        private readonly IInputRegistration _registration;

        public Filter(IInputHub hub, Action<LockedKey> key)
        {
            _key = key;
            _registration = hub.Add(this);
        }

        public InputOrder Order => InputOrder.CleaningLock;

        public InputDevices Devices => InputDevices.Keyboard;

        // Not Phase 6: Cleaning mode locks the keyboard over a game too.
        public bool Pauses => false;

        public bool Mouse(in MouseInput mouse) => false;

        public bool Key(in KeyInput key)
        {
            // Windows' own state still has the key down when it was down before the lock (its repeat comes here).
            if (!_filter.Swallow(key.Vk, key.Down, key.WasDown))
                return false;
            uint time = unchecked((uint)(key.Timestamp * 1000 / Stopwatch.Frequency));
            _dispatcher.BeginInvoke(_key, new LockedKey(key.Vk, key.Down, time));
            return true;
        }

        public void Dispose() => _registration.Dispose();
    }
}
