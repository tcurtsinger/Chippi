// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using Chippi.Core.Actions;
using Chippi.Core.Input;
namespace Chippi.Features.Shortcuts.Tests;

public class CapsFilterTests
{
    private static KeyInput Key(uint vk, bool down, int ms = 0, bool repeat = false) =>
        new(vk, 0, down, repeat, false, false, Stopwatch.Frequency + ms * Stopwatch.Frequency / 1000);

    [Fact]
    public void Binding_fires_once_and_keeps_repeats_and_release_even_after_disable()
    {
        var runs = new List<uint>();
        var f = new CapsTriggerFilter { Fired = runs.Add };
        f.Set(0x14, KeyModifiers.Ctrl, [0x4B], []);
        Assert.True(f.Key(Key(0x14, true)));
        Assert.True(f.Key(Key(0x4B, true)));
        Assert.True(f.Key(Key(0x4B, true, repeat: true)));
        f.Set(0, KeyModifiers.None, [], []);
        Assert.True(f.Key(Key(0x4B, false)));
        Assert.True(f.Key(Key(0x14, false)));
        Assert.Equal([0x4Bu], runs);
    }

    [Theory]
    [InlineData(100, 1)]
    [InlineData(501, 0)]
    public void Solo_tap_sends_no_modifiers_and_long_hold_does_nothing(int duration, int taps)
    {
        int tapped = 0;
        var f = new CapsTriggerFilter { Tapped = () => tapped++, Hold = (_, _) => Assert.Fail("Solo tap sent modifiers") };
        f.Set(0x14, KeyModifiers.Win | KeyModifiers.Alt, [], []);
        Assert.True(f.Key(Key(0x14, true)));
        Assert.True(f.Key(Key(0x14, true, 50, true)));
        Assert.True(f.Key(Key(0x14, false, duration)));
        Assert.Equal(taps, tapped);
    }

    [Fact]
    public void Unbound_keys_pass_with_modifiers_once_per_press_and_cancel_tap()
    {
        var holds = new List<(uint, ushort[])>();
        var f = new CapsTriggerFilter { Hold = (vk, mods) => holds.Add((vk, mods)), Tapped = () => Assert.Fail("Chord tapped") };
        f.Set(0x14, KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift, [], []);
        f.Key(Key(0x14, true));
        Assert.False(f.Key(Key(0x4B, true)));
        Assert.False(f.Key(Key(0x4B, true, repeat: true)));
        Assert.False(f.Key(Key(0x4B, false)));
        f.Key(Key(0x14, false, 100));
        Assert.Equal(0x4Bu, Assert.Single(holds).Item1);
        Assert.Equal(new ushort[] { 0x11, 0x12, 0x10 }, holds[0].Item2);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Admin_and_excluded_apps_keep_plain_Caps(bool admin, bool excluded)
    {
        var f = new CapsTriggerFilter { Blocked = () => admin, ForegroundApp = () => @"C:\Apps\game.exe" };
        f.Set(0x14, KeyModifiers.Ctrl, [], excluded ? ["game.exe"] : []);
        Assert.False(f.Key(Key(0x14, true)));
        Assert.False(f.Key(Key(0x14, false)));
    }

    [Theory]
    [InlineData(0x14)]
    [InlineData(0xA3)]
    [InlineData(0xA1)]
    [InlineData(0x5C)]
    public void Every_held_key_resets_after_lost_input(int held)
    {
        var f = new CapsTriggerFilter { Fired = _ => Assert.Fail("Stale hold") };
        f.Set((uint)held, KeyModifiers.None, [0x4B], []);
        Assert.True(f.Key(Key((uint)held, true)));
        f.InputReset(InputDevices.Keyboard);
        Assert.False(f.Key(Key(0x4B, true)));
    }
}
