// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Notifications;
using Chippi.Features.Sound.Mute;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Sound.Routing;

public sealed class AudioRoutingModule : IFeatureModule
{
    private static readonly AudioRole[] AllRoles = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];
    private static readonly AudioRole[] WithoutCalls = [AudioRole.Console, AudioRole.Multimedia];
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Func<AudioEndpoint, bool> _excluded;
    private readonly Func<IMicHotkey> _createHotkey;
    private AudioService? _audio;
    private IAsyncDisposable? _lease;
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private IMicHotkey? _hotkey;
    private volatile bool _stopping;
    private volatile bool _capturingShortcut;

    public AudioRoutingModule() : this(null) { }

    /// <param name="excluded">Verified transport endpoints supplied by the processing integration; never a name heuristic.</param>
    /// <param name="hotkey">The output shortcut's registration; a message window, made only once a shortcut is set.</param>
    public AudioRoutingModule(AudioService? audio, Func<AudioEndpoint, bool>? excluded = null, Func<IMicHotkey>? hotkey = null)
    {
        _audio = audio;
        _excluded = excluded ?? (_ => false);
        _createHotkey = hotkey ?? GlobalMicHotkey.Create;
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
    public string? WinningMicrophone => PriorityPolicy.Resolve(Order(AudioFlow.Capture),
        Snapshot.Active(AudioFlow.Capture).Where(e => !_excluded(e)).ToList());

    /// <summary>A pick holds while the same devices stay connected; the next plug or unplug hands back to the list.</summary>
    private sealed record Pick(string Id, IReadOnlySet<string> Connected)
    {
        public bool Holds(IEnumerable<string> connected) => Connected.SetEquals(connected);
    }

    private volatile Pick? _outputPick, _micPick;

    private Pick? PickFor(AudioFlow flow) => flow == AudioFlow.Render ? _outputPick : _micPick;

    private void SetPick(AudioFlow flow, Pick? pick)
    {
        if (flow == AudioFlow.Render) _outputPick = pick;
        else _micPick = pick;
    }

    private HashSet<string> Connected(AudioFlow flow) =>
        Snapshot.Active(flow).Where(e => !_excluded(e)).Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The flow's priority list with a pick that still holds first: what decides the device in use, and
    /// Mic processing's source. Read on the audio engine thread too, so it only reads immutable state.
    /// </summary>
    public IReadOnlyList<PriorityEntry> Order(AudioFlow flow)
    {
        var list = Settings.For(flow).Devices;
        return PickFor(flow) is { } pick ? [new(pick.Id, pick.Id), .. list.Where(e => !Eq(e.Id, pick.Id))] : list;
    }

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
            context.Ui.SetSettingsPage(section => section switch
            {
                "microphone" => new RoutingPage(this, AudioFlow.Capture),
                OutputShortcutSection => new OutputShortcutView(this, list: false),
                "shortcut-list" => new OutputShortcutView(this, list: true),
                _ => new RoutingPage(this, AudioFlow.Render),
            });
            if (Settings.SwitchShortcut is { IsValid: false })
                Settings = Settings with { SwitchShortcut = null };
            if (Settings.SwitchShortcut is { } shortcut)
            {
                _hotkey = _createHotkey();
                ShortcutError = _hotkey.Register(shortcut, OnShortcut) ? null : "Shortcut in use";
            }
            _audio.SnapshotChanged += OnSnapshot;
            _audio.RolesChanged += OnRoles;
            Error = null;
            UpdateTiles();
        }
        catch
        {
            _hotkey?.Dispose();
            _hotkey = null;
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
        var pick = PickFor(flow);
        return list.Enabled
            ? _audio!.ClaimRolesAsync(RoleOwner.Routing, flow, list.Calls ? AllRoles : WithoutCalls,
                active =>
                {
                    var available = active.Where(e => !_excluded(e)).ToList();
                    // A pick wins until the connected devices change; the module then drops it.
                    IReadOnlyList<PriorityEntry> order = pick is not null && pick.Holds(available.Select(e => e.Id)) ? [new(pick.Id, pick.Id), .. list.Devices] : list.Devices;
                    var id = PriorityPolicy.Resolve(order, available);
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
    /// A pick on the Sound page, in Settings or by the shortcut: the device is used now and the list's
    /// order stays. With priority switching on, the pick holds until a device is plugged in or out, and
    /// then the list decides again; with it off, it is set once and left to Windows.
    /// </summary>
    public async Task UseAsync(AudioFlow flow, string id)
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is null || _stopping) throw new AudioFailure("Audio routing uninstalled");
            var connected = Connected(flow);
            if (!connected.Contains(id)) throw new AudioFailure("Device unavailable");
            SetPick(flow, new(id, connected));
            var list = Settings.For(flow);
            if (list.Enabled)
                await EnforceAsync(flow);
            else
                await PickOnceAsync(flow, id, list.Calls);
            Error = null;
            UpdateTiles();
        }
        catch (Exception ex) { Error = Failed("Switch", ex); _context?.Logger.LogWarning(ex, "Switching the device in use failed"); throw; }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private async Task EditAsync(AudioFlow flow, Func<RoutingList, RoutingList> edit)
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is null || _stopping) throw new AudioFailure("Audio routing uninstalled");
            var settings = Settings.With(flow, edit(Settings.For(flow)));
            settings.Validate();
            _context.Settings.Save(settings);
            Settings = settings;
            SetPick(flow, null); // an edited list decides at once
            await EnforceAsync(flow);
            Error = null;
            UpdateTiles();
        }
        catch (Exception ex) { Error = Failed("Change", ex); _context?.Logger.LogWarning(ex, "Changing audio routing failed"); throw; }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    /// <summary>Settings › Output's shortcut section.</summary>
    public const string OutputShortcutSection = "output-shortcut";

    public string? ShortcutError { get; private set; }

    /// <summary>
    /// The output shortcut: the next connected output after the one in use, in priority order, used now
    /// as a pick is, and the bar says which, since the island may be hidden behind a game.
    /// </summary>
    private async void OnShortcut()
    {
        if (_capturingShortcut || _context is null || _stopping) return;
        try
        {
            var connected = Settings.Output.Devices.Where(e => Snapshot.Find(e.Id) is { IsActive: true } d && !_excluded(d)).ToList();
            if (connected.Count < 2) return;
            var inUse = InUse(AudioFlow.Render);
            var next = connected[(connected.FindIndex(e => Eq(e.Id, inUse)) + 1) % connected.Count];
            await UseAsync(AudioFlow.Render, next.Id);
            _context?.Notifier.Show(new("output-switched", "Output switched") { Body = next.Name, IslandKind = IslandActivityKind.Status });
        }
        catch { /* UseAsync shows the error. */ }
    }

    public void CaptureShortcut(bool capture) => _capturingShortcut = capture;

    public bool ChangeShortcut(MicShortcut shortcut)
    {
        if (!shortcut.IsValid) { ShortcutError = "Shortcut unavailable"; Changed?.Invoke(); return false; }
        if (_context is not null && !_stopping) _hotkey ??= _createHotkey();
        if (_hotkey?.Register(shortcut, OnShortcut) != true) { ShortcutError = "Shortcut in use"; Changed?.Invoke(); return false; }
        ShortcutError = null;
        Settings = Settings with { SwitchShortcut = shortcut };
        _context?.Settings.Save(Settings);
        Changed?.Invoke();
        return true;
    }

    public void RemoveShortcut()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        ShortcutError = null;
        Settings = Settings with { SwitchShortcut = null };
        _context?.Settings.Save(Settings);
        Changed?.Invoke();
    }

    /// <summary>"Switch failed · Device in use": what failed, then why, as every error reads (DESIGN.md §3).</summary>
    internal static string Failed(string action, Exception error) =>
        AudioErrors.Reason(error) is var reason && reason.EndsWith(" failed", StringComparison.Ordinal) ? reason : $"{action} failed · {reason}";

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
            bool listed = !ReferenceEquals(refreshed.Output, Settings.Output) || !ReferenceEquals(refreshed.Microphone, Settings.Microphone);
            if (listed)
            {
                _context.Settings.Save(refreshed);
                Settings = refreshed;
            }
            // A device came or went: a pick made before it is spent, so the list decides again.
            bool spent = false;
            foreach (var flow in new[] { AudioFlow.Render, AudioFlow.Capture })
                if (PickFor(flow) is { } pick && !pick.Holds(Connected(flow)))
                {
                    SetPick(flow, null);
                    spent = true;
                }
            if (listed || spent)
            {
                await EnforceAsync(AudioFlow.Render);
                await EnforceAsync(AudioFlow.Capture);
            }
            UpdateTiles();
        }
        catch (Exception ex) { Error = Failed("Switch", ex); _context?.Logger.LogWarning(ex, "Refreshing audio routing failed"); }
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
        catch (Exception ex) { Error = Failed("Volume", ex); _context?.Logger.LogWarning(ex, "Setting the output volume failed"); throw; }
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
        catch (Exception ex) { Error = Failed("Mute", ex); _context?.Logger.LogWarning(ex, "Muting the output failed"); throw; }
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
        _capturingShortcut = false;
        _hotkey?.Dispose();
        _hotkey = null;
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