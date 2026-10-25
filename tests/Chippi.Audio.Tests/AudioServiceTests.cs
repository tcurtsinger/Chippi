// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using static Chippi.Audio.Tests.FakeDeviceSystem;
using static Chippi.Audio.Tests.SessionCatalogTests;

namespace Chippi.Audio.Tests;

public sealed class AudioServiceTests : IAsyncDisposable
{
    private static readonly AudioRole[] All = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeDeviceSystem _sys = new();
    private readonly ConcurrentQueue<Thread> _createdOn = new();
    private readonly ConcurrentQueue<RoleChange> _changes = new();
    private readonly AudioService _service;
    private IAsyncDisposable? _lease;

    public AudioServiceTests()
    {
        _service = new AudioService(() =>
        {
            _createdOn.Enqueue(Thread.CurrentThread);
            return _sys;
        }, context: null, options: new AudioServiceOptions { Debounce = TimeSpan.FromMilliseconds(30), MeterInterval = TimeSpan.FromMilliseconds(10), OwnProcessId = 99 });
        _service.RolesChanged += (_, c) => _changes.Enqueue(c);
    }

    public async ValueTask DisposeAsync()
    {
        if (_lease is not null)
            await _lease.DisposeAsync();
    }

    private async Task StartAsync()
    {
        _lease = _service.Acquire();
        await _service.RefreshAsync();
    }

    private static RoleTarget List(params string[] ids) =>
        active => PriorityPolicy.Resolve(ids.Select(id => new PriorityEntry(id, id)).ToList(), active);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Device_system_lives_on_one_mta_engine_thread()
    {
        await StartAsync();
        var thread = Assert.Single(_createdOn);
        Assert.Equal("Chippi.Audio", thread.Name);
        Assert.Equal(ApartmentState.MTA, thread.GetApartmentState());

        var ranOn = await _service.InvokeAsync(_ => Thread.CurrentThread);
        Assert.Same(thread, ranOn);

        await _lease!.DisposeAsync();
        _lease = null;
        Assert.Same(thread, _sys.DisposedOn);
    }

    [Fact]
    public async Task Engine_runs_while_any_lease_is_held()
    {
        var first = _service.Acquire();
        var second = _service.Acquire();
        await first.DisposeAsync();
        await first.DisposeAsync(); // a lease gives back once
        Assert.True(_service.IsRunning);

        await second.DisposeAsync();
        Assert.False(_service.IsRunning);
        Assert.True(_sys.IsDisposed);
        Assert.Same(AudioSnapshot.Empty, _service.Snapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(_service.RefreshAsync);
    }

    [Fact]
    public async Task Engine_restarts_with_a_fresh_device_system_after_the_last_lease()
    {
        await (_service.Acquire()).DisposeAsync();
        await StartAsync();
        Assert.Equal(2, _createdOn.Count);
        Assert.True(_service.IsRunning);
    }

    [Fact]
    public async Task Snapshot_shows_endpoints_defaults_owners_and_levels()
    {
        _sys.With(Speaker("A"), Speaker("B"), Mic("M")).WithDefault("B").WithDefault("M");
        await StartAsync();

        await _service.ClaimRolesAsync(RoleOwner.Routing, AudioFlow.Render, All, List("A", "B"));

        var snap = _service.Snapshot;
        Assert.Equal(["A", "B"], snap.Active(AudioFlow.Render).Select(e => e.Id));
        Assert.Equal("A", snap.Default(AudioFlow.Render, AudioRole.Communications));
        Assert.Equal(RoleOwner.Routing, snap.Owner(AudioFlow.Render, AudioRole.Console));
        Assert.Equal(RoleOwner.None, snap.Owner(AudioFlow.Capture, AudioRole.Console));
        Assert.Equal(new EndpointLevel(0.5f, false), snap.Levels["m"]);
        var change = Assert.Single(_changes);
        Assert.Equal(RoleChangeKind.Claim, change.Kind);
    }

    [Fact]
    public async Task A_bluetooth_burst_reconciles_once_after_the_debounce()
    {
        _sys.With(Speaker("B")).WithDefault("B");
        await StartAsync();
        await _service.ClaimRolesAsync(RoleOwner.Routing, AudioFlow.Render, All, List("A", "B"));
        var before = _sys.Enumerations;

        _sys.Connect(Speaker("A"));
        for (var i = 0; i < 20; i++)
            _sys.Raise(DeviceChange.Devices);

        await WaitUntil(() => _changes.Any(c => c.Kind == RoleChangeKind.Switch));
        await Task.Delay(100);
        // A reconcile reads both flows: one for the burst, one for the echo of our three writes.
        Assert.Equal(4, _sys.Enumerations - before);
        Assert.Equal("A", _service.Snapshot.Default(AudioFlow.Render, AudioRole.Multimedia));
    }

    [Fact]
    public async Task External_default_change_is_repaired_and_reported()
    {
        _sys.With(Mic("Real"), Mic("Headset")).WithDefault("Real");
        await StartAsync();
        await _service.ClaimRolesAsync(RoleOwner.Routing, AudioFlow.Capture, All, List("Real"));

        _sys.ChangeDefault("Headset", AudioRole.Communications);

        await WaitUntil(() => _changes.Any(c => c.Kind == RoleChangeKind.Repair));
        Assert.Equal("Real", _sys.DefaultOf(AudioFlow.Capture, AudioRole.Communications));
    }

    [Fact]
    public async Task A_role_in_back_off_is_repaired_when_the_back_off_ends_without_a_new_event()
    {
        // Karl's S1 review probe: three repairs, a fourth external change, then no further event.
        AudioRole[] mm = [AudioRole.Multimedia];
        var service = new AudioService(() => _sys, null, options: new AudioServiceOptions
        {
            Debounce = TimeSpan.FromMilliseconds(10),
            RepairBackoff = TimeSpan.FromMilliseconds(300),
        });
        _sys.With(Mic("Preferred"), Mic("Other")).WithDefault("Preferred");
        var lease = service.Acquire();
        await service.ClaimRolesAsync(RoleOwner.Routing, AudioFlow.Capture, mm, List("Preferred"));

        for (var i = 0; i < RoleArbiter.MaxRepairs; i++)
        {
            _sys.ChangeDefault("Other", mm);
            await WaitUntil(() => _sys.DefaultOf(AudioFlow.Capture, AudioRole.Multimedia) == "Preferred");
        }
        _sys.ChangeDefault("Other", mm);
        await Task.Delay(100);
        Assert.Equal("Other", _sys.DefaultOf(AudioFlow.Capture, AudioRole.Multimedia));

        await WaitUntil(() => _sys.DefaultOf(AudioFlow.Capture, AudioRole.Multimedia) == "Preferred");
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task A_failed_start_reaches_the_caller_and_a_later_command_recovers()
    {
        var broken = true;
        var service = new AudioService(() => broken ? throw new InvalidOperationException("Core Audio unavailable") : _sys, null);
        _sys.With(Speaker("A"));
        var lease = service.Acquire();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(service.RefreshAsync);
        Assert.Equal("Core Audio unavailable", error.InnerException?.Message);
        Assert.False(service.IsRunning);

        broken = false;
        await service.ClaimRolesAsync(RoleOwner.Routing, AudioFlow.Render, All, List("A"));
        Assert.True(service.IsRunning);
        Assert.Equal("A", service.Snapshot.Default(AudioFlow.Render, AudioRole.Multimedia));
        await lease.DisposeAsync();
        Assert.True(_sys.IsDisposed);
    }

    [Fact]
    public async Task A_start_that_keeps_failing_fails_every_command()
    {
        var service = new AudioService(() => throw new InvalidOperationException("Core Audio unavailable"), null);
        var lease = service.Acquire();

        await Assert.ThrowsAsync<InvalidOperationException>(service.RefreshAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClaimRolesAsync(RoleOwner.Routing, AudioFlow.Render, All, List("A")));
        Assert.False(service.IsRunning);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task Events_are_posted_to_the_given_context()
    {
        var context = new RecordingContext();
        var service = new AudioService(() => _sys, context);
        var snapshots = 0;
        service.SnapshotChanged += (_, _) => snapshots++;
        var lease = service.Acquire();
        await service.RefreshAsync();

        Assert.Equal(0, snapshots);
        context.RunAll();
        Assert.True(snapshots >= 1);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task Level_events_refresh_without_reconciling()
    {
        _sys.With(Speaker("A")).WithDefault("A");
        await StartAsync();
        var before = _sys.Enumerations;

        _sys.ChangeLevel("A", new EndpointLevel(0.1f, true));

        await WaitUntil(() => _service.Snapshot.Levels["A"].Muted);
        Assert.Equal(before, _sys.Enumerations);
    }

    [Fact]
    public async Task Mute_reports_the_endpoints_that_failed()
    {
        _sys.With(Mic("M1"), Mic("M2"));
        _sys.Failing.Add("M2");
        await StartAsync();

        var failed = await _service.SetEndpointMuteAsync(["M1", "M2"], true);

        Assert.Equal(["M2"], failed);
        Assert.True(_service.Snapshot.Levels["M1"].Muted);
        Assert.False(_service.Snapshot.Levels["M2"].Muted);
    }

    [Fact]
    public async Task Endpoint_volume_is_clamped()
    {
        _sys.With(Speaker("A"));
        await StartAsync();

        await _service.SetEndpointVolumeAsync("A", 1.7f);

        Assert.Equal(1f, _service.Snapshot.Levels["A"].Volume);
    }

    [Fact]
    public async Task App_volume_and_mute_reach_every_session_of_the_app()
    {
        _sys.With(Speaker("Spk"), Speaker("Hdp"))
            .WithSession(Session("1", "Spk", 10, @"C:\chat.exe", "Chat"))
            .WithSession(Session("2", "Hdp", 11, @"C:\chat.exe", "Chat"))
            .WithSession(Session("3", "Spk", 12, @"C:\game.exe", "Game"));
        await StartAsync();
        var chat = AppKey.ForExe(@"C:\chat.exe");

        await _service.SetAppVolumeAsync(chat, 0.25f);
        await _service.SetAppMuteAsync(chat, true);

        Assert.All(["1", "2"], id => Assert.Equal((0.25f, true), (_sys.SessionById(id).Volume, _sys.SessionById(id).Muted)));
        Assert.Equal((1f, false), (_sys.SessionById("3").Volume, _sys.SessionById("3").Muted));
        var app = _service.Snapshot.Apps.Single(a => a.Key == chat);
        Assert.True(app.Muted);
    }

    [Fact]
    public async Task Meters_poll_only_while_watched()
    {
        _sys.With(Mic("M"))
            .WithSession(Session("s", "M", 10, @"C:\a.exe", "A"));
        _sys.SetPeak("M", 0.4f);
        await StartAsync();
        var readings = new ConcurrentQueue<MeterReadings>();
        _service.MetersChanged += (_, r) => readings.Enqueue(r);

        await Task.Delay(50);
        Assert.Equal(0, _sys.PeakReads);

        var watch = _service.WatchMeters(["M"], []);
        await WaitUntil(() => readings.Count >= 3);
        Assert.Equal(0.4f, readings.Last().Endpoints["M"]);

        watch.Dispose();
        await Task.Delay(50);
        var after = _sys.PeakReads;
        await Task.Delay(80);
        Assert.Equal(after, _sys.PeakReads);
    }

    [Fact]
    public async Task A_vanished_endpoint_reads_zero_instead_of_failing()
    {
        _sys.With(Mic("M"));
        await StartAsync();
        var readings = new ConcurrentQueue<MeterReadings>();
        _service.MetersChanged += (_, r) => readings.Enqueue(r);

        using var watch = _service.WatchMeters(["Gone"], [AppKey.ForExe(@"C:\none.exe")]);

        await WaitUntil(() => !readings.IsEmpty);
        Assert.Equal(0f, readings.First().Endpoints["Gone"]);
        Assert.Equal(0f, readings.First().Apps[AppKey.ForExe(@"C:\none.exe")]);
    }

    [Fact]
    public async Task A_failing_operation_does_not_stop_the_engine()
    {
        await StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.InvokeAsync<int>(_ => throw new InvalidOperationException("boom")));

        Assert.Equal(1, await _service.InvokeAsync(_ => 1));
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback, object?)> _posts = new();

        public override void Post(SendOrPostCallback d, object? state) => _posts.Enqueue((d, state));

        public void RunAll()
        {
            while (_posts.TryDequeue(out var post))
                post.Item1(post.Item2);
        }
    }
}
