// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Actions;
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

    private void OnArrangementsChanged() => ArrangementsChanged?.Invoke();

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
        string display = invocationDisplay ?? PointerDisplayId?.Invoke() ?? Displays.FirstOrDefault(d => d.IsMain)?.Id ?? "";
        if (originalWindow == 0) originalWindow = _context?.Actions?.WindowInFront() ?? 0;
        if (originalWindow == 0) originalWindow = _context?.IslandPasteTarget()?.Window ?? _context?.SettingsPasteTarget()?.Window ?? 0;
        desktop.BeginInvocation(originalWindow);
        if (desktop.StopReason(new HashSet<ArrangementProcess>()) is { } refusal) throw new ActionFailure(refusal);
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
        _arrangementEpoch++;
        ArrangementsPreparing = false;
        Arrangements?.Cancel();
        OnArrangementsChanged();
    }

    public Task RetryArrangementAsync() => _arrangementTask = Arrangements?.RetryAsync() ?? Task.CompletedTask;
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
            commands.Add(new($"{Id}/arrangement/{id}", "Launch " + saved.Name, "LayoutGrid", () =>
            {
                FireArrangement(id);
                return Task.CompletedTask;
            }) { Words = "snap arrangement workspace", Detail = "Snap", Tile = SnapTile });
        }
    }
}
