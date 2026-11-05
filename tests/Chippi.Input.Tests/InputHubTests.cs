// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;

namespace Chippi.Input.Tests;

/// <summary>
/// The real hub's thread and calls, with filters that need no device, so no hook goes in: real hooks are checked only in
/// Windows Sandbox (Sandbox/README.md).
/// </summary>
public class InputHubTests
{
    [Fact]
    public void Calls_from_other_threads_run_on_the_hubs_thread_and_a_filter_needing_nothing_puts_in_no_hook()
    {
        using var hub = new InputHub();
        var filter = new TestFilter("f", InputOrder.Triggers, InputDevices.None);

        using var registration = hub.Add(filter);
        registration.Refresh();
        hub.Paused = true;

        Assert.True(hub.Paused);
        Assert.Equal(InputDevices.None, hub.Installed);
        Assert.Equal(0, hub.Reinstalls);
    }

    [Fact]
    public void After_dispose_the_hub_refuses_new_filters_and_old_registrations_go_quietly()
    {
        var hub = new InputHub();
        var registration = hub.Add(new TestFilter("f", InputOrder.Triggers, InputDevices.None));

        hub.Dispose();
        hub.Dispose();

        registration.Dispose();
        Assert.Throws<ObjectDisposedException>(() => hub.Add(new TestFilter("g", InputOrder.Triggers, InputDevices.None)));
    }

    [Fact]
    public void Two_hubs_in_one_process_each_get_their_own_window()
    {
        using var first = new InputHub();
        using var second = new InputHub();

        using var a = first.Add(new TestFilter("a", InputOrder.Triggers, InputDevices.None));
        using var b = second.Add(new TestFilter("b", InputOrder.Triggers, InputDevices.None));
        first.Paused = true;

        Assert.True(first.Paused);
        Assert.False(second.Paused);
    }
}
