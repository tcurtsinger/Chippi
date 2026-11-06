// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.UI.Displays;

namespace Chippi.Features.Displays.Tests;

/// <summary>The brightness engine over a fake monitor bus (docs/phase-7/displays.md, Tests).</summary>
public sealed class DisplayBrightnessTests
{
    internal sealed class FakeMonitor(string id) : IMonitor
    {
        public readonly object Gate = new();
        public readonly Dictionary<Vcp, int> Levels = new() { [Vcp.Brightness] = 50, [Vcp.Contrast] = 70 };
        public readonly List<(Vcp Code, int Raw)> Writes = [];
        public readonly List<Vcp> Reads = [];
        public int FailReads;
        public bool FailWrites;
        public bool NoContrast;
        public ManualResetEventSlim? Hold;

        /// <summary>A read waits here, as a DDC call that takes its time does.</summary>
        public ManualResetEventSlim? ReadHold;
        public readonly ManualResetEventSlim ReadStarted = new(false);
        public volatile bool InCall;
        public volatile bool Closed;
        public volatile bool ClosedDuringCall;

        /// <summary>A driver both monitors share: its calls are taken in turn, each taking <see cref="WriteMs"/>.</summary>
        public object? Driver;
        public int WriteMs;
        public long LastEnd;
        public ManualResetEventSlim WriteStarted = new(false);
        public string Id { get; } = id;
        public int Max => 100;

        public bool TryRead(Vcp code, out int current, out int max)
        {
            ReadStarted.Set();
            if (ReadHold is not null)
            {
                InCall = true;
                ReadHold.Wait(TimeSpan.FromSeconds(10));
                InCall = false;
            }
            lock (Gate)
            {
                Reads.Add(code);
                current = max = 0;
                if (FailReads > 0 && code == Vcp.Brightness) { FailReads--; return false; }
                if (code == Vcp.Contrast && NoContrast) return false;
                current = Levels[code];
                max = Max;
                return true;
            }
        }

        public bool TryWrite(Vcp code, int raw)
        {
            WriteStarted.Set();
            Hold?.Wait(TimeSpan.FromSeconds(10));
            if (Driver is not null)
                lock (Driver) Thread.Sleep(WriteMs);
            lock (Gate)
            {
                Writes.Add((code, raw));
                if (FailWrites) return false;
                Levels[code] = raw;
                LastEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                return true;
            }
        }

        public void Dispose()
        {
            ClosedDuringCall = InCall;
            Closed = true;
        }
    }

    internal sealed class FakeBus : IMonitorBus
    {
        public readonly Dictionary<string, FakeMonitor> Monitors = [];
        public int Opens;
        public IMonitor? Open(DisplayInfo display)
        {
            Interlocked.Increment(ref Opens);
            return Monitors.TryGetValue(display.Id, out var m) ? m : null;
        }
    }

    internal static DisplayInfo Info(string id, string name, nint handle, bool main = false) =>
        new(id, name + " (1920×1080)", handle, main) { Friendly = name };

    private sealed class Rig : IDisposable
    {
        public readonly FakeBus Bus = new();
        public long Now = 1_000_000;
        public readonly List<TimeSpan> Sleeps = [];
        public nint Pointer = 1;
        public IReadOnlyList<DisplayInfo> List;
        public readonly DisplayBrightness Engine;

        public Rig(params DisplayInfo[] displays)
        {
            List = displays;
            foreach (var d in displays) Bus.Monitors[d.Id] = new FakeMonitor(d.Id);
            Engine = new DisplayBrightness(Bus, () => List, () => Pointer, () => Interlocked.Read(ref Now), s => Sleeps.Add(s));
        }

        public FakeMonitor this[string id] => Bus.Monitors[id];

        public void Dispose() => Engine.Dispose();
    }

    private static void Until(Func<bool> condition, string what = "condition")
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(what);
            Thread.Sleep(2);
        }
    }

    private static Rig Two()
    {
        var rig = new Rig(Info("A", "OMEN 34c", 1, main: true), Info("B", "Samsung", 2));
        rig["B"].Levels[Vcp.Brightness] = 60;
        rig.Engine.Refresh();
        Until(() => rig.Engine.Statuses.All(s => s.Brightness is not null), "first reads");
        return rig;
    }

    [Fact]
    public void Reads_each_display_and_support_is_a_read_that_answers()
    {
        using var rig = new Rig(Info("A", "OMEN", 1, true), Info("B", "Samsung", 2));
        rig["B"].NoContrast = true;
        rig.Engine.Refresh();
        Until(() => rig.Engine.Statuses.All(s => s.Brightness is not null));
        var statuses = rig.Engine.Statuses;
        Assert.Equal([50, 50], statuses.Select(s => s.Brightness!.Value));
        Assert.True(statuses[0].Contrast);
        Assert.Equal(70, statuses[0].ContrastLevel);
        Assert.False(statuses[1].Contrast);
        Assert.Equal("OMEN 50% · Samsung 50%", rig.Engine.Tooltip);
    }

    [Fact]
    public void Latest_value_wins_while_a_write_is_in_flight_and_the_release_lands()
    {
        using var rig = Two();
        var a = rig["A"];
        a.Hold = new ManualResetEventSlim(false);
        rig.Engine.Set("A", Vcp.Brightness, 10);
        Assert.True(a.WriteStarted.Wait(TimeSpan.FromSeconds(5)));
        foreach (int v in new[] { 20, 30, 40, 55 }) rig.Engine.Set("A", Vcp.Brightness, v);
        a.Hold.Set();
        Until(() => a.Writes.Count == 2, "two writes");
        Assert.Equal([10, 55], a.Writes.Select(w => w.Raw));
        Assert.Equal(55, rig.Engine.Statuses[0].Brightness);
    }

    [Fact]
    public void A_value_equal_to_the_level_is_not_sent()
    {
        using var rig = Two();
        rig.Engine.Set("A", Vcp.Brightness, 50);
        rig.Engine.Set("A", Vcp.Brightness, 33);
        Until(() => rig["A"].Writes.Count == 1);
        rig.Engine.Set("A", Vcp.Brightness, 33);
        Thread.Sleep(50);
        Assert.Single(rig["A"].Writes);
    }

    [Fact]
    public void All_displays_keeps_the_differences_and_stops_a_display_at_its_limit()
    {
        using var rig = Two();
        rig["A"].Levels[Vcp.Brightness] = 50;
        rig["B"].Levels[Vcp.Brightness] = 95;
        rig.Engine.Refresh();
        Until(() => rig.Engine.Statuses[1].Brightness == 95);
        Assert.Equal(50, rig.Engine.AllLevel());
        rig.Engine.BeginAll();
        rig.Engine.MoveAll(60);
        Until(() => rig.Engine.Statuses[0].Brightness == 60 && rig.Engine.Statuses[1].Brightness == 100, "up");
        // The differences were taken at the start: B stays at its limit while A goes on, then comes back from its own start.
        rig.Engine.MoveAll(40);
        Until(() => rig.Engine.Statuses[0].Brightness == 40 && rig.Engine.Statuses[1].Brightness == 85, "down");
        rig.Engine.EndAll();
    }

    [Fact]
    public void All_displays_moves_contrast_and_a_queued_write_shows_as_the_level()
    {
        using var rig = Two();
        rig["A"].Levels[Vcp.Contrast] = 40;
        rig["B"].Levels[Vcp.Contrast] = 70;
        rig.Engine.Refresh();
        Until(() => rig.Engine.Statuses.All(s => s.ContrastLevel is not null), "contrast reads");
        Assert.Equal(40, rig.Engine.AllLevel(Vcp.Contrast));
        rig.Engine.BeginAll(Vcp.Contrast);
        rig.Engine.MoveAll(50, Vcp.Contrast);
        Until(() => rig.Engine.Statuses[0].ContrastLevel == 50 && rig.Engine.Statuses[1].ContrastLevel == 80, "contrast up");
        rig.Engine.EndAll();
        rig.Engine.Set("A", Vcp.Brightness, 77);
        Assert.Equal(77, rig.Engine.Statuses[0].Brightness);
    }

    [Fact]
    public void Keys_step_the_display_under_the_pointer_and_stop_at_100()
    {
        using var rig = Two();
        rig.Pointer = 2;
        rig.Engine.Step(10, BrightnessTarget.UnderPointer);
        Until(() => rig["B"].Writes.Count == 1);
        Assert.Equal(70, rig["B"].Writes[0].Raw);
        Assert.Empty(rig["A"].Writes);
        for (int i = 0; i < 5; i++)
        {
            rig.Engine.Step(10, BrightnessTarget.UnderPointer);
            Until(() => rig.Engine.Statuses[1].Brightness == Math.Min(100, 70 + 10 * (i + 1)));
        }
        Until(() => rig["B"].Levels[Vcp.Brightness] == 100, "monitor at 100");
    }

    [Fact]
    public void Keys_act_on_every_display_with_all_displays()
    {
        using var rig = Two();
        rig.Engine.Step(-10, BrightnessTarget.AllDisplays);
        Until(() => rig["A"].Writes.Count == 1 && rig["B"].Writes.Count == 1);
        Assert.Equal(40, rig["A"].Writes[0].Raw);
        Assert.Equal(50, rig["B"].Writes[0].Raw);
    }

    [Fact]
    public void Fast_presses_add_to_the_queued_value()
    {
        using var rig = Two();
        var a = rig["A"];
        a.Hold = new ManualResetEventSlim(false);
        rig.Engine.Step(10, BrightnessTarget.UnderPointer);
        Assert.True(a.WriteStarted.Wait(TimeSpan.FromSeconds(5)));
        int reads = a.Reads.Count;
        rig.Engine.Step(10, BrightnessTarget.UnderPointer);
        rig.Engine.Step(10, BrightnessTarget.UnderPointer);
        a.Hold.Set();
        Until(() => a.Writes.Count == 2);
        Assert.Equal([60, 80], a.Writes.Select(w => w.Raw));
        Assert.Equal(reads, a.Reads.Count); // a press during a write never reads
    }

    [Fact]
    public void A_press_on_a_stale_level_reads_first_and_writes_from_the_real_level()
    {
        using var rig = Two();
        rig["A"].Levels[Vcp.Brightness] = 30; // changed by the monitor's own buttons
        Interlocked.Add(ref rig.Now, 15_000);
        int reads = rig["A"].Reads.Count(c => c == Vcp.Brightness);
        rig.Engine.Step(10, BrightnessTarget.UnderPointer);
        Until(() => rig["A"].Writes.Count == 1);
        Assert.Equal(40, rig["A"].Writes[0].Raw);
        Assert.Equal(reads + 1, rig["A"].Reads.Count(c => c == Vcp.Brightness));
    }

    [Fact]
    public void A_fresh_level_is_not_read_again()
    {
        using var rig = Two();
        Interlocked.Add(ref rig.Now, 5_000);
        int reads = rig["A"].Reads.Count;
        rig.Engine.Step(10, BrightnessTarget.UnderPointer);
        Until(() => rig["A"].Writes.Count == 1);
        Assert.Equal(reads, rig["A"].Reads.Count);
    }

    [Fact]
    public void A_failed_read_is_tried_once_more_after_500_ms()
    {
        using var rig = new Rig(Info("A", "OMEN", 1, true));
        rig["A"].FailReads = 1;
        rig.Engine.Refresh();
        Until(() => rig.Engine.Statuses[0].Brightness is not null);
        Assert.Equal([TimeSpan.FromMilliseconds(500)], rig.Sleeps);
        Assert.False(rig.Engine.Statuses[0].Failed);
    }

    [Fact]
    public void A_display_that_doesnt_answer_fails_and_try_again_reads_only_that_display()
    {
        using var rig = Two();
        rig["B"].FailReads = 2;
        rig.Engine.Refresh();
        Until(() => rig.Engine.Statuses[1].Failed, "failed");
        int aReads = rig["A"].Reads.Count;
        rig.Engine.TryAgain("B");
        Until(() => !rig.Engine.Statuses[1].Failed && rig.Engine.Statuses[1].Brightness == 60, "recovered");
        Assert.Equal(aReads, rig["A"].Reads.Count);
    }

    [Fact]
    public void A_key_on_a_display_that_doesnt_answer_reports_it_for_the_bar()
    {
        using var rig = new Rig(Info("A", "OMEN", 1, true));
        rig["A"].FailReads = 100;
        rig.Engine.Refresh();
        Until(() => rig.Engine.Statuses[0].Failed);
        var reported = new List<(string, bool)>();
        rig.Engine.ControlFailed += (id, key) => { lock (reported) reported.Add((id, key)); };
        rig.Engine.Step(10, BrightnessTarget.UnderPointer);
        Until(() => { lock (reported) return reported.Count == 1; });
        Assert.Equal(("A", true), reported[0]);
    }

    [Fact]
    public void A_failed_write_keeps_the_last_level_and_is_flagged()
    {
        using var rig = Two();
        rig["A"].FailWrites = true;
        var reported = new List<bool>();
        rig.Engine.ControlFailed += (_, key) => { lock (reported) reported.Add(key); };
        rig.Engine.Set("A", Vcp.Brightness, 10);
        Until(() => rig.Engine.Statuses[0].WriteFailed);
        Assert.Equal(50, rig.Engine.Statuses[0].Brightness);
        Assert.Equal([false], reported); // a slider, not a key: the card shows it, not the bar
    }

    [Fact]
    public void A_display_removed_leaves_the_list_and_gets_no_more_writes()
    {
        using var rig = Two();
        var b = rig["B"];
        rig.List = [rig.List[0]];
        rig.Engine.Refresh();
        Assert.Single(rig.Engine.Statuses);
        rig.Engine.Set("B", Vcp.Brightness, 5);
        Thread.Sleep(50);
        Assert.Empty(b.Writes);
    }

    [Fact]
    public void A_display_change_reopens_the_handles()
    {
        using var rig = Two();
        int opens = rig.Bus.Opens;
        rig.Engine.Refresh(reopen: true);
        Until(() => rig.Bus.Opens == opens + 2);
    }

    [Fact]
    public void The_hotkey_call_returns_before_the_bus_is_called()
    {
        using var rig = Two();
        var a = rig["A"];
        a.Hold = new ManualResetEventSlim(false);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        rig.Engine.Step(10, BrightnessTarget.UnderPointer);
        Assert.True(timer.ElapsedMilliseconds < 200);
        Assert.True(a.WriteStarted.Wait(TimeSpan.FromSeconds(5)));
        a.Hold.Set();
    }

    [Fact]
    public void Same_named_displays_get_a_number_and_dont_otherwise()
    {
        var named = DisplayBrightness.Named([Info("1", "Dell U2720Q", 1, true), Info("2", "Dell U2720Q", 2), Info("3", "Samsung", 3)]);
        Assert.Equal(["Dell U2720Q 1", "Dell U2720Q 2", "Samsung"], named.Select(n => n.Name));
    }

    [Theory]
    [InlineData("brightness 40", true, 40)]
    [InlineData("bright 40%", true, 40)]
    [InlineData("Brightness 140", true, 100)]
    [InlineData("brightness", false, 0)]
    [InlineData("brightness up", false, 0)]
    public void Search_values(string text, bool parsed, int percent)
    {
        Assert.Equal(parsed, DisplayBrightness.TryParseLevel(text, out int got));
        Assert.Equal(percent, got);
    }

    [Fact]
    public void Only_brightness_and_contrast_are_ever_written()
    {
        using var rig = Two();
        rig.Engine.Set("A", Vcp.Brightness, 11);
        rig.Engine.Set("A", Vcp.Contrast, 22);
        rig.Engine.Step(10, BrightnessTarget.AllDisplays);
        Until(() => rig["A"].Writes.Count >= 2 && rig["B"].Writes.Count >= 1);
        Assert.All(rig["A"].Writes.Concat(rig["B"].Writes), w => Assert.True(w.Code is Vcp.Brightness or Vcp.Contrast));
        Assert.Equal([0x10, 0x12], Enum.GetValues<Vcp>().Select(c => (int)c));
    }
}

public sealed class DisplayRemovalTests
{
    // A removed display is dropped on the UI thread (WM_DISPLAYCHANGE): that must not wait for its monitor, and its handle
    // must outlive the call in flight.
    [Fact]
    public void Removing_a_display_never_waits_and_the_handle_closes_after_the_call_in_flight()
    {
        var a = DisplayBrightnessTests.Info("A", "OMEN", 1, true);
        var b = DisplayBrightnessTests.Info("B", "Samsung", 2);
        var bus = new DisplayBrightnessTests.FakeBus();
        bus.Monitors["A"] = new DisplayBrightnessTests.FakeMonitor("A");
        var slow = new DisplayBrightnessTests.FakeMonitor("B") { ReadHold = new ManualResetEventSlim(false), FailReads = 99 };
        bus.Monitors["B"] = slow;
        var sleeps = new List<TimeSpan>();
        IReadOnlyList<DisplayInfo> list = [a, b];
        using var engine = new DisplayBrightness(bus, () => list, () => 1, null, s => sleeps.Add(s));
        engine.Refresh();
        Assert.True(slow.ReadStarted.Wait(TimeSpan.FromSeconds(5)), "the read started");

        list = [a];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        engine.Refresh();
        Assert.True(clock.ElapsedMilliseconds < 100, $"Refresh waited {clock.ElapsedMilliseconds} ms");
        Thread.Sleep(50);
        Assert.False(slow.Closed, "the handle stays open while a call uses it");

        slow.ReadHold.Set();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!slow.Closed && DateTime.UtcNow < deadline) Thread.Sleep(2);
        Assert.True(slow.Closed, "the worker closed its own handle");
        Assert.False(slow.ClosedDuringCall);
        Assert.Empty(sleeps); // stopping skips the 500 ms retry
    }
}

public sealed class DisplayDragLagTests
{
    // The real monitors' DDC/CI calls are taken in turn by the driver (two plain threads reading take twice as long as one);
    // this keeps that in the test and checks the last write of an All displays drag lands soon after the hand stops.
    [Fact]
    public void The_last_write_of_an_All_displays_drag_lands_within_a_write_or_two_of_the_release()
    {
        var driver = new object();
        var a = DisplayBrightnessTests.Info("A", "OMEN", 1, true);
        var b = DisplayBrightnessTests.Info("B", "Samsung", 2);
        var bus = new DisplayBrightnessTests.FakeBus();
        bus.Monitors["A"] = new DisplayBrightnessTests.FakeMonitor("A") { Driver = driver, WriteMs = 30 };
        bus.Monitors["B"] = new DisplayBrightnessTests.FakeMonitor("B") { Driver = driver, WriteMs = 30 };
        bus.Monitors["B"].Levels[Vcp.Brightness] = 60;
        using var engine = new DisplayBrightness(bus, () => [a, b], () => 1);
        engine.Refresh();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (engine.Statuses.Any(s => s.Brightness is null) && DateTime.UtcNow < deadline) Thread.Sleep(2);
        engine.BeginAll();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 1; i <= 50; i++)
        {
            engine.MoveAll(50 - i / 4);
            long until = clock.ElapsedTicks + 16 * System.Diagnostics.Stopwatch.Frequency / 1000;
            while (clock.ElapsedTicks < until) Thread.SpinWait(50);
        }
        engine.EndAll();
        long release = System.Diagnostics.Stopwatch.GetTimestamp();
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (engine.Statuses.Any(s => s.Brightness != (s.Id == "A" ? 38 : 48)) && DateTime.UtcNow < deadline) Thread.Sleep(2);
        long last = Math.Max(bus.Monitors["A"].LastEnd, bus.Monitors["B"].LastEnd);
        double lagMs = (last - release) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Assert.Equal([38, 48], engine.Statuses.Select(s => s.Brightness!.Value));
        Assert.True(lagMs < 300, $"the last write landed {lagMs:F0} ms after the release");
    }

}
