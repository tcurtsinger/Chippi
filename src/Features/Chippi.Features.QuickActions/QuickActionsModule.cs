// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.Features.QuickActions.Views;
using Chippi.Features.QuickActions.Windows;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions;

/// <summary>
/// Quick actions (docs/phase-5.5/quick-actions.md): Windows settings and system actions in one click from the island's
/// Actions page, the pick and the kept apps in Settings, and a shortcut for any of them.
/// </summary>
public sealed class QuickActionsModule : IFeatureModule
{
    public const string Id = "quick-actions";

    /// <summary>The island tile's key.</summary>
    public const string TileKey = "quick-actions";

    /// <summary>The page reads the PC again this often while it shows, so a change made elsewhere shows on its button.</summary>
    internal static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(2);

    private readonly Func<IQuickSystem> _createSystem;
    private readonly Func<IMicHotkey> _createHotkey;
    private readonly Dictionary<QuickActionId, IMicHotkey> _hotkeys = [];
    private readonly Dictionary<QuickActionId, string> _shortcutErrors = [];
    private readonly SemaphoreSlim _reading = new(1);
    private IQuickSystem? _system;
    private ModuleContext? _context;
    private ActionsPage? _page;
    private bool _capturing;

    public QuickActionsModule() : this(() => new WindowsQuickSystem(), GlobalMicHotkey.Create) { }

    public QuickActionsModule(Func<IQuickSystem> system, Func<IMicHotkey> hotkey)
    {
        _createSystem = system;
        _createHotkey = hotkey;
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Quick actions", "Dark mode, hidden files, Lock, Eject and more in one click", FeatureGroup.System)
    {
        Icon = nameof(LucideIcons.ToggleRight),
        Presets = PresetMembership.PowerUser,
    };

    public QuickActionsSettings Settings { get; private set; } = new();

    /// <summary>The PC as last read. The page opens showing it, then reads again.</summary>
    public QuickState State { get; private set; } = QuickState.Empty;

    /// <summary>Why a shortcut didn't register ("Shortcut in use"), by action.</summary>
    public IReadOnlyDictionary<QuickActionId, string> ShortcutErrors => _shortcutErrors;

    /// <summary>The settings or the state changed. Raised on any thread.</summary>
    public event Action? Changed;

    /// <summary>A list or a confirm a shortcut asked for; the page shows it when it opens, and clears it.</summary>
    internal QuickActionId? Pending { get; set; }

    /// <summary>What this PC can do, in order.</summary>
    public IReadOnlyList<QuickAction> Offered => [.. QuickAction.All.Where(a => !State.Unsupported.Contains(a.Id))];

    /// <summary>What the page shows, in order: picked, and possible on this PC.</summary>
    public IReadOnlyList<QuickAction> Shown => [.. Offered.Where(a => Settings.Shows(a.Id))];

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        Settings = context.Settings.Get<QuickActionsSettings>();
        _system ??= _createSystem();
        _capturing = false;
        // No status on the home: Actions is a door to its page.
        context.Ui.AddIslandTile(new IslandTileSpec(TileKey, "Actions", nameof(LucideIcons.ToggleRight), Order: 100, SectionOrder: 0, () => Page())
        {
            CreateHeader = () => Page().Header,
            InitialFocus = () => Page().InitialFocus,
            HandleBack = () => _page?.Back() == true,
            HandleEscape = () => _page?.Escape() == true,
        });
        context.Ui.SetSettingsPage(section => section == "shortcut-list" ? new ShortcutList(this) : new QuickActionsSettingsPage(this));
        foreach (var action in QuickAction.All)
            if (Settings.ShortcutOf(action.Id) is { IsValid: true } shortcut)
                TryRegister(action.Id, shortcut);
        // Read once now, so the page opens already filled.
        _ = RefreshAsync(CancellationToken.None);
        return Task.CompletedTask;
    }

    private ActionsPage Page() => _page ??= new ActionsPage(this);

    public Task StopAsync(CancellationToken ct)
    {
        foreach (var hotkey in _hotkeys.Values)
            hotkey.Dispose();
        _hotkeys.Clear();
        _shortcutErrors.Clear();
        _context = null;
        _page = null;
        Pending = null;
        return Task.CompletedTask;
    }

    /// <summary>Reads the PC again; a read already running makes this one a no-op.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_system is not { } system || !await _reading.WaitAsync(0, ct))
            return;
        try
        {
            uint front = _context?.IslandPasteTarget()?.ProcessId ?? 0;
            State = await system.ReadAsync(front, ct);
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn("Quick actions: reading the PC failed", ex);
        }
        finally { _reading.Release(); }
    }

    public void Save(QuickActionsSettings settings)
    {
        _context?.Settings.Save(settings);
        Settings = settings;
        Changed?.Invoke();
    }

    /// <summary>The picker's Done: the page shows these from now on.</summary>
    public void Pick(IEnumerable<QuickActionId> ids) =>
        Save(Settings with { Shown = [.. ids.Distinct().Order().Select(id => QuickAction.Of(id).Key)] });

    /// <summary>Settings' switch for one action: shown on the page or not.</summary>
    public void SetShown(QuickActionId id, bool shown)
    {
        var keys = QuickAction.All.Where(a => a.Id == id ? shown : Settings.Shows(a.Id)).Select(a => a.Key);
        Save(Settings with { Shown = [.. keys] });
    }

    public void Keep(string path)
    {
        if (!Settings.Keeps(path))
            Save(Settings with { Kept = [.. Settings.Kept, path] });
    }

    public void Unkeep(string path) =>
        Save(Settings with { Kept = [.. Settings.Kept.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))] });

    /// <summary>The apps "Quit all" (or "Quit all but" the front one) closes: neither kept nor, then, in front.</summary>
    public IReadOnlyList<OpenApp> Quitting(bool keepFront) =>
        [.. State.Apps.Where(a => !Settings.Keeps(a.Path) && !(keepFront && a.Front))];

    // Each of these throws when Windows refuses; the page says "Failed".

    public async Task SetAsync(QuickActionId id, bool on)
    {
        try { await Pc.SetAsync(id, on); }
        finally { await RefreshAsync(); }
    }

    public Task SetPowerPlanAsync(Guid plan) => ThenRefresh(Pc.SetPowerPlanAsync(plan));

    public Task EjectAsync(RemovableDrive drive) => ThenRefresh(Pc.EjectAsync(drive));

    public Task QuitAsync(OpenApp app) => ThenRefresh(Pc.QuitAsync(app));

    public async Task QuitAllAsync(bool keepFront)
    {
        try
        {
            foreach (var app in Quitting(keepFront))
                await Pc.QuitAsync(app);
        }
        finally { await RefreshAsync(); }
    }

    public Task EndFrozenAppsAsync() => ThenRefresh(Pc.EndFrozenAppsAsync());

    public Task RunAsync(QuickActionId id) => ThenRefresh(Pc.RunAsync(id));

    public Task PowerAsync(PowerChoice choice) => Pc.PowerAsync(choice);

    private IQuickSystem Pc => _system ?? throw new InvalidOperationException("Quick actions uninstalled");

    private async Task ThenRefresh(Task work)
    {
        try { await work; }
        finally { await RefreshAsync(); }
    }

    // Shortcuts: none by default, one per action at most.

    public bool ChangeShortcut(QuickActionId id, MicShortcut shortcut)
    {
        if (_context is null)
            return false;
        if (!shortcut.IsValid)
        {
            _shortcutErrors[id] = "Shortcut unavailable";
            Changed?.Invoke();
            return false;
        }
        if (!TryRegister(id, shortcut))
        {
            Changed?.Invoke();
            return false;
        }
        Save(Settings with { Shortcuts = new(Settings.Shortcuts) { [QuickAction.Of(id).Key] = shortcut } });
        return true;
    }

    public void RemoveShortcut(QuickActionId id)
    {
        if (_context is null)
            return;
        if (_hotkeys.Remove(id, out var hotkey))
            hotkey.Dispose();
        _shortcutErrors.Remove(id);
        var shortcuts = new Dictionary<string, MicShortcut>(Settings.Shortcuts);
        shortcuts.Remove(QuickAction.Of(id).Key);
        Save(Settings with { Shortcuts = shortcuts });
    }

    /// <summary>While Settings records a shortcut, pressing one of ours doesn't run it.</summary>
    public void CaptureShortcut(bool capture) => _capturing = capture;

    private bool TryRegister(QuickActionId id, MicShortcut shortcut)
    {
        try
        {
            if (!_hotkeys.TryGetValue(id, out var hotkey))
                _hotkeys[id] = hotkey = _createHotkey();
            if (hotkey.Register(shortcut, () => _ = PressedAsync(id)))
                _shortcutErrors.Remove(id);
            else
                _shortcutErrors[id] = "Shortcut in use";
        }
        catch (Exception ex)
        {
            Log.Warn("Quick actions: registering a shortcut failed", ex);
            _shortcutErrors[id] = "Shortcut unavailable";
        }
        return !_shortcutErrors.ContainsKey(id);
    }

    /// <summary>
    /// A shortcut: a toggle flips and an action runs, as its button does. One that needs a choice or asks first opens the
    /// island on its list or its confirm. The button's state is the only feedback.
    /// </summary>
    internal async Task PressedAsync(QuickActionId id)
    {
        if (_capturing || _context is not { } context)
            return;
        await RefreshAsync();
        if (State.Unsupported.Contains(id))
            return;
        var action = QuickAction.Of(id);
        try
        {
            if (action.Toggle && !action.OpensList)
                await SetAsync(id, !State.On.GetValueOrDefault(id));
            else if (id is QuickActionId.Lock or QuickActionId.Displays or QuickActionId.ScreenSaver)
                await RunAsync(id);
            else if (id == QuickActionId.Eject && State.Drives is [var only])
                await EjectAsync(only);
            else
            {
                Pending = id;
                context.OpenIslandPage(TileKey);
                _page?.ShowPending();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Quick actions: the {action.Key} shortcut failed", ex);
            _page?.ShowFailed(id);
        }
    }
}
