// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;

namespace Chippi.Input.Tests;

/// <summary>Windows drops a low-level hook it thinks is slow without saying so; the hub notices and puts it back.</summary>
public class WatchdogTests
{
    private const uint A = 0x41;

    [Fact]
    public void A_keyboard_hook_dropped_behind_the_hubs_back_is_put_back_within_five_seconds_and_hears_again()
    {
        var hub = new TestHub();
        var filter = new TestFilter("f", InputOrder.Triggers, InputDevices.Keyboard);
        hub.Core.Add(filter);
        hub.Key(A, down: true);
        hub.Key(A, down: false);

        hub.Hooks.Dropped = InputDevices.Keyboard;
        hub.System.Advance(TimeSpan.FromMilliseconds(300));
        hub.WindowsSawInput();
        Assert.Null(hub.Key(A, down: true)); // nobody hears it

        hub.Tick(5);
        Assert.Equal(0, hub.Core.Reinstalls);
        hub.Tick(1);

        Assert.Equal(1, hub.Core.Reinstalls);
        Assert.Equal(["install Keyboard", "install Keyboard"], hub.Hooks.Calls);
        hub.Key(A, down: true);
        Assert.Equal(3, filter.Heard.Count);
    }

    [Fact]
    public void A_mouse_hook_that_hears_nothing_while_the_pointer_moves_is_put_back()
    {
        var hub = new TestHub();
        hub.Core.Add(new TestFilter("f", InputOrder.Triggers, InputDevices.Mouse));
        hub.Hooks.Dropped = InputDevices.Mouse;

        for (int i = 1; i <= 6; i++)
        {
            Assert.Null(hub.Mouse(TestHub.WmMouseMove, 40 * i, 40)); // nobody hears it
            hub.Tick(1);
        }
        Assert.Equal(0, hub.Core.Reinstalls);
        hub.Tick(1);

        Assert.Equal(1, hub.Core.Reinstalls);
        Assert.False(hub.Mouse(TestHub.WmMouseMove, 41, 41));
    }

    [Fact]
    public void A_hook_that_hears_is_left_alone()
    {
        var hub = new TestHub();
        hub.Core.Add(new TestFilter("f", InputOrder.Triggers, InputDevices.Both));
        for (int i = 0; i < 30; i++)
        {
            hub.System.Advance(TimeSpan.FromMilliseconds(500));
            hub.Mouse(TestHub.WmMouseMove, i, i);
            hub.Key(A, down: i % 2 == 0);
            hub.Core.Tick();
        }

        Assert.Equal(0, hub.Core.Reinstalls);
    }

    [Fact]
    public void Mouse_input_with_only_the_keyboard_hook_in_is_not_taken_for_a_deaf_keyboard_hook()
    {
        var hub = new TestHub();
        hub.Core.Add(new TestFilter("lock", InputOrder.CleaningLock, InputDevices.Keyboard));
        for (int i = 0; i < 20; i++)
        {
            hub.System.Cursor = (i * 10, 0); // the mouse moving, which no hook of ours hears
            hub.WindowsSawInput();
            hub.Tick(1);
        }

        Assert.Equal(0, hub.Core.Reinstalls);
    }

    [Fact]
    public void A_hook_is_put_back_at_most_once_a_minute()
    {
        var hub = new TestHub();
        hub.Core.Add(new TestFilter("f", InputOrder.Triggers, InputDevices.Keyboard));
        for (int second = 0; second < 90; second++)
        {
            // Over an admin window the hook hears nothing by design, however often it's put back.
            hub.Hooks.Dropped = InputDevices.Keyboard;
            hub.WindowsSawInput();
            hub.Tick(1);
        }

        Assert.Equal(2, hub.Core.Reinstalls); // at about 6 s and 66 s
    }

    [Fact]
    public void No_hook_in_means_no_watchdog()
    {
        var hub = new TestHub();
        hub.Core.Add(new TestFilter("f", InputOrder.Triggers, InputDevices.None));
        hub.WindowsSawInput();
        hub.System.Cursor = (500, 500);

        hub.Tick(30);

        Assert.Equal(0, hub.Core.Reinstalls);
        Assert.Empty(hub.Hooks.Calls);
    }
}
