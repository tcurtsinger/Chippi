// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using Chippi.UI.Displays;

namespace Chippi.Features.Displays.Tests;

/// <summary>
/// Writes the real monitors' brightness and contrast for about a minute and puts them back (docs/phase-7/displays.md, Tests).
/// Runs only when CHIPPI_DISPLAYS_WRITE names a report file, and only with the owner's yes: it moves his screens.
/// </summary>
public sealed class RealMonitorWriteTests
{
    private readonly record struct Op(long Ms, string Display, string What);

    private sealed class TimedBus(IMonitorBus inner, Stopwatch clock, ConcurrentQueue<Op> log) : IMonitorBus
    {
        public IMonitor? Open(DisplayInfo display) => inner.Open(display) is { } m ? new TimedMonitor(m, display.Friendly, clock, log) : null;
    }

    private sealed class TimedMonitor(IMonitor inner, string name, Stopwatch clock, ConcurrentQueue<Op> log) : IMonitor
    {
        public bool TryRead(Vcp code, out int current, out int max)
        {
            log.Enqueue(new Op(clock.ElapsedMilliseconds, name, $"read {code} start"));
            bool ok = inner.TryRead(code, out current, out max);
            log.Enqueue(new Op(clock.ElapsedMilliseconds, name, $"read {code} end ok={ok} {current}/{max}"));
            return ok;
        }

        public bool TryWrite(Vcp code, int raw)
        {
            log.Enqueue(new Op(clock.ElapsedMilliseconds, name, $"write {code} {raw} start"));
            bool ok = inner.TryWrite(code, raw);
            log.Enqueue(new Op(clock.ElapsedMilliseconds, name, $"write {code} {raw} end ok={ok}"));
            return ok;
        }

        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public void Writes_brightness_and_contrast_and_restores_them()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_DISPLAYS_WRITE") is not { Length: > 0 } report) return;
        var lines = new List<string>();
        var log = new ConcurrentQueue<Op>();
        var clock = Stopwatch.StartNew();
        var displays = DisplayCatalog.Connected();
        var bus = new TimedBus(new WindowsMonitorBus(), clock, log);
        var baseline = new Dictionary<string, (int B, int? C)>();
        using (var engine = new DisplayBrightness(bus, DisplayCatalog.Connected, WindowsMonitorBus.UnderPointer))
        {
            try
            {
                engine.Refresh();
                Until(() => engine.Statuses.All(s => s.Brightness is not null || s.Failed), 15_000);
                foreach (var s in engine.Statuses.Where(s => !s.Failed && s.Brightness is not null))
                    baseline[s.Id] = (s.Brightness!.Value, s.Contrast ? s.ContrastLevel : null);
                foreach (var s in engine.Statuses)
                    lines.Add($"baseline {s.Name}: brightness={s.Brightness} contrast={s.ContrastLevel} failed={s.Failed}");

                // 1. Fresh key press: levels are a second old.
                Settle(log);
                long t0 = Mark(log, clock, "fresh key +5, all displays");
                var handoff = Stopwatch.StartNew();
                engine.Step(+5, BrightnessTarget.AllDisplays);
                lines.Add($"fresh key: Step() returned in {handoff.Elapsed.TotalMilliseconds:F2} ms");
                Settle(log);
                Report(lines, log, t0, "fresh key");

                // 2. Stale key press: wait out the 10 s staleness; the press reads first, then writes from the real level.
                if (Environment.GetEnvironmentVariable("CHIPPI_DISPLAYS_SKIP_STALE") != "1") Thread.Sleep((int)MonitorWorker.StaleMs + 1_000);
                long t1 = Mark(log, clock, "stale key -5, all displays");
                handoff.Restart();
                engine.Step(-5, BrightnessTarget.AllDisplays);
                lines.Add($"stale key: Step() returned in {handoff.Elapsed.TotalMilliseconds:F2} ms");
                Settle(log);
                Report(lines, log, t1, "stale key");

                // 3. A press while a write is in flight: three presses 20 ms apart add up.
                long t2 = Mark(log, clock, "three quick presses -5, all displays");
                for (int i = 0; i < 3; i++)
                {
                    log.Enqueue(new Op(clock.ElapsedMilliseconds, "hand", $"press {i + 1}"));
                    engine.Step(-5, BrightnessTarget.AllDisplays);
                    Pause(clock, 20); // Thread.Sleep(20) waits about 31 ms: the 15.6 ms timer tick
                }
                Settle(log);
                Report(lines, log, t2, "in-flight presses");
                foreach (var s in engine.Statuses) lines.Add($"after presses {s.Name}: brightness={s.Brightness}");

                // 4. The panel's All displays slider: a drag of 30 frames, 16 ms apart, 15 down from the start, then release.
                int start = engine.AllLevel() ?? 50;
                long t3 = Mark(log, clock, $"All displays drag from {start} down 15 and back 10, release");
                engine.BeginAll();
                for (int i = 1; i <= 30; i++) { engine.MoveAll(start - i * 15 / 30); Pause(clock, 16); }
                for (int i = 1; i <= 20; i++) { engine.MoveAll(start - 15 + i * 10 / 20); Pause(clock, 16); }
                engine.EndAll();
                log.Enqueue(new Op(clock.ElapsedMilliseconds, "hand", "released"));
                Settle(log);
                Report(lines, log, t3, "All displays drag");
                foreach (var s in engine.Statuses) lines.Add($"after drag {s.Name}: brightness={s.Brightness} (target {start - 5} + each display's own offset)");

                // 5. Contrast: -5 on every display that has it, then the restore below puts it back.
                long t4 = Mark(log, clock, "contrast -5");
                foreach (var s in engine.Statuses.Where(s => s.Contrast && s.ContrastLevel is not null))
                    engine.Set(s.Id, Vcp.Contrast, s.ContrastLevel!.Value - 5);
                Settle(log);
                Report(lines, log, t4, "contrast");
            }
            finally
            {
                long t5 = Mark(log, clock, "restore");
                foreach (var (id, (b, c)) in baseline)
                {
                    engine.Set(id, Vcp.Brightness, b);
                    if (c is not null) engine.Set(id, Vcp.Contrast, c.Value);
                }
                Settle(log);
                Report(lines, log, t5, "restore");
            }
        }

        // The read back, on a fresh engine: what the monitors report now.
        using (var check = new DisplayBrightness(new WindowsMonitorBus(), DisplayCatalog.Connected, WindowsMonitorBus.UnderPointer))
        {
            check.Refresh();
            Until(() => check.Statuses.All(s => s.Brightness is not null || s.Failed), 15_000);
            foreach (var s in check.Statuses)
            {
                var want = baseline.TryGetValue(s.Id, out var w) ? w : default;
                lines.Add($"readback {s.Name}: brightness={s.Brightness} contrast={s.ContrastLevel} (was {want.B}/{want.C}) restored={s.Brightness == want.B && s.ContrastLevel == want.C}");
            }
        }
        File.WriteAllLines(report, lines);
        Assert.All(lines.Where(l => l.StartsWith("readback")), l => Assert.Contains("restored=True", l));
    }

    private static long Mark(ConcurrentQueue<Op> log, Stopwatch clock, string what)
    {
        long now = clock.ElapsedMilliseconds;
        log.Enqueue(new Op(now, "-", what));
        return now;
    }

    private static void Pause(Stopwatch clock, int ms)
    {
        long until = clock.ElapsedTicks + ms * Stopwatch.Frequency / 1000;
        while (clock.ElapsedTicks < until) Thread.SpinWait(50);
    }

    // No new operation for 600 ms: the queue is quiet.
    private static void Settle(ConcurrentQueue<Op> log)
    {
        int seen;
        do { seen = log.Count; Thread.Sleep(600); } while (log.Count != seen);
    }

    private static void Report(List<string> lines, ConcurrentQueue<Op> log, long since, string title)
    {
        var ops = log.Where(o => o.Ms >= since && o.Display != "-").ToList();
        lines.Add($"-- {title}: {ops.Count(o => o.What.StartsWith("write") && o.What.Contains("start"))} writes, {ops.Count(o => o.What.StartsWith("read") && o.What.Contains("start"))} reads");
        foreach (var o in ops) lines.Add($"   +{o.Ms - since,5} ms  {o.Display}: {o.What}");
    }

    private static void Until(Func<bool> done, int timeoutMs)
    {
        var deadline = Stopwatch.StartNew();
        while (!done() && deadline.ElapsedMilliseconds < timeoutMs) Thread.Sleep(5);
    }
}
