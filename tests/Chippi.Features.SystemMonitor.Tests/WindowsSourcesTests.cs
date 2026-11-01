// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Features.SystemMonitor.Sensors.Platform;

namespace Chippi.Features.SystemMonitor.Tests;

/// <summary>
/// The real sources on whatever PC runs the tests, read twice a second apart. Hardware differs (CI has
/// no GPU, no battery), so these check shape and ranges, never particular devices.
/// </summary>
public sealed class WindowsSourcesTests
{
    [Fact]
    public void Every_source_reads_as_a_standard_user()
    {
        using var sources = WindowsSources.Create();
        Assert.Null(sources.Cpu.Read(perProcessor: true));
        Assert.Null(sources.Disks.Read());
        Assert.All(sources.Gpu.Read(load: true), g => Assert.Null(g.Load));
        sources.Network.Read();
        Thread.Sleep(1000);

        var cpu = sources.Cpu.Read(perProcessor: true);
        var memory = sources.Memory.Read();
        var gpus = sources.Gpu.Read(load: true);
        var network = sources.Network.Read();
        var disks = sources.Disks.Read();
        var battery = sources.Battery.Read();
        var zones = sources.Temperature.Read();

        Assert.NotNull(cpu);
        Assert.InRange(cpu.Utility, 0, 1000);
        Assert.True(cpu.LogicalProcessors >= 1);
        Assert.NotNull(cpu.PerProcessor);
        Assert.Equal(cpu.LogicalProcessors, cpu.PerProcessor.Count);
        Assert.All(cpu.PerProcessor, p => Assert.True(p >= 0));

        Assert.True(memory.TotalBytes > 0);
        Assert.InRange(memory.UsedBytes, 1UL, memory.TotalBytes);
        Assert.True(memory.CommitLimitBytes >= memory.CommitBytes);

        // A virtual GPU (CI's Hyper-V adapter) has no engine counters, temperature or fan: those
        // readings are null there, and in range wherever they're reported.
        Assert.All(gpus, g =>
        {
            Assert.DoesNotContain("BasicRender", g.Id, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(g.Name));
            if (g.Load is { } load)
                Assert.InRange(load, 0, 100);
            if (g.TemperatureC is { } t)
                Assert.InRange(t, 1, 150);
            if (g.FanRpm is { } rpm)
                Assert.True(rpm > 0);
            if (g.FanPercent is { } percent)
                Assert.InRange(percent, 0U, 100U);        });
        Assert.Equal(gpus.Count, gpus.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // The history's read: load without temperature, fan or each process's load.
        Assert.All(sources.Gpu.Read(load: true, sensors: false, processes: false),
            g => Assert.True(g is { TemperatureC: null, FanRpm: null, FanPercent: null, ProcessLoads: null }));

        Assert.Equal(network.Count, network.Select(n => n.Id).Distinct().Count());
        Assert.All(network, n =>
        {
            Assert.True(Guid.TryParse(n.Id, out _));
            if (!n.Connected)
                Assert.Null(n.ReceiveBytesPerSecond);
            if (n.ReceiveBytesPerSecond is { } rx)
                Assert.True(rx >= 0);
        });

        // Disks are past their first sample, so the list is there, though a VM's may differ from a PC's.
        Assert.NotNull(disks);
        Assert.Equal(disks.Count, disks.Select(d => d.Number).Distinct().Count());
        Assert.All(disks, d =>
        {
            if (d.ActivePercent is { } a)
                Assert.InRange(a, 0, 100);
            if (d.ReadBytesPerSecond is { } r)
                Assert.True(r >= 0);
            Assert.All(d.Volumes, v =>
            {
                Assert.Matches("^[A-Z]:$", v.Letter);
                Assert.InRange(v.FreeBytes, 0UL, v.TotalBytes);
            });
        });

        if (battery is not null)
        {
            if (battery.Percent is { } p)
                Assert.InRange(p, 0, 100);
            if (battery.TimeLeft is { } left)
                Assert.True(left > TimeSpan.Zero);
        }

        Assert.All(zones, z => Assert.InRange(z.TemperatureC, -50, 150));
    }

    // Before a rate counter's second collection PDH may say it has no data on either of its calls
    // (GPU Engine says so only on the second): no values, never a failed read.
    [Fact]
    public void A_rate_counter_has_no_values_until_its_second_collection()
    {
        using var query = new PdhQuery();
        nint counter;
        try { counter = query.Add(@"\GPU Engine(*)\Utilization Percentage"); }
        catch (PdhException) { counter = query.Add(@"\Processor(*)\% Processor Time"); } // CI's GPU has no engine counters
        query.Collect();
        foreach (var instance in query.Instances(counter))
            Assert.Null(instance.Value);
        Thread.Sleep(1000);
        query.Collect();
        var values = query.Values(counter);
        Assert.NotEmpty(values);
        Assert.All(values, v => Assert.False(string.IsNullOrEmpty(v.Name)));
    }

    [Fact]
    public void A_rate_read_after_a_long_pause_starts_over()
    {
        using var sources = WindowsSources.Create();
        sources.Cpu.Read(perProcessor: false);
        Thread.Sleep(500);
        Assert.NotNull(sources.Cpu.Read(perProcessor: false));

        Thread.Sleep(RateClock.StaleAfter + TimeSpan.FromMilliseconds(500));

        Assert.Null(sources.Cpu.Read(perProcessor: false));
        Thread.Sleep(500);
        Assert.NotNull(sources.Cpu.Read(perProcessor: false));
    }

    [Fact]
    public void Processes_read_with_names_memory_and_cpu_on_the_second_read()
    {
        using var sources = WindowsSources.Create();
        var first = sources.Processes.Read();
        Assert.All(first, p => Assert.Null(p.CpuPercent));
        Thread.Sleep(500);
        var processes = sources.Processes.Read();

        // This test's own process: the layout reads its ID, program file, memory and CPU time.
        var self = Assert.Single(processes, p => p.Id == Environment.ProcessId);
        Assert.Equal(Path.GetFileName(Environment.ProcessPath!).ToLowerInvariant(), self.App);
        Assert.True(self.PrivateBytes > 1024 * 1024);
        Assert.NotNull(self.CpuPercent);
        Assert.Equal(Environment.ProcessPath, self.Path, StringComparer.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(self.Name));

        Assert.True(processes.Count > 10);
        Assert.DoesNotContain(processes, p => p.Id is 0 or 4 || p.App is "registry" or "memory compression");
        Assert.All(processes, p => Assert.InRange(p.CpuPercent ?? 0, 0, 100));
    }
}
