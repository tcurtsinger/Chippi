// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Text.Json;
using Chippi.Core.Admin;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Uninstaller;

/// <summary>
/// The list behind Settings › Apps › Uninstaller, the queue that runs each app's own uninstaller, and the leftover steps. Nothing here asks: the
/// screens' confirm comes first, and nothing is removed that was not ticked. A machine app runs through the admin helper (one session, one prompt for the
/// queue); a user app runs as the standard user, never elevated; a Store app through its package for the current user. The queue runs the machine
/// apps first, then the user's and the Store's, each in the order given. The entry is read before the uninstaller runs and kept for the scan, and
/// read again after: still there means "Uninstall failed". An app that is open is "Running": never ended. The log holds an app's name only,
/// never a path, a key name or a command.
/// </summary>
public sealed class UninstallerEngine : IDisposable
{
    public static readonly TimeSpan UserRunLimit = TimeSpan.FromHours(2);
    private readonly UninstallerSystem _system;
    private readonly IAdminHelper? _admin;
    private readonly AppReader _reader;
    private readonly LeftoverScanner _scanner;
    private readonly LeftoverRemover _remover;
    private readonly RecordStore _records;
    private readonly string? _cachePath;
    private readonly ILogger? _log;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _settle;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, AppStatus> _status = [];
    private int _refreshing;
    private volatile bool _running, _disposed;

    public UninstallerEngine(UninstallerSystem system, IAdminHelper? admin, RecordStore records, LeftoverRoots roots, string backupFolder, string? cachePath = null,
        ILogger? logger = null, TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? settle = null, Func<string?, bool>? isSelfLocation = null)
    {
        _system = system;
        _admin = admin;
        _records = records;
        _cachePath = cachePath;
        _log = logger;
        _time = time ?? TimeProvider.System;
        _delay = delay ?? Task.Delay;
        _settle = settle ?? TimeSpan.FromSeconds(20);
        _reader = new AppReader(system.Registry, system.Packages, isSelfLocation);
        _scanner = new LeftoverScanner(system, roots);
        _remover = new LeftoverRemover(system, admin, backupFolder, _time, logger);
    }

    // ---- the list ----

    /// <summary>The apps as last read (or cached); replaced whole, so any thread may read it.</summary>
    public IReadOnlyList<AppEntry> Apps { get; private set; } = [];

    /// <summary>A read finished at least once (or the cache loaded).</summary>
    public bool Loaded { get; private set; }

    /// <summary>The last read failed; the list is the last good one.</summary>
    public string? Error { get; private set; }

    /// <summary>A fresh read is running behind the list.</summary>
    public bool IsRefreshing => _refreshing != 0;

    /// <summary>Raised when the list, a row's status or the queue changed.</summary>
    public event Action? Changed;

    /// <summary>A queue finished (or stopped): the module shows the result on the bar.</summary>
    public event Action<IReadOnlyList<AppOutcome>>? RunCompleted;

    /// <summary>Loads the cached list, so Search and the page open with rows before the first read ends.</summary>
    public async Task LoadCachedAsync()
    {
        if (_cachePath is not { } path || Loaded) return;
        var cached = await Task.Run(() =>
        {
            try { return File.Exists(path) ? JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllBytes(path)) : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
        }).ConfigureAwait(true);
        if (cached is not null && !Loaded && !_disposed) { Apps = cached; Loaded = true; Changed?.Invoke(); }
    }

    /// <summary>A fresh read behind the cached list. A second call while one runs is ignored.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        Changed?.Invoke();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            var list = await Task.Run(() => _reader.Read(linked.Token), linked.Token).ConfigureAwait(true);
            Apps = list;
            Loaded = true;
            Error = null;
            SaveCache(list);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Error = "Reading apps failed";
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
            Changed?.Invoke();
        }
    }

    private void SaveCache(IReadOnlyList<AppEntry> list)
    {
        if (_cachePath is not { } path) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(list));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---- the queue ----

    /// <summary>A queue is running. The app asks before quitting while this is true.</summary>
    public bool IsRunning => _running;

    /// <summary>The app being uninstalled now; null between apps and outside a queue.</summary>
    public string? CurrentName { get; private set; }

    /// <summary>1-based position in the queue and its length, while one runs.</summary>
    public int Index { get; private set; }
    public int Count { get; private set; }

    public AppStatus StatusOf(string id) => _status.TryGetValue(id, out var s) ? s : new AppStatus(AppRunState.Idle);

    /// <summary>Forgets a row's last result (the row goes back to its verb).</summary>
    public void ClearStatus(string id)
    {
        if (_status.Remove(id)) Changed?.Invoke();
    }

    private void Set(AppEntry app, AppRunState state)
    {
        _status[app.Id] = new AppStatus(state, app.Name);
        Changed?.Invoke();
    }

    /// <summary>
    /// Runs the confirmed queue. Returns one outcome per app asked for, in the order the queue ran them. Cancel skips the apps not yet started and
    /// never stops an uninstaller that is running. A second call while one runs returns nothing.
    /// </summary>
    public async Task<IReadOnlyList<AppOutcome>> UninstallAsync(IReadOnlyList<AppEntry> apps, CancellationToken ct = default)
    {
        if (_disposed || _running) return [];
        var asked = apps.GroupBy(a => a.Id).Select(g => g.First()).ToList();
        if (asked.Count == 0) return [];
        _running = true;
        var outcomes = new List<AppOutcome>();
        try
        {
            var queue = new List<(AppEntry App, AppRecord Record)>();
            foreach (var app in asked)
            {
                if (!app.CanUninstall) { outcomes.Add(new(app, new AppStatus(AppRunState.Refused, app.Name))); Set(app, AppRunState.Refused); continue; }
                if ((await Task.Run(() => _system.Processes.RunningIn(Folders(app)), CancellationToken.None).ConfigureAwait(true)).Count > 0) { outcomes.Add(new(app, new AppStatus(AppRunState.Running, app.Name))); Set(app, AppRunState.Running); continue; }
                queue.Add((app, Snapshot(app)));
                Set(app, AppRunState.Queued);
            }
            // The machine apps first, as one helper session; then the user's and the Store's, one after another.
            queue = [.. queue.Where(q => q.App.IsMachine), .. queue.Where(q => q.App.Source == AppSource.User), .. queue.Where(q => q.App.IsStore)];
            Count = queue.Count;
            Index = 0;
            bool any = false;

            var machine = queue.Where(q => q.App.IsMachine).ToList();
            if (machine.Count > 0)
            {
                var results = await RunMachineAsync(machine, ct);
                for (int i = 0; i < machine.Count; i++)
                {
                    var outcome = await FinishMachineAsync(machine[i].App, machine[i].Record, results[i]);
                    outcomes.Add(outcome);
                    any |= outcome.Succeeded;
                }
            }
            for (int q = 0; q < queue.Count; q++)
            {
                var (app, record) = queue[q];
                if (app.IsMachine) continue;
                if (ct.IsCancellationRequested) { Set(app, AppRunState.Cancelled); outcomes.Add(new(app, new AppStatus(AppRunState.Cancelled, app.Name))); continue; }
                Index = q + 1;
                CurrentName = app.Name;
                Set(app, AppRunState.Uninstalling);
                _log?.LogInformation("Uninstalling {App}", app.Name);
                var outcome = app.IsStore ? await RunStoreAsync(app, record, ct) : await RunUserAsync(app, record, ct);
                outcomes.Add(outcome);
                any |= outcome.Succeeded;
            }
            if (!any) await EndAdminSessionAsync();   // nothing to follow up: the helper's session ends now
        }
        finally
        {
            _running = false;
            CurrentName = null;
            Index = Count = 0;
            Changed?.Invoke();
        }
        if (outcomes.Count > 0) RunCompleted?.Invoke(outcomes);
        return outcomes;
    }

    private async Task<IReadOnlyList<AdminUninstallResult>> RunMachineAsync(List<(AppEntry App, AppRecord Record)> machine, CancellationToken ct)
    {
        var items = machine.Select(m => new AdminUninstallItem(m.App.HelperView!.Value, m.App.Key, m.App.Name)).ToArray();
        if (_admin is null) return [.. items.Select(i => new AdminUninstallResult(i, AdminUninstallState.NeedsAdmin))];
        var progress = new Progress<AdminUninstallProgress>(p =>
        {
            var entry = machine.FirstOrDefault(m => m.App.Key == p.Item.KeyName && m.App.HelperView == p.Item.View).App;
            if (entry is null) return;
            if (p.Result is null && !StatusOf(entry.Id).IsFinal)   // a start posted late must not undo the result
            {
                Index = p.Index;
                CurrentName = entry.Name;
                _log?.LogInformation("Uninstalling {App}", entry.Name);
                Set(entry, AppRunState.Uninstalling);
            }
        });
        try { return await _admin.UninstallAsync(items, progress, ct).ConfigureAwait(true); }
        catch (OperationCanceledException) { return [.. items.Select(i => new AdminUninstallResult(i, AdminUninstallState.Cancelled))]; }
    }

    private async Task<AppOutcome> FinishMachineAsync(AppEntry app, AppRecord record, AdminUninstallResult result)
    {
        switch (result.State)
        {
            case AdminUninstallState.NeedsAdmin: Set(app, AppRunState.NeedsAdmin); return new(app, new AppStatus(AppRunState.NeedsAdmin, app.Name));
            case AdminUninstallState.Cancelled: Set(app, AppRunState.Cancelled); return new(app, new AppStatus(AppRunState.Cancelled, app.Name));
            case AdminUninstallState.Refused: Set(app, AppRunState.Refused); return new(app, new AppStatus(AppRunState.Refused, app.Name));
        }
        var withHelper = record with { HelperRecordId = result.RecordId };
        if (result.State == AdminUninstallState.RestartNeeded) return Keep(app, withHelper with { Restart = true }, AppRunState.RestartNeeded);
        bool gone = result.KeyGone || await WaitGoneAsync(app, result.State == AdminUninstallState.Uninstalled);
        return gone ? Keep(app, withHelper, AppRunState.Uninstalled) : Fail(app);
    }

    private async Task<AppOutcome> RunUserAsync(AppEntry app, AppRecord record, CancellationToken ct)
    {
        var line = UserCommand.For(app);
        if (line is null) return Fail(app);
        int? exit = await _system.UserProcess.RunAsync(line.Application, line.Arguments, UserRunLimit, ct).ConfigureAwait(true);
        if (exit is 3010 or 1641) return Keep(app, record with { Restart = true }, AppRunState.RestartNeeded);
        return await WaitGoneAsync(app, wait: exit == 0) ? Keep(app, record, AppRunState.Uninstalled) : Fail(app);
    }

    private async Task<AppOutcome> RunStoreAsync(AppEntry app, AppRecord record, CancellationToken ct)
    {
        bool ok = await _system.Remover.RemoveAsync(app.Key, ct).ConfigureAwait(true);
        bool still = _system.Packages.List().Any(p => string.Equals(p.FullName, app.Key, StringComparison.OrdinalIgnoreCase));
        return ok && !still ? Keep(app, record, AppRunState.Uninstalled) : Fail(app);
    }

    private AppOutcome Keep(AppEntry app, AppRecord record, AppRunState state)
    {
        _records.Put(record);
        if (state == AppRunState.Uninstalled) Apps = [.. Apps.Where(a => a.Id != app.Id)];
        Set(app, state);
        return new(app, new AppStatus(state, app.Name), record.Id);
    }

    private AppOutcome Fail(AppEntry app)
    {
        Set(app, AppRunState.Failed);
        return new(app, new AppStatus(AppRunState.Failed, app.Name));
    }

    /// <summary>The entry is gone: at once, or (when the uninstaller exited cleanly and may be finishing from a copy of itself) within the settle time.</summary>
    private async Task<bool> WaitGoneAsync(AppEntry app, bool wait)
    {
        var end = _time.GetTimestamp();
        while (true)
        {
            if (IsGone(app)) return true;
            if (!wait || _time.GetElapsedTime(end) >= _settle) return false;
            try { await _delay(TimeSpan.FromMilliseconds(500), _lifetime.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { return IsGone(app); }
        }
    }

    private bool IsGone(AppEntry app) => app.IsStore
        ? !_system.Packages.List().Any(p => string.Equals(p.FullName, app.Key, StringComparison.OrdinalIgnoreCase))
        : _system.Registry.ReadOne(app.Source, app.Key) is null;

    private static IReadOnlyList<string> Folders(AppEntry app) => app.InstallLocation is { Length: > 3 } location ? [location] : [];

    /// <summary>What the entry says now, kept before its uninstaller deletes it. A Store app keeps its family name only: the OS removes its install folder.</summary>
    private AppRecord Snapshot(AppEntry app)
    {
        var fresh = app;
        if (!app.IsStore && _system.Registry.ReadOne(app.Source, app.Key) is { } raw && _reader.Registered(app.Source, raw) is { } read) fresh = read;
        string? uninstallerFolder = app.IsStore ? null : UserCommand.FolderOf(fresh);
        return new AppRecord(Guid.NewGuid(), fresh.Name, fresh.Publisher, fresh.Version, fresh.Source, fresh.Key, app.IsStore ? null : fresh.InstallLocation,
            fresh.ProductCode, uninstallerFolder is null ? [] : [uninstallerFolder], fresh.PackageFamilyName, _time.GetUtcNow().UtcDateTime);
    }

    // ---- leftovers ----

    /// <summary>Apps whose record Chippi still holds (30 days), newest first. The Scan for leftovers picker lists them.</summary>
    public IReadOnlyList<AppRecord> Recent => _records.List();

    /// <summary>
    /// The read-only scan, off the UI thread. Null when there is nothing to scan: the app's entry is still there (the uninstall failed or is
    /// unfinished), or the record asked for a restart first.
    /// </summary>
    public async Task<LeftoverScan?> ScanAsync(AppRecord record, CancellationToken ct = default)
    {
        if (_disposed || record.Restart) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        return await Task.Run(() =>
        {
            var installed = _reader.Read(linked.Token);
            if (installed.Any(a => a.Source == record.Source && string.Equals(a.Key, record.Key, StringComparison.OrdinalIgnoreCase))) return null;
            return _scanner.Scan(record, installed, linked.Token);
        }, linked.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Removes the ticked items. When every item that could be removed is gone, the record is marked Cleaned and the helper's record is dropped.
    /// The helper's session ends when this returns.
    /// </summary>
    public async Task<LeftoverRun> RemoveAsync(LeftoverScan scan, IReadOnlyCollection<string> ticked, IProgress<LeftoverResult>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var run = await _remover.RemoveAsync(scan, ticked, progress, ct).ConfigureAwait(true);
            var removable = scan.Removable;
            if (removable.Count > 0 && removable.All(i => run.Results.Any(r => r.Item.Id == i.Id && r.Ok)))
            {
                _records.Put(scan.Record with { Cleaned = true });
                if (scan.Record.HelperRecordId is { } id && _admin is not null) await _admin.DropRecordAsync(id, CancellationToken.None).ConfigureAwait(true);
            }
            return run;
        }
        finally { await EndAdminSessionAsync(); }
    }

    /// <summary>Forgets an app's record: Chippi's copy and the helper's.</summary>
    public async Task DismissAsync(AppRecord record)
    {
        _records.Remove(record.Id);
        try
        {
            if (record.HelperRecordId is { } id && _admin is not null) await _admin.DropRecordAsync(id, CancellationToken.None).ConfigureAwait(true);
        }
        finally { await EndAdminSessionAsync(); }
    }

    /// <summary>Ends the helper's session a queue or a leftover step left open. Safe with none open.</summary>
    public async Task EndAdminSessionAsync()
    {
        if (_admin is null) return;
        try { await _admin.EndSessionAsync().ConfigureAwait(true); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
