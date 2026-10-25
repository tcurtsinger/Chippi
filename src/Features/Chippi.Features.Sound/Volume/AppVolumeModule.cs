// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;
using Chippi.Core.Modules;

namespace Chippi.Features.Sound.Volume;

/// <summary>Shared state for Settings, Sound flyout and Volume mixer. Never owns a default role.</summary>
public sealed class AppVolumeModule(AudioService? audio = null) : IFeatureModule
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<AppKey, AppDeviceChoice> _current = [];
    private readonly Dictionary<AppKey, string> _seen = [];
    private AudioService? _audio = audio;
    private ModuleContext? _context;
    private IAsyncDisposable? _lease;
    private IIslandTile? _tile;
    private volatile bool _stopping;
    private bool _routingFailed;
    private string? _viewedOutput;

    public FeatureDescriptor Descriptor { get; } = new("app-volume", "App volume", "Volume and output device for each app, right from the tray", FeatureGroup.Sound)
    {
        Icon = "Sliders",
        Presets = PresetMembership.Essentials | PresetMembership.Audio | PresetMembership.PowerUser,
        Badges = [FeatureBadge.Overlap("Overlaps Volume mixer")],
    };
    public AppVolumeSettings Settings { get; private set; } = new();
    public AudioSnapshot Snapshot => _audio?.Snapshot ?? AudioSnapshot.Empty;
    public IEnumerable<AppAudio> Apps => Snapshot.Apps.Where(a => Settings.ShowQuietApps || a.IsActive);
    public IReadOnlyList<AudioEndpoint> Outputs => Snapshot.Active(AudioFlow.Render).Where(e => !VbCable.IsCable(e)).ToList();
    public AudioEndpoint? Output => Outputs.FirstOrDefault(e => e.Id == _viewedOutput)
        ?? Outputs.FirstOrDefault(e => e.Id == Snapshot.Default(AudioFlow.Render, AudioRole.Multimedia)) ?? Outputs.FirstOrDefault();
    public void ViewOutput(string? id) { _viewedOutput = id; Changed?.Invoke(); }
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
            _tile = context.Ui.AddIslandTile(new("sound.mixer", "Volume mixer", "Sliders", 10, 0, () => new VolumeView(this, VolumeSurface.Island))
            { PageWidth = 344, Orbit = IslandOrbit.Right });
            context.Ui.AddIslandTile(new("sound.output", "Output", "Headphones", 20, 20, () => new VolumeView(this, VolumeSurface.Output)));
            context.Ui.AddFlyoutSection("sound", "Sound", "AudioLines", 30, () => new VolumeView(this, VolumeSurface.Flyout));
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
        }
        finally { _gate.Release(); }
    }

    public Task SetVolumeAsync(AppKey app, float volume) => Change(async () => await _audio!.SetAppVolumeAsync(app, volume));
    public Task SetMutedAsync(AppKey app, bool muted) => Change(async () => await _audio!.SetAppMuteAsync(app, muted));
    public Task SetSystemVolumeAsync(float volume) => Change(async () =>
    {
        if (Output is not { } output) throw new InvalidOperationException("No output");
        await _audio!.SetEndpointVolumeAsync(output.Id, volume);
    });
    public Task SetSystemMutedAsync(bool muted) => Change(async () =>
    {
        if (Output is not { } output) throw new InvalidOperationException("No output");
        var failed = await _audio!.SetEndpointMuteAsync([output.Id], muted);
        if (failed.Count > 0) throw new InvalidOperationException("Couldn't mute output");
    });

    public Task SetOutputAsync(AppKey app, string? id) => Change(async () =>
    {
        if (!CanPickOutput(app)) throw new InvalidOperationException("App output unavailable");
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
            if (_context is null || _stopping) throw new InvalidOperationException("App volume uninstalled");
            await command();
            Error = null;
            UpdateTile();
        }
        catch (Exception ex) { Error = ex.Message; throw; }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private async void OnSnapshot(object? sender, AudioSnapshot snapshot)
    {
        await _gate.WaitAsync();
        try
        {
            if (_stopping || _context is null) return;
            await RefreshOutputsAsync();
            UpdateTile();
        }
        catch (Exception ex) { Error = ex.Message; }
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

    private void UpdateTile() => _tile?.SetStatus($"{Apps.Count()} apps");
}