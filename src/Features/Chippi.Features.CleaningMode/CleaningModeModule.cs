// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.Features.CleaningMode.Screens;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.CleaningMode;

/// <summary>Cleaning mode's settings: its shortcut, none by default.</summary>
public sealed record CleaningModeSettings
{
    public MicShortcut? Shortcut { get; init; }
}

/// <summary>
/// Cleaning mode (docs/phase-5.5/cleaning-mode.md): wipe the keyboard and the screens without typing into whatever's open.
/// Started from Settings › Cleaning mode, its shortcut, or the Actions page's Cleaning while Quick actions is installed.
/// No home button of its own.
/// </summary>
public sealed class CleaningModeModule : IFeatureModule
{
    public const string Id = "cleaning-mode";

    private readonly CleaningSession _session;
    private readonly Func<IMicHotkey> _createHotkey;
    private IMicHotkey? _hotkey;
    private ModuleContext? _context;
    private bool _capturing;

    public CleaningModeModule() : this(new KeyboardLock(), () => new CleaningScreens(), GlobalMicHotkey.Create) { }

    public CleaningModeModule(IKeyboardLock keyboard, Func<ICleaningScreens> screens, Func<IMicHotkey> hotkey)
    {
        _session = new CleaningSession(keyboard, screens);
        _session.Changed += () => Changed?.Invoke();
        _createHotkey = hotkey;
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Cleaning mode", "Locks the keyboard and blacks out the screens so you can wipe them", FeatureGroup.System)
    {
        Icon = nameof(LucideIcons.Droplet),
        Requires = [Capability.InputHooks],
    };

    public CleaningModeSettings Settings { get; private set; } = new();

    /// <summary>Installed and running, so it can start (Quick actions offers Cleaning then).</summary>
    public bool Installed => _context is not null;

    /// <summary>Cleaning mode is on, from its start until the black has gone.</summary>
    public bool IsOn => _session.Active;

    /// <summary>Why the shortcut didn't register ("Shortcut in use"), or null.</summary>
    public string? ShortcutError { get; private set; }

    /// <summary>It started or ended, or the settings changed.</summary>
    public event Action? Changed;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _capturing = false;
        ShortcutError = null;
        Settings = context.Settings.Get<CleaningModeSettings>();
        if (Settings.Shortcut is { IsValid: false })
            Settings = new();
        if (Settings.Shortcut is { } shortcut)
            TryRegister(shortcut);
        context.Ui.SetSettingsPage(section => section == "shortcut-list" ? new Views.ShortcutRow(this, Descriptor.Name) : new Views.CleaningModeSettingsPage(this));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        // Its windows are the UI thread's; the registry may stop it from another.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.Invoke(_session.End);
        else
            _session.End();
        _hotkey?.Dispose();
        _hotkey = null;
        _context = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts Cleaning mode: every screen black, then the keyboard locked. Call on the UI thread. Throws when it can't start,
    /// with nothing left locked or shown, so Settings and the Actions page can say so.
    /// </summary>
    public void Start()
    {
        if (_context is null || _capturing)
            return;
        try { _session.Start(); }
        catch (Exception ex)
        {
            Log.Warn("Cleaning mode: starting failed", ex);
            throw;
        }
    }

    // The shortcut has nowhere to say it failed; the log has it.
    private void StartFromShortcut()
    {
        try { Start(); }
        catch (Exception) { }
    }

    // Its shortcut: none by default, as Paste as plain text's.

    public bool ChangeShortcut(MicShortcut shortcut)
    {
        if (_context is null)
            return false;
        if (!shortcut.IsValid)
        {
            ShortcutError = "Shortcut unavailable";
            Changed?.Invoke();
            return false;
        }
        if (!TryRegister(shortcut))
        {
            Changed?.Invoke();
            return false;
        }
        Settings = Settings with { Shortcut = shortcut };
        _context.Settings.Save(Settings);
        Changed?.Invoke();
        return true;
    }

    public void RemoveShortcut()
    {
        if (_context is null)
            return;
        _hotkey?.Dispose();
        _hotkey = null;
        ShortcutError = null;
        Settings = Settings with { Shortcut = null };
        _context.Settings.Save(Settings);
        Changed?.Invoke();
    }

    /// <summary>While Settings records a shortcut, pressing ours doesn't start Cleaning mode.</summary>
    public void CaptureShortcut(bool capture) => _capturing = capture;

    private bool TryRegister(MicShortcut shortcut)
    {
        try
        {
            _hotkey ??= _createHotkey();
            ShortcutError = _hotkey.Register(shortcut, StartFromShortcut) ? null : "Shortcut in use";
        }
        catch (Exception ex)
        {
            Log.Warn("Cleaning mode: registering the shortcut failed", ex);
            ShortcutError = "Shortcut unavailable";
        }
        return ShortcutError is null;
    }
}
