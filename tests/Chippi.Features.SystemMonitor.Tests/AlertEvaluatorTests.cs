// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Core.Notifications;
using Chippi.Features.SystemMonitor.Alerts;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Tests;

public sealed class AlertEvaluatorTests
{
    private readonly ManualTime _time = new();
    private readonly FakeSources _fake = new();
    private readonly AlertEvaluator _alerts;
    private SystemMonitorSettings _settings = new();

    public AlertEvaluatorTests() => _alerts = new(_time);

    private SystemSnapshot Snapshot()
    {
        using var core = new SamplerCore(_fake.Sources, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return core.Tick(_time.GetUtcNow(), (SensorNeed)511, TimeSpan.FromSeconds(2));
    }

    private AlertEvaluation Tick(int seconds = 0)
    {
        _time.Advance(seconds);
        return _alerts.Evaluate(Snapshot(), _settings);
    }

    private void CpuHigh()
    {
        _settings = _settings with { CpuLoad = new() { On = true, Percent = 90 } };
        _fake.Cpu = _ => new CpuReading(97, 4, 4000);
    }

    [Fact]
    public void Defaults_declare_five_kinds_but_need_no_reads_or_notices()
    {
        Assert.Equal(5, AlertEvaluator.Kinds.Count);
        Assert.All(AlertEvaluator.Kinds, k => Assert.Equal(NoticePlaces.Windows | NoticePlaces.Island, k.Default));
        Assert.Equal(SensorNeed.None, AlertEvaluator.Need(_settings));
        Assert.Empty(Tick().Conditions);
    }

    [Fact]
    public void Cpu_is_caution_immediately_but_notifies_only_after_12_seconds()
    {
        CpuHigh();
        Assert.Equal("CPU 97%", Tick().First!.TileText);
        for (int i = 0; i < 5; i++) Assert.Empty(Tick(2).Notices);
        Assert.Equal("CPU load 97%", Assert.Single(Tick(2).Notices).Text);
    }

    [Fact]
    public void A_cached_high_reading_cannot_age_into_a_notice()
    {
        CpuHigh();
        var s = Snapshot();
        _alerts.Evaluate(s, _settings);
        _time.Advance(12);
        var result = _alerts.Evaluate(s, _settings);
        Assert.Empty(result.Notices);
        Assert.Empty(result.Conditions);
    }

    [Fact]
    public void A_future_reading_is_not_current()
    {
        CpuHigh();
        var s = Snapshot();
        _time.Advance(-10);
        Assert.Empty(_alerts.Evaluate(s, _settings).Conditions);
    }

    [Fact]
    public void A_short_spike_does_not_notify()
    {
        CpuHigh(); Tick(); Tick(2);
        _fake.Cpu = _ => new CpuReading(20, 4, 4000);
        Assert.Empty(Tick(2).Conditions);
        CpuHigh();
        Assert.Empty(Tick(2).Notices);
    }

    [Theory]
    [InlineData(ReadingStatus.Pending)]
    [InlineData(ReadingStatus.Unavailable)]
    [InlineData(ReadingStatus.NotRead)]
    [InlineData(ReadingStatus.NotSupported)]
    public void Missing_data_clears_caution_and_restarts_sustain(ReadingStatus status)
    {
        CpuHigh(); Tick(); Tick(2); Tick(2);
        _time.Advance(2);
        var s = Snapshot() with { Cpu = new(status, null, _time.GetUtcNow()) };
        Assert.Empty(_alerts.Evaluate(s, _settings).Conditions);
        Assert.Empty(Tick(2).Notices);
    }

    [Fact]
    public void A_long_gap_restarts_sustain()
    {
        CpuHigh(); Tick(); Tick(2);
        Assert.Empty(Tick(30).Notices);
        for(int i=0;i<5;i++) Assert.Empty(Tick(2).Notices);
        Assert.Single(Tick(2).Notices);
    }

    [Fact]
    public void Resume_restarts_sustain_even_after_a_short_sleep()
    {
        CpuHigh(); Tick(); Tick(2); Tick(2); Tick(2); Tick(2); Tick(2);
        _alerts.ResetSustain();
        Assert.Empty(Tick(2).Notices);
    }

    [Fact]
    public void Memory_repeats_after_cooldown_and_recovery_does_not_bypass_it()
    {
        _settings = _settings with { Memory = new() { On = true, Percent = 90 }, RepeatMinutes = 2 };
        _fake.Memory = () => new(93, 100, 0, 100);
        Assert.Single(Tick().Notices);
        Assert.Empty(Tick(2).Notices);
        _fake.Memory = () => new(30,100,0,100); Tick(2);
        _fake.Memory = () => new(93,100,0,100);
        Assert.Empty(Tick(2).Notices);
        Assert.Single(Tick(114).Notices);
    }

    [Fact]
    public void Disk_floor_is_decimal_and_threshold_is_strict()
    {
        _settings = _settings with { LowDisk = new() { On=true, Percent=10 } };
        _fake.Disks = () => [new DiskReading(0, 0, 0, 0, [
            new("small", "S:", 0, 9_999_999_999),
            new("boundary", "B:", 1_000_000_000, 10_000_000_000),
            new("low", "L:", 999_999_999, 10_000_000_000)])];
        Assert.Equal("low", Assert.Single(Tick().Notices).Target);
    }

    [Fact]
    public void Volumes_have_separate_cooldowns_and_are_deduplicated()
    {
        _settings = _settings with { LowDisk = new() { On=true, Percent=10 } };
        var a = new VolumeReading("a", "A:", 0, 10_000_000_000);
        _fake.Disks = () => [new DiskReading(0,0,0,0,[a]),new DiskReading(1,0,0,0,[a])];
        Assert.Single(Tick().Notices);
        _fake.Disks = () => [new DiskReading(0,0,0,0,[a,new("b","B:",0,10_000_000_000)])];
        Assert.Equal("b", Assert.Single(Tick(2).Notices).Target);
    }

    [Theory]
    [InlineData(BatteryState.Charging, false)]
    [InlineData(BatteryState.PluggedIn, false)]
    [InlineData(BatteryState.OnBattery, true)]
    public void Low_battery_requires_discharge(BatteryState state, bool expected)
    {
        _settings = _settings with { LowBattery = new() { On=true, Percent=15 } };
        _fake.Battery = () => new(state, 15, null, null);
        Assert.Equal(expected, Tick().Notices.Count == 1);
    }

    [Fact]
    public void Gpu_temperature_needs_a_limit_and_never_requests_load_counters()
    {
        _settings = _settings with { GpuTemperature = new() { On=true } };
        Assert.Equal(SensorNeed.None, AlertEvaluator.Need(_settings));
        Assert.Empty(Tick().Conditions);
        _settings = _settings with { GpuTemperature = new() { On=true, Limits=new() { ["gpu-a"]=80 } } };
        Assert.Equal(SensorNeed.GpuSensors, AlertEvaluator.Need(_settings));
        _fake.Gpu = _ => [new("gpu-a","RTX",TemperatureC:86),new("gpu-b","Other",TemperatureC:100)];
        Tick();
        for(int i=0;i<5;i++) Assert.Empty(Tick(2).Notices);
        Assert.Equal("RTX 86 °C", Assert.Single(Tick(2).Notices).Text);
    }

    [Fact]
    public void Gpu_ids_match_case_insensitively_and_fahrenheit_is_display_only()
    {
        _settings = _settings with { Temperature=TemperatureUnit.Fahrenheit,
            GpuTemperature=new() { On=true, Limits=new() { ["gpu-a"]=80 } } };
        _fake.Gpu = _ => [new("GPU-A","GPU",TemperatureC:80)];
        Assert.Equal("GPU 176 °F", Tick().First!.Text);
    }

    [Fact]
    public void Caution_order_is_settings_order_and_disabling_clears_it()
    {
        CpuHigh();
        _settings = _settings with { Memory=new() { On=true, Percent=90 } };
        _fake.Memory = () => new(93,100,0,100);
        Assert.Equal("cpu-load", Tick().First!.Kind);
        _settings = _settings with { CpuLoad=new() { On=false, Percent=90 } };
        Assert.Equal("memory", Tick(2).First!.Kind);
    }

    [Fact]
    public void Controller_declares_kinds_takes_demand_and_clears_on_stop()
    {
        var fake = new FakeSources();
        using var sampler = new SensorSampler(fake.Sources) { Interval=TimeSpan.FromHours(1) };
        fake.Memory = () => new(93,100,0,100);
        var notifier = new RecordingNotifier();
        var island = new RecordingIsland();
        using var alerts = new MonitorAlerts(sampler,notifier,island,new SystemMonitorSettings());
        Assert.Equal(5,notifier.Kinds.Count);
        Assert.Equal(SensorNeed.None,sampler.Need);
        alerts.Configure(new() { Memory=new() { On=true, Percent=90 } });
        Assert.True(notifier.Arrived.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal("memory",notifier.Last!.Kind);
        Assert.Equal("--system-monitor",notifier.Last.Activation);
        Assert.Empty(notifier.Last.Actions);
        Assert.False(notifier.Last.Celebrate);
        alerts.Configure(new());
        Assert.Equal(SensorNeed.None,sampler.Need);
        Assert.Null(alerts.Current.First);
        Assert.Null(island.Last);
        alerts.Dispose();
        Assert.Null(island.Last);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now=new(2026,10,26,12,0,0,TimeSpan.Zero);
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _ticks;
        public void Advance(int seconds) { _now=_now.AddSeconds(seconds); _ticks+=seconds*TimeSpan.TicksPerSecond; }
    }

    private sealed class RecordingNotifier : INotifier
    {
        public IReadOnlyList<NoticeKind> Kinds { get; private set; }=[];
        public Notice? Last;
        public SemaphoreSlim Arrived { get; }=new(0);
        public void Declare(IReadOnlyList<NoticeKind> kinds)=>Kinds=kinds;
        public NoticePlaces PlacesFor(string kindId)=>NoticePlaces.Windows|NoticePlaces.Island;
        public void SetPlaces(string kindId,NoticePlaces places) { }
        public void Show(Notice notice) { Last=notice; Arrived.Release(); }
    }

    private sealed class RecordingIsland : IIslandPublisher
    {
        public IslandActivity? Last;
        public void Publish(IslandActivity activity)=>Last=activity;
        public void Clear(string key)=>Last=null;
        public void ReportSuccess(string? message=null)=>throw new InvalidOperationException("Alerts never celebrate");
    }
}
