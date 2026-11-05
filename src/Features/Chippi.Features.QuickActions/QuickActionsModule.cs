// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
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

    /// <summary>How long a read is waited for. One that runs longer (a stuck drive) is joined by the next read, never started again.</summary>
    internal static readonly TimeSpan ReadWait = TimeSpan.FromSeconds(5);

    /// <summary>How long a shortcut's list or confirm waits for the page to show it.</summary>
    internal static readonly TimeSpan PendingFor = TimeSpan.FromSeconds(3);

    private readonly Func<IQuickSystem> _createSystem;
    private readonly Func<IMicHotkey> _createHotkey;
    private readonly Func<Action?> _cleaning;
    private readonly Dictionary<QuickActionId, IMicHotkey> _hotkeys = [];
    private readonly Dictionary<QuickActionId, string> _shortcutErrors = [];
    private sealed class Running(ModuleContext context, IQuickSystem system)
    {
        public ModuleContext Context { get; } = context;
        public IQuickSystem System { get; } = system;
        public CancellationTokenSource Stop { get; } = new();
        public SemaphoreSlim Reading { get; } = new(1);
        public ConcurrentDictionary<QuickActionId, byte> Pressing { get; } = new();
        public Task<QuickState>? Read;
        public int Revision, ReadRevision;
        public bool Slow;
    }

    private Running? _run;
    private QuickActionId? _pending;
    private long _pendingAt;
    private ModuleContext? Context => _run?.Context;
    private ActionsPage? _page;
    private bool _capturing;

    /// <param name="cleaning">Starts Cleaning mode while it's installed, else null: the page offers Cleaning only then.</param>
    public QuickActionsModule(Func<Action?>? cleaning = null) : this(() => new WindowsQuickSystem(), GlobalMicHotkey.Create, cleaning) { }

    public QuickActionsModule(Func<IQuickSystem> system, Func<IMicHotkey> hotkey, Func<Action?>? cleaning = null)
    {
        _cleaning = cleaning ?? (() => null);
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

    /// <summary>A list or a confirm a shortcut asked for. The page shows it when it opens within a few seconds; after that it's dropped.</summary>
    internal QuickActionId? Pending
    {
        get => _pending is { } id && Environment.TickCount64 - _pendingAt <= PendingFor.TotalMilliseconds ? id : null;
        set
        {
            _pending = value;
            _pendingAt = Environment.TickCount64;
        }
    }

    /// <summary>What this PC can do, in order.</summary>
    public IReadOnlyList<QuickAction> Offered =>
        [.. QuickAction.All.Where(a => !State.Unsupported.Contains(a.Id) && (a.Id != QuickActionId.Cleaning || _cleaning() is not null))];

    /// <summary>The actions that have a gallery tile and can be offered on this PC.</summary>
    public IReadOnlyList<QuickAction> Gallery => [.. Offered.Where(a => a.InGallery)];

    /// <summary>All gallery positions, including hidden actions so hiding one never moves its neighbors.</summary>
    public IReadOnlyList<string?> Layout => QuickActionsLayout.Normalize(Settings.Layout, Settings.Shown);

    /// <summary>What the page shows, in the saved order: picked, and possible on this PC.</summary>
    public IReadOnlyList<QuickAction> Shown
    {
        get
        {
            var offered = Gallery.ToDictionary(a => a.Key);
            return [.. Layout.OfType<string>().Where(offered.ContainsKey).Select(key => offered[key]).Where(a => Settings.Shows(a.Id))];
        }
    }

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Settings = context.Settings.Get<QuickActionsSettings>();
        _run = new Running(context, _createSystem());
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
        // The Command bar runs each as its shortcut does: one that needs a choice or asks first opens its list or its confirm.
        context.Ui.AddCommands(() => [.. Offered.Where(a => a.Id != QuickActionId.Cleaning).Select(a =>
            new ChippiCommand($"quick-actions/{a.Key}", a.Name, LucideIcons.KeyOf(a.Icon), () => PressedAsync(a.Id)) { Words = a.Label })]);
        foreach (var action in QuickAction.All)
            if (Settings.ShortcutOf(action.Id) is { IsValid: true } shortcut)
                TryRegister(action.Id, shortcut);
        // Read once now, so the page opens already filled.
        _ = PollAsync();
        return Task.CompletedTask;
    }

    private ActionsPage Page() => _page ??= new ActionsPage(this);

    public Task StopAsync(CancellationToken ct)
    {
        // Stop waiting immediately even if a Windows lookup ignores cancellation. A later completion belongs only to this run.
        var run = _run;
        _run = null;
        run?.Stop.Cancel();
        foreach (var hotkey in _hotkeys.Values)
            hotkey.Dispose();
        _hotkeys.Clear();
        _shortcutErrors.Clear();
        _page = null;
        Pending = null;
        return Task.CompletedTask;
    }

    /// <summary>Reads the PC afresh, joining an older lookup before replacing it so a stuck drive cannot pile up threads.</summary>
    public Task RefreshAsync(CancellationToken ct = default) => _run is { } run ? ReadAsync(run, wait: true, ct) : Task.CompletedTask;

    /// <summary>The page's timer and first look: skipped while a read runs.</summary>
    internal Task PollAsync() => _run is { } run ? ReadAsync(run, wait: false, CancellationToken.None) : Task.CompletedTask;

    private bool Current(Running run) => ReferenceEquals(_run, run) && !run.Stop.IsCancellationRequested;

    private async Task ReadAsync(Running run, bool wait, CancellationToken ct)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct, run.Stop.Token);
        bool entered = false;
        try
        {
            if (wait)
            {
                await run.Reading.WaitAsync(cancelled.Token);
                entered = true;
            }
            else
                entered = await run.Reading.WaitAsync(0, cancelled.Token);
            if (!entered || !Current(run)) return;
            bool older = wait && run.Read is not null;
            // At most one replacement read. Another action can invalidate that too; the next poll will try again.
            for (int attempt = 0; attempt < 2 && Current(run); attempt++)
            {
                if (run.Read is null)
                {
                    uint front = run.Context.IslandPasteTarget()?.ProcessId ?? 0;
                    run.ReadRevision = Volatile.Read(ref run.Revision);
                    run.Read = run.System.ReadAsync(front, run.Stop.Token);
                    // Observe a late failure even when stop/cancellation has already ended our wait.
                    _ = run.Read.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                var state = await run.Read.WaitAsync(ReadWait, cancelled.Token);
                run.Read = null;
                if (!Current(run)) return;
                if (older || run.ReadRevision != Volatile.Read(ref run.Revision))
                {
                    older = false;
                    continue;
                }
                State = state;
                run.Slow = false;
                Changed?.Invoke();
                return;
            }
        }
        catch (OperationCanceledException) when (run.Stop.IsCancellationRequested)
        {
            // Uninstalled or exiting. Neither a stale state nor a delayed shortcut may escape this run.
        }
        catch (TimeoutException ex)
        {
            if (!run.Slow) Log.Warn("Quick actions: reading the PC is taking long", ex);
            run.Slow = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Read = null;
            Log.Warn("Quick actions: reading the PC failed", ex);
        }
        finally { if (entered) run.Reading.Release(); }
    }

    public void Save(QuickActionsSettings settings)
    {
        Active.Context.Settings.Save(settings);
        Settings = settings;
        Changed?.Invoke();
    }

    /// <summary>The picker's Done: the page shows these from now on.</summary>
    public void Pick(IEnumerable<QuickActionId> ids)
    {
        var shown = ids.Distinct().Order().Where(id => QuickAction.Of(id).InGallery).Select(id => QuickAction.Of(id).Key).ToList();
        Save(Settings with { Shown = shown, Layout = QuickActionsLayout.Normalize(null, shown) });
    }

    /// <summary>Commit the editor's selection and exact positions together; unavailable actions keep their saved choice.</summary>
    public void SaveLayout(IEnumerable<QuickActionId> selected, IEnumerable<string?> slots)
    {
        var offered = Gallery.Select(a => a.Id).ToHashSet();
        var chosen = selected.ToHashSet();
        var shown = QuickAction.All.Where(a => a.InGallery).Where(a => offered.Contains(a.Id) ? chosen.Contains(a.Id) : Settings.Shows(a.Id)).Select(a => a.Key).ToList();
        Save(Settings with { Shown = shown, Layout = QuickActionsLayout.Normalize(slots, shown) });
    }

    /// <summary>Settings' switch for one action: shown on the page or not.</summary>
    public void SetShown(QuickActionId id, bool shown)
    {
        var keys = QuickAction.All.Where(a => a.InGallery).Where(a => a.Id == id ? shown : Settings.Shows(a.Id)).Select(a => a.Key);
        Save(Settings with { Shown = [.. keys], Layout = [.. Layout] });
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

    public Task SetAsync(QuickActionId id, bool on) => ThenRefresh(pc => pc.SetAsync(id, on));

    public Task SetPowerPlanAsync(Guid plan) => ThenRefresh(pc => pc.SetPowerPlanAsync(plan));

    public Task EjectAsync(RemovableDrive drive) => ThenRefresh(pc => pc.EjectAsync(drive));

    public Task QuitAsync(OpenApp app) => ThenRefresh(pc => pc.QuitAsync(app));

    /// <summary>Asks each app to close, all of them even when one refuses (an elevated app); then throws once if any did.</summary>
    public Task QuitAllAsync(bool keepFront) => QuitAllAsync(Quitting(keepFront));

    /// <summary>Close only the exact app set reviewed in a confirmation, even if another app opens meanwhile.</summary>
    public async Task QuitAllAsync(IReadOnlyList<OpenApp> apps)
    {
        var reviewed = apps.ToArray();
        var run = Active;
        await ThenRefresh(async pc =>
        {
            var failed = new List<Exception>();
            var refused = new List<OpenApp>();
            foreach (var app in reviewed)
            {
                if (!Current(run)) break;
                try { await pc.QuitAsync(app); }
                catch (Exception ex) { failed.Add(ex); refused.Add(app); }
            }
            if (failed.Count > 0) throw new QuitAppsException(refused, failed);
        }, run);
    }

    public Task EndAsync(OpenApp app) => ThenRefresh(pc => pc.EndAsync(app));

    /// <summary>Ends exactly the apps the confirm named, each only if it is still not responding; an app that hung since isn't touched.</summary>
    public async Task EndAsync(IReadOnlyList<OpenApp> apps)
    {
        var reviewed = apps.ToArray();
        var run = Active;
        await ThenRefresh(async pc =>
        {
            var failed = new List<Exception>();
            foreach (var app in reviewed)
            {
                if (!Current(run)) break;
                try { await pc.EndAsync(app); }
                catch (Exception ex) { failed.Add(ex); }
            }
            if (failed.Count > 0) throw new AggregateException("End failed", failed);
        }, run);
    }

    public Task RunAsync(QuickActionId id)
    {
        var run = Active;
        if (id != QuickActionId.Cleaning)
            return ThenRefresh(pc => pc.RunAsync(id), run);
        // Started first, so a failure says Failed on the page; then the island closes under the black.
        if (_cleaning() is { } start)
        {
            start();
            if (Current(run)) run.Context.CloseIsland();
        }
        return Task.CompletedTask;
    }

    public Task PowerAsync(PowerChoice choice) => Active.System.PowerAsync(choice);

    private Running Active => _run ?? throw new InvalidOperationException("Quick actions uninstalled");

    private async Task ThenRefresh(Func<IQuickSystem, Task> work, Running? owner = null)
    {
        var run = owner ?? Active;
        if (!Current(run)) return;
        Interlocked.Increment(ref run.Revision);
        try { await work(run.System); }
        finally
        {
            Interlocked.Increment(ref run.Revision);
            if (Current(run)) await ReadAsync(run, wait: true, CancellationToken.None);
        }
    }

    // Shortcuts: none by default, one per action at most.

    public bool ChangeShortcut(QuickActionId id, MicShortcut shortcut)
    {
        if (Context is null)
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
        var previous = Settings.ShortcutOf(id);
        try
        {
            Save(Settings with { Shortcuts = new(Settings.Shortcuts) { [QuickAction.Of(id).Key] = shortcut } });
            return true;
        }
        catch (Exception ex)
        {
            // Registration happened first so a clash never reaches settings. A failed save must restore that registration too.
            bool restored = previous is not null && TryRegister(id, previous);
            // The old chord might have been claimed after we released it. Never leave the unsaved new chord active.
            if (!restored && _hotkeys.Remove(id, out var added))
                added.Dispose();
            Log.Warn("Quick actions: saving shortcut failed", ex);
            _shortcutErrors[id] = "Save failed";
            Changed?.Invoke();
            return false;
        }
    }

    public void RemoveShortcut(QuickActionId id)
    {
        if (Context is null)
            return;
        var shortcuts = new Dictionary<string, MicShortcut>(Settings.Shortcuts);
        shortcuts.Remove(QuickAction.Of(id).Key);
        try { Save(Settings with { Shortcuts = shortcuts }); }
        catch (Exception ex)
        {
            Log.Warn("Quick actions: removing shortcut failed", ex);
            _shortcutErrors[id] = "Save failed";
            Changed?.Invoke();
            return;
        }
        if (_hotkeys.Remove(id, out var hotkey)) hotkey.Dispose();
        if (_shortcutErrors.Remove(id)) Changed?.Invoke();
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
        var run = _run;
        if (_capturing || run is null || !run.Pressing.TryAdd(id, 0)) return;
        var action = QuickAction.Of(id);
        try
        {
            await ReadAsync(run, wait: true, CancellationToken.None);
            if (!Current(run) || _capturing || State.Unsupported.Contains(id)) return;
            if (action.Toggle && !action.OpensList)
                await ThenRefresh(pc => pc.SetAsync(id, !State.On.GetValueOrDefault(id)), run);
            else if (id is QuickActionId.Lock or QuickActionId.Displays or QuickActionId.ScreenSaver)
                await ThenRefresh(pc => pc.RunAsync(id), run);
            else if (id == QuickActionId.Eject && State.Drives is [var only])
                await ThenRefresh(pc => pc.EjectAsync(only), run);
            else
            {
                Pending = id;
                // With the island off or hidden for a fullscreen app, nothing opens: no Settings, and no confirm later.
                if (!run.Context.TryOpenIslandPage(TileKey))
                {
                    Pending = null;
                    return;
                }
                if (Current(run)) _page?.ShowPending();
                return;
            }
            if (Current(run)) _page?.ShowSucceeded(id);
        }
        catch (Exception ex)
        {
            if (!Current(run)) return;
            Log.Warn($"Quick actions: the {action.Key} shortcut failed", ex);
            _page?.ShowFailed(id);
        }
        finally { run.Pressing.TryRemove(id, out _); }
    }
}
