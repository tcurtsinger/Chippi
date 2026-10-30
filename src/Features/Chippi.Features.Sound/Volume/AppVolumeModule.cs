// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Sound.Volume;

/// <summary>Shared state for Settings and the island's App volume page. Never owns a default role.</summary>
public sealed class AppVolumeModule(AudioService? audio = null) : IFeatureModule
{
    /// <summary>The island's App volume tile.</summary>
    public const string TileKey = "sound.mixer";

    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<AppKey, AppDeviceChoice> _current = [];
    private readonly Dictionary<AppKey, string> _seen = [];
    private AudioService? _audio = audio;
    private ModuleContext? _context;
    private IAsyncDisposable? _lease;
    private IIslandTile? _tile;
    private volatile bool _stopping;
    private bool _routingFailed;
    private const long OutputsStaleMs = 2000;
    private string? _outputsRead; // the apps and outputs RefreshOutputsAsync last read (OutputsShape)
    private long _outputsReadAt; // Environment.TickCount64 then

    public FeatureDescriptor Descriptor { get; } = new("app-volume", "App volume", "Volume and output device per app", FeatureGroup.Sound)
    {
        Icon = "Sliders",
        Presets = PresetMembership.Essentials | PresetMembership.Audio | PresetMembership.PowerUser,
    };
    public AppVolumeSettings Settings { get; private set; } = new();
    public AudioSnapshot Snapshot => _audio?.Snapshot ?? AudioSnapshot.Empty;
    public IEnumerable<AppAudio> Apps => Snapshot.Apps.Where(a => Settings.ShowQuietApps || a.IsActive);
    public IReadOnlyList<AudioEndpoint> Outputs => Snapshot.Active(AudioFlow.Render).Where(e => !VbCable.IsCable(e)).ToList();
    public string? Error { get; private set; }
    public bool DevicePickerAvailable => !_routingFailed && _current.Values.Any(v => v.Available);
    public event Action? Changed;

    public string? ChosenOutput(AppKey app) => Settings.RememberOutputs && Settings.Outputs.TryGetValue(app.Value, out var id)
        ? id : _current.GetValueOrDefault(app)?.EndpointId;
    public bool CanPickOutput(AppKey app) => DevicePickerAvailable && _current.GetValueOrDefault(app) is { Available: true };

    public async Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _context = context;
            _stopping = _routingFailed = false;
            Settings = context.Settings.Get<AppVolumeSettings>();
            if (Settings.Outputs is null) Settings = Settings with { Outputs = [] };
            _audio ??= SoundAudio.Get(context.Logger);
            _lease = _audio.Acquire();
            await _audio.RefreshAsync();
            await RefreshOutputsAsync();
            (_outputsRead, _outputsReadAt) = (OutputsShape(Snapshot), Environment.TickCount64);
            _tile = context.Ui.AddIslandTile(new(TileKey, Descriptor.Name, "Sliders", 20, 0, () => new VolumeView(this, VolumeSurface.Island)));
            context.Ui.SetSettingsPage(_ => new VolumeView(this, VolumeSurface.Settings));
            _audio.SnapshotChanged += OnSnapshot;
            UpdateTile();
        }
        catch
        {
            if (_lease is not null) { await _lease.DisposeAsync(); _lease = null; }
            _context = null;
            throw;
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _stopping = true;
        if (_audio is not null) _audio.SnapshotChanged -= OnSnapshot;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (_lease is not null) { await _lease.DisposeAsync(); _lease = null; }
            _context = null;
            _tile = null;
            _current.Clear(); _seen.Clear();
            _outputsRead = null;
        }
        finally { _gate.Release(); }
    }

    public Task SetVolumeAsync(AppKey app, float volume) => Change(async () => await _audio!.SetAppVolumeAsync(app, volume));
    public Task SetMutedAsync(AppKey app, bool muted) => Change(async () => await _audio!.SetAppMuteAsync(app, muted));
    public Task SetOutputAsync(AppKey app, string? id) => Change(async () =>
    {
        if (!CanPickOutput(app)) throw new AudioFailure("App output unavailable");
        try { await _audio!.SetAppOutputAsync(app, id); }
        catch (AppOutputPolicyException) { _routingFailed = true; throw; }
        _current[app] = new(true, id);
        _seen.Remove(app);
        var choices = new Dictionary<string, string>(Settings.Outputs);
        if (id is null) choices.Remove(app.Value); else choices[app.Value] = id;
        if (Settings.RememberOutputs) Save(Settings with { Outputs = choices });
    });
    public Task SetShowQuietAsync(bool value) => Change(() => { Save(Settings with { ShowQuietApps = value }); return Task.CompletedTask; });
    public Task SetRememberAsync(bool value) => Change(() =>
    {
        Save(Settings with { RememberOutputs = value, Outputs = value
            ? _current.Where(p => p.Value.EndpointId is not null).ToDictionary(p => p.Key.Value, p => p.Value.EndpointId!) : [] });
        _seen.Clear();
        return Task.CompletedTask;
    });

    private void Save(AppVolumeSettings settings) { _context!.Settings.Save(settings); Settings = settings; }

    private async Task Change(Func<Task> command)
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is null || _stopping) throw new AudioFailure("App volume uninstalled");
            await command();
            Error = null;
            UpdateTile();
        }
        catch (Exception ex) { Error = AudioErrors.Reason(ex); _context?.Logger.LogWarning(ex, "Changing app volume failed"); throw; }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private async void OnSnapshot(object? sender, AudioSnapshot snapshot)
    {
        await _gate.WaitAsync();
        try
        {
            if (_stopping || _context is null) return;
            // A slider drag publishes a snapshot per step: outputs are read again when the apps or the
            // outputs changed, otherwise at most every 2s (an output changed in Windows that doesn't
            // move the app's stream changes nothing here).
            var shape = OutputsShape(snapshot);
            if (shape != _outputsRead || Environment.TickCount64 - _outputsReadAt >= OutputsStaleMs)
            {
                await RefreshOutputsAsync();
                _outputsRead = shape;
                _outputsReadAt = Environment.TickCount64;
            }
            UpdateTile();
        }
        catch (Exception ex) { Error = AudioErrors.Reason(ex); }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    private async Task RefreshOutputsAsync()
    {
        var apps = Snapshot.Apps;
        foreach (var key in _seen.Keys.Where(k => apps.All(a => a.Key != k)).ToList()) { _seen.Remove(key); _current.Remove(key); }
        if (_routingFailed) return;
        foreach (var app in apps.Where(a => a.ExePath is not null && a.Key != AppKey.SystemSounds))
        {
            var choice = await _audio!.GetAppOutputAsync(app.Key);
            if (!choice.HasSession)
            {
                _current.Remove(app.Key); _seen.Remove(app.Key);
                continue;
            }
            if (!choice.Available)
            {
                if (_current.Values.Any(v => v.Available)) _routingFailed = true;
                _current[app.Key] = choice;
                continue;
            }
            _current[app.Key] = choice;
            var hasSaved = Settings.RememberOutputs && Settings.Outputs.TryGetValue(app.Key.Value, out _);
            var desired = hasSaved ? Settings.Outputs[app.Key.Value] : null;
            var available = desired is not null && Outputs.Any(e => string.Equals(e.Id, desired, StringComparison.OrdinalIgnoreCase));
            var signature = string.Join(",", app.ProcessIds.Order()) + ":" + available;
            if (hasSaved && _seen.GetValueOrDefault(app.Key) != signature)
            {
                var target = available ? desired : null;
                if (!string.Equals(choice.EndpointId, target, StringComparison.OrdinalIgnoreCase))
                {
                    try { await _audio.SetAppOutputAsync(app.Key, target); _current[app.Key] = new(true, target); }
                    catch (AppOutputPolicyException) { _routingFailed = true; throw; }
                }
            }
            _seen[app.Key] = signature;
        }
    }

    /// <summary>Each app's processes and the outputs its sessions play on, and the outputs there are.</summary>
    internal static string OutputsShape(AudioSnapshot snapshot) =>
        string.Join("|", snapshot.Apps.Select(a => $"{a.Key}:{string.Join(",", a.ProcessIds.Order())}:{string.Join(",", a.EndpointIds)}"))
        + "#" + string.Join("|", snapshot.Active(AudioFlow.Render).Select(e => e.Id));

    private void UpdateTile() => _tile?.SetStatus(Apps.Count() is var count && count == 1 ? "1 app" : $"{count} apps");
}