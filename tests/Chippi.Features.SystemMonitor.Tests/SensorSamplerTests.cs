// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Tests;

public sealed class SensorSamplerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public void Nothing_is_read_while_nothing_watches()
    {
        var fake = new FakeSources();
        using var sampler = new SensorSampler(fake.Sources) { Interval = TimeSpan.FromMilliseconds(20) };

        Thread.Sleep(200);

        Assert.Equal(0, fake.CpuReads + fake.MemoryReads + fake.GpuReads + fake.NetworkReads);
        Assert.Equal(SensorNeed.None, sampler.Need);
    }

    [Fact]
    public void A_watch_reads_at_once_and_disposing_it_stops_reading()
    {
        var fake = new FakeSources();
        using var sampler = new SensorSampler(fake.Sources) { Interval = TimeSpan.FromHours(1) };
        using var updated = new SemaphoreSlim(0);
        sampler.Updated += _ => updated.Release();

        var watch = sampler.Watch(SensorNeed.Cpu);
        Assert.True(updated.Wait(Wait));
        Assert.Equal(2, fake.CpuReads); // the unpublished first sample, then the published one
        Assert.Equal(ReadingStatus.Ok, sampler.Latest.Cpu.Status);

        watch.Dispose();
        Thread.Sleep(100);
        int reads = fake.CpuReads;
        sampler.Interval = TimeSpan.FromMilliseconds(10);
        Thread.Sleep(100);

        Assert.Equal(reads, fake.CpuReads);
        Assert.Equal(SensorNeed.None, sampler.Need);
    }

    [Fact]
    public void A_new_need_is_read_before_the_next_interval()
    {
        var fake = new FakeSources();
        using var sampler = new SensorSampler(fake.Sources) { Interval = TimeSpan.FromHours(1) };
        using var updated = new SemaphoreSlim(0);
        sampler.Updated += _ => updated.Release();

        using var cpu = sampler.Watch(SensorNeed.Cpu);
        Assert.True(updated.Wait(Wait));
        using var memory = sampler.Watch(SensorNeed.Memory);
        Assert.True(updated.Wait(Wait));

        Assert.Equal(SensorNeed.Cpu | SensorNeed.Memory, sampler.Need);
        Assert.Equal(2, fake.MemoryReads);
        Assert.Equal(ReadingStatus.Ok, sampler.Latest.Memory.Status);
    }

    [Fact]
    public void Changing_a_watch_changes_what_is_read()
    {
        var fake = new FakeSources();
        using var sampler = new SensorSampler(fake.Sources) { Interval = TimeSpan.FromHours(1) };
        using var updated = new SemaphoreSlim(0);
        sampler.Updated += _ => updated.Release();

        using var watch = sampler.Watch(SensorNeed.Cpu);
        Assert.True(updated.Wait(Wait));
        watch.Need = SensorNeed.Cpu | SensorNeed.Network;
        Assert.True(updated.Wait(Wait));

        Assert.Equal(2, fake.NetworkReads);
        Assert.Equal(ReadingStatus.Ok, sampler.Latest.Network.Status);
    }

    // Rates need two samples: a page that just opened gets its first update with every rate in it.
    [Fact]
    public void A_new_need_is_published_after_a_short_unpublished_first_sample()
    {
        var fake = new FakeSources();
        using var sampler = new SensorSampler(fake.Sources) { Interval = TimeSpan.FromHours(1) };
        var published = new List<int>();
        using var updated = new SemaphoreSlim(0);
        sampler.Updated += _ => { lock (published) published.Add(fake.NetworkReads); updated.Release(); };

        var started = System.Diagnostics.Stopwatch.StartNew();
        using var watch = sampler.Watch(SensorNeed.Network);
        Assert.True(updated.Wait(Wait));

        Assert.Equal([2], published);
        Assert.True(started.Elapsed >= SensorSampler.PrimeDelay - TimeSpan.FromMilliseconds(20));
    }

    // Opening the page adds its watch and lets go of the tile's: that second wake mustn't cut the first sample's wait short.
    [Fact]
    public void Another_watch_changing_during_the_first_sample_keeps_the_whole_delay()
    {
        var fake = new FakeSources();
        using var sampler = new SensorSampler(fake.Sources) { Interval = TimeSpan.FromHours(1) };
        using var updated = new SemaphoreSlim(0);
        sampler.Updated += _ => updated.Release();
        var tile = sampler.Watch(SensorNeed.Cpu);
        Assert.True(updated.Wait(Wait));

        var started = System.Diagnostics.Stopwatch.StartNew();
        using var page = sampler.Watch(SensorNeed.Cpu | SensorNeed.Network);
        Thread.Sleep(10);
        tile.Dispose();
        Assert.True(updated.Wait(Wait));

        Assert.True(started.Elapsed >= SensorSampler.PrimeDelay - TimeSpan.FromMilliseconds(20), $"published after {started.Elapsed.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public void Watching_ticks_at_the_interval()
    {
        var fake = new FakeSources();
        using var sampler = new SensorSampler(fake.Sources) { Interval = TimeSpan.FromMilliseconds(50) };
        using var updated = new SemaphoreSlim(0);
        sampler.Updated += _ => updated.Release();

        using var watch = sampler.Watch(SensorNeed.Cpu);
        for (int i = 0; i < 3; i++)
            Assert.True(updated.Wait(Wait));

        Assert.True(fake.CpuReads >= 3);
        Assert.True(sampler.History(Series.Cpu).Count >= 3);
    }

    [Fact]
    public void Dispose_stops_the_thread_and_disposes_the_sources()
    {
        var fake = new FakeSources();
        var sampler = new SensorSampler(fake.Sources);

        sampler.Dispose();
        sampler.Dispose();

        Assert.True(fake.Disposed);
    }

    [Fact]
    public void A_watch_outliving_the_sampler_can_still_change_and_let_go()
    {
        var sampler = new SensorSampler(new FakeSources().Sources);
        var watch = sampler.Watch(SensorNeed.Cpu);

        sampler.Dispose();

        Assert.Null(Record.Exception(() =>
        {
            watch.Need = SensorNeed.Memory;
            watch.Dispose();
            sampler.Interval = TimeSpan.FromSeconds(1);
            sampler.Resumed();
            sampler.Watch(SensorNeed.Cpu).Dispose();
        }));
    }
}
