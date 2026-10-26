// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;

namespace Chippi.Audio.Tests;

/// <summary>
/// An <see cref="IDeviceSystem"/> where endpoints appear, vanish, change defaults and mute on
/// command, raising <see cref="Changed"/> the way Windows would. The Sound feature tests link this
/// file. Thread-safe: tests change it while the engine thread reads it.
/// </summary>
public sealed class FakeDeviceSystem : IDeviceSystem, IPerAppDeviceSystem
{
    private readonly Lock _gate = new();
    private readonly List<AudioEndpoint> _endpoints = [];
    private readonly Dictionary<RoleKey, string> _defaults = [];
    private readonly Dictionary<string, EndpointLevel> _levels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _peaks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AudioSessionInfo> _sessions = [];
    private readonly List<(string Id, AudioRole Role)> _writes = [];
    private int _enumerations;
    private int _peakReads;

    public bool AppPolicyAvailable { get; set; }
    public bool AppPolicyFailsWrite { get; set; }
    public Dictionary<int, string?> AppOutputs { get; } = [];
    public List<(int Pid, string? Output)> AppOutputWrites { get; } = [];
    public AppDeviceChoice GetAppOutput(int processId) => new(AppPolicyAvailable, AppOutputs.GetValueOrDefault(processId));
    public bool SetAppOutput(int processId, string? endpointId)
    {
        if (!AppPolicyAvailable || AppPolicyFailsWrite) return false;
        AppOutputs[processId] = endpointId;
        AppOutputWrites.Add((processId, endpointId));
        return true;
    }
    public void EndSession(string id) => UpdateSession(id, s => s with { State = SessionState.Expired });
    public event Action<DeviceChange>? Changed;

    /// <summary>Endpoint IDs whose SetDefault or SetMute throws.</summary>
    public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Endpoint reads that still succeed before one throws; null never throws.</summary>
    public int? ReadsBeforeFailure { get; set; }

    public Thread? CreatedOn { get; } = Thread.CurrentThread;

    public Thread? DisposedOn { get; private set; }

    public bool IsDisposed => DisposedOn is not null;

    /// <summary>Every SetDefault call the arbiter made, in order.</summary>
    public IReadOnlyList<(string Id, AudioRole Role)> Writes
    {
        get { lock (_gate) return _writes.ToList(); }
    }

    public int Enumerations => Volatile.Read(ref _enumerations);

    public int PeakReads => Volatile.Read(ref _peakReads);

    public static AudioEndpoint Speaker(string id, string? name = null) => new(id, name ?? id, AudioFlow.Render, EndpointState.Active);

    public static AudioEndpoint Mic(string id, string? name = null) => new(id, name ?? id, AudioFlow.Capture, EndpointState.Active);

    /// <summary>The KS device VB-CABLE's endpoints hang off (root-enumerated, as its INF installs it).</summary>
    public const string CableDevice = @"{2}.\\?\root#media#0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}";

    /// <summary>VB-CABLE's render endpoint, with the reference string from vbMmeCable64_win10.inf.</summary>
    public static AudioEndpoint CableInput(string id = "CableIn", string device = CableDevice) =>
        new(id, "CABLE Input", AudioFlow.Render, EndpointState.Active, "VB-Audio Virtual Cable", device + @"\vbaudiovacwdm2022_out1_topo_e51a78a01fb100001");

    /// <summary>VB-CABLE's capture endpoint, the one apps record from.</summary>
    public static AudioEndpoint CableOutput(string id = "CableOut", string device = CableDevice) =>
        new(id, "CABLE Output", AudioFlow.Capture, EndpointState.Active, "VB-Audio Virtual Cable", device + @"\vbaudiovacwdm2022_in1_topo_e51a78a01fb200001");

    /// <summary>An endpoint is turned on or off in Sound settings, or unplugged.</summary>
    public void SetState(string id, EndpointState state)
    {
        lock (_gate)
        {
            var i = _endpoints.FindIndex(e => e.Id == id);
            _endpoints[i] = _endpoints[i] with { State = state };
        }
        Raise(DeviceChange.Devices);
    }

    /// <summary>A driver is uninstalled: its endpoints are gone from every list.</summary>
    public void Remove(params string[] ids)
    {
        lock (_gate)
        {
            _endpoints.RemoveAll(e => ids.Contains(e.Id));
            foreach (var key in _defaults.Where(d => ids.Contains(d.Value)).Select(d => d.Key).ToList())
                _defaults.Remove(key);
        }
        Raise(DeviceChange.Devices);
    }

    public FakeDeviceSystem With(params AudioEndpoint[] endpoints)
    {
        lock (_gate)
        {
            foreach (var e in endpoints)
            {
                var i = _endpoints.FindIndex(x => x.Id == e.Id);
                if (i >= 0)
                {
                    _endpoints[i] = e; // the same device plugged back in
                    continue;
                }
                _endpoints.Add(e);
                _levels[e.Id] = new EndpointLevel(0.5f, false);
            }
        }
        return this;
    }

    /// <summary>Sets the default for every role (or <paramref name="roles"/>) without raising, for setup.</summary>
    public FakeDeviceSystem WithDefault(string id, params AudioRole[] roles)
    {
        var flow = Find(id).Flow;
        lock (_gate)
        {
            foreach (var role in roles.Length == 0 ? AllRoles : roles)
                _defaults[new RoleKey(flow, role)] = id;
        }
        return this;
    }

    public FakeDeviceSystem WithSession(AudioSessionInfo session)
    {
        lock (_gate)
            _sessions.Add(session);
        return this;
    }

    /// <summary>A device is plugged in.</summary>
    public void Connect(AudioEndpoint endpoint)
    {
        With(endpoint);
        Raise(DeviceChange.Devices);
    }

    /// <summary>
    /// A device is unplugged: it stays listed as Unplugged, and Windows moves any default it held to
    /// <paramref name="windowsPicks"/>, else the first active endpoint of the flow.
    /// </summary>
    public void Disconnect(string id, string? windowsPicks = null)
    {
        lock (_gate)
        {
            var i = _endpoints.FindIndex(e => e.Id == id);
            var flow = _endpoints[i].Flow;
            _endpoints[i] = _endpoints[i] with { State = EndpointState.Unplugged };
            var next = windowsPicks ?? _endpoints.FirstOrDefault(e => e.Flow == flow && e.IsActive)?.Id;
            foreach (var key in _defaults.Where(d => d.Value == id).Select(d => d.Key).ToList())
            {
                if (next is null)
                    _defaults.Remove(key);
                else
                    _defaults[key] = next;
            }
        }
        Raise(DeviceChange.Devices);
        Raise(DeviceChange.Defaults);
    }

    /// <summary>Something outside Chippi changes a default (Windows, a vendor app, the user in Sound settings).</summary>
    public void ChangeDefault(string id, params AudioRole[] roles)
    {
        WithDefault(id, roles);
        Raise(DeviceChange.Defaults);
    }

    public void ChangeLevel(string id, EndpointLevel level)
    {
        lock (_gate)
            _levels[id] = level;
        Raise(DeviceChange.Levels);
    }

    public void SetPeak(string id, float peak)
    {
        lock (_gate)
            _peaks[id] = peak;
    }

    public void Raise(DeviceChange change) => Changed?.Invoke(change);

    public string? DefaultOf(AudioFlow flow, AudioRole role)
    {
        lock (_gate)
            return _defaults.GetValueOrDefault(new RoleKey(flow, role));
    }

    public AudioSessionInfo SessionById(string id)
    {
        lock (_gate)
            return _sessions.Single(s => s.SessionId == id);
    }

    public IReadOnlyList<AudioEndpoint> GetEndpoints(AudioFlow flow)
    {
        Interlocked.Increment(ref _enumerations);
        lock (_gate)
        {
            if (ReadsBeforeFailure is { } left)
            {
                ReadsBeforeFailure = left == 0 ? null : left - 1;
                if (left == 0) throw new InvalidOperationException("Reading endpoints failed");
            }
            return _endpoints.Where(e => e.Flow == flow && e.State != EndpointState.NotPresent).ToList();
        }
    }

    public string? GetDefault(AudioFlow flow, AudioRole role) => DefaultOf(flow, role);

    public void SetDefault(string endpointId, AudioRole role)
    {
        if (Failing.Contains(endpointId))
            throw new InvalidOperationException("SetDefault failed");
        var flow = Find(endpointId).Flow;
        lock (_gate)
        {
            _writes.Add((endpointId, role));
            _defaults[new RoleKey(flow, role)] = endpointId;
        }
        Raise(DeviceChange.Defaults); // Windows echoes our own change back as a notification
    }

    public EndpointLevel GetLevel(string endpointId)
    {
        lock (_gate)
            return _levels[endpointId];
    }

    public void SetVolume(string endpointId, float volume)
    {
        lock (_gate)
            _levels[endpointId] = _levels[endpointId] with { Volume = volume };
        Raise(DeviceChange.Levels);
    }

    public void SetMute(string endpointId, bool muted)
    {
        if (Failing.Contains(endpointId))
            throw new InvalidOperationException("SetMute failed");
        lock (_gate)
            _levels[endpointId] = _levels[endpointId] with { Muted = muted };
        Raise(DeviceChange.Levels);
    }

    public float GetPeak(string endpointId)
    {
        Interlocked.Increment(ref _peakReads);
        lock (_gate)
            return _peaks.GetValueOrDefault(endpointId);
    }

    public IReadOnlyList<AudioSessionInfo> GetSessions(string renderEndpointId)
    {
        lock (_gate)
            return _sessions.Where(s => s.EndpointId == renderEndpointId).ToList();
    }

    public void SetSessionVolume(string sessionId, float volume) => UpdateSession(sessionId, s => s with { Volume = volume });

    public void SetSessionMute(string sessionId, bool muted) => UpdateSession(sessionId, s => s with { Muted = muted });

    public float GetSessionPeak(string sessionId)
    {
        Interlocked.Increment(ref _peakReads);
        lock (_gate)
            return _peaks.GetValueOrDefault(sessionId);
    }

    public void Dispose() => DisposedOn = Thread.CurrentThread;

    private static readonly AudioRole[] AllRoles = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];

    private AudioEndpoint Find(string id)
    {
        lock (_gate)
            return _endpoints.Single(e => e.Id == id);
    }

    private void UpdateSession(string id, Func<AudioSessionInfo, AudioSessionInfo> change)
    {
        lock (_gate)
        {
            var i = _sessions.FindIndex(s => s.SessionId == id);
            _sessions[i] = change(_sessions[i]);
        }
        Raise(DeviceChange.Levels);
    }
}

/// <summary>A clock tests move by hand.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Records every pipeline the engine builds instead of capturing or rendering anything.</summary>
public sealed class FakePipelines
{
    private readonly Lock _gate = new();
    private readonly List<FakePipeline> _created = [];

    /// <summary>Source IDs whose pipeline throws on Start.</summary>
    public HashSet<string> FailOnStart { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FakePipeline> Created
    {
        get { lock (_gate) return _created.ToList(); }
    }

    public FakePipeline? Live => Created.LastOrDefault(p => p.Started && !p.Disposed);

    public Chippi.Audio.Processing.MicPipelineFactory Factory => (source, sink, settings) =>
    {
        var pipeline = new FakePipeline(source, sink, settings, FailOnStart.Contains(source));
        lock (_gate)
            _created.Add(pipeline);
        return pipeline;
    };
}

public sealed class FakePipeline(string source, string sink, Chippi.Audio.Processing.MicSettings settings, bool failOnStart) : Chippi.Audio.Processing.IMicPipeline
{
    public string Source { get; } = source;

    public string Sink { get; } = sink;

    public Chippi.Audio.Processing.MicSettings Settings { get; } = settings;

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public float InputLevel { get; set; } = 0.3f;

    public float OutputLevel { get; set; } = 0.2f;

    public event Action<Exception?>? Failed;

    public void Start()
    {
        if (failOnStart)
            throw new AudioFailure("Device in use");
        Started = true;
    }

    /// <summary>The device went away under a running stream.</summary>
    public void Fail(string message) => Failed?.Invoke(new AudioFailure(message));

    public void Dispose() => Disposed = true;
}
