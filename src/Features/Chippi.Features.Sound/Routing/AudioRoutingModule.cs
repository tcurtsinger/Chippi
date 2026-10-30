// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Sound.Routing;

public sealed class AudioRoutingModule : IFeatureModule
{
    private static readonly AudioRole[] AllRoles = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];
    private static readonly AudioRole[] WithoutCalls = [AudioRole.Console, AudioRole.Multimedia];
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Func<AudioEndpoint, bool> _excluded;
    private AudioService? _audio;
    private IAsyncDisposable? _lease;
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private volatile bool _stopping;

    public AudioRoutingModule() : this(null) { }

    /// <param name="excluded">Verified transport endpoints supplied by the processing integration; never a name heuristic.</param>
    public AudioRoutingModule(AudioService? audio, Func<AudioEndpoint, bool>? excluded = null)
    {
        _audio = audio;
        _excluded = excluded ?? (_ => false);
    }

    public FeatureDescriptor Descriptor { get; } = new("audio-routing", "Audio routing",
        "Default output and mic by priority", FeatureGroup.Sound)
    {
        Icon = "Headphones",
        Presets = PresetMembership.Audio | PresetMembership.PowerUser,
    };

    public RoutingSettings Settings { get; private set; } = new();
    public AudioSnapshot Snapshot => _audio?.Snapshot ?? AudioSnapshot.Empty;
    /// <summary>Routing's real-input winner even while Processing owns the Windows capture roles.</summary>
    public string? WinningMicrophone => PriorityPolicy.Resolve(Settings.Microphone.Devices,
        Snapshot.Active(AudioFlow.Capture).Where(e => !_excluded(e)).ToList());

    public string? Error { get; private set; }
    public event Action? Changed;

    public async Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _context = context;
            _stopping = false;
            Settings = context.Settings.Get<RoutingSettings>();
            try { Settings.Validate(); }
            catch (ArgumentException) { Settings = new(); }
            _audio ??= SoundAudio.Get(context.Logger);
            _lease = _audio.Acquire();
            await _audio.RefreshAsync();
            ct.ThrowIfCancellationRequested();
            Settings = RefreshLists(Settings, Snapshot);
            context.Settings.Save(Settings);
            await EnforceAsync(AudioFlow.Render);
            await EnforceAsync(AudioFlow.Capture);
            context.Notifier.Declare([
                new("output-switched", "Output switched", NoticePlaces.Island),
                new("mic-pinned", "Microphone switched", NoticePlaces.None),
            ]);
            _tile = context.Ui.AddIslandTile(SoundTile.Section(0, SoundTile.Output, () => new RoutingQuickView(this, AudioFlow.Render))
                with { Volume = new IslandTileVolume(OutputVolume, SetHomeVolume, OutputMuted, SetHomeMutedAsync) });
            context.Ui.AddIslandTile(SoundTile.Section(20, SoundTile.Microphone, () => new RoutingQuickView(this, AudioFlow.Capture)));
            context.Ui.SetSettingsPage(section => new RoutingPage(this, section == "microphone" ? AudioFlow.Capture : AudioFlow.Render));
            _audio.SnapshotChanged += OnSnapshot;
            _audio.RolesChanged += OnRoles;
            Error = null;
            UpdateTiles();
        }
        catch
        {
            if (_lease is not null)
            {
                try { await ReleaseLeaseAsync(); }
                catch (Exception cleanup) { context.Logger.LogWarning(cleanup, "Cleaning up failed audio routing start"); }
            }
            _context = null;
            throw;
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    private RoutingSettings RefreshLists(RoutingSettings settings, AudioSnapshot snapshot)
    {
        foreach (var flow in new[] { AudioFlow.Render, AudioFlow.Capture })
        {
            var known = snapshot.Known(flow).Where(e => !_excluded(e)).ToList();
            settings = settings.With(flow, settings.For(flow).Refresh(known, snapshot.Default(flow, AudioRole.Multimedia)));
        }
        return settings;
    }

    private Task EnforceAsync(AudioFlow flow)
    {
        var list = Settings.For(flow);
        return list.Enabled
            ? _audio!.ClaimRolesAsync(RoleOwner.Routing, flow, list.Calls ? AllRoles : WithoutCalls,
                active =>
                {
                    var available = active.Where(e => !_excluded(e)).ToList();
                    var id = PriorityPolicy.Resolve(list.Devices, available);
                    return available.FirstOrDefault(e => Eq(e.Id, id))?.Id;
                })
            : _audio!.ReleaseRolesAsync(RoleOwner.Routing, flow);
    }

    public Task SetEnabledAsync(AudioFlow flow, bool enabled) => EditAsync(flow, list => list with { Enabled = enabled });
    public Task SetCallsAsync(AudioFlow flow, bool calls) => EditAsync(flow, list => list with { Calls = calls });

    public Task AddAsync(AudioFlow flow, string id) => EditAsync(flow, list =>
    {
        var device = Snapshot.Known(flow).FirstOrDefault(e => Eq(e.Id, id) && !_excluded(e))
            ?? throw new AudioFailure("Device unavailable");
        if (list.Devices.Any(e => Eq(e.Id, id))) return list;
        return list with
        {
            Seeded = true,
            Devices = [.. list.Devices, new(device.Id, device.Name)],
            Removed = list.Removed.Where(e => !Eq(e.Id, id)).ToList(),
        };
    });

    public Task RemoveAsync(AudioFlow flow, string id) => EditAsync(flow, list =>
    {
        var item = list.Devices.FirstOrDefault(e => Eq(e.Id, id));
        return item is null ? list : list with
        {
            Seeded = true,
            Devices = list.Devices.Where(e => !Eq(e.Id, id)).ToList(),
            Removed = [.. list.Removed.Where(e => !Eq(e.Id, id)), item],
        };
    });

    public Task MoveAsync(AudioFlow flow, string id, int target) => EditAsync(flow, list =>
    {
        var items = list.Devices.ToList();
        var from = items.FindIndex(e => Eq(e.Id, id));
        if (from < 0 || target < 0 || target >= items.Count || from == target) return list;
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(target, item);
        return list with { Devices = items };
    });

    /// <summary>
    /// A pick on the Sound page: the device moves to the top of its priority list and is used now.
    /// With priority switching off, it is set once and then left to Windows, so a pick always works;
    /// switching only decides what happens when devices come and go.
    /// </summary>
    public Task PreferAsync(AudioFlow flow, string id) => EditAsync(flow, list =>
    {
        var items = list.Devices.ToList();
        var from = items.FindIndex(e => Eq(e.Id, id));
        if (from <= 0) return list;
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(0, item);
        return list with { Devices = items };
    }, pick: id);

    private async Task EditAsync(AudioFlow flow, Func<RoutingList, RoutingList> edit, string? pick = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is null || _stopping) throw new AudioFailure("Audio routing uninstalled");
            var settings = Settings.With(flow, edit(Settings.For(flow)));
            settings.Validate();
            _context.Settings.Save(settings);
            Settings = settings;
            await EnforceAsync(flow);
            if (pick is not null && !settings.For(flow).Enabled)
                await PickOnceAsync(flow, pick, settings.For(flow).Calls);
            Error = null;
            UpdateTiles();
        }
        catch (Exception ex) { Error = AudioErrors.Reason(ex); _context?.Logger.LogWarning(ex, "Changing audio routing failed"); throw; }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    /// <summary>Points the flow's roles at <paramref name="id"/> once: claimed, applied, then released to Windows.</summary>
    private async Task PickOnceAsync(AudioFlow flow, string id, bool calls)
    {
        try
        {
            await _audio!.ClaimRolesAsync(RoleOwner.Routing, flow, calls ? AllRoles : WithoutCalls,
                active => active.FirstOrDefault(e => Eq(e.Id, id))?.Id);
        }
        finally { await _audio!.ReleaseRolesAsync(RoleOwner.Routing, flow); } // a failed pick must not leave switching on
    }

    private async void OnSnapshot(object? sender, AudioSnapshot snapshot)
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is null || _stopping) return;
            var refreshed = RefreshLists(Settings, Snapshot);
            if (!ReferenceEquals(refreshed.Output, Settings.Output) || !ReferenceEquals(refreshed.Microphone, Settings.Microphone))
            {
                _context.Settings.Save(refreshed);
                Settings = refreshed;
                await EnforceAsync(AudioFlow.Render);
                await EnforceAsync(AudioFlow.Capture);
            }
            UpdateTiles();
        }
        catch (Exception ex) { Error = AudioErrors.Reason(ex); _context?.Logger.LogWarning(ex, "Refreshing audio routing failed"); }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private void OnRoles(object? sender, RoleChange change)
    {
        if (_context is null || _stopping || change.Owner != RoleOwner.Routing || change.Kind == RoleChangeKind.Claim) return;
        var name = Snapshot.Find(change.EndpointId)?.Name ?? change.EndpointId;
        if (change.Flow == AudioFlow.Render && change.Roles.Contains(AudioRole.Multimedia))
            _context.Notifier.Show(new("output-switched", "Output switched") { Body = name, IslandKind = IslandActivityKind.Status });
        else if (change.Flow == AudioFlow.Capture && change.Kind == RoleChangeKind.Repair)
            _context.Notifier.Show(new("mic-pinned", "Microphone switched") { Body = name, IslandKind = IslandActivityKind.Status });
    }

    public async Task SetOutputVolumeAsync(string id, float volume)
    {
        try { await _audio!.SetEndpointVolumeAsync(id, volume); Error = null; }
        catch (Exception ex) { Error = AudioErrors.Reason(ex); _context?.Logger.LogWarning(ex, "Setting the output volume failed"); throw; }
        finally { Changed?.Invoke(); }
    }

    /// <summary>Sets the output's mute.</summary>
    public async Task SetOutputMutedAsync(string id, bool muted)
    {
        try
        {
            if ((await _audio!.SetEndpointMuteAsync([id], muted)).Count > 0) throw new AudioFailure("Mute failed");
            Error = null;
        }
        catch (Exception ex) { Error = AudioErrors.Reason(ex); _context?.Logger.LogWarning(ex, "Muting the output failed"); throw; }
        finally { Changed?.Invoke(); }
    }

    /// <summary>
    /// The device in use: the Windows default, except a mic while Mic processing holds the default
    /// (its cable), when it is the real mic Routing picked.
    /// </summary>
    public string? InUse(AudioFlow flow)
    {
        var id = Snapshot.Default(flow, AudioRole.Multimedia);
        return flow == AudioFlow.Capture && (id is null || Snapshot.Find(id) is not { } endpoint || _excluded(endpoint)) ? WinningMicrophone : id;
    }

    public string CurrentName(AudioFlow flow) =>
        Snapshot.Find(InUse(flow) ?? "")?.Name ?? (flow == AudioFlow.Render ? "No output" : "No microphone");

    public IReadOnlyList<AudioEndpoint> Addable(AudioFlow flow) => Snapshot.Known(flow)
        .Where(e => !_excluded(e) && !Settings.For(flow).Devices.Any(d => Eq(d.Id, e.Id))).ToList();

    public string Status(AudioFlow flow, PriorityEntry entry)
    {
        var endpoint = Snapshot.Find(entry.Id);
        if (endpoint is null || !endpoint.IsActive) return "Not connected";
        return Eq(InUse(flow), entry.Id)
            ? flow == AudioFlow.Render ? "Playing now" : "In use"
            : "Connected";
    }

    public void OpenSettings(AudioFlow flow) => _context?.OpenSettings(flow == AudioFlow.Render ? "output" : "microphone");

    /// <summary>The Sound tile shows the output in use.</summary>
    private void UpdateTiles()
    {
        _tile?.SetStatus(CurrentName(AudioFlow.Render));
        _tile?.Refresh();
    }

    private readonly LatestWrite _homeVolume = new();

    /// <summary>The output in use's system volume, 0 to 1 (the home's slider).</summary>
    private double? OutputVolume() =>
        InUse(AudioFlow.Render) is { } id && Snapshot.Levels.TryGetValue(id, out var level) ? level.Volume : null;

    private void SetHomeVolume(double volume)
    {
        if (InUse(AudioFlow.Render) is { } id)
            _homeVolume.Send(() => SetOutputVolumeAsync(id, (float)Math.Clamp(volume, 0, 1)));
    }

    /// <summary>Whether the output in use is muted (the home's speaker).</summary>
    private bool OutputMuted() =>
        InUse(AudioFlow.Render) is { } id && Snapshot.Levels.TryGetValue(id, out var level) && level.Muted;

    private Task SetHomeMutedAsync(bool muted) =>
        InUse(AudioFlow.Render) is { } id ? SetOutputMutedAsync(id, muted) : Task.CompletedTask;

    public async Task StopAsync(CancellationToken ct)
    {
        _stopping = true;
        if (_audio is not null) { _audio.SnapshotChanged -= OnSnapshot; _audio.RolesChanged -= OnRoles; }
        await _gate.WaitAsync();
        try
        {
            if (_lease is not null)
            {
                await ReleaseLeaseAsync();
            }
            _context = null;
            _tile = null;
        }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private async Task ReleaseLeaseAsync()
    {
        var lease = _lease;
        _lease = null;
        if (lease is null) return;
        try
        {
            try { await _audio!.ReleaseRolesAsync(RoleOwner.Routing, AudioFlow.Render); }
            finally { await _audio!.ReleaseRolesAsync(RoleOwner.Routing, AudioFlow.Capture); }
        }
        finally { await lease.DisposeAsync(); }
    }

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}