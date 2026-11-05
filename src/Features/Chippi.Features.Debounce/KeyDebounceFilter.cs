// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;

namespace Chippi.Features.Debounce;

/// <summary>
/// Drops a second key-down of the same key that comes inside its window: while the key is still down, or just after it
/// came up and it was the last key accepted. A down on a key that's down and past the window is the keyboard's auto-repeat
/// and passes. Key-ups always pass, except the up of a down this dropped, which the hub keeps with it.
/// </summary>
internal sealed class KeyDebounceFilter : IInputFilter
{
    private static readonly long Reset = 5 * Stopwatch.Frequency;

    private readonly long[] _lastDown = new long[256];
    private readonly long[] _lastUp = new long[256];
    private readonly bool[] _hasDown = new bool[256];
    private readonly bool[] _hasUp = new bool[256];
    private readonly bool[] _dropping = new bool[256];
    private int _lastAccepted = -1;
    private long _lastEvent;
    private long _default;
    private long[] _own = Array.Empty<long>();
    private int _ignored;

    public KeyDebounceFilter() => WindowMs = 5;

    public InputOrder Order => InputOrder.Debounce;

    public InputDevices Devices => InputDevices.Keyboard;

    public bool Pauses => true;

    public bool IncludeInjected { get; init; }

    public int WindowMs
    {
        set => Volatile.Write(ref _default, value * Stopwatch.Frequency / 1000);
    }

    /// <summary>The keys with a window of their own (virtual-key code, milliseconds).</summary>
    public void SetOwn(IEnumerable<(uint Vk, int Ms)> own)
    {
        var table = new long[256];
        Array.Fill(table, -1);
        foreach (var (vk, ms) in own)
            table[vk & 0xFF] = ms * Stopwatch.Frequency / 1000;
        Volatile.Write(ref _own, table);
    }

    public int Ignored => Volatile.Read(ref _ignored);

    public bool Mouse(in MouseInput mouse) => false;

    public bool Key(in KeyInput key)
    {
        if (key.Injected && !IncludeInjected)
            return false;
        int vk = (int)(key.Vk & 0xFF);
        long now = key.Timestamp;
        if (now < _lastEvent || now - _lastEvent > Reset)
            Clear();
        _lastEvent = now;
        if (!key.Down)
        {
            if (_dropping[vk])
            {
                _dropping[vk] = false;
                return true;
            }
            _lastUp[vk] = now;
            _hasUp[vk] = true;
            return false;
        }
        var own = Volatile.Read(ref _own);
        long window = own.Length > 0 && own[vk] >= 0 ? own[vk] : Volatile.Read(ref _default);
        bool chatter;
        if (key.WasDown)
            chatter = window > 0 && _hasDown[vk] && now - _lastDown[vk] < window;
        else
            chatter = window > 0 && _lastAccepted == vk && _hasUp[vk] && now - _lastUp[vk] < window;
        if (chatter)
        {
            _dropping[vk] = !key.WasDown; // the hub keeps the up of a down it never passed on
            Interlocked.Increment(ref _ignored);
            return true;
        }
        if (!key.WasDown)
        {
            _lastDown[vk] = now;
            _hasDown[vk] = true;
            _lastAccepted = vk;
        }
        return false;
    }

    private void Clear()
    {
        Array.Clear(_hasDown);
        Array.Clear(_hasUp);
        Array.Clear(_dropping);
        _lastAccepted = -1;
    }
}
