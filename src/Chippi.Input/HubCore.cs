// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;
using Microsoft.Extensions.Logging;

namespace Chippi.Input;

/// <summary>Puts in and takes off the real hooks. Tests use a fake that delivers events by hand.</summary>
internal interface IHookSource
{
    /// <summary>Puts in the device's hook, or a new one in place of the one in (the new one first, so nothing slips past). Throws when Windows refuses.</summary>
    void Install(InputDevices device);

    void Remove(InputDevices device);
}

/// <summary>Sends keys marked as Chippi's. Tests record them.</summary>
internal interface IInputSender
{
    void Send(ReadOnlySpan<SentKey> keys);
}

public readonly record struct SentKey(ushort Vk, bool Up);

/// <summary>What the hub reads from Windows besides its hooks. Tests use a fake.</summary>
internal interface IInputSystem
{
    /// <summary>The hub's clock, in <see cref="Stopwatch"/> ticks.</summary>
    long Now { get; }

    /// <summary>When Windows last saw a key or the mouse (<c>GetLastInputInfo</c>), on the same clock.</summary>
    long LastInput { get; }

    (int X, int Y) Cursor { get; }

    /// <summary>Windows' own state has the key down (<c>GetAsyncKeyState</c>).</summary>
    bool IsDown(uint vk);

    /// <summary>Any key, not a mouse button, is down in Windows' own state.</summary>
    bool AnyKeyDown();
}

/// <summary>
/// The hub's decisions, on its one thread (architecture §2): filters in order, a kept down keeping its repeats and up, the
/// held modifiers and their release, which hooks are in, and the watchdog. No Win32 here; <see cref="InputHub"/> calls it
/// from the real hooks and timers, tests from a fake source.
/// </summary>
internal sealed class HubCore(IHookSource hooks, IInputSender sender, IInputSystem system, ILogger? log = null)
{
    public static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ReinstallEvery = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan IdleRelease = TimeSpan.FromSeconds(5);

    /// <summary>Unassigned (VK 0xE8): sent between a kept chord and its Alt or Win up, so neither opens a menu.</summary>
    public const ushort MaskKey = 0xE8;

    private const uint KeyInjected = 0x10, KeyExtended = 0x01, MouseInjected = 0x01;
    private const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmRButtonDown = 0x0204, WmRButtonUp = 0x0205,
        WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmMouseWheel = 0x020A, WmXButtonDown = 0x020B, WmXButtonUp = 0x020C, WmMouseHWheel = 0x020E;

    private static readonly long Slack = Stopwatch.Frequency / 20; // 50 ms: Windows notes input a moment before the hook hears it

    private readonly Entry?[] _keyOwner = new Entry?[256];
    private readonly Entry?[] _buttonOwner = new Entry?[6];
    private readonly List<Held> _held = [];
    private Entry[] _chain = [];
    private int _added, _ownedKeys, _ownedButtons;
    private InputDevices _installed;
    private bool _paused, _inCall;

    // The watchdog's notes, on the hub's clock. 0 is "never" for the suspects.
    private long _heardKey, _heardMouse, _handKey, _lastTick, _keySuspect, _mouseSuspect;
    private long _keyReinstalled = long.MinValue / 2, _mouseReinstalled = long.MinValue / 2;
    private (int X, int Y) _tickCursor;

    /// <summary>Asks for <see cref="UpdateHooks"/> once the current hook call has returned (a hook doesn't take itself off from inside).</summary>
    public Action RequestUpdate { get; set; } = () => { };

    /// <summary>The hooks in now.</summary>
    public InputDevices Installed => _installed;

    /// <summary>The keys and buttons kept whose ups haven't come yet.</summary>
    public int Kept => _ownedKeys + _ownedButtons;

    /// <summary>Times a hook was found deaf and put back.</summary>
    public int Reinstalls { get; private set; }

    public sealed class Entry(IInputFilter filter, int added)
    {
        public IInputFilter Filter { get; } = filter;

        public int Added { get; } = added;

        public InputDevices Devices { get; set; }

        public bool Alive { get; set; } = true;

        public bool Hears(InputDevices device, bool paused) => (Devices & device) != 0 && !(paused && Filter.Pauses);
    }

    private sealed record Held(Entry Owner, uint By, ushort[] Modifiers);

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;
            _paused = value;
            if (value)
                ReleaseHeld(_ => true, "a pause began");
            TryUpdateHooks();
        }
    }

    public Entry Add(IInputFilter filter)
    {
        var entry = new Entry(filter, _added++) { Devices = filter.Devices };
        _chain = [.. _chain.Append(entry).OrderBy(e => (int)e.Filter.Order).ThenBy(e => e.Added)];
        try { UpdateHooks(); }
        catch
        {
            Remove(entry);
            throw;
        }
        return entry;
    }

    public void Refresh(Entry entry)
    {
        if (!entry.Alive)
            return;
        var was = entry.Devices;
        entry.Devices = entry.Filter.Devices;
        if ((was & InputDevices.Keyboard) != 0 && (entry.Devices & InputDevices.Keyboard) == 0)
            ReleaseHeld(h => h.Owner == entry, "its filter turned off");
        UpdateHooks();
    }

    public void Remove(Entry entry)
    {
        if (!entry.Alive)
            return;
        entry.Alive = false;
        ReleaseHeld(h => h.Owner == entry, "its filter turned off");
        _chain = [.. _chain.Where(e => e != entry)];
        TryUpdateHooks();
    }

    public void HoldModifiers(Entry entry, uint heldBy, ReadOnlySpan<ushort> modifiers)
    {
        if (!entry.Alive || modifiers.IsEmpty)
            return;
        var downs = new SentKey[modifiers.Length];
        for (int i = 0; i < modifiers.Length; i++)
            downs[i] = new SentKey(modifiers[i], false);
        sender.Send(downs);
        _held.Add(new Held(entry, heldBy, modifiers.ToArray()));
        _handKey = system.Now; // the 5 s without a key count from the hold, whatever triggered it
    }

    public void Mask() => sender.Send([new SentKey(MaskKey, false), new SentKey(MaskKey, true)]);

    /// <summary>The session locked or the desktop switched (UAC, Ctrl+Alt+Del): keys going up there never come here.</summary>
    public void SessionChanged()
    {
        ReleaseHeld(_ => true, "the session locked or the desktop switched");
        ForgetKept(InputDevices.Both);
    }

    /// <summary>
    /// Drops the keepers of keys and buttons whose ups won't come here (they went up on another desktop, or while the hook
    /// was deaf), so their next press starts fresh instead of going to a stale keeper.
    /// </summary>
    private void ForgetKept(InputDevices devices)
    {
        int before = Kept;
        if ((devices & InputDevices.Keyboard) != 0)
        {
            Array.Clear(_keyOwner);
            _ownedKeys = 0;
        }
        if ((devices & InputDevices.Mouse) != 0)
        {
            Array.Clear(_buttonOwner);
            _ownedButtons = 0;
        }
        if (Kept != before)
            RequestUpdate();
    }

    /// <summary>Chippi is quitting: every held modifier goes up and every hook comes off.</summary>
    public void Stop()
    {
        ReleaseHeld(_ => true, "Chippi quit");
        foreach (var entry in _chain)
            entry.Alive = false;
        _chain = [];
        Array.Clear(_keyOwner);
        Array.Clear(_buttonOwner);
        _ownedKeys = _ownedButtons = 0;
        foreach (var device in (ReadOnlySpan<InputDevices>)[InputDevices.Keyboard, InputDevices.Mouse])
            if ((_installed & device) != 0)
                hooks.Remove(device);
        _installed = InputDevices.None;
    }

    /// <summary>
    /// Puts in the hooks an active filter needs or a kept key's up waits on, and takes off the rest. From inside a hook call
    /// (a filter taking itself out) it waits until the call has returned and its kept key is counted.
    /// </summary>
    public void UpdateHooks()
    {
        if (_inCall)
        {
            RequestUpdate();
            return;
        }
        var wanted = InputDevices.None;
        foreach (var entry in _chain)
            if (!(_paused && entry.Filter.Pauses))
                wanted |= entry.Devices;
        if (_ownedKeys > 0)
            wanted |= InputDevices.Keyboard;
        if (_ownedButtons > 0)
            wanted |= InputDevices.Mouse;
        foreach (var device in (ReadOnlySpan<InputDevices>)[InputDevices.Keyboard, InputDevices.Mouse])
        {
            bool want = (wanted & device) != 0, have = (_installed & device) != 0;
            if (want && !have)
            {
                hooks.Install(device);
                _installed |= device;
                long now = system.Now;
                if (device == InputDevices.Keyboard)
                    (_heardKey, _keySuspect) = (now, 0);
                else
                    (_heardMouse, _mouseSuspect, _tickCursor) = (now, 0, system.Cursor);
            }
            else if (!want && have)
            {
                hooks.Remove(device);
                _installed &= ~device;
                if (device == InputDevices.Keyboard)
                    ReleaseHeld(_ => true, "the keyboard hook came off");
            }
        }
    }

    private void TryUpdateHooks()
    {
        try { UpdateHooks(); }
        catch (Exception ex) { log?.LogWarning(ex, "Input hub: putting in a hook failed"); }
    }

    /// <summary>A key from the keyboard hook. True keeps it from the apps.</summary>
    public bool Key(uint vk, uint scan, uint flags, nint extra, bool down)
    {
        long now = system.Now;
        _heardKey = now;
        _keySuspect = 0;
        bool injected = (flags & KeyInjected) != 0;
        if (injected && extra == ChippiInput.Marker)
            return false;
        _handKey = now;
        _inCall = true;
        try { return Key(vk, scan, flags, down, injected, now); }
        finally { _inCall = false; }
    }

    private bool Key(uint vk, uint scan, uint flags, bool down, bool injected, long now)
    {
        uint index = vk & 0xFF;
        var input = new KeyInput(vk, scan, down, down && system.IsDown(vk), injected, (flags & KeyExtended) != 0, now);
        bool keep = false;
        if (_keyOwner[index] is { } owner)
        {
            // A kept key's repeats and up are its keeper's, whatever it answers: the app never saw it go down.
            if (owner.Alive)
                owner.Filter.Key(in input);
            keep = true;
            if (!down)
            {
                _keyOwner[index] = null;
                if (--_ownedKeys == 0)
                    RequestUpdate();
            }
        }
        else if (down)
        {
            foreach (var entry in _chain)
            {
                if (!entry.Hears(InputDevices.Keyboard, _paused) || !entry.Filter.Key(in input))
                    continue;
                keep = true;
                // A key Windows already had down went to the app: its up must reach the app too.
                if (!input.WasDown)
                {
                    _keyOwner[index] = entry;
                    _ownedKeys++;
                }
                break;
            }
        }
        else
        {
            // An up whose down nobody kept: filters may note it, and it always passes on.
            foreach (var entry in _chain)
                if (entry.Hears(InputDevices.Keyboard, _paused))
                    entry.Filter.Key(in input);
        }
        if (!down && _held.Count > 0)
            ReleaseHeld(h => h.By == vk, null);
        return keep;
    }

    /// <summary>A mouse event from the mouse hook (its window message and <c>MSLLHOOKSTRUCT</c>). True keeps it.</summary>
    public bool Mouse(uint message, int x, int y, uint data, uint flags, nint extra)
    {
        long now = system.Now;
        _heardMouse = now;
        _mouseSuspect = 0;
        bool injected = (flags & MouseInjected) != 0;
        if (injected && extra == ChippiInput.Marker)
            return false;
        _inCall = true;
        try { return Mouse(message, x, y, data, injected, now); }
        finally { _inCall = false; }
    }

    private bool Mouse(uint message, int x, int y, uint data, bool injected, long now)
    {
        (MouseKind kind, MouseButton button) = message switch
        {
            WmMouseMove => (MouseKind.Move, MouseButton.None),
            WmLButtonDown => (MouseKind.Down, MouseButton.Left),
            WmLButtonUp => (MouseKind.Up, MouseButton.Left),
            WmRButtonDown => (MouseKind.Down, MouseButton.Right),
            WmRButtonUp => (MouseKind.Up, MouseButton.Right),
            WmMButtonDown => (MouseKind.Down, MouseButton.Middle),
            WmMButtonUp => (MouseKind.Up, MouseButton.Middle),
            WmXButtonDown => (MouseKind.Down, (data >> 16) == 1 ? MouseButton.Back : MouseButton.Forward),
            WmXButtonUp => (MouseKind.Up, (data >> 16) == 1 ? MouseButton.Back : MouseButton.Forward),
            WmMouseWheel => (MouseKind.Wheel, MouseButton.None),
            WmMouseHWheel => (MouseKind.HorizontalWheel, MouseButton.None),
            _ => ((MouseKind)(-1), MouseButton.None),
        };
        if ((int)kind < 0)
            return false;
        int delta = kind is MouseKind.Wheel or MouseKind.HorizontalWheel ? (short)(data >> 16) : 0;
        var input = new MouseInput(kind, button, x, y, delta, injected, now);
        if (kind == MouseKind.Up)
        {
            if (_buttonOwner[(int)button] is { } owner)
            {
                if (owner.Alive)
                    owner.Filter.Mouse(in input);
                _buttonOwner[(int)button] = null;
                if (--_ownedButtons == 0)
                    RequestUpdate();
                return true;
            }
            foreach (var entry in _chain)
                if (entry.Hears(InputDevices.Mouse, _paused))
                    entry.Filter.Mouse(in input);
            return false;
        }
        foreach (var entry in _chain)
        {
            if (!entry.Hears(InputDevices.Mouse, _paused) || !entry.Filter.Mouse(in input))
                continue;
            if (kind == MouseKind.Down && _buttonOwner[(int)button] is null)
            {
                _buttonOwner[(int)button] = entry;
                _ownedButtons++;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Once a second: lets go of held modifiers after 5 s without a key, and puts back a hook that has gone deaf. Windows
    /// drops a low-level hook it thinks is slow without saying so. A mouse hook is deaf when Windows saw input it didn't
    /// hear and the pointer moved (a program moving the pointer isn't input). A keyboard hook is deaf when Windows saw
    /// input the hub didn't; with the mouse hook out, that input could be a click or a wheel, so only while a key is down.
    /// Either way it's put back after 5 s of that, at most once a minute. Over an admin window the hooks hear nothing by
    /// design, and a reinstall there costs a log line.
    /// </summary>
    public void Tick()
    {
        long now = system.Now;
        long second = Stopwatch.Frequency;
        if (_held.Count > 0 && now - _handKey >= IdleRelease.TotalSeconds * second)
            ReleaseHeld(_ => true, "no key came for 5 seconds");

        var cursor = system.Cursor;
        bool moved = cursor != _tickCursor;
        _tickCursor = cursor;
        bool mouseIn = (_installed & InputDevices.Mouse) != 0;
        long lastInput = system.LastInput;
        if (mouseIn && moved && _heardMouse <= _lastTick && lastInput > _heardMouse + Slack && _mouseSuspect == 0)
            _mouseSuspect = now;
        if ((_installed & InputDevices.Keyboard) != 0 && _keySuspect == 0
            && lastInput > Math.Max(_heardKey, _heardMouse) + Slack && (mouseIn || system.AnyKeyDown()))
            _keySuspect = now;
        _lastTick = now;

        long after = (long)(Watchdog.TotalSeconds * second), every = (long)(ReinstallEvery.TotalSeconds * second);
        if (_mouseSuspect != 0 && now - _mouseSuspect >= after && now - _mouseReinstalled >= every)
        {
            _mouseReinstalled = now;
            Reinstall(InputDevices.Mouse);
        }
        if (_keySuspect != 0 && now - _keySuspect >= after && now - _keyReinstalled >= every)
        {
            _keyReinstalled = now;
            Reinstall(InputDevices.Keyboard);
        }
    }

    private void Reinstall(InputDevices device)
    {
        string name = device == InputDevices.Keyboard ? "keyboard" : "mouse";
        log?.LogWarning("Input hub: the {Device} hook heard nothing for 5 s while Windows saw input; putting it back", name);
        ReleaseHeld(_ => true, $"the {name} hook was found gone");
        ForgetKept(device);
        try
        {
            hooks.Install(device);
            Reinstalls++;
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Input hub: putting the {Device} hook back failed", name);
        }
        if (device == InputDevices.Keyboard)
            (_heardKey, _keySuspect) = (system.Now, 0);
        else
            (_heardMouse, _mouseSuspect) = (system.Now, 0);
    }

    private void ReleaseHeld(Func<Held, bool> which, string? why)
    {
        if (_held.Count == 0)
            return;
        var ups = new List<SentKey>();
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (!which(_held[i]))
                continue;
            var modifiers = _held[i].Modifiers;
            for (int m = modifiers.Length - 1; m >= 0; m--)
                ups.Add(new SentKey(modifiers[m], true));
            _held.RemoveAt(i);
        }
        if (ups.Count == 0)
            return;
        sender.Send(ups.ToArray());
        if (why is not null)
            log?.LogInformation("Input hub: let go of {Count} held modifiers: {Why}", ups.Count, why);
    }
}
