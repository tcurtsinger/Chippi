// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Audio.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Audio;

public sealed class AudioServiceOptions
{
    /// <summary>Device and default-role notifications restart this wait, so a Bluetooth burst reconciles once (Hopper's 400 ms).</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(400);

    public TimeSpan MeterInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How long a role is left alone after 3 repairs in 10 s (a fight with other software).</summary>
    public TimeSpan RepairBackoff { get; init; } = TimeSpan.FromSeconds(60);

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Sessions of this process are left out of <see cref="AudioSnapshot.Apps"/>.</summary>
    public int OwnProcessId { get; init; } = Environment.ProcessId;

    /// <summary>Builds Mic processing's pipeline. Tests pass a fake; the default captures and renders for real.</summary>
    public MicPipelineFactory PipelineFactory { get; init; } = MicPipeline.Factory;
}

/// <summary>
/// The shared audio service for every Sound feature (architecture §2). All Core Audio work runs on
/// one MTA engine thread, one operation at a time (Hopper's EngineLoop). Commands return Tasks that
/// complete when the engine has done them; Core Audio notifications only enqueue work. After each
/// operation the engine publishes an immutable <see cref="AudioSnapshot"/> on the
/// <see cref="SynchronizationContext"/> it was given (the UI thread), or inline when there is none.
/// </summary>
/// <remarks>
/// The engine runs while at least one lease is held: each Sound module takes one in StartAsync and
/// disposes it in StopAsync, so with no Sound feature installed no audio code runs. Claims, meter
/// watches and snapshots don't survive the last lease.
/// </remarks>
public sealed class AudioService
{
    private readonly Func<IDeviceSystem> _createSystem;
    private readonly SynchronizationContext? _context;
    private readonly ILogger _logger;
    private readonly AudioServiceOptions _options;
    private readonly Lock _gate = new();
    private int _leases;
    private Engine? _engine;
    private volatile AudioSnapshot _snapshot = AudioSnapshot.Empty;

    /// <param name="createSystem">Called on the engine thread each time the engine starts.</param>
    public AudioService(Func<IDeviceSystem> createSystem, SynchronizationContext? context, ILogger? logger = null, AudioServiceOptions? options = null)
    {
        _createSystem = createSystem;
        _context = context;
        _logger = logger ?? NullLogger.Instance;
        _options = options ?? new AudioServiceOptions();
    }

    /// <summary>The latest snapshot. <see cref="AudioSnapshot.Empty"/> while the engine is stopped.</summary>
    public AudioSnapshot Snapshot => _snapshot;

    public bool IsRunning
    {
        get { lock (_gate) return _engine is { Failed: false }; }
    }

    public event EventHandler<AudioSnapshot>? SnapshotChanged;

    /// <summary>Raised for each default the arbiter set, after the snapshot that shows it.</summary>
    public event EventHandler<RoleChange>? RolesChanged;

    /// <summary>Raised every meter interval while at least one <see cref="WatchMeters"/> is open.</summary>
    public event EventHandler<MeterReadings>? MetersChanged;

    /// <summary>
    /// Starts the engine on the first lease. Dispose the lease to give it back; the last one stops the
    /// engine. If the device system can't be created, commands fail with that error and
    /// <see cref="IsRunning"/> is false; the next lease or command tries to start again.
    /// </summary>
    public IAsyncDisposable Acquire()
    {
        lock (_gate)
        {
            if (_leases++ == 0)
            {
                _engine = new Engine(this);
                _engine.Start();
            }
            else
            {
                CurrentEngine();
            }
        }
        return new Lease(this);
    }

    /// <summary>
    /// <paramref name="owner"/> claims <paramref name="roles"/> of <paramref name="flow"/> (and gives up
    /// that flow's other roles). <paramref name="target"/> runs on the engine thread at every
    /// reconcile, so it must only read its argument and data it captured.
    /// </summary>
    public Task ClaimRolesAsync(RoleOwner owner, AudioFlow flow, IReadOnlyCollection<AudioRole> roles, RoleTarget target) =>
        Run(e =>
        {
            e.Arbiter.Claim(owner, flow, roles, target);
            e.Reconcile();
            return true;
        });

    /// <summary>
    /// Gives up <paramref name="owner"/>'s roles in <paramref name="flow"/>. Roles the other owner
    /// claims go to it at once; unclaimed roles go back to <paramref name="restore"/> where given.
    /// </summary>
    public Task ReleaseRolesAsync(RoleOwner owner, AudioFlow flow, IReadOnlyDictionary<AudioRole, string>? restore = null) =>
        Run(e =>
        {
            e.Arbiter.Release(owner, flow, restore, e.Catalog, e.System);
            e.Reconcile();
            return true;
        });

    /// <summary>Re-reads devices and enforces claims now, without waiting for a notification.</summary>
    public Task RefreshAsync() => Run(e =>
    {
        e.Reconcile();
        return true;
    });

    public Task SetEndpointVolumeAsync(string endpointId, float volume) =>
        Run(e =>
        {
            e.Controls.SetVolume(e.System, endpointId, volume);
            e.RefreshLevels();
            return true;
        });

    /// <summary>Mutes or unmutes each endpoint and returns the IDs that failed.</summary>
    public Task<IReadOnlyList<string>> SetEndpointMuteAsync(IReadOnlyCollection<string> endpointIds, bool muted) =>
        Run(e =>
        {
            var failed = e.Controls.SetMute(e.System, endpointIds, muted);
            e.RefreshLevels();
            return failed;
        });

    /// <summary>Sets the volume of every session the app has, on every output.</summary>
    public Task SetAppVolumeAsync(AppKey app, float volume) =>
        Run(e =>
        {
            e.ForEachSession(app, id => e.System.SetSessionVolume(id, Math.Clamp(volume, 0f, 1f)));
            e.RefreshLevels();
            return true;
        });

    public Task SetAppMuteAsync(AppKey app, bool muted) =>
        Run(e =>
        {
            e.ForEachSession(app, id => e.System.SetSessionMute(id, muted));
            e.RefreshLevels();
            return true;
        });

    /// <summary>
    /// Runs <paramref name="op"/> on the engine thread with the device system, for work that has no
    /// command of its own yet (the mic pipeline, per-app devices).
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<IDeviceSystem, T> op) => Run(e => op(e.System));

    /// <summary>
    /// Turns Mic processing on with <paramref name="request"/>, or off with null, and returns where it
    /// stands after the engine tried. A new request retries after a failure.
    /// </summary>
    public Task<ProcessingStatus> SetProcessingAsync(ProcessingRequest? request) =>
        Run(e =>
        {
            e.Processing.Set(request);
            e.Reconcile();
            return e.Processing.Status;
        });

    /// <summary>
    /// After a crash: capture roles still on the marker's cable go back to what it replaced (roles
    /// another owner claims are left to it). Returns whether anything was restored.
    /// </summary>
    public Task<bool> RecoverProcessingAsync(ProcessingMarker marker) =>
        Run(e =>
        {
            e.Catalog.Refresh(e.System);
            var restored = e.Arbiter.Recover(marker, e.Catalog, e.System);
            e.Reconcile();
            return restored;
        });

    /// <summary>
    /// Polls peaks for these endpoints and apps until disposed, raising <see cref="MetersChanged"/>.
    /// Nothing polls while no watch is open.
    /// </summary>
    public IDisposable WatchMeters(IReadOnlyCollection<string> endpointIds, IReadOnlyCollection<AppKey> apps)
    {
        Engine engine;
        lock (_gate)
            engine = CurrentEngine() ?? throw new InvalidOperationException("The audio service isn't running.");
        return engine.Watch(endpointIds, apps);
    }

    private Task<T> Run<T>(Func<Engine, T> op)
    {
        Engine? engine;
        lock (_gate)
            engine = CurrentEngine();
        return engine is null
            ? Task.FromException<T>(new InvalidOperationException("The audio service isn't running."))
            : engine.Invoke(op);
    }

    // Caller holds _gate. An engine whose device system failed to start is replaced, so a later
    // lease or command retries instead of failing forever.
    private Engine? CurrentEngine()
    {
        if (_engine is not { Failed: true } failed)
            return _engine;
        _engine = new Engine(this);
        _engine.Start();
        _ = failed.StopAsync();
        return _engine;
    }

    private async ValueTask ReleaseAsync()
    {
        Engine? stopping = null;
        lock (_gate)
        {
            if (--_leases == 0)
            {
                stopping = _engine;
                _engine = null;
            }
        }
        if (stopping is not null)
        {
            await stopping.StopAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (_engine is not null)
                    return; // a new lease started a new engine meanwhile; its snapshot stands
            }
            Publish(AudioSnapshot.Empty, []);
        }
    }

    private void Publish(AudioSnapshot snapshot, IReadOnlyList<RoleChange> changes)
    {
        _snapshot = snapshot;
        Post(() =>
        {
            SnapshotChanged?.Invoke(this, snapshot);
            foreach (var change in changes)
                RolesChanged?.Invoke(this, change);
        });
    }

    private void Post(Action action)
    {
        if (_context is null)
            action();
        else
            _context.Post(_ => action(), null);
    }

    private sealed class Lease(AudioService service) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _disposed, 1) == 0 ? service.ReleaseAsync() : ValueTask.CompletedTask;
    }

    /// <summary>One run of the engine thread, from the first lease to the last.</summary>
    private sealed class Engine
    {
        private readonly AudioService _service;
        private readonly BlockingCollection<Action> _queue = [];
        private readonly Thread _thread;
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ITimer _debounce;
        private readonly ITimer _meterTimer;
        private readonly ITimer _backoffTimer;
        private readonly List<MeterWatch> _watches = [];
        private IDeviceSystem? _system;
        private volatile Exception? _startError;
        private int _lightQueued;
        private int _meterQueued;
        private long _version;

        public Engine(AudioService service)
        {
            _service = service;
            var log = service._logger;
            Arbiter = new RoleArbiter(log, service._options.Time, service._options.RepairBackoff);
            Processing = new MicPipelineHost(log, service._options.PipelineFactory, op => Enqueue(op), Reconcile);
            Controls = new EndpointControls(log);
            Sessions = new SessionCatalog(service._options.OwnProcessId);
            _debounce = service._options.Time.CreateTimer(_ => Enqueue(Reconcile), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _meterTimer = service._options.Time.CreateTimer(_ => OnMeterTick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _backoffTimer = service._options.Time.CreateTimer(_ => Enqueue(Reconcile), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _thread = new Thread(Loop) { IsBackground = true, Name = "Chippi.Audio" };
            _thread.SetApartmentState(ApartmentState.MTA);
        }

        public DeviceCatalog Catalog { get; } = new();

        public RoleArbiter Arbiter { get; }

        public EndpointControls Controls { get; }

        public SessionCatalog Sessions { get; }

        public MicPipelineHost Processing { get; }

        public IDeviceSystem System => _system ?? throw new InvalidOperationException("The audio service couldn't start.", _startError);

        /// <summary>The device system couldn't be created; commands fail with its error until the engine is replaced.</summary>
        public bool Failed => _startError is not null;

        private ILogger Log => _service._logger;

        public void Start()
        {
            _thread.Start();
            Enqueue(() =>
            {
                try
                {
                    _system = _service._createSystem();
                }
                catch (Exception ex)
                {
                    Log.LogError(ex, "The audio device system couldn't start");
                    _startError = ex;
                    return;
                }
                _system.Changed += OnChanged;
                Reconcile();
            });
        }

        public Task StopAsync()
        {
            Enqueue(() =>
            {
                _debounce.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _backoffTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                if (_system is not null)
                {
                    try { Processing.Shutdown(Arbiter, Catalog, _system); }
                    catch (Exception ex) { Log.LogError(ex, "Stopping mic processing failed"); }
                }
                _meterTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                if (_system is not null)
                {
                    _system.Changed -= OnChanged;
                    _system.Dispose();
                    _system = null;
                }
            });
            _queue.CompleteAdding();
            return _stopped.Task;
        }

        public Task<T> Invoke<T>(Func<Engine, T> op)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!Enqueue(() =>
            {
                try { done.SetResult(op(this)); }
                catch (Exception ex) { done.SetException(ex); }
            }))
            {
                done.SetException(new InvalidOperationException("The audio service is stopping."));
            }
            return done.Task;
        }

        /// <summary>Re-reads devices, enforces role claims, then publishes. Device events land here after the debounce.</summary>
        public void Reconcile()
        {
            var system = System;
            Catalog.Refresh(system);
            Processing.Update(Arbiter, Catalog, system);
            var changes = Arbiter.Enforce(Catalog, system);
            if (changes.Count > 0)
                Catalog.RefreshDefaults(system);
            ScheduleBackoffRecheck();
            Read();
            Publish(changes);
        }

        /// <summary>Re-reads volumes, mutes and sessions only. Level and session events land here.</summary>
        public void RefreshLevels()
        {
            Volatile.Write(ref _lightQueued, 0);
            Read();
            Publish([]);
        }

        public void ForEachSession(AppKey app, Action<string> set)
        {
            foreach (var session in Sessions.SessionsOf(app).ToList())
            {
                try { set(session.SessionId); }
                catch (Exception ex) { Log.LogWarning(ex, "Changing session {Id} of {App} failed", session.SessionId, app); }
            }
        }

        public IDisposable Watch(IReadOnlyCollection<string> endpointIds, IReadOnlyCollection<AppKey> apps)
        {
            var watch = new MeterWatch(this, endpointIds, apps);
            lock (_watches)
            {
                _watches.Add(watch);
                if (_watches.Count == 1)
                    SetMeterTimer(TimeSpan.Zero, _service._options.MeterInterval);
            }
            return watch;
        }

        private void Unwatch(MeterWatch watch)
        {
            lock (_watches)
            {
                if (_watches.Remove(watch) && _watches.Count == 0)
                    SetMeterTimer(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        // A role in back-off is repaired again only by a reconcile, so one is scheduled for when it ends.
        private void ScheduleBackoffRecheck()
        {
            if (Arbiter.BackoffEnds is not { } end)
                return;
            var due = end - _service._options.Time.GetUtcNow();
            _backoffTimer.Change(due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        private void SetMeterTimer(TimeSpan due, TimeSpan period)
        {
            try { _meterTimer.Change(due, period); }
            catch (ObjectDisposedException) { } // the engine already stopped
        }

        private void Read()
        {
            var system = System;
            Controls.Refresh(system, Catalog.Active(AudioFlow.Render).Concat(Catalog.Active(AudioFlow.Capture)));
            try { Sessions.Refresh(system, Catalog.Active(AudioFlow.Render)); }
            catch (Exception ex) { Log.LogWarning(ex, "Reading sessions failed"); }
        }

        private void Publish(IReadOnlyList<RoleChange> changes)
        {
            var snapshot = new AudioSnapshot(
                ++_version,
                [.. Catalog.Endpoints(AudioFlow.Render), .. Catalog.Endpoints(AudioFlow.Capture)],
                new Dictionary<RoleKey, string>(Catalog.Defaults),
                Arbiter.Owners,
                Controls.Levels,
                Sessions.Apps) { Processing = Processing.Status };
            _service.Publish(snapshot, changes);
        }

        // Core Audio callbacks arrive on COM threads: they only schedule work.
        private void OnChanged(DeviceChange change)
        {
            if (change is DeviceChange.Devices or DeviceChange.Defaults)
            {
                try { _debounce.Change(_service._options.Debounce, Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { }
            }
            else if (Interlocked.Exchange(ref _lightQueued, 1) == 0)
            {
                Enqueue(RefreshLevels);
            }
        }

        private void OnMeterTick()
        {
            if (Interlocked.Exchange(ref _meterQueued, 1) == 0)
                Enqueue(PollMeters);
        }

        private void PollMeters()
        {
            Volatile.Write(ref _meterQueued, 0);
            if (_system is null)
                return;

            HashSet<string> endpointIds;
            HashSet<AppKey> apps;
            lock (_watches)
            {
                if (_watches.Count == 0)
                    return;
                endpointIds = _watches.SelectMany(w => w.EndpointIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
                apps = _watches.SelectMany(w => w.Apps).ToHashSet();
            }

            // An endpoint or session can vanish between refresh and read: it reads 0 until the next refresh.
            var endpoints = endpointIds.ToDictionary(id => id, id => Peak(() => _system.GetPeak(id)), StringComparer.OrdinalIgnoreCase);
            var appPeaks = apps.ToDictionary(
                app => app,
                app => Sessions.SessionsOf(app).Select(s => Peak(() => _system.GetSessionPeak(s.SessionId))).DefaultIfEmpty(0f).Max());
            var readings = new MeterReadings(endpoints, appPeaks, Processing.InputLevel, Processing.OutputLevel);
            _service.Post(() => _service.MetersChanged?.Invoke(_service, readings));

            static float Peak(Func<float> read)
            {
                try { return read(); }
                catch { return 0f; }
            }
        }

        private bool Enqueue(Action op)
        {
            if (_queue.IsAddingCompleted)
                return false;
            try
            {
                _queue.Add(op);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false; // completed during shutdown
            }
        }

        private void Loop()
        {
            try
            {
                foreach (var op in _queue.GetConsumingEnumerable())
                {
                    try { op(); }
                    catch (Exception ex) { Log.LogError(ex, "Audio engine operation failed"); }
                }
            }
            finally
            {
                _debounce.Dispose();
                _meterTimer.Dispose();
                _backoffTimer.Dispose();
                _queue.Dispose();
                _stopped.TrySetResult();
            }
        }

        private sealed class MeterWatch(Engine engine, IReadOnlyCollection<string> endpointIds, IReadOnlyCollection<AppKey> apps) : IDisposable
        {
            public IReadOnlyCollection<string> EndpointIds { get; } = endpointIds;

            public IReadOnlyCollection<AppKey> Apps { get; } = apps;

            public void Dispose() => engine.Unwatch(this);
        }
    }
}
