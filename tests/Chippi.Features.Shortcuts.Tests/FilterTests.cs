// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.Shortcuts.Tests;

/// <summary>The two filters on an injected clock. A kept down's up reaches the filter and the hub keeps it, as the hub does.</summary>
public class FilterTests
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;

    private static MouseInput Button(MouseKind kind, MouseButton button, long ms = 0) => new(kind, button, 0, 0, 0, false, 1000 * Ms + ms * Ms);

    private static MouseInput Tilt(int delta, long ms) => new(MouseKind.HorizontalWheel, MouseButton.None, 0, 0, delta, false, 1000 * Ms + ms * Ms);

    private static KeyInput Key(uint vk, bool down, bool wasDown = false) => new(vk, 0, down, wasDown, false, false, 0);

    private const uint LWin = 0x5B, LCtrl = 0xA2, LAlt = 0xA4, K = 0x4B;

    [Fact]
    public void A_bound_button_fires_once_on_its_down_and_is_kept()
    {
        var fired = new List<MouseTrigger>();
        var f = new MouseTriggerFilter { Fired = fired.Add };
        f.Set([MouseTrigger.Back], []);
        Assert.True(f.Mouse(Button(MouseKind.Down, MouseButton.Back)));
        Assert.False(f.Mouse(Button(MouseKind.Up, MouseButton.Back, 40)));
        Assert.Equal([MouseTrigger.Back], fired);
    }

    [Fact]
    public void An_unbound_button_and_the_left_and_right_buttons_pass()
    {
        var fired = new List<MouseTrigger>();
        var f = new MouseTriggerFilter { Fired = fired.Add };
        f.Set([MouseTrigger.Back], []);
        Assert.False(f.Mouse(Button(MouseKind.Down, MouseButton.Forward)));
        Assert.False(f.Mouse(Button(MouseKind.Down, MouseButton.Middle)));
        Assert.False(f.Mouse(Button(MouseKind.Down, MouseButton.Left)));
        Assert.False(f.Mouse(Button(MouseKind.Down, MouseButton.Right)));
        Assert.Empty(fired);
    }

    [Fact]
    public void A_button_the_Radial_menu_holds_passes_until_it_is_freed()
    {
        bool radial = true;
        var fired = new List<MouseTrigger>();
        var f = new MouseTriggerFilter { Fired = fired.Add, HeldByRadial = _ => radial };
        f.Set([MouseTrigger.Middle], []);
        Assert.False(f.Mouse(Button(MouseKind.Down, MouseButton.Middle)));
        Assert.Empty(fired);
        radial = false;
        Assert.True(f.Mouse(Button(MouseKind.Down, MouseButton.Middle, 10)));
        Assert.Single(fired);
    }

    [Fact]
    public void In_an_app_on_the_list_the_press_and_the_tilt_reach_the_app()
    {
        string? app = @"C:\Program Files\Browser\browser.EXE";
        var fired = new List<MouseTrigger>();
        var f = new MouseTriggerFilter { Fired = fired.Add, ForegroundApp = () => app };
        f.Set([MouseTrigger.Back, MouseTrigger.WheelRight], [@"D:\apps\Browser.exe"]); // matched by the exe's name, any case
        Assert.False(f.Mouse(Button(MouseKind.Down, MouseButton.Back)));
        Assert.False(f.Mouse(Tilt(120, 5)));
        app = @"C:\Windows\notepad.exe";
        Assert.True(f.Mouse(Button(MouseKind.Down, MouseButton.Back, 10)));
        app = null; // unreadable: the app can't be told, so the binding acts
        Assert.True(f.Mouse(Tilt(120, 400)));
        Assert.Equal([MouseTrigger.Back, MouseTrigger.WheelRight], fired);
    }

    [Fact]
    public void A_tilt_fires_once_per_notch_and_a_finer_wheel_counts_up_to_one()
    {
        var fired = new List<MouseTrigger>();
        var f = new MouseTriggerFilter { Fired = fired.Add };
        f.Set([MouseTrigger.WheelLeft, MouseTrigger.WheelRight], []);
        Assert.True(f.Mouse(Tilt(-120, 0)));
        Assert.True(f.Mouse(Tilt(-120, 20)));
        Assert.Equal([MouseTrigger.WheelLeft, MouseTrigger.WheelLeft], fired);

        fired.Clear();
        for (int i = 0; i < 4; i++)
            Assert.True(f.Mouse(Tilt(30, 1000 + i * 10))); // four 30s make one notch
        Assert.Equal([MouseTrigger.WheelRight], fired);

        fired.Clear();
        Assert.True(f.Mouse(Tilt(240, 2000))); // a fast flick of two notches
        Assert.Equal([MouseTrigger.WheelRight, MouseTrigger.WheelRight], fired);
    }

    [Fact]
    public void A_turn_the_other_way_or_a_pause_starts_the_count_again_and_an_unbound_direction_passes()
    {
        var fired = new List<MouseTrigger>();
        var f = new MouseTriggerFilter { Fired = fired.Add };
        f.Set([MouseTrigger.WheelLeft, MouseTrigger.WheelRight], []);
        f.Mouse(Tilt(60, 0));
        f.Mouse(Tilt(-60, 10)); // half a notch right, then half left: neither is one
        Assert.Empty(fired);
        f.Mouse(Tilt(60, 20));
        f.Mouse(Tilt(60, 1000)); // a second later: the first 60 is forgotten
        Assert.Empty(fired);

        var right = new MouseTriggerFilter { Fired = fired.Add };
        right.Set([MouseTrigger.WheelRight], []);
        Assert.False(right.Mouse(Tilt(-120, 0)));
        Assert.Empty(fired);
    }

    [Fact]
    public void Moves_and_the_ordinary_wheel_are_never_taken()
    {
        var f = new MouseTriggerFilter { Fired = _ => throw new InvalidOperationException() };
        f.Set([MouseTrigger.Middle, MouseTrigger.Back, MouseTrigger.Forward, MouseTrigger.WheelLeft, MouseTrigger.WheelRight], []);
        Assert.False(f.Mouse(Button(MouseKind.Move, MouseButton.None)));
        Assert.False(f.Mouse(new MouseInput(MouseKind.Wheel, MouseButton.None, 0, 0, 120, false, 0)));
        Assert.False(f.Key(Key(K, true)));
    }

    private static ChordFilter Chords(List<string> fired, out List<string> log, params (ShortcutModifiers, uint, string)[] chords)
    {
        var made = new List<string>();
        log = made;
        var f = new ChordFilter { Fired = fired.Add, Mask = () => made.Add("mask") };
        f.Set(chords.Select(c => (new KeyShortcut(c.Item1, c.Item2), c.Item3)));
        return f;
    }

    [Fact]
    public void A_Win_chord_is_kept_with_its_repeats_and_Win_is_masked_before_it_comes_up()
    {
        var fired = new List<string>();
        var f = Chords(fired, out var log, (ShortcutModifiers.Windows, K, "k"));
        Assert.False(f.Key(Key(LWin, true)));
        Assert.True(f.Key(Key(K, true)));
        Assert.True(f.Key(Key(K, true, wasDown: true))); // a repeat is the chord's: kept, not fired again
        Assert.Equal(["k"], fired);
        Assert.False(f.Key(Key(LWin, false)));
        Assert.Equal(["mask"], log); // sent from inside the Win up, so before it reaches Windows
        Assert.False(f.Key(Key(LWin, false)));
        Assert.Single(log);
    }

    [Fact]
    public void A_Ctrl_chord_needs_no_mask_and_wrong_or_extra_modifiers_pass()
    {
        var fired = new List<string>();
        var f = Chords(fired, out var log, (ShortcutModifiers.Control | ShortcutModifiers.Windows, K, "ctrl-win-k"));
        f.Key(Key(LWin, true));
        Assert.False(f.Key(Key(K, true))); // Win alone: not the chord
        f.Key(Key(K, false));
        f.Key(Key(LCtrl, true));
        f.Key(Key(LAlt, true));
        Assert.False(f.Key(Key(K, true))); // an Alt too many
        f.Key(Key(LAlt, false));
        Assert.True(f.Key(Key(K, true)));
        f.Key(Key(K, false));
        f.Key(Key(LCtrl, false));
        Assert.Empty(log); // Win is part of the chord, so it is masked; the Ctrl up needs none
        f.Key(Key(LWin, false));
        Assert.Equal(["mask"], log);
    }

    [Fact]
    public void Letting_go_of_one_Ctrl_keeps_Ctrl_down_while_the_other_is_held()
    {
        var fired = new List<string>();
        var f = Chords(fired, out _, (ShortcutModifiers.Control | ShortcutModifiers.Windows, K, "k"));
        f.Key(Key(LWin, true));
        f.Key(Key(LCtrl, true));
        f.Key(Key(0xA3, true)); // right Ctrl too
        f.Key(Key(LCtrl, false));
        Assert.True(f.Key(Key(K, true)));
        Assert.Equal(["k"], fired);
    }

    [Fact]
    public void A_reset_forgets_held_modifiers_so_a_stale_Win_cant_trigger_the_chord()
    {
        var fired = new List<string>();
        var f = Chords(fired, out _, (ShortcutModifiers.Windows, K, "k"));
        f.Key(Key(LWin, true));
        f.InputReset(InputDevices.Keyboard); // the Win's up went to another desktop
        Assert.False(f.Key(Key(K, true)));
        Assert.Empty(fired);
    }
}
