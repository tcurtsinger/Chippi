// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.CleaningMode;

/// <summary>
/// Counts the Esc presses that unlock Cleaning mode (docs/phase-5.5/cleaning-mode.md): three, each within a second of the
/// last, measured by when each key was pressed. Any other key, or a held key's repeat, starts the count over; a key coming
/// up changes nothing.
/// </summary>
internal sealed class UnlockCount
{
    public const int Needed = 3;
    public const uint Escape = 0x1B;

    /// <summary>The longest wait between two presses that still count together, in milliseconds.</summary>
    public const uint Within = 1000;

    private readonly HashSet<uint> _down = [];
    private uint _last;

    /// <summary>The presses counted so far: 0 to <see cref="Needed"/>.</summary>
    public int Presses { get; private set; }

    public bool Unlocked => Presses >= Needed;

    /// <summary>A key went down or up at <paramref name="time"/> (Windows' millisecond tick); returns <see cref="Presses"/> after it.</summary>
    public int Key(uint vk, bool down, uint time)
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
        // The tick wraps every 49.7 days; the difference doesn't care.
        Presses = Presses > 0 && unchecked(time - _last) <= Within ? Presses + 1 : 1;
        _last = time;
        return Presses;
    }
}

/// <summary>
/// Which keys the lock swallows: every key going down, and its coming up. A key Windows saw go down before the lock (the
/// shortcut's own keys, a Ctrl held while clicking Start) still gets its key-up through, even after its repeats were
/// swallowed, so Windows never thinks it's held after the unlock.
/// </summary>
internal sealed class KeyFilter
{
    private readonly HashSet<uint> _swallowed = [];

    /// <param name="heldBefore">For a key going down: Windows' own state already has it down, so it went down before the lock.</param>
    public bool Swallow(uint vk, bool down, bool heldBefore = false)
    {
        if (down)
        {
            if (!heldBefore)
                _swallowed.Add(vk);
            return true;
        }
        return _swallowed.Remove(vk);
    }
}
