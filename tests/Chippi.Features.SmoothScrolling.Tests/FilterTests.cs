// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Input;

namespace Chippi.Features.SmoothScrolling.Tests;

public sealed class FilterTests
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;

    [Fact]
    public void A_glide_sends_exactly_what_was_queued_in_small_steps()
    {
        var engine = new ScrollEngine(Stopwatch.Frequency);
        engine.Add(0, 360, 1, 150, 0);
        int total = 0, steps = 0, largest = 0;
        for (long t = 8; t <= 400 && !engine.Idle; t += 8)
        {
            (int v, _) = engine.Step(t * Ms);
            total += v;
            if (v != 0) steps++;
            largest = Math.Max(largest, v);
        }
        Assert.Equal(360, total);
        Assert.True(steps > 10, $"{steps} steps");
        Assert.True(largest < 120, $"largest {largest}");
    }

    [Fact]
    public void The_first_frame_already_moves_and_the_glide_settles_near_the_response_time()
    {
        var engine = new ScrollEngine(Stopwatch.Frequency);
        engine.Add(0, 120, 1, 150, 0);
        Assert.NotEqual(0, engine.Step(8 * Ms).Vertical);
        long settled = 8;
        while (!engine.Idle) { engine.Step((settled += 8) * Ms); }
        Assert.InRange(settled, 100, 300);
    }

    [Fact]
    public void Equal_notches_give_equal_distance_slow_or_fast()
    {
        Assert.Equal(Run(gapMs: 200), Run(gapMs: 10));
        static int Run(int gapMs)
        {
            var engine = new ScrollEngine(Stopwatch.Frequency);
            long now = 0;
            int total = 0;
            for (int i = 0; i < 6; i++)
            {
                engine.Add(0, 120, 1, 150, now * Ms);
                for (int k = 0; k < gapMs / 8; k++) total += engine.Step((now += 8) * Ms).Vertical;
            }
            while (!engine.Idle) total += engine.Step((now += 8) * Ms).Vertical;
            return total;
        }
    }

    [Fact]
    public void A_reversal_drops_the_tail_of_the_old_direction()
    {
        var engine = new ScrollEngine(Stopwatch.Frequency);
        engine.Add(0, 600, 1, 400, 0);
        int sent = engine.Step(16 * Ms).Vertical;
        engine.Add(0, -120, 1, 400, 16 * Ms);
        int total = sent;
        for (long t = 24; !engine.Idle; t += 8) total += engine.Step(t * Ms).Vertical;
        Assert.Equal(sent - 120, total);
    }

    [Fact]
    public void A_new_target_drops_what_was_queued_for_the_old_one_and_cancel_empties_it()
    {
        var engine = new ScrollEngine(Stopwatch.Frequency);
        engine.Add(0, 600, 1, 400, 0);
        engine.Add(0, 120, 2, 400, 0);
        int total = 0;
        for (long t = 8; !engine.Idle; t += 8) total += engine.Step(t * Ms).Vertical;
        Assert.Equal(120, total);
        engine.Add(1, 240, 2, 100, 0);
        engine.Cancel();
        Assert.True(engine.Idle);
        Assert.Equal((0, 0), engine.Step(Ms * 50));
    }

    [Fact]
    public void Both_axes_glide_and_zero_response_sends_it_all_at_once()
    {
        var engine = new ScrollEngine(Stopwatch.Frequency);
        engine.Add(0, 120, 1, 0, 0);
        engine.Add(1, -240, 1, 0, 0);
        Assert.Equal((120, -240), engine.Step(8 * Ms));
        Assert.True(engine.Idle);
    }

    [Fact]
    public void Smooth_takes_a_whole_notch_and_sends_it_back_from_the_tick()
    {
        var (filter, env) = Make(new ScrollTuning(true, false, 100, 150, 0, []));
        Assert.True(filter.Mouse(Wheel(120)));
        Assert.Equal(1, env.Wakes);
        long now = 0;
        while (filter.Tick(now += 8 * Ms)) { }
        Assert.Equal(120, env.Vertical);
        Assert.True(env.Sends.Count > 5);
    }

    [Fact]
    public void Fine_deltas_modifiers_injected_and_no_window_pass_untouched()
    {
        var (filter, env) = Make(new ScrollTuning(true, true, 100, 150, 0, []));
        Assert.False(filter.Mouse(Wheel(30)));
        Assert.False(filter.Mouse(Wheel(-60)));
        Assert.False(filter.Mouse(Wheel(120, injected: true)));
        env.Modifier = true;
        Assert.False(filter.Mouse(Wheel(120)));
        env.Modifier = false;
        env.Window = 0;
        Assert.False(filter.Mouse(Wheel(120)));
        Assert.False(filter.Mouse(new MouseInput(MouseKind.Move, MouseButton.None, 5, 5, 0, false, 0)));
        Assert.False(filter.Mouse(new MouseInput(MouseKind.Down, MouseButton.Left, 5, 5, 0, false, 0)));
        Assert.Empty(env.Sends);
        Assert.Equal(0, env.Wakes);
    }

    [Fact]
    public void Linear_makes_a_big_driver_message_one_notch_and_leaves_a_plain_one_alone()
    {
        var (filter, env) = Make(new ScrollTuning(false, true, 100, 150, 0, []));
        Assert.False(filter.Mouse(Wheel(120)), "a plain notch is the app's own");
        Assert.True(filter.Mouse(Wheel(480)));
        long now = 0;
        while (filter.Tick(now += 8 * Ms)) { }
        Assert.True(filter.Mouse(Wheel(-360)));
        while (filter.Tick(now += 8 * Ms)) { }
        Assert.Equal([120, -120], env.Sends.Select(s => s.Delta));
    }

    [Fact]
    public void Distance_and_a_per_app_speed_multiply_and_an_app_left_alone_passes()
    {
        var (filter, env) = Make(new ScrollTuning(true, false, 200, 100, 0, [new(@"C:\Apps\Slow.exe", 50), new(@"C:\Apps\Quiet.exe", 0)]));
        env.Path = @"D:\other\slow.EXE";
        Assert.True(filter.Mouse(Wheel(120)));
        long now = 0;
        while (filter.Tick(now += 8 * Ms)) { }
        Assert.Equal(120, env.Vertical); // 200% of 50%

        env.Sends.Clear();
        env.Path = @"C:\Apps\Quiet.exe";
        Assert.False(filter.Mouse(Wheel(120)));
        env.Path = null;
        Assert.True(filter.Mouse(Wheel(120)));
        while (filter.Tick(now += 8 * Ms)) { }
        Assert.Equal(240, env.Vertical);
    }

    [Fact]
    public void Moving_to_another_window_a_pause_or_a_closed_window_ends_the_glide_with_no_tail()
    {
        foreach (var how in new[] { "window", "pause", "closed" })
        {
            bool paused = false;
            var (filter, env) = Make(new ScrollTuning(true, false, 100, 400, 0, []), () => paused);
            filter.Mouse(Wheel(600));
            long now = 0;
            filter.Tick(now += 8 * Ms);
            int before = env.Vertical;
            Assert.NotEqual(0, before);
            switch (how)
            {
                case "window": env.CursorWindow = 99; break;
                case "pause": paused = true; break;
                default: env.Alive = false; break;
            }
            Assert.False(filter.Tick(now += 8 * Ms));
            Assert.False(filter.Tick(now += 8 * Ms));
            Assert.Equal(before, env.Vertical);
        }
    }

    [Fact]
    public void Disposing_the_filter_drops_the_glide()
    {
        var (filter, env) = Make(new ScrollTuning(true, false, 100, 400, 0, []));
        filter.Mouse(Wheel(600));
        filter.Dispose();
        Assert.False(filter.Tick(8 * Ms));
        Assert.Empty(env.Sends);
    }

    [Fact]
    public void Horizontal_wheel_glides_sideways()
    {
        var (filter, env) = Make(new ScrollTuning(true, false, 100, 100, 0, []));
        Assert.True(filter.Mouse(new MouseInput(MouseKind.HorizontalWheel, MouseButton.None, 10, 10, 120, false, 0)));
        long now = 0;
        while (filter.Tick(now += 8 * Ms)) { }
        Assert.All(env.Sends, s => Assert.True(s.Horizontal));
        Assert.Equal(120, env.Sends.Sum(s => s.Delta));
    }

    private static MouseInput Wheel(int delta, bool injected = false) => new(MouseKind.Wheel, MouseButton.None, 10, 10, delta, injected, 0);

    private static (ScrollFilter, FakeEnv) Make(ScrollTuning tuning, Func<bool>? paused = null)
    {
        var env = new FakeEnv();
        return (new ScrollFilter(env, paused ?? (() => false)) { Tuning = tuning }, env);
    }

    private sealed class FakeEnv : IScrollEnvironment
    {
        public nint Window { get; set; } = 7;
        public nint CursorWindow { get; set; } = 7;
        public bool Alive { get; set; } = true;
        public string? Path { get; set; }
        public bool Modifier { get; set; }
        public int Wakes { get; private set; }
        public List<(bool Horizontal, int Delta)> Sends { get; } = [];
        public int Vertical => Sends.Where(s => !s.Horizontal).Sum(s => s.Delta);

        public nint WindowAt(int x, int y) => Window;
        public nint WindowUnderCursor() => CursorWindow;
        public bool IsWindow(nint window) => Alive;
        public string? AppPath(nint window) => Path;
        public bool ModifierDown() => Modifier;
        public void Send(bool horizontal, int delta) => Sends.Add((horizontal, delta));
        public void Wake() => Wakes++;
    }
}
