// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;

namespace Chippi.Features.Debounce.Tests;

/// <summary>The filters on an injected clock. A kept down's up reaches the filter and is kept, as the hub does.</summary>
public class FilterTests
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;
    private const uint E = 0x45, Space = 0x20;

    private static MouseInput Click(MouseKind kind, long ms, MouseButton button = MouseButton.Left, bool injected = false) =>
        new(kind, button, 0, 0, 0, injected, 1000 * Ms + ms * Ms);

    private static KeyInput Key(bool down, long ms, uint vk = E, bool wasDown = false, bool injected = false) =>
        new(vk, 0, down, wasDown, injected, false, 1000 * Ms + ms * Ms);

    [Fact]
    public void A_press_inside_the_window_after_a_release_is_dropped_with_its_release()
    {
        var f = new ClickDebounceFilter();
        Assert.False(f.Mouse(Click(MouseKind.Down, 0)));
        Assert.False(f.Mouse(Click(MouseKind.Up, 80)));
        Assert.True(f.Mouse(Click(MouseKind.Down, 82)));
        Assert.True(f.Mouse(Click(MouseKind.Up, 90)));
        Assert.Equal(1, f.Ignored);
    }

    [Fact]
    public void A_press_outside_the_window_passes_and_the_up_of_an_accepted_press_always_passes()
    {
        var f = new ClickDebounceFilter();
        f.Mouse(Click(MouseKind.Down, 0));
        f.Mouse(Click(MouseKind.Up, 60));
        Assert.False(f.Mouse(Click(MouseKind.Down, 60 + 25)));
        Assert.False(f.Mouse(Click(MouseKind.Up, 200)));
        Assert.Equal(0, f.Ignored);
    }

    [Fact]
    public void An_orphan_up_passes_and_buttons_are_independent()
    {
        var f = new ClickDebounceFilter();
        Assert.False(f.Mouse(Click(MouseKind.Up, 0)));
        Assert.True(f.Mouse(Click(MouseKind.Down, 1))); // the orphan up started a window for left
        f.Mouse(Click(MouseKind.Up, 40));
        Assert.False(f.Mouse(Click(MouseKind.Down, 41, MouseButton.Right)));
    }

    [Theory]
    [InlineData(MouseButton.Back)]
    [InlineData(MouseButton.Forward)]
    public void Side_buttons_and_wheels_are_never_touched(MouseButton button)
    {
        var f = new ClickDebounceFilter();
        f.Mouse(Click(MouseKind.Up, 0, button));
        Assert.False(f.Mouse(Click(MouseKind.Down, 1, button)));
        Assert.False(f.Mouse(Click(MouseKind.Wheel, 2, MouseButton.None)));
    }

    [Fact]
    public void Injected_input_is_left_alone_unless_the_probe_asks()
    {
        var f = new ClickDebounceFilter();
        f.Mouse(Click(MouseKind.Up, 0, injected: true));
        Assert.False(f.Mouse(Click(MouseKind.Down, 1, injected: true)));
        var probe = new ClickDebounceFilter { IncludeInjected = true };
        probe.Mouse(Click(MouseKind.Up, 0, injected: true));
        Assert.True(probe.Mouse(Click(MouseKind.Down, 1, injected: true)));
    }

    [Fact]
    public void Silence_and_time_going_backwards_start_over()
    {
        var f = new ClickDebounceFilter { WindowMs = 100 };
        f.Mouse(Click(MouseKind.Up, 5000));
        Assert.False(f.Mouse(Click(MouseKind.Down, 5000 + 6000 - 10)));
        var g = new ClickDebounceFilter { WindowMs = 100 };
        g.Mouse(Click(MouseKind.Up, 500));
        Assert.False(g.Mouse(Click(MouseKind.Down, 10)));
    }

    [Fact]
    public void The_window_follows_the_setting()
    {
        var f = new ClickDebounceFilter { WindowMs = 50 };
        f.Mouse(Click(MouseKind.Up, 0));
        Assert.True(f.Mouse(Click(MouseKind.Down, 49)));
        f.Mouse(Click(MouseKind.Up, 60));
        f.WindowMs = 5;
        Assert.False(f.Mouse(Click(MouseKind.Down, 66)));
    }

    [Fact]
    public void A_key_that_bounces_back_down_after_its_release_is_dropped_with_its_up()
    {
        var f = new KeyDebounceFilter { WindowMs = 20 };
        Assert.False(f.Key(Key(true, 0)));
        Assert.False(f.Key(Key(false, 60)));
        Assert.True(f.Key(Key(true, 64)));
        Assert.True(f.Key(Key(false, 70)));
        Assert.Equal(1, f.Ignored);
    }

    [Fact]
    public void A_second_down_while_the_key_is_still_down_inside_the_window_is_dropped_and_its_up_passes()
    {
        var f = new KeyDebounceFilter { WindowMs = 20 };
        f.Key(Key(true, 0));
        Assert.True(f.Key(Key(true, 5, wasDown: true)));
        Assert.False(f.Key(Key(false, 90)));
    }

    [Fact]
    public void Auto_repeat_past_the_window_passes()
    {
        var f = new KeyDebounceFilter { WindowMs = 200 };
        f.Key(Key(true, 0));
        Assert.False(f.Key(Key(true, 250, wasDown: true)));
        Assert.False(f.Key(Key(true, 283, wasDown: true)));
        Assert.Equal(0, f.Ignored);
    }

    [Fact]
    public void A_different_key_right_after_a_release_passes_and_a_window_of_zero_is_off()
    {
        var f = new KeyDebounceFilter { WindowMs = 20 };
        f.Key(Key(true, 0));
        f.Key(Key(false, 30));
        Assert.False(f.Key(Key(true, 31, Space)));
        var off = new KeyDebounceFilter { WindowMs = 0 };
        off.Key(Key(true, 0));
        off.Key(Key(false, 1));
        Assert.False(off.Key(Key(true, 2)));
    }

    [Fact]
    public void A_key_with_its_own_window_uses_it_while_others_use_the_default()
    {
        var f = new KeyDebounceFilter { WindowMs = 5 };
        f.SetOwn([(E, 40)]);
        f.Key(Key(true, 0));
        f.Key(Key(false, 100));
        Assert.True(f.Key(Key(true, 130)));
        f.Key(Key(false, 135));
        f.Key(Key(true, 200, Space));
        f.Key(Key(false, 300, Space));
        Assert.False(f.Key(Key(true, 320, Space)));
    }

    [Fact]
    public void A_key_with_its_own_window_works_with_the_default_off()
    {
        var f = new KeyDebounceFilter { WindowMs = 0 };
        f.SetOwn([(E, 30)]);
        f.Key(Key(true, 0));
        f.Key(Key(false, 100));
        Assert.True(f.Key(Key(true, 110)));
    }

    [Fact]
    public void Key_state_resets_after_five_seconds_of_silence_and_when_time_goes_back()
    {
        var f = new KeyDebounceFilter { WindowMs = 200 };
        f.Key(Key(true, 0));
        f.Key(Key(false, 10));
        Assert.False(f.Key(Key(true, 10 + 6000)));
        var g = new KeyDebounceFilter { WindowMs = 200 };
        g.Key(Key(true, 1000));
        g.Key(Key(false, 1010));
        Assert.False(g.Key(Key(true, 5)));
    }

    [Fact]
    public void Injected_keys_are_left_alone()
    {
        var f = new KeyDebounceFilter { WindowMs = 50 };
        f.Key(Key(true, 0, injected: true));
        f.Key(Key(false, 10, injected: true));
        Assert.False(f.Key(Key(true, 12, injected: true)));
    }

    [Fact]
    public void Both_filters_pause_and_sit_in_their_place()
    {
        Assert.True(new ClickDebounceFilter().Pauses);
        Assert.True(new KeyDebounceFilter().Pauses);
        Assert.Equal(InputOrder.Debounce, new KeyDebounceFilter().Order);
        Assert.Equal(InputDevices.Mouse, new ClickDebounceFilter().Devices);
        Assert.Equal(InputDevices.Keyboard, new KeyDebounceFilter().Devices);
    }

    [Fact]
    public void Each_event_costs_microseconds()
    {
        // The filters answer from memory. Time 200,000 events each, a stopwatch read around every call.
        var click = new ClickDebounceFilter();
        var key = new KeyDebounceFilter();
        key.SetOwn([(E, 30)]);
        var clickTicks = new long[200_000];
        var keyTicks = new long[200_000];
        for (int i = 0; i < 200_000; i++)
        {
            var down = i % 2 == 0;
            var m = Click(down ? MouseKind.Down : MouseKind.Up, i * 40L);
            long t0 = Stopwatch.GetTimestamp();
            click.Mouse(m);
            clickTicks[i] = Stopwatch.GetTimestamp() - t0;
            var k = Key(down, i * 40L, (uint)(0x41 + i % 26));
            t0 = Stopwatch.GetTimestamp();
            key.Key(k);
            keyTicks[i] = Stopwatch.GetTimestamp() - t0;
        }
        double ClickP99() => Percentile(clickTicks, 0.99);
        double KeyP99() => Percentile(keyTicks, 0.99);
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is { Length: > 0 } folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "debounce-filter-cost.txt"),
                $"Click filter, 200000 events: median {Percentile(clickTicks, 0.5):F3} us, p99 {ClickP99():F3} us, p99.9 {Percentile(clickTicks, 0.999):F3} us.\n" +
                $"Key filter, 200000 events: median {Percentile(keyTicks, 0.5):F3} us, p99 {KeyP99():F3} us, p99.9 {Percentile(keyTicks, 0.999):F3} us.\n");
        }
        Assert.True(ClickP99() < 50 && KeyP99() < 50, $"click p99 {ClickP99():F2} us, key p99 {KeyP99():F2} us");
    }

    private static double Percentile(long[] ticks, double p)
    {
        var sorted = (long[])ticks.Clone();
        Array.Sort(sorted);
        return sorted[(int)(p * (sorted.Length - 1))] * 1_000_000.0 / Stopwatch.Frequency;
    }
}
