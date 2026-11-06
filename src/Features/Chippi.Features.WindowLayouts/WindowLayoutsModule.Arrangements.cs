// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Actions;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Microsoft.Win32;

namespace Chippi.Features.WindowLayouts;

public sealed partial class WindowLayoutsModule
{
    public Func<ModuleContext, IArrangementDesktop>? CreateArrangementDesktop { get; set; }
    public Func<string?>? PointerDisplayId { get; set; }
    private IArrangementDesktop? _arrangementDesktop;
    public ArrangementRunner? Arrangements { get; private set; }
    public bool ArrangementsLocked { get; private set; }
    public bool ArrangementsPreparing { get; private set; }
    public event Action? ArrangementProgressRequested;
    public event Action? ArrangementsChanged;
    public event Action<string>? ArrangementFailed;
    public string? ArrangementError { get; private set; }

    /// <summary>The arrangement of the last run, or of a launch that was refused; its results are the runner's.</summary>
    public SavedArrangement? ArrangementRun { get; private set; }

    /// <summary>Why the last launch started nothing ("Paused"), until the next launch.</summary>
    public string? ArrangementRefused { get; private set; }

    /// <summary>Whether the last run's summary was dismissed from the bar.</summary>
    public bool ArrangementDismissed { get; private set; } = true;

    public void DismissArrangement()
    {
        if (Arrangements?.IsRunning == true || ArrangementsPreparing) return;
        ArrangementDismissed = true;
        OnArrangementsChanged();
    }
    private readonly Dictionary<string, (string Name, IHotkey Key)> _arrangementKeys = [];
    private Task? _arrangementTask;
    private int _arrangementEpoch;
    private Action? _arrangementPause;

    private void StartArrangements(ModuleContext context)
    {
        _arrangementDesktop = CreateArrangementDesktop?.Invoke(context);
        if (_arrangementDesktop is not null)
        {
            Arrangements = new(_arrangementDesktop);
            Arrangements.Changed += OnArrangementsChanged;
        }
        _arrangementPause = () => _ui?.BeginInvoke(SyncArrangementShortcuts);
        if (context.Input is { } input) input.PauseChanged += _arrangementPause;
        SystemEvents.SessionSwitch += ArrangementSessionSwitch;
        SyncArrangementShortcuts();
    }

    private void StopArrangements()
    {
        CancelArrangement();
        SystemEvents.SessionSwitch -= ArrangementSessionSwitch;
        if (_context?.Input is { } input && _arrangementPause is not null) input.PauseChanged -= _arrangementPause;
        foreach (var entry in _arrangementKeys.Values) entry.Key.Dispose();
        _arrangementKeys.Clear();
        if (Arrangements is { } runner) runner.Changed -= OnArrangementsChanged;
        Arrangements = null;
        _arrangementDesktop = null;
        _arrangementPause = null;
    }

    private void ArrangementSessionSwitch(object? sender, SessionSwitchEventArgs e) => _ui?.BeginInvoke(() =>
    {
        if (_context is null) return;
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect)
        { ArrangementsLocked = true; CancelArrangement(); }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
            ArrangementsLocked = false;
        SyncArrangementShortcuts();
    });

    private void OnArrangementsChanged()
    {
        PublishRun();
        ArrangementsChanged?.Invoke();
    }

    /// <summary>The bar's key for a run, and the Actions page a click on it opens (Quick actions' tile).</summary>
    internal const string RunKey = "arrangement-run", ActionsTileKey = "quick-actions";
    private bool _ownerCancelled, _showRun;
    private string? _beeped;

    /// <summary>Whether the owner pressed Cancel, rather than a fullscreen app or a lock stopping the run.</summary>
    public bool ArrangementCancelled => _ownerCancelled;

    /// <summary>Actions › Snap opens on the run once after the bar shows it; the menu's own page takes the ask when it shows the run.</summary>
    internal bool TakeRunAsk()
    {
        bool ask = _showRun;
        _showRun = false;
        _openRunPage |= ask;
        return ask;
    }

    private bool _openRunPage;

    /// <summary>The Snap menu this ask opened starts on the run, once.</summary>
    internal bool TakeOpenRunPage()
    {
        bool open = _openRunPage;
        _openRunPage = false;
        return open;
    }

    /// <summary>Manage: Settings › Snap, where arrangements are made and edited.</summary>
    public void OpenArrangementSettings() => _context?.OpenSettings(null);

    /// <summary>
    /// The run on the bar (arrangements.html, the bar's four lines): "5 of 12 ready" over its name with the ring while it
    /// runs; once it's done, a clean run folds and one that's partly done, guessed or moved a window to another display stays
    /// until Dismiss. A refused launch shows "Not launched" for six seconds, or beeps while the widget can't show it.
    /// </summary>
    private void PublishRun()
    {
        if (_context is not { } context) return;
        var run = ArrangementRun;
        if (run is null) { context.Island.Clear(RunKey); return; }
        if (ArrangementRefused is { } refused)
        {
            if (context.CanShowIslandFeedback())
                context.Island.Publish(new IslandActivity(RunKey, IslandActivityKind.Status, "Not launched", $"{run.Name} · {refused}")
                { IsFeedback = true, NeedsAttention = true, Icon = "TriangleAlert", OpensTile = ActionsTileKey });
            else
            {
                context.Island.Clear(RunKey);
                // Once a refusal, not on every change while it's the latest.
                if (_beeped != run.Id + refused) _arrangementDesktop?.Beep();
            }
            _beeped = run.Id + refused;
            return;
        }
        _beeped = null;
        var summary = ArrangementSummary.Of(this);
        if (summary.Running)
        {
            ArrangementDismissed = false;
            if (!_showRunArmed) { _showRun = true; _showRunArmed = true; }
            context.Island.Publish(new IslandActivity(RunKey, IslandActivityKind.Timer, $"{summary.Ready} of {summary.Total} ready", run.Name)
            { Progress = summary.Total == 0 ? 0 : (double)summary.Ready / summary.Total, OpensTile = ActionsTileKey });
            return;
        }
        _showRunArmed = false;
        if (ArrangementDismissed || summary.Clean)
        {
            ArrangementDismissed = true;
            _showRun = false;
            context.Island.Clear(RunKey);
            return;
        }
        if (!_summaryArmed) { _showRun = true; _summaryArmed = true; }
        context.Island.Publish(new IslandActivity(RunKey, IslandActivityKind.Status, summary.Headline, $"{run.Name} · {summary.Note}")
        { NeedsAttention = summary.Attention, Icon = summary.Attention ? "TriangleAlert" : "LayoutGrid", OpensTile = ActionsTileKey });
    }

    private bool _showRunArmed, _summaryArmed;

    public async Task<ArrangementCapture> CaptureArrangementAsync()
    {
        var desktop = _arrangementDesktop ?? throw new ActionFailure("Unavailable");
        await desktop.PrepareAsync();
        if (_arrangementDesktop != desktop) throw new ActionFailure("Unavailable");
        return ArrangementCaptureBuilder.Capture(desktop.Windows());
    }

    /// <summary>Surfaces pass their display and original window. Search and shortcuts use the display under the pointer.</summary>
    public Task LaunchArrangementAsync(string id, string? invocationDisplay = null, nint originalWindow = 0)
    {
        ArrangementProgressRequested?.Invoke();
        if (_arrangementTask is { IsCompleted: false }) return _arrangementTask;
        if (ArrangementsPreparing) return Task.CompletedTask;
        var saved = Layouts?.Settings.Arrangements.FirstOrDefault(a => a.Id == id) ?? throw new ActionFailure("Arrangement deleted");
        var desktop = _arrangementDesktop ?? throw new ActionFailure("Unavailable");
        var runner = Arrangements ?? throw new ActionFailure("Unavailable");
        ArrangementRun = saved;
        ArrangementRefused = null;
        ArrangementDismissed = false;
        _ownerCancelled = _summaryArmed = false;
        string display = invocationDisplay ?? PointerDisplayId?.Invoke() ?? Displays.FirstOrDefault(d => d.IsMain)?.Id ?? "";
        if (originalWindow == 0) originalWindow = _context?.Actions?.WindowInFront() ?? 0;
        if (originalWindow == 0) originalWindow = _context?.IslandPasteTarget()?.Window ?? _context?.SettingsPasteTarget()?.Window ?? 0;
        desktop.BeginInvocation(originalWindow);
        if (desktop.StopReason(new HashSet<ArrangementProcess>()) is { } refusal)
        {
            ArrangementRefused = refusal;
            OnArrangementsChanged();
            throw new ActionFailure(refusal);
        }
        int epoch = ++_arrangementEpoch;
        ArrangementsPreparing = true;
        ArrangementError = null;
        _arrangementTask = PrepareAndLaunchAsync();
        ArrangementProgressRequested?.Invoke();
        OnArrangementsChanged();
        return _arrangementTask;
        async Task PrepareAndLaunchAsync()
        {
            // Assign the task before Changed can reenter Launch.
            await Task.Yield();
            try
            {
                await desktop.PrepareAsync();
                if (epoch != _arrangementEpoch || _context is null) return;
                ArrangementsPreparing = false;
                await runner.LaunchAsync(saved, display);
            }
            finally
            {
                if (epoch == _arrangementEpoch) ArrangementsPreparing = false;
                OnArrangementsChanged();
            }
        }
    }

    public void CancelArrangement()
    {
        if (ArrangementsPreparing || Arrangements?.IsRunning == true) _ownerCancelled = true;
        _arrangementEpoch++;
        ArrangementsPreparing = false;
        Arrangements?.Cancel();
        OnArrangementsChanged();
    }

    /// <summary>Runs the failed rows again; the ones that are ready aren't launched or moved again.</summary>
    public Task RetryArrangementAsync()
    {
        if (Arrangements is not { } runner) return Task.CompletedTask;
        ArrangementDismissed = false;
        _ownerCancelled = _summaryArmed = false;
        var retry = runner.RetryAsync();
        _arrangementTask = retry;
        _ = AfterRetry();
        return retry;
        // The runner says Changed as rows move; once more at the end, so the bar and page read the finished run.
        async Task AfterRetry()
        {
            try { await retry; } catch (Exception ex) when (ex is not OutOfMemoryException) { }
            OnArrangementsChanged();
        }
    }
    public void EditArrangementShortcut(string id)
    {
        var saved = Layouts?.Settings.Arrangements.FirstOrDefault(a => a.Id == id);
        if (saved is not null) _context?.EditShortcut("Launch " + saved.Name);
    }

    private void SyncArrangementShortcuts()
    {
        if (_context is not { } context || Layouts is not { } store) return;
        foreach (string id in _arrangementKeys.Keys.ToArray())
        {
            var saved = store.Settings.Arrangements.FirstOrDefault(a => a.Id == id);
            if (saved is not null && _arrangementKeys[id].Name == saved.Name) continue;
            _arrangementKeys[id].Key.Dispose();
            _arrangementKeys.Remove(id);
        }
        foreach (var saved in store.Settings.Arrangements)
        {
            string id = saved.Id;
            if (!_arrangementKeys.TryGetValue(id, out var entry))
            {
                var key = context.Shortcuts.Add(Id, "Launch " + saved.Name, new(
                    () => Layouts?.Settings.Arrangements.FirstOrDefault(a => a.Id == id)?.Shortcut,
                    keys => ChangeArrangementShortcut(id, keys), () =>
                    {
                        if (ChangeArrangementShortcut(id, null) is { } error) throw new ActionFailure(error);
                    }) { Icon = "LayoutGrid" });
                _arrangementKeys[id] = entry = (saved.Name, key);
            }
            if (context.Input?.Paused == true || ArrangementsLocked || saved.Shortcut is null) entry.Key.Unregister();
            else entry.Key.Register(saved.Shortcut, () => FireArrangement(id));
        }
    }

    private string? ChangeArrangementShortcut(string id, KeyShortcut? keys)
    {
        if (keys is not null && !keys.IsBindable) return "Shortcut unavailable";
        if (Layouts is not { } store || !_arrangementKeys.TryGetValue(id, out var entry)) return "Unavailable";
        var saved = store.Settings.Arrangements.FirstOrDefault(a => a.Id == id);
        if (saved is null) return "Unavailable";
        bool paused = _context?.Input?.Paused == true || ArrangementsLocked;
        if (keys is not null && !paused && !entry.Key.Register(keys, () => FireArrangement(id)))
            return entry.Key.Error ?? "Shortcut unavailable";
        try { store.SaveArrangement(saved with { Shortcut = keys }); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!paused && saved.Shortcut is { } previous) entry.Key.Register(previous, () => FireArrangement(id));
            else entry.Key.Unregister();
            return "Save failed";
        }
    }

    private async void FireArrangement(string id)
    {
        try { await LaunchArrangementAsync(id); }
        catch (Exception ex) when (ex is ActionFailure or System.Runtime.InteropServices.COMException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ArrangementError = ex is ActionFailure ? ex.Message : "App discovery unavailable";
            ArrangementFailed?.Invoke("Launching failed");
            ArrangementProgressRequested?.Invoke();
            OnArrangementsChanged();
        }
    }

    private void AddArrangementCommands(List<ChippiCommand> commands)
    {
        foreach (var saved in Layouts?.Settings.Arrangements ?? [])
        {
            string id = saved.Id;
            // Search lists it by its name with Launch as the verb (arrangements.html, the Search board).
            int windows = saved.Rows.Count;
            commands.Add(new($"{Id}/arrangement/{id}", saved.Name, "LayoutGrid", () =>
            {
                FireArrangement(id);
                return Task.CompletedTask;
            }) { Words = "launch snap arrangement workspace", Detail = windows == 1 ? "Arrangement · 1 window" : $"Arrangement · {windows} windows",
                Kind = ChippiCommandKind.Launch, Tile = SnapTile });
        }
    }
}
