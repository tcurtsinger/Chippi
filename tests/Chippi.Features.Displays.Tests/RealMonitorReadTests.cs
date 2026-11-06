// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.UI.Displays;

namespace Chippi.Features.Displays.Tests;

/// <summary>
/// Reads the real monitors' brightness and contrast, never writes (docs/phase-7/displays.md, Tests). Runs only when
/// CHIPPI_DISPLAYS_READ names a report file.
/// </summary>
public sealed class RealMonitorReadTests
{
    [Fact]
    public void Reads_every_connected_display_and_times_it()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_DISPLAYS_READ") is not { Length: > 0 } report) return;
        var lines = new List<string>();
        var bus = new WindowsMonitorBus();
        var all = Stopwatch.StartNew();
        using var engine = new DisplayBrightness(bus, DisplayCatalog.Connected, WindowsMonitorBus.UnderPointer);
        engine.Refresh();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (engine.Statuses.Any(s => s.Brightness is null && !s.Failed) && DateTime.UtcNow < deadline) Thread.Sleep(2);
        lines.Add($"all displays read in {all.ElapsedMilliseconds} ms (cold)");
        foreach (var s in engine.Statuses)
            lines.Add($"{s.Name}: main={s.IsMain} brightness={s.Brightness} contrast={(s.Contrast ? s.ContrastLevel?.ToString() : "none")} failed={s.Failed}");
        foreach (var d in DisplayCatalog.Connected())
        {
            using var monitor = bus.Open(d);
            if (monitor is null) { lines.Add($"{d.Friendly}: open failed"); continue; }
            var timer = Stopwatch.StartNew();
            bool ok = monitor.TryRead(Vcp.Brightness, out int cur, out int max);
            lines.Add($"{d.Friendly}: read 0x10 ok={ok} {cur}/{max} in {timer.ElapsedMilliseconds} ms (warm handle)");
        }

        // Do two monitors' reads overlap, or does the driver take them in turn? 10 reads each: one monitor at a time, then both
        // monitors from their own threads at once. Reads only.
        var monitors = DisplayCatalog.Connected().Select(d => bus.Open(d)).OfType<IMonitor>().ToList();
        if (monitors.Count >= 2)
        {
            void Reads(IMonitor m) { for (int i = 0; i < 10; i++) m.TryRead(Vcp.Brightness, out _, out _); }
            var one = Stopwatch.StartNew();
            Reads(monitors[0]);
            long first = one.ElapsedMilliseconds;
            one.Restart();
            Reads(monitors[1]);
            long second = one.ElapsedMilliseconds;
            var both = Stopwatch.StartNew();
            var threads = monitors.Take(2).Select(m => new Thread(() => Reads(m))).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            lines.Add($"10 reads: monitor 1 alone {first} ms, monitor 2 alone {second} ms, both at once from two threads {both.ElapsedMilliseconds} ms (parallel would be about {Math.Max(first, second)} ms, in turn about {first + second} ms)");
        }
        monitors.ForEach(m => m.Dispose());
        File.WriteAllLines(report, lines);
    }
}
