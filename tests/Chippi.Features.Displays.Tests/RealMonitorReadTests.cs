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
        File.WriteAllLines(report, lines);
    }
}
