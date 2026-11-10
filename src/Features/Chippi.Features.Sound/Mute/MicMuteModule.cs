// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using System.Media;
using Chippi.Audio;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Sound.Mute;

public sealed record MicMuteSettings
{
    /// <summary>Null once removed in Settings: no global shortcut until one is set again.</summary>
    public KeyShortcut? Shortcut { get; init; } = new();
    public bool PlaySound { get; init; }
}

public sealed class MicMuteModule : IFeatureModule
{
    public const string Id = "mic-mute";
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Func<IHotkey>? _createHotkey;
    private readonly Action<bool> _tone;
    private readonly Func<string?>? _microphone;
    private AudioService? _audio;
    private IHotkey? _hotkey;
    private IAsyncDisposable? _lease;
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private bool _stopping;
    private bool _capturingShortcut;
    private bool _levelFailed; // Error is a level's failure, which a level that works clears; a mute's failure stays

    /// <param name="microphone">The mic in use as Audio routing picks it, never VB-CABLE (the app passes routing's); null while routing is off.</param>
    public MicMuteModule(AudioService? audio = null, Func<IHotkey>? hotkey = null, Action<bool>? tone = null, Func<string?>? microphone = null)
    {
        _audio = audio;
        _createHotkey = hotkey;
        _tone = tone ?? (muted => { if (muted) SystemSounds.Hand.Play(); else SystemSounds.Asterisk.Play(); });
        _microphone = microphone;
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Mic mute shortcut", "Hotkey for all microphones", FeatureGroup.Sound)
    {
        Icon = "MicOff", Requires = [Capability.InputHooks], Presets = PresetMembership.Audio | PresetMembership.PowerUser,
    };

    public MicMuteSettings Settings { get; private set; } = new();
    public MicrophoneMuteStatus Status => _audio?.Snapshot.MicrophoneMute ?? MicrophoneMuteStatus.Empty;
    public string? Error { get; private set; }
    public string? ShortcutError { get; private set; }

    /// <summary>
    /// The mic whose input level the Sound page's Microphone row shows: routing's mic in use, else
    /// Windows' default mic unless it's VB-CABLE. Null when neither is connected.
    /// </summary>
    public string? LevelMicrophone
    {
        get
        {
            if (_audio?.Snapshot is not { } snapshot) return null;
            if (_microphone?.Invoke() is { } id && Settable(snapshot, id)) return id;
            return snapshot.Default(AudioFlow.Capture, AudioRole.Multimedia) is { } fallback && Settable(snapshot, fallback) ? fallback : null;
        }
    }

    /// <summary>Windows' name for <see cref="LevelMicrophone"/>.</summary>
    public string? LevelMicrophoneName => LevelMicrophone is { } id ? _audio!.Snapshot.Find(id)?.Name : null;

    /// <summary><see cref="LevelMicrophone"/> and its input level, 0 to 1; null when there's none to show.</summary>
    public (string Id, float Volume)? MicLevel =>
        LevelMicrophone is { } id && _audio!.Snapshot.Levels.TryGetValue(id, out var level) ? (id, level.Volume) : null;

    /// <summary>
    /// Sets the input level of the mic <paramref name="id"/>, the one a slider shows; a failure reads
    /// "Volume failed · why". A mic that's gone, or VB-CABLE, is left alone.
    /// </summary>
    public async Task SetMicLevelAsync(string id, float volume)
    {
        if (_lease is null || !Settable(_audio!.Snapshot, id)) return;
        try
        {
            await _audio.SetEndpointVolumeAsync(id, volume);
            if (_levelFailed) { Error = null; _levelFailed = false; }
        }
        catch (Exception ex)
        {
            Error = Routing.AudioRoutingModule.Failed("Volume", ex);
            _levelFailed = true;
            _context?.Logger.LogWarning(ex, "Setting the microphone level failed");
            throw;
        }
        finally { Changed?.Invoke(); }
    }

    private static bool Settable(AudioSnapshot snapshot, string id) =>
        snapshot.Find(id) is { IsActive: true, Flow: AudioFlow.Capture } endpoint && !VbCable.IsCable(endpoint);

    public string StatusText => Status.State switch
    {
        MicrophoneMuteState.Muted => "Muted",
        MicrophoneMuteState.Mixed => "Partly muted",
        MicrophoneMuteState.Unavailable => "Microphone unavailable",
        MicrophoneMuteState.NoMicrophone => "No microphone",
        _ => "Live",
    };

    // The Sound destination also contains playback controls, so name the input in its live reading.
    private string GalleryStatus() => Status.State switch
    {
        MicrophoneMuteState.Live => "Microphone live",
        MicrophoneMuteState.Muted => "Microphone muted",
        MicrophoneMuteState.Mixed => "Microphone partly muted",
        _ => StatusText,
    };
    public event Action? Changed;

    public async Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        _context = context;
        _stopping = false;
        Settings = context.Settings.Get<MicMuteSettings>();
        if (Settings.Shortcut is { IsBindable: false }) Settings = Settings with { Shortcut = new() };
        _audio ??= SoundAudio.Get(context.Logger);
        _lease = _audio.Acquire();
        try
        {
            await _audio.ReadyAsync();
            ct.ThrowIfCancellationRequested();
            _hotkey = CreateHotkey(context);
            ShortcutError = Settings.Shortcut is null || _hotkey.Register(Settings.Shortcut, OnHotkey) ? null : _hotkey.Error ?? ShortcutService.InUse;
            _tile = context.Ui.AddIslandTile(SoundTile.Section(10, SoundTile.Microphone, () => new MicQuickView(this))
                with { StatusPriority = 100, Toggle = new IslandTileToggle(() => Status.State == MicrophoneMuteState.Muted, ToggleAsync, "Microphone", "Mic", GalleryStatus) });
            // The Command bar: mute or unmute every microphone, as the shortcut does.
            context.Ui.AddCommands(() => Status.State is MicrophoneMuteState.Muted or MicrophoneMuteState.Live
                ? [new ChippiCommand("mic-mute/toggle", Status.State == MicrophoneMuteState.Muted ? "Unmute microphone" : "Mute microphone",
                    Status.State == MicrophoneMuteState.Muted ? nameof(LucideIcons.Mic) : nameof(LucideIcons.MicOff), ToggleAsync) { Words = "mic microphone mute unmute" }]
                : []);
            // Settings › Microphone: "mute" is the Microphone row at the top, the rest the Mute shortcut section.
            context.Ui.SetSettingsPage(section => new MicMuteView(this, section == "mute" ? MicMuteSurface.Microphone : MicMuteSurface.Shortcut));
            _audio.SnapshotChanged += OnSnapshot;
            UpdateState();
        }
        catch { await StopAsync(CancellationToken.None); throw; }
    }

    private async void OnHotkey()
    {
        if (_capturingShortcut) return;
        try { await ToggleAsync(); }
        catch { /* Error is shown in the module. */ }
    }

    public async Task ToggleAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_stopping || _lease is null) return;
            await _audio!.ToggleMicrophoneMuteAsync();
            Error = Status.Failed.Count > 0 ? "Mute failed · some mics" : null;
            _levelFailed = false;
            if (Error is null && Settings.PlaySound && Status.State is MicrophoneMuteState.Muted or MicrophoneMuteState.Live)
                _tone(Status.State == MicrophoneMuteState.Muted);
            UpdateState();
        }
        catch (Exception ex)
        {
            Error = Routing.AudioRoutingModule.Failed("Mute", ex);
            _levelFailed = false;
            _context?.Logger.LogWarning(ex, "Toggling the microphone mute failed");
            Changed?.Invoke();
            throw;
        }
        finally { _gate.Release(); }
    }

    public void CaptureShortcut(bool capture) => _capturingShortcut = capture;

    /// <summary>The shortcut in the shell's list, set or not, from start to stop.</summary>
    private IHotkey CreateHotkey(ModuleContext context) => _createHotkey?.Invoke() ?? context.Shortcuts.Add(Id, "Mute microphone", new(() => Settings.Shortcut, k => ChangeShortcut(k) ? null : ShortcutError, RemoveShortcut) { Icon = Descriptor.Icon });

    public bool ChangeShortcut(KeyShortcut shortcut)
    {
        if (!shortcut.IsBindable) { ShortcutError = "Shortcut unavailable"; Changed?.Invoke(); return false; }
        if (_context is not null && !_stopping) _hotkey ??= CreateHotkey(_context);
        if (_hotkey?.Register(shortcut, OnHotkey) != true) { ShortcutError = _hotkey?.Error ?? ShortcutService.InUse; Changed?.Invoke(); return false; }
        ShortcutError = null;
        Settings = Settings with { Shortcut = shortcut };
        _context?.Settings.Save(Settings);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Settings › Shortcuts with this shortcut's editor open in its row: it's recorded only there.</summary>
    public void EditShortcut() => _context?.EditShortcut("Mute microphone");

    public void RemoveShortcut()
    {
        _hotkey?.Unregister(); // still listed in Settings › Shortcuts, unset
        ShortcutError = null;
        Settings = Settings with { Shortcut = null };
        _context?.Settings.Save(Settings);
        Changed?.Invoke();
    }

    public void SetPlaySound(bool enabled)
    {
        Settings = Settings with { PlaySound = enabled };
        _context?.Settings.Save(Settings);
        Changed?.Invoke();
    }

    private void OnSnapshot(object? sender, AudioSnapshot snapshot) { if (!_stopping) UpdateState(); }

    private void UpdateState()
    {
        bool warning = Status.State is MicrophoneMuteState.Muted or MicrophoneMuteState.Mixed;
        _tile?.SetStatus(!warning ? "" : Status.State == MicrophoneMuteState.Muted ? "Mic muted" : StatusText);
        _tile?.Refresh();
        if (warning)
            _context?.Island.Publish(new("muted", IslandActivityKind.Status, StatusText, "Microphone")
                { NeedsAttention = true, TileKey = SoundTile.Key });
        else _context?.Island.Clear("muted");
        Changed?.Invoke();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            _stopping = true;
            _capturingShortcut = false;
            _hotkey?.Dispose();
            _hotkey = null;
            if (_audio is not null) _audio.SnapshotChanged -= OnSnapshot;
            try { if (_lease is not null) await _audio!.ReleaseMicrophoneMuteAsync(); }
            finally
            {
                _context?.Island.Clear("muted");
                _tile = null;
                _context = null;
                if (_lease is not null) { await _lease.DisposeAsync(); _lease = null; }
            }
        }
        finally { _gate.Release(); }
    }
}
