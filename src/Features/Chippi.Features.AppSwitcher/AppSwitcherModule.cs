// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.AppSwitcher.Windows;

namespace Chippi.Features.AppSwitcher;

/// <summary>The shortcut starts unset (task 226): the tile and Search work as soon as it's installed.</summary>
public sealed record AppSwitcherSettings
{
    public KeyShortcut? Shortcut { get; init; }

    /// <summary>Only the windows on the display it was opened from.</summary>
    public bool CurrentDisplayOnly { get; init; }

    /// <summary>Live previews; off, every window shows its app's icon.</summary>
    public bool Previews { get; init; } = true;

    /// <summary>Program files whose windows show their icon instead of a preview. They're still listed and searched.</summary>
    public string[] NoPreview { get; init; } = [];
}

/// <summary>
/// The App switcher (docs/phase-6/app-switcher.md): one shared command, <c>app-switcher/open</c>, that the Switcher
/// tile, Search, Radial slices and bound shortcuts fire, and its own shortcut. The overlay is the screen's
/// (<see cref="ISwitcherView"/>). Its shortcut is let go while the shared pause holds; an open overlay closes then, and
/// when the PC locks.
/// </summary>
public sealed class AppSwitcherModule : IFeatureModule
{
    public const string Id = "app-switcher";
    public const string OpenKey = "app-switcher/open";
    public const string SwitchFailed = "Switch failed";

    /// <summary>One tile in Actions, not a menu.</summary>
    public static ActionsTile Tile { get; } = new("switcher", "Switcher", "AppWindow");

    private readonly Func<Switcher, ISwitcherView> _view;
    private readonly Func<ISwitcherPc> _createPc;
    private readonly Func<IWindowEvents> _createEvents;
    private readonly Func<IHotkey>? _createHotkey;
    private readonly RecentWindows _recent = new();
    private ModuleContext? _context;
    private ISwitcherPc? _pc;
    private IWindowEvents? _events;
    private IHotkey? _hotkey;
    private Switcher? _switcher;
    private SynchronizationContext? _ui;
    private Action? _pauseListener;
    private bool _paused;

    /// <param name="view">The overlay, made once on first open; the app's by default.</param>
    public AppSwitcherModule(Func<Switcher, ISwitcherView>? view = null, Func<ISwitcherPc>? pc = null, Func<IWindowEvents>? events = null,
        Func<IHotkey>? createHotkey = null)
    {
        _view = view ?? (switcher => new Views.SwitcherOverlay(switcher, this));
        _createPc = pc ?? (() => new WindowsSwitcherPc());
        _createEvents = events ?? (() => new WindowsWindowEvents());
        _createHotkey = createHotkey;
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "App switcher", "Switch to an open window, with previews", FeatureGroup.Windows)
    { Icon = "AppWindow", PausesInFullscreen = true, Presets = PresetMembership.PowerUser };

    public AppSwitcherSettings Settings { get; private set; } = new();

    /// <summary>"Shortcut in use" and the like while the saved shortcut isn't held, else null.</summary>
    public string? ShortcutError { get; private set; }

    /// <summary>The switcher, while the feature runs.</summary>
    public Switcher? Switcher => _switcher;

    public event Action? Changed;

    /// <summary>Recent windows, for tests.</summary>
    internal RecentWindows Recent => _recent;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _ui = SynchronizationContext.Current;
        Settings = context.Settings.Get<AppSwitcherSettings>();
        if (Settings.Shortcut is { IsBindable: false })
            Settings = Settings with { Shortcut = null };
        _pc = _createPc();
        _events = _createEvents();
        _events.Foreground += _recent.Activated;
        _switcher = new Switcher(_pc, _recent, _view);
        _paused = context.Input?.Paused == true;
        if (context.Input is { } hub)
        {
            _pauseListener = () => Post(OnPauseChanged);
            hub.PauseChanged += _pauseListener;
        }
        _hotkey = _createHotkey?.Invoke() ?? context.Shortcuts.Add(Id, Descriptor.Name,
            new(() => Settings.Shortcut, k => ChangeShortcut(k) ? null : ShortcutError, RemoveShortcut) { Icon = Descriptor.Icon });
        if (Settings.Shortcut is { } shortcut && !_paused)
            Reserve(shortcut);
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        context.Ui.SetSettingsPage(_ => new Views.AppSwitcherPage(this));
        context.Ui.AddCommands(() =>
        [
            new ChippiCommand(OpenKey, "App switcher", "AppWindow", OpenFromCommand) { Words = "switch window alt tab", Tile = Tile },
        ]);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _switcher?.Dismiss();
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (_context?.Input is { } hub && _pauseListener is not null)
            hub.PauseChanged -= _pauseListener;
        _pauseListener = null;
        _hotkey?.Dispose();
        _hotkey = null;
        if (_events is not null)
        {
            _events.Foreground -= _recent.Activated;
            SetWatchingClosed(false);
            _events.Dispose();
        }
        _events = null;
        _switcher = null;
        _pc = null;
        _context = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The shared command: the island's tile, Search, a Radial slice or a bound shortcut. The window to go back to is
    /// the one in front, or when that's Chippi's own (the island, Search), the one in front before the island opened.
    /// </summary>
    public Task OpenFromCommand()
    {
        if (_context is not { } context)
            return Task.CompletedTask;
        nint original = context.Actions?.WindowInFront() ?? 0;
        if (original == 0)
            original = context.IslandPasteTarget()?.Window ?? 0;
        context.CloseIsland();
        if (!Open(original))
            throw new InvalidOperationException(SwitchFailed);
        return Task.CompletedTask;
    }

    /// <summary>The overlay's display filter, kept for next time.</summary>
    public void ShowCurrentDisplayOnly(bool on)
    {
        _switcher?.Session?.ShowCurrentDisplayOnly(on);
        Update(s => s with { CurrentDisplayOnly = on });
    }

    public void Update(Func<AppSwitcherSettings, AppSwitcherSettings> change)
    {
        if (_context is not { } context)
            return;
        var next = change(Settings);
        context.Settings.Save(next);
        Settings = next;
        Changed?.Invoke();
    }

    public bool ChangeShortcut(KeyShortcut shortcut)
    {
        if (_context is null || _hotkey is null)
            return false;
        if (!shortcut.IsBindable)
        {
            ShortcutError = "Shortcut unavailable";
            Changed?.Invoke();
            return false;
        }
        var previous = Settings.Shortcut;
        // While paused the keys stay free, so only the save can fail.
        bool held = _paused || Reserve(shortcut);
        bool saved = held && Save(shortcut);
        if (held && !saved && !_paused && (previous is null || !_hotkey.Register(previous, OnShortcut)))
            _hotkey.Unregister();
        Changed?.Invoke();
        return saved;
    }

    public void RemoveShortcut()
    {
        if (_context is null)
            return;
        if (Save(null))
            _hotkey?.Unregister(); // still listed, unset
        Changed?.Invoke();
    }

    /// <summary>The PC locked: an open overlay closes.</summary>
    public void Locked() => _switcher?.Dismiss();

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect)
            Post(Locked);
    }

    private bool Open(nint original)
    {
        if (_switcher is null)
            return false;
        bool wasOpen = _switcher.IsOpen;
        bool opened = _switcher.Open(original, Settings.CurrentDisplayOnly);
        if (opened && !wasOpen)
            SetWatchingClosed(true);
        return opened;
    }

    private void OnShortcut()
    {
        if (_paused || _pc is null)
            return;
        // A shortcut leaves the window it was pressed in at the front, Chippi's Settings included: Esc goes back to it.
        Open(_pc.Foreground());
    }

    private void OnPauseChanged()
    {
        if (_context?.Input is not { } hub || _hotkey is null)
            return;
        bool paused = hub.Paused;
        if (paused == _paused)
            return;
        _paused = paused;
        if (paused)
        {
            _switcher?.Dismiss();
            _hotkey.Unregister();
        }
        else if (Settings.Shortcut is { } shortcut)
            Reserve(shortcut);
        Changed?.Invoke();
    }

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
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShortcutError = "Shortcut unavailable";
        }
        return false;
    }

    private bool Save(KeyShortcut? shortcut)
    {
        var candidate = Settings with { Shortcut = shortcut };
        try
        {
            _context!.Settings.Save(candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShortcutError = "Save failed";
            return false;
        }
        Settings = candidate;
        ShortcutError = null;
        return true;
    }

    // While the overlay is open, a window that closes leaves its list; the hook comes off when it closes.
    private bool _watching;

    private void SetWatchingClosed(bool on)
    {
        if (_events is null || on == _watching)
            return;
        _watching = on;
        if (on)
        {
            _events.Destroyed += OnDestroyed;
            if (_switcher is { } switcher)
                switcher.Closing += OnSwitcherClosing;
        }
        else
        {
            _events.Destroyed -= OnDestroyed;
            if (_switcher is { } switcher)
                switcher.Closing -= OnSwitcherClosing;
        }
    }

    private void OnDestroyed(nint hwnd) => _switcher?.Closed(hwnd);

    private void OnSwitcherClosing() => SetWatchingClosed(false);

    private void Post(Action action)
    {
        if (_ui is null)
            action();
        else
            _ui.Post(_ => action(), null);
    }
}
