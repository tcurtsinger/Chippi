// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Core.Shortcuts;
using System.IO;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Mute;

namespace Chippi.Features.PlainPaste;

public sealed record PlainPasteSettings
{
    // Reuse Mic mute's shortcut value/registrar, without its default combination or audio service.
    public KeyShortcut? Shortcut { get; init; }
}

/// <summary>Shortcut only. No clipboard listener, tile, page, notification or successful-paste status.</summary>
public sealed class PlainPasteModule : IFeatureModule
{
    public const string Id = "paste-plain-text";
    public const string FeedbackKey = "plain-paste";
    private readonly Func<IHotkey>? _create;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly Lock _feedbackGate = new();
    private ModuleContext? _context;
    private IHotkey? _hotkey;
    private volatile bool _stopping = true, _capturing;

    public PlainPasteModule(Func<IHotkey>? createHotkey = null)
    { _create = createHotkey; }
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
        if (Settings.Shortcut is { IsBindable: false }) Settings = new();
        _hotkey = Create(); // listed in Settings › Shortcuts while it runs, set or not
        if (Settings.Shortcut is { } shortcut) Reserve(shortcut);
        context.Ui.SetSettingsPage(_ => new PlainPasteView(this));
        return Task.CompletedTask;
    }
    /// <summary>The listed shortcut takes these keys; the service holds them before it frees the old ones.</summary>
    private bool Reserve(KeyShortcut shortcut)
    {
        try
        {
            if (_hotkey!.Register(shortcut, OnShortcut))
            {
                ShortcutError = null;
                return true;
            }
            ShortcutError = _hotkey.Error ?? ShortcutService.InUse;
        }
        catch { ShortcutError = "Shortcut unavailable"; }
        return false;
    }

    private bool Save(KeyShortcut? shortcut)
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

    public bool ChangeShortcut(KeyShortcut shortcut)
    {
        if (_stopping) return false;
        if (!shortcut.IsBindable) { ShortcutError = "Shortcut unavailable"; Changed?.Invoke(); return false; }
        // A refused change keeps the previous keys held. A failed save takes them back, or holds nothing unsaved.
        var previous = Settings.Shortcut;
        bool saved = Reserve(shortcut) && Save(shortcut);
        if (!saved && ShortcutError == "Save failed" && (previous is null || _hotkey?.Register(previous, OnShortcut) != true))
            _hotkey?.Unregister();
        Changed?.Invoke();
        return saved;
    }
    /// <summary>Settings › Shortcuts with this shortcut's editor open in its row: it's recorded only there.</summary>
    public void EditShortcut() => _context?.EditShortcut(Descriptor.Name);

    public void RemoveShortcut()
    {
        if (_stopping) return;
        if (Save(null))
            _hotkey?.Unregister(); // still listed, unset
        Changed?.Invoke();
    }
    public void CaptureShortcut(bool capture) => _capturing = capture;

    /// <summary>The shortcut in the shell's list, set or not, from start to stop; Settings › Shortcuts changes it here.</summary>
    private IHotkey Create() => _create?.Invoke() ?? (_context ?? throw new InvalidOperationException("Not started")).Shortcuts.Add(Id, Descriptor.Name,
        new(() => Settings.Shortcut, k => ChangeShortcut(k) ? null : ShortcutError, RemoveShortcut) { Icon = Descriptor.Icon });
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
                    _context.Island.Clear(FeedbackKey);
                    return;
                }
                hub.Beep();
                string title = outcome switch { PasteOutcome.Copied => "Plain text copied", PasteOutcome.NoText => "No text copied", _ => "Clipboard busy" };
                _context.Island.Publish(new IslandActivity(FeedbackKey, IslandActivityKind.Status, title, "")
                { IsFeedback = true, NeedsAttention = outcome != PasteOutcome.Copied });
            }
        }
        finally { _running.Release(); }
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
                _context?.Island.Clear(FeedbackKey); _context = null;
            }
        }
        finally { _running.Release(); }
    }
}
