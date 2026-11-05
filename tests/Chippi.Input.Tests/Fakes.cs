// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;

namespace Chippi.Input.Tests;

/// <summary>Hooks that are only flags: a test delivers events by hand, and can drop one behind the hub's back.</summary>
internal sealed class FakeHooks : IHookSource
{
    public InputDevices In { get; private set; }

    /// <summary>Hooks Windows has silently dropped: still "in" for the hub, but deaf.</summary>
    public InputDevices Dropped { get; set; }

    public List<string> Calls { get; } = [];

    public InputDevices Refuse { get; set; }

    public void Install(InputDevices device)
    {
        if ((Refuse & device) != 0)
            throw new System.ComponentModel.Win32Exception(5);
        Calls.Add($"install {device}");
        In |= device;
        Dropped &= ~device;
    }

    public void Remove(InputDevices device)
    {
        Calls.Add($"remove {device}");
        In &= ~device;
    }

    public bool Hears(InputDevices device) => (In & device) != 0 && (Dropped & device) == 0;
}

internal sealed class RecordingSender : IInputSender
{
    public List<SentKey> Sent { get; } = [];

    public List<int> Batches { get; } = [];

    public void Send(ReadOnlySpan<SentKey> keys)
    {
        Sent.AddRange(keys.ToArray());
        Batches.Add(keys.Length);
    }

    public int Ups(ushort vk) => Sent.Count(k => k.Vk == vk && k.Up);

    public int Downs(ushort vk) => Sent.Count(k => k.Vk == vk && !k.Up);
}

internal sealed class FakeSystem : IInputSystem
{
    public long Now { get; set; } = Stopwatch.Frequency * 100;

    public long LastInput { get; set; }

    public (int X, int Y) Cursor { get; set; }

    public HashSet<uint> Down { get; } = [];

    public bool IsDown(uint vk) => Down.Contains(vk);

    public void Advance(TimeSpan by) => Now += (long)(by.TotalSeconds * Stopwatch.Frequency);
}

/// <summary>A filter that notes what it heard and keeps what <see cref="KeepKey"/> or <see cref="KeepMouse"/> say.</summary>
internal sealed class TestFilter(string name, InputOrder order, InputDevices devices = InputDevices.Both, bool pauses = true) : IInputFilter
{
    public InputOrder Order { get; } = order;

    public InputDevices Devices { get; set; } = devices;

    public bool Pauses { get; } = pauses;

    public Func<KeyInput, bool> KeepKey { get; set; } = _ => false;

    public Func<MouseInput, bool> KeepMouse { get; set; } = _ => false;

    public List<string> Heard { get; } = [];

    public List<string>? Shared { get; set; }

    public bool Key(in KeyInput key)
    {
        string note = $"{name} key {key.Vk:X2} {(key.Down ? "down" : "up")}";
        Heard.Add(note);
        Shared?.Add(note);
        return KeepKey(key);
    }

    public bool Mouse(in MouseInput mouse)
    {
        string note = $"{name} {mouse.Kind} {mouse.Button}";
        Heard.Add(note);
        Shared?.Add(note);
        return KeepMouse(mouse);
    }
}

/// <summary>The hub's core with a fake source, a recording sender and a fake clock; events go in as the real hooks would send them.</summary>
internal sealed class TestHub
{
    public const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmMButtonDown = 0x0207, WmMButtonUp = 0x0208,
        WmMouseWheel = 0x020A, WmXButtonDown = 0x020B, WmXButtonUp = 0x020C;

    public TestHub()
    {
        Core = new HubCore(Hooks, Sender, System) { RequestUpdate = () => UpdateAsked++ };
    }

    public FakeHooks Hooks { get; } = new();

    public RecordingSender Sender { get; } = new();

    public FakeSystem System { get; } = new();

    public HubCore Core { get; }

    public int UpdateAsked { get; private set; }

    /// <summary>A key from a hand (or another program with <paramref name="injected"/>), if the keyboard hook hears it. Null when it didn't.</summary>
    public bool? Key(uint vk, bool down, bool injected = false, bool ours = false)
    {
        if (!Hooks.Hears(InputDevices.Keyboard))
            return null;
        System.LastInput = System.Now;
        bool keep = Core.Key(vk, 0, injected || ours ? 0x10u : 0, ours ? ChippiInput.Marker : 0, down);
        Settle();
        return keep;
    }

    public bool? Mouse(uint message, int x = 0, int y = 0, uint data = 0, bool ours = false)
    {
        System.Cursor = (x, y);
        if (!Hooks.Hears(InputDevices.Mouse))
            return null;
        System.LastInput = System.Now;
        bool keep = Core.Mouse(message, x, y, data, ours ? 1u : 0, ours ? ChippiInput.Marker : 0);
        Settle();
        return keep;
    }

    /// <summary>What the real hub does once a hook call returns: the update it asked for.</summary>
    public void Settle()
    {
        if (UpdateAsked == 0)
            return;
        UpdateAsked = 0;
        Core.UpdateHooks();
    }

    /// <summary>Input Windows saw (it notes it whether or not a hook hears it).</summary>
    public void WindowsSawInput() => System.LastInput = System.Now;

    /// <summary>Ticks once a second for <paramref name="seconds"/>.</summary>
    public void Tick(int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            System.Advance(TimeSpan.FromSeconds(1));
            Core.Tick();
        }
    }
}
