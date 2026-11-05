// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;

namespace Chippi.Input.Tests;

/// <summary>The hub's rules (architecture §2) on a fake source and a recording sender: nothing here hooks the real input.</summary>
public class HubTests
{
    private const uint A = 0x41, Esc = 0x1B, Caps = 0x14;

    [Fact]
    public void Filters_hear_input_in_their_order_whatever_order_they_were_added_in()
    {
        var hub = new TestHub();
        var heard = new List<string>();
        hub.Core.Add(new TestFilter("recorder", InputOrder.Recorder) { Shared = heard });
        hub.Core.Add(new TestFilter("radial", InputOrder.Triggers) { Shared = heard });
        hub.Core.Add(new TestFilter("lock", InputOrder.CleaningLock) { Shared = heard });
        hub.Core.Add(new TestFilter("shortcuts", InputOrder.Triggers) { Shared = heard });

        hub.Key(A, down: true);

        Assert.Equal(["lock key 41 down", "radial key 41 down", "shortcuts key 41 down", "recorder key 41 down"], heard);
    }

    [Fact]
    public void A_kept_down_stops_the_chain_and_its_repeats_and_up_go_only_to_its_keeper()
    {
        var hub = new TestHub();
        var first = new TestFilter("first", InputOrder.CleaningLock) { KeepKey = k => k.Down };
        var second = new TestFilter("second", InputOrder.Recorder);
        hub.Core.Add(first);
        hub.Core.Add(second);

        Assert.True(hub.Key(A, down: true));
        Assert.True(hub.Key(A, down: true)); // the keyboard's repeat
        Assert.True(hub.Key(A, down: false)); // kept although the keeper said nothing about ups

        Assert.Equal(["first key 41 down", "first key 41 down", "first key 41 up"], first.Heard);
        Assert.Empty(second.Heard);
        Assert.Equal(0, hub.Core.Kept);
    }

    [Fact]
    public void An_up_whose_down_nobody_kept_always_passes_on()
    {
        var hub = new TestHub();
        var greedy = new TestFilter("greedy", InputOrder.Triggers) { KeepKey = k => !k.Down };
        hub.Core.Add(greedy);

        Assert.False(hub.Key(A, down: true));
        Assert.False(hub.Key(A, down: false));
        Assert.Equal(["greedy key 41 down", "greedy key 41 up"], greedy.Heard);
    }

    [Fact]
    public void A_key_Windows_already_had_down_keeps_only_its_repeats_so_its_up_reaches_the_app()
    {
        var hub = new TestHub();
        hub.Core.Add(new TestFilter("lock", InputOrder.CleaningLock) { KeepKey = k => k.Down });
        hub.System.Down.Add(A); // held before the lock

        Assert.True(hub.Key(A, down: true));
        Assert.False(hub.Key(A, down: false));
    }

    [Fact]
    public void Chippis_own_input_passes_untouched_and_no_filter_hears_it_but_another_programs_counts_as_a_hands()
    {
        var hub = new TestHub();
        var filter = new TestFilter("f", InputOrder.Triggers) { KeepKey = _ => true, KeepMouse = _ => true };
        hub.Core.Add(filter);

        Assert.False(hub.Key(A, down: true, ours: true));
        Assert.False(hub.Mouse(TestHub.WmMButtonDown, ours: true));
        Assert.Empty(filter.Heard);

        Assert.True(hub.Key(A, down: true, injected: true));
        Assert.Equal(["f key 41 down"], filter.Heard);
    }

    [Fact]
    public void A_kept_button_keeps_its_up_and_moves_go_to_the_first_filter_that_keeps_them()
    {
        var hub = new TestHub();
        var radial = new TestFilter("radial", InputOrder.Triggers) { KeepMouse = m => m.Button == MouseButton.Middle };
        var scroll = new TestFilter("scroll", InputOrder.SmoothScrolling) { KeepMouse = m => m.Kind == MouseKind.Wheel };
        hub.Core.Add(radial);
        hub.Core.Add(scroll);

        Assert.True(hub.Mouse(TestHub.WmMButtonDown));
        Assert.False(hub.Mouse(TestHub.WmMouseMove, 5, 5));
        Assert.True(hub.Mouse(TestHub.WmMouseWheel, data: unchecked((uint)(120 << 16))));
        Assert.True(hub.Mouse(TestHub.WmMButtonUp));
        Assert.False(hub.Mouse(TestHub.WmLButtonDown));

        Assert.Equal(["scroll Move None", "scroll Wheel None", "scroll Down Left"], scroll.Heard);
        Assert.Equal(["radial Down Middle", "radial Move None", "radial Wheel None", "radial Up Middle", "radial Down Left"], radial.Heard);
    }

    [Fact]
    public void Back_and_forward_are_told_apart()
    {
        var hub = new TestHub();
        var filter = new TestFilter("f", InputOrder.Triggers);
        hub.Core.Add(filter);

        hub.Mouse(TestHub.WmXButtonDown, data: 1u << 16);
        hub.Mouse(TestHub.WmXButtonDown, data: 2u << 16);

        Assert.Equal(["f Down Back", "f Down Forward"], filter.Heard);
    }

    [Fact]
    public void A_hook_is_in_only_while_a_filter_needs_it_or_a_kept_key_waits_for_its_up()
    {
        var hub = new TestHub();
        var filter = new TestFilter("f", InputOrder.Triggers, InputDevices.Keyboard) { KeepKey = k => k.Down };
        var entry = hub.Core.Add(filter);
        Assert.Equal(InputDevices.Keyboard, hub.Hooks.In);

        filter.Devices = InputDevices.Both;
        hub.Core.Refresh(entry);
        Assert.Equal(InputDevices.Both, hub.Hooks.In);

        hub.Key(A, down: true);
        filter.Devices = InputDevices.None;
        hub.Core.Refresh(entry);
        Assert.Equal(InputDevices.Keyboard, hub.Hooks.In); // A's up hasn't come

        Assert.True(hub.Key(A, down: false));
        Assert.Equal(InputDevices.None, hub.Hooks.In);
    }

    [Fact]
    public void A_filter_taken_out_mid_press_still_has_its_up_kept_and_then_the_hook_comes_off()
    {
        var hub = new TestHub();
        var filter = new TestFilter("f", InputOrder.Recorder, InputDevices.Keyboard) { KeepKey = _ => true };
        var entry = hub.Core.Add(filter);
        hub.Key(A, down: true);

        hub.Core.Remove(entry);
        Assert.Equal(InputDevices.Keyboard, hub.Hooks.In);

        Assert.True(hub.Key(A, down: false));
        Assert.Equal(["f key 41 down"], filter.Heard);
        Assert.Equal(InputDevices.None, hub.Hooks.In);
    }

    [Fact]
    public void A_filter_that_takes_itself_out_while_keeping_a_key_keeps_the_hook_until_that_keys_up()
    {
        var hub = new TestHub();
        HubCore.Entry? entry = null;
        var filter = new TestFilter("recorder", InputOrder.Recorder, InputDevices.Keyboard);
        filter.KeepKey = k =>
        {
            hub.Core.Remove(entry!); // the recorder leaves at its first key
            return true;
        };
        entry = hub.Core.Add(filter);

        Assert.True(hub.Key(A, down: true));
        Assert.Equal(InputDevices.Keyboard, hub.Hooks.In);

        Assert.True(hub.Key(A, down: false));
        Assert.Equal(InputDevices.None, hub.Hooks.In);
    }

    [Fact]
    public void A_hook_Windows_refuses_fails_the_add_and_leaves_nothing_behind()
    {
        var hub = new TestHub();
        hub.Hooks.Refuse = InputDevices.Mouse;

        Assert.Throws<System.ComponentModel.Win32Exception>(() => hub.Core.Add(new TestFilter("f", InputOrder.Triggers)));

        Assert.Equal(InputDevices.None, hub.Hooks.In);
        hub.Hooks.Refuse = InputDevices.None;
        var other = new TestFilter("g", InputOrder.Triggers, InputDevices.Keyboard);
        hub.Core.Add(other);
        hub.Key(A, down: true);
        Assert.Equal(["g key 41 down"], other.Heard);
    }

    [Fact]
    public void Paused_filters_hear_nothing_new_but_the_up_of_a_key_they_kept_before_still_comes_to_them()
    {
        var hub = new TestHub();
        var pausing = new TestFilter("debounce", InputOrder.Debounce, InputDevices.Keyboard) { KeepKey = k => k.Down };
        var never = new TestFilter("lock", InputOrder.CleaningLock, InputDevices.Keyboard, pauses: false);
        hub.Core.Add(never);
        hub.Core.Add(pausing);

        Assert.True(hub.Key(A, down: true));
        hub.Core.Paused = true;
        Assert.True(hub.Key(A, down: false)); // no stuck key: the up of the kept down is still kept
        Assert.False(hub.Key(Esc, down: true));

        Assert.Equal(["debounce key 41 down", "debounce key 41 up"], pausing.Heard);
        Assert.Equal(["lock key 41 down", "lock key 1B down"], never.Heard);
    }

    [Fact]
    public void While_paused_a_hook_no_unpaused_filter_needs_comes_off_and_goes_back_after()
    {
        var hub = new TestHub();
        hub.Core.Add(new TestFilter("scroll", InputOrder.SmoothScrolling, InputDevices.Mouse));
        hub.Core.Add(new TestFilter("lock", InputOrder.CleaningLock, InputDevices.Keyboard, pauses: false));

        hub.Core.Paused = true;
        Assert.Equal(InputDevices.Keyboard, hub.Hooks.In);
        Assert.Null(hub.Mouse(TestHub.WmMouseMove, 1, 1)); // the game's mouse doesn't pass through Chippi at all

        hub.Core.Paused = false;
        Assert.Equal(InputDevices.Both, hub.Hooks.In);
    }

    [Fact]
    public void Mask_sends_the_unassigned_key_down_and_up_marked_as_chippis()
    {
        var hub = new TestHub();

        hub.Core.Mask();

        Assert.Equal([new SentKey(0xE8, false), new SentKey(0xE8, true)], hub.Sender.Sent);
        Assert.Equal([2], hub.Sender.Batches);
    }

    [Fact]
    public void The_hub_times_events_on_its_own_clock_taken_as_the_hook_is_called()
    {
        var hub = new TestHub();
        var times = new List<long>();
        hub.Core.Add(new TestFilter("f", InputOrder.Debounce) { KeepKey = k => { times.Add(k.Timestamp); return false; } });

        hub.Key(A, down: true);
        hub.System.Advance(TimeSpan.FromMilliseconds(7));
        hub.Key(A, down: false);

        Assert.Equal((long)(0.007 * System.Diagnostics.Stopwatch.Frequency), times[1] - times[0]);
    }
}
