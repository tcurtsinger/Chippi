// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Mute;

namespace Chippi.Features.PlainPaste;

public sealed record PlainPasteSettings
{
    // Reuse Mic mute's shortcut value/registrar, without its default combination or audio service.
    public MicShortcut? Shortcut { get; init; }
}

/// <summary>Shortcut only. No clipboard listener, tile, page, notification or successful-paste status.</summary>
public sealed class PlainPasteModule : IFeatureModule
{
    public const string Id = "paste-plain-text";
    public const string FeedbackKey = "plain-paste";
    public static readonly TimeSpan FeedbackDuration = TimeSpan.FromSeconds(6);
    private readonly Func<IMicHotkey> _create;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly Lock _feedbackGate = new();
    private ModuleContext? _context;
    private IMicHotkey? _hotkey;
    private ITimer? _timer;
    private DateTimeOffset _expires;
    private volatile bool _stopping = true, _capturing;

    public PlainPasteModule(Func<IMicHotkey>? createHotkey = null, TimeProvider? time = null)
    { _create = createHotkey ?? GlobalMicHotkey.Create; _time = time ?? TimeProvider.System; }
    public FeatureDescriptor Descriptor { get; } = new(Id, "Paste as plain text", "Pastes without the source formatting", FeatureGroup.ClipboardText)
    {
        Icon = "ClipboardType", Requires = [Capability.InputHooks],
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
    };
    public PlainPasteSettings Settings { get; private set; } = new();
    public string? ShortcutError { get; private set; }
    public event Action? Changed;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context; _stopping = false; _capturing = false;
        ShortcutError = null;
        Settings = context.Settings.Get<PlainPasteSettings>();
        if (Settings.Shortcut is { IsValid: false }) Settings = new();
        _timer = _time.CreateTimer(_ => Expire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (Settings.Shortcut is { } shortcut) _hotkey = Reserve(shortcut);
        context.Ui.SetSettingsPage(section => new PlainPasteView(this, section == "shortcut-list" ? Descriptor.Name : "Shortcut"));
        return Task.CompletedTask;
    }
    private IMicHotkey? Reserve(MicShortcut shortcut)
    {
        IMicHotkey? candidate = null;
        try
        {
            candidate = _create();
            if (candidate.Register(shortcut, () => { if (ReferenceEquals(candidate, _hotkey)) OnShortcut(); }))
            {
                ShortcutError = null;
                return candidate;
            }
            ShortcutError = "Shortcut in use";
        }
        catch { ShortcutError = "Shortcut unavailable"; }
        candidate?.Dispose();
        return null;
    }

    private bool Save(MicShortcut? shortcut)
    {
        var candidate = Settings with { Shortcut = shortcut };
        try { _context!.Settings.Save(candidate); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShortcutError = "Save failed";
            return false;
        }
        Settings = candidate;
        ShortcutError = null;
        return true;
    }

    public bool ChangeShortcut(MicShortcut shortcut)
    {
        if (_stopping) return false;
        if (!shortcut.IsValid) { ShortcutError = "Shortcut unavailable"; Changed?.Invoke(); return false; }
        if (Settings.Shortcut == shortcut && _hotkey is not null)
        {
            ShortcutError = null;
            Changed?.Invoke();
            return true;
        }
        // Keep the previous combination registered until its replacement is both available and saved.
        var replacement = Reserve(shortcut);
        bool saved = replacement is not null && Save(shortcut);
        if (saved)
        {
            _hotkey?.Dispose();
            _hotkey = replacement;
        }
        else replacement?.Dispose();
        Changed?.Invoke();
        return saved;
    }
    public void RemoveShortcut()
    {
        if (_stopping) return;
        if (Save(null))
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }
        Changed?.Invoke();
    }
    public void CaptureShortcut(bool capture) => _capturing = capture;
    private async void OnShortcut() { try { await InvokeAsync(); } catch { /* The shortcut never logs clipboard contents or exception messages. */ } }

    public async Task InvokeAsync()
    {
        if (_stopping || _capturing || Settings.Shortcut is null || !_running.Wait(0)) return;
        try
        {
            if (_stopping || _context?.Clipboard is not { } hub) return;
            var outcome = await hub.PastePlainTextAsync().ConfigureAwait(false);
            lock (_feedbackGate)
            {
                if (_stopping) return;
                if (outcome == PasteOutcome.Sent)
                {
                    _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    _context.Island.Clear(FeedbackKey);
                    return;
                }
                hub.Beep();
                string title = outcome switch { PasteOutcome.Copied => "Plain text copied", PasteOutcome.NoText => "No text copied", _ => "Clipboard busy" };
                _context.Island.Publish(new IslandActivity(FeedbackKey, IslandActivityKind.Status, title, "") { NeedsAttention = outcome != PasteOutcome.Copied });
                _expires = _time.GetUtcNow() + FeedbackDuration;
                _timer?.Change(FeedbackDuration, Timeout.InfiniteTimeSpan);
            }
        }
        finally { _running.Release(); }
    }
    private void Expire()
    {
        lock (_feedbackGate)
            if (!_stopping && _time.GetUtcNow() >= _expires) _context?.Island.Clear(FeedbackKey);
    }
    public async Task StopAsync(CancellationToken ct)
    {
        _stopping = true; _capturing = false;
        _hotkey?.Dispose(); _hotkey = null;
        await _running.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_feedbackGate)
            {
                _timer?.Dispose(); _timer = null;
                _context?.Island.Clear(FeedbackKey); _context = null;
            }
        }
        finally { _running.Release(); }
    }
}
