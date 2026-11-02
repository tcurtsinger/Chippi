// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.CleaningMode;

/// <summary>
/// Counts the Esc presses that unlock Cleaning mode (docs/phase-5.5/cleaning-mode.md): three, each within a second of the
/// last. Any other key, or a held key's repeat, starts the count over; a key coming up changes nothing.
/// </summary>
internal sealed class UnlockCount(TimeProvider time)
{
    public const int Needed = 3;
    public const uint Escape = 0x1B;

    /// <summary>The longest wait between two presses that still count together.</summary>
    public static readonly TimeSpan Within = TimeSpan.FromSeconds(1);

    private readonly HashSet<uint> _down = [];
    private long _last;

    /// <summary>The presses counted so far: 0 to <see cref="Needed"/>.</summary>
    public int Presses { get; private set; }

    public bool Unlocked => Presses >= Needed;

    /// <summary>A key went down or up; returns <see cref="Presses"/> after it.</summary>
    public int Key(uint vk, bool down)
    {
        if (Unlocked)
            return Presses;
        if (!down)
        {
            _down.Remove(vk);
            return Presses;
        }
        bool repeat = !_down.Add(vk);
        if (repeat || vk != Escape)
            return Presses = 0;
        long now = time.GetTimestamp();
        Presses = Presses > 0 && time.GetElapsedTime(_last, now) <= Within ? Presses + 1 : 1;
        _last = now;
        return Presses;
    }
}

/// <summary>
/// Which keys the lock swallows: every key going down, and its coming up. A key already held when the lock started (the
/// shortcut's own Ctrl or Alt) still gets its key-up through, so Windows never thinks it's held after the unlock.
/// </summary>
internal sealed class KeyFilter
{
    private readonly HashSet<uint> _swallowed = [];

    public bool Swallow(uint vk, bool down)
    {
        if (down)
        {
            _swallowed.Add(vk);
            return true;
        }
        return _swallowed.Remove(vk);
    }
}
