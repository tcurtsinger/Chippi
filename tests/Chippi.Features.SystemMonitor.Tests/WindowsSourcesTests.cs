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
        sources.Gpu.Read(load: true);
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

        Assert.All(gpus, g =>
        {
            Assert.DoesNotContain("BasicRender", g.Id, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(g.Name));
            Assert.NotNull(g.Load);
            Assert.InRange(g.Load.Value, 0, 100);
            if (g.TemperatureC is { } t)
                Assert.InRange(t, 1, 150);
        });
        Assert.Equal(gpus.Count, gpus.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Equal(network.Count, network.Select(n => n.Id).Distinct().Count());
        Assert.All(network, n =>
        {
            Assert.True(Guid.TryParse(n.Id, out _));
            if (!n.Connected)
                Assert.Null(n.ReceiveBytesPerSecond);
            if (n.ReceiveBytesPerSecond is { } rx)
                Assert.True(rx >= 0);
        });

        Assert.NotNull(disks);
        Assert.NotEmpty(disks);
        Assert.Contains(disks, d => d.Volumes.Any(v => v.Letter == Path.GetPathRoot(Environment.SystemDirectory)![..2].ToUpperInvariant()));
        Assert.All(disks, d =>
        {
            if (d.ActivePercent is { } a)
                Assert.InRange(a, 0, 100);
            Assert.All(d.Volumes, v => Assert.InRange(v.FreeBytes, 0UL, v.TotalBytes));
        });

        if (battery is not null)
            Assert.True(battery.Percent is null or (>= 0 and <= 100));

        Assert.All(zones, z => Assert.InRange(z.TemperatureC, -50, 150));
    }

    [Fact]
    public void A_rate_read_after_a_long_pause_starts_over()
    {
        using var sources = WindowsSources.Create();

        Thread.Sleep(RateClock.StaleAfter + TimeSpan.FromMilliseconds(500));

        Assert.Null(sources.Cpu.Read(perProcessor: false));
        Thread.Sleep(500);
        Assert.NotNull(sources.Cpu.Read(perProcessor: false));
    }
}
