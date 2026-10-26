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
    public const string OutputTile = "sound.output";
    public const string MicrophoneTile = "sound.microphone";
    /// <summary>The shell's Now Playing page; sections under this key sit below the player.</summary>
    public const string NowPlayingTile = "now-playing";
    private static readonly AudioRole[] AllRoles = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];
    private static readonly AudioRole[] WithoutCalls = [AudioRole.Console, AudioRole.Multimedia];
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Func<AudioEndpoint, bool> _excluded;
    private AudioService? _audio;
    private IAsyncDisposable? _lease;
    private ModuleContext? _context;
    private IIslandTile? _outputTile;
    private IIslandTile? _microphoneTile;
    private volatile bool _stopping;

    public AudioRoutingModule() : this(null) { }

    /// <param name="excluded">Verified transport endpoints supplied by the processing integration; never a name heuristic.</param>
    public AudioRoutingModule(AudioService? audio, Func<AudioEndpoint, bool>? excluded = null)
    {
        _audio = audio;
        _excluded = excluded ?? (_ => false);
    }

    public FeatureDescriptor Descriptor { get; } = new("audio-routing", "Audio routing",
        "Switches outputs by priority and keeps your mic pinned when devices change", FeatureGroup.Sound)
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
                new("mic-pinned", "Mic pinned again", NoticePlaces.None),
            ]);
            _outputTile = context.Ui.AddIslandTile(new(OutputTile, "Output", "Headphones", 20, 0,
                () => new RoutingQuickView(this, AudioFlow.Render, island: true)));
            _microphoneTile = context.Ui.AddIslandTile(new(MicrophoneTile, "Microphone", "Mic", 30, 0,
                () => new RoutingQuickView(this, AudioFlow.Capture, island: true)));
            context.Ui.AddIslandTile(new(NowPlayingTile, "Now Playing", "Music", 0, 20, () => RoutingQuickView.OutputPicker(this)));
            context.Ui.AddFlyoutSection("sound", "Sound", "AudioLines", 10, () => new RoutingQuickView(this));
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
            ?? throw new ArgumentException("Device unavailable");
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

    /// <summary>A quick selection permanently promotes the device, not a one-off OS override.</summary>
    public Task PreferAsync(AudioFlow flow, string id) => MoveAsync(flow, id, 0);

    private async Task EditAsync(AudioFlow flow, Func<RoutingList, RoutingList> edit)
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is null || _stopping) throw new InvalidOperationException("Audio routing uninstalled");
            var settings = Settings.With(flow, edit(Settings.For(flow)));
            settings.Validate();
            _context.Settings.Save(settings);
            Settings = settings;
            await EnforceAsync(flow);
            Error = null;
            UpdateTiles();
        }
        catch (Exception ex) { Error = ex.Message; throw; }
        finally { _gate.Release(); Changed?.Invoke(); }
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
        catch (Exception ex) { Error = ex.Message; _context?.Logger.LogWarning(ex, "Refreshing audio routing failed"); }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private void OnRoles(object? sender, RoleChange change)
    {
        if (_context is null || _stopping || change.Owner != RoleOwner.Routing || change.Kind == RoleChangeKind.Claim) return;
        var name = Snapshot.Find(change.EndpointId)?.Name ?? change.EndpointId;
        if (change.Flow == AudioFlow.Render && change.Roles.Contains(AudioRole.Multimedia))
            _context.Notifier.Show(new("output-switched", "Output switched") { Body = name, IslandKind = IslandActivityKind.Status });
        else if (change.Flow == AudioFlow.Capture && change.Kind == RoleChangeKind.Repair)
            _context.Notifier.Show(new("mic-pinned", "Mic pinned again") { Body = name, IslandKind = IslandActivityKind.Status });
    }

    public async Task SetOutputVolumeAsync(string id, float volume)
    {
        try { await _audio!.SetEndpointVolumeAsync(id, volume); Error = null; }
        catch (Exception ex) { Error = ex.Message; throw; }
        finally { Changed?.Invoke(); }
    }

    public string CurrentName(AudioFlow flow) =>
        Snapshot.Find(Snapshot.Default(flow, AudioRole.Multimedia) ?? "")?.Name ?? (flow == AudioFlow.Render ? "No output" : "No microphone");

    public IReadOnlyList<AudioEndpoint> Addable(AudioFlow flow) => Snapshot.Known(flow)
        .Where(e => !_excluded(e) && !Settings.For(flow).Devices.Any(d => Eq(d.Id, e.Id))).ToList();

    public string Status(AudioFlow flow, PriorityEntry entry)
    {
        var endpoint = Snapshot.Find(entry.Id);
        if (endpoint is null || !endpoint.IsActive) return "Not connected";
        return Eq(Snapshot.Default(flow, AudioRole.Multimedia), entry.Id)
            ? flow == AudioFlow.Render ? "Playing now" : "In use"
            : "Connected";
    }

    public void OpenSettings(AudioFlow flow) => _context?.OpenSettings(flow == AudioFlow.Render ? "output" : "microphone");

    private void UpdateTiles()
    {
        _outputTile?.SetStatus(CurrentName(AudioFlow.Render));
        _microphoneTile?.SetStatus(CurrentName(AudioFlow.Capture));
    }

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
            _outputTile = _microphoneTile = null;
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