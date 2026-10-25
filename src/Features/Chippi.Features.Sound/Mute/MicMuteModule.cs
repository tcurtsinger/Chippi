// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Media;
using Chippi.Audio;
using Chippi.Core.Island;
using Chippi.Core.Modules;

namespace Chippi.Features.Sound.Mute;

public sealed record MicMuteSettings
{
    public MicShortcut Shortcut { get; init; } = new();
    public bool PlaySound { get; init; }
}

public sealed class MicMuteModule : IFeatureModule
{
    public const string Id = "mic-mute";
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Func<IMicHotkey> _createHotkey;
    private readonly Action<bool> _tone;
    private AudioService? _audio;
    private IMicHotkey? _hotkey;
    private IAsyncDisposable? _lease;
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private bool _stopping;
    private bool _capturingShortcut;

    public MicMuteModule(AudioService? audio = null, Func<IMicHotkey>? hotkey = null, Action<bool>? tone = null)
    {
        _audio = audio;
        _createHotkey = hotkey ?? GlobalMicHotkey.Create;
        _tone = tone ?? (muted => { if (muted) SystemSounds.Hand.Play(); else SystemSounds.Asterisk.Play(); });
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Mic mute shortcut", "One shortcut mutes your mic in every app", FeatureGroup.Sound)
    {
        Icon = "MicOff", Requires = [Capability.InputHooks], Presets = PresetMembership.Audio | PresetMembership.PowerUser,
        Badges = [FeatureBadge.Overlap("Overlaps Win+Alt+K")],
    };

    public MicMuteSettings Settings { get; private set; } = new();
    public MicrophoneMuteStatus Status => _audio?.Snapshot.MicrophoneMute ?? MicrophoneMuteStatus.Empty;
    public string? Error { get; private set; }
    public string? ShortcutError { get; private set; }
    public string StatusText => Status.State switch
    {
        MicrophoneMuteState.Muted => "Muted",
        MicrophoneMuteState.Mixed => "Some microphones muted",
        MicrophoneMuteState.Unavailable => "Microphone unavailable",
        MicrophoneMuteState.NoMicrophone => "No microphone",
        _ => "Live",
    };
    public event Action? Changed;

    public async Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        _context = context;
        _stopping = false;
        Settings = context.Settings.Get<MicMuteSettings>();
        if (Settings.Shortcut is null || !Settings.Shortcut.IsValid) Settings = Settings with { Shortcut = new() };
        _audio ??= SoundAudio.Get(context.Logger);
        _lease = _audio.Acquire();
        try
        {
            await _audio.RefreshAsync();
            ct.ThrowIfCancellationRequested();
            _hotkey = _createHotkey();
            ShortcutError = _hotkey.Register(Settings.Shortcut, OnHotkey) ? null : "Shortcut in use";
            _tile = context.Ui.AddIslandTile(new("sound.microphone", "Microphone", "MicOff", 30, 20,
                () => new MicMuteView(this, compact: true)) { StatusPriority = 100 });
            context.Ui.AddFlyoutSection("sound", "Sound", "AudioLines", 30, () => new MicMuteView(this, compact: true));
            context.Ui.SetSettingsPage(() => new MicMuteView(this));
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
            Error = Status.Failed.Count > 0 ? "Some microphones could not be changed" : null;
            if (Error is null && Settings.PlaySound && Status.State is MicrophoneMuteState.Muted or MicrophoneMuteState.Live)
                _tone(Status.State == MicrophoneMuteState.Muted);
            UpdateState();
        }
        catch (Exception ex) { Error = ex.Message; Changed?.Invoke(); throw; }
        finally { _gate.Release(); }
    }

    public void CaptureShortcut(bool capture) => _capturingShortcut = capture;

    public bool ChangeShortcut(MicShortcut shortcut)
    {
        if (!shortcut.IsValid) { ShortcutError = "Shortcut unavailable"; Changed?.Invoke(); return false; }
        if (_hotkey?.Register(shortcut, OnHotkey) != true) { ShortcutError = "Shortcut in use"; Changed?.Invoke(); return false; }
        ShortcutError = null;
        Settings = Settings with { Shortcut = shortcut };
        _context?.Settings.Save(Settings);
        Changed?.Invoke();
        return true;
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
        _tile?.SetStatus(warning ? StatusText : "");
        if (warning)
            _context?.Island.Publish(new("muted", IslandActivityKind.Status, StatusText, "Microphone")
                { NeedsAttention = true, TileKey = "sound.microphone" });
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