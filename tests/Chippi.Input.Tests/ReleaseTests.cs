// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;

namespace Chippi.Input.Tests;

/// <summary>
/// No modifier Chippi sent down stays down (architecture §2): each way out sends exactly one up for each held modifier.
/// The filter holds Ctrl+Alt+Shift for Caps Lock, as Shortcuts' Caps Lock combos will.
/// </summary>
public class ReleaseTests
{
    private const uint Caps = 0x14, A = 0x41;
    private const ushort Ctrl = 0xA2, Alt = 0xA4, Shift = 0xA0;
    private static readonly ushort[] Held = [Ctrl, Alt, Shift];

    private static (TestHub Hub, HubCore.Entry Entry) Holding()
    {
        var hub = new TestHub();
        var filter = new TestFilter("caps", InputOrder.Triggers, InputDevices.Keyboard) { KeepKey = k => k.Vk == Caps };
        var entry = hub.Core.Add(filter);
        Assert.True(hub.Key(Caps, down: true));
        hub.Core.HoldModifiers(entry, Caps, Held);
        Assert.Equal(1, hub.Sender.Downs(Ctrl));
        return (hub, entry);
    }

    private static void ReleasedOnce(TestHub hub)
    {
        foreach (var vk in Held)
            Assert.Equal(1, hub.Sender.Ups(vk));
        // One batch for the ups, after the one for the downs, in the reverse order.
        Assert.Equal([3, 3], hub.Sender.Batches);
        Assert.Equal([Shift, Alt, Ctrl], hub.Sender.Sent.Skip(3).Select(k => k.Vk));
    }

    [Fact]
    public void Overlapping_chords_release_a_shared_modifier_only_after_the_last_key()
    {
        var hub = new TestHub();
        var entry = hub.Core.Add(new TestFilter("caps", InputOrder.Triggers, InputDevices.Keyboard));
        hub.Core.HoldModifiers(entry, 0x41, Held);
        hub.Core.HoldModifiers(entry, 0x42, Held);
        foreach (ushort vk in Held) Assert.Equal(1, hub.Sender.Downs(vk));
        hub.Key(0x41, down: false);
        foreach (ushort vk in Held) Assert.Equal(0, hub.Sender.Ups(vk));
        hub.Key(0x42, down: false);
        foreach (ushort vk in Held) Assert.Equal(1, hub.Sender.Ups(vk));
    }

    [Fact]
    public void Physical_modifiers_are_never_owned_or_released()
    {
        var hub = new TestHub();
        hub.System.Down.Add(Ctrl);
        var entry = hub.Core.Add(new TestFilter("caps", InputOrder.Triggers, InputDevices.Keyboard));
        hub.Core.HoldModifiers(entry, 0x41, Held);
        hub.Core.Remove(entry);
        Assert.Equal(0, hub.Sender.Downs(Ctrl));
        Assert.Equal(0, hub.Sender.Ups(Ctrl));
        Assert.Equal(1, hub.Sender.Ups(Alt));
    }
    [Fact]
    public void Its_key_coming_up()
    {
        var (hub, _) = Holding();
        hub.Key(A, down: true);
        Assert.DoesNotContain(hub.Sender.Sent, k => k.Up);

        hub.Key(Caps, down: false);
        hub.Key(Caps, down: false);

        ReleasedOnce(hub);
    }

    [Fact]
    public void The_filter_turning_off()
    {
        var (hub, entry) = Holding();

        hub.Core.Remove(entry);
        hub.Core.Remove(entry);

        ReleasedOnce(hub);
    }

    [Fact]
    public void The_filter_no_longer_needing_the_keyboard()
    {
        var (hub, entry) = Holding();
        ((TestFilter)entry.Filter).Devices = InputDevices.None;

        hub.Core.Refresh(entry);

        ReleasedOnce(hub);
    }

    [Fact]
    public void Chippi_quitting()
    {
        var (hub, _) = Holding();

        hub.Core.Stop();

        ReleasedOnce(hub);
        Assert.Equal(InputDevices.None, hub.Hooks.In);
    }

    [Fact]
    public void The_hook_found_gone()
    {
        var (hub, _) = Holding();
        hub.Hooks.Dropped = InputDevices.Keyboard;
        hub.System.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Null(hub.Key(A, down: true)); // a key the dropped hook never hears

        hub.Tick(6);

        ReleasedOnce(hub);
        Assert.Equal(1, hub.Core.Reinstalls);
    }

    [Fact]
    public void The_session_locking_or_the_desktop_switching()
    {
        var (hub, _) = Holding();

        hub.Core.SessionChanged();
        hub.Core.SessionChanged();

        ReleasedOnce(hub);
    }

    [Fact]
    public void A_pause_beginning()
    {
        var (hub, _) = Holding();

        hub.Core.Paused = true;
        hub.Core.Paused = false;
        hub.Core.Paused = true;

        ReleasedOnce(hub);
    }

    [Fact]
    public void Five_seconds_without_a_key()
    {
        var (hub, _) = Holding();
        hub.Tick(4);
        Assert.DoesNotContain(hub.Sender.Sent, k => k.Up);

        hub.Tick(2);
        hub.Tick(10);

        ReleasedOnce(hub);
    }

    [Fact]
    public void Not_while_the_held_key_repeats()
    {
        var (hub, _) = Holding();
        for (int i = 0; i < 20; i++)
        {
            hub.System.Advance(TimeSpan.FromMilliseconds(500));
            hub.Key(Caps, down: true); // the keyboard's repeat
            hub.Core.Tick();
        }

        Assert.DoesNotContain(hub.Sender.Sent, k => k.Up);
    }

    [Fact]
    public void Chippis_own_modifier_keys_coming_back_through_the_hook_change_nothing()
    {
        var (hub, _) = Holding();

        hub.Key(Ctrl, down: false, ours: true);

        Assert.DoesNotContain(hub.Sender.Sent, k => k.Up);
    }
}
