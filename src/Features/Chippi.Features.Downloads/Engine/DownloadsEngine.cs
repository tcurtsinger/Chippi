// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Notifications;
using Chippi.Features.Downloads.Import;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Chippi.Rules;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Downloads.Engine;

/// <summary>
/// The organizer's pipeline (architecture.md §2): watch → debounce → readiness → mode → plan → execute.
/// One background loop checks files that are due; previews, approvals and undo are serialized
/// with it so only one thing moves files at a time. Events may fire on any thread.
/// </summary>
public sealed class DownloadsEngine : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    private readonly IHistoryStore _history;
    private readonly INotifier _notifier;
    private readonly ILogger _logger;
    private readonly EngineOptions _options;
    private readonly TimeProvider _time;
    private readonly Executor _executor;
    private readonly SelfTriggerGuard _guard;
    private readonly SemaphoreSlim _work = new(1, 1);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Tracked> _tracked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingFile> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, PreviewPlan> _previews = [];
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Stamp, IReadOnlyList<string> Patterns)> _ignore = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();
    private DownloadsSettings _settings = new();
    private IReadOnlyList<Rule> _rules = [];
    private Task? _loop;
    private bool _disposed;
    private volatile bool _closed;

    public DownloadsEngine(IHistoryStore history, INotifier notifier, ILogger logger, EngineOptions? options = null, TimeProvider? time = null)
    {
        _history = history;
        _notifier = notifier;
        _logger = logger;
        _options = options ?? new EngineOptions();
        _time = time ?? TimeProvider.System;
        _executor = new Executor(history, _options);
        _guard = new SelfTriggerGuard(history);
    }

    public IHistoryStore History => _history;

    /// <summary>The Tidy tab's list: ready, held and waiting files across watched folders.</summary>
    public IReadOnlyList<PendingFile> Pending
    {
        get { lock (_gate) return [.. _pending.Values.OrderBy(p => p.Folder).ThenBy(p => p.Name)]; }
    }

    /// <summary>Raised after <see cref="Pending"/> changes.</summary>
    public event EventHandler? PendingChanged;

    /// <summary>Raised after files were organized, failed or restored (the Tidy tab's recent list reads history).</summary>
    public event EventHandler? HistoryChanged;

    /// <summary>Settles interrupted operations, prunes old history, then watches the folders.</summary>
    public void Start(DownloadsSettings settings, bool runLoop = true)
    {
        int settled = _executor.Reconcile();
        if (settled > 0)
            _logger.LogInformation("Reconciled {Count} interrupted operations", settled);
        int pruned = _history.ClearBefore(_time.GetUtcNow() - TimeSpan.FromDays(Math.Max(1, settings.HistoryDays)));
        if (pruned > 0)
            _logger.LogInformation("Pruned {Count} history entries older than {Days} days", pruned, settings.HistoryDays);
        Apply(settings);
        if (runLoop)
            _loop = Task.Run(() => LoopAsync(_stop.Token));
    }

    /// <summary>Settings changed (folders, modes, rules): re-sync watchers and rescan.</summary>
    public void Apply(DownloadsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var rules = ValidRules(settings.Rules);
        var watched = settings.Folders.Where(f => f.Mode != FolderMode.Paused && Directory.Exists(f.Path)).ToList();
        lock (_gate)
        {
            _settings = settings;
            _rules = rules;
            foreach (var path in _watchers.Keys.Where(k => !watched.Any(f => Same(f.Path, k))).ToList())
            {
                _watchers[path].Dispose();
                _watchers.Remove(path);
            }
            // Everything is looked at again under the new rules and modes.
            _tracked.Clear();
            _pending.Clear();
        }
        foreach (var folder in watched)
        {
            lock (_gate)
            {
                if (!_watchers.ContainsKey(folder.Path))
                    _watchers[folder.Path] = Watch(folder.Path);
            }
            Rescan(folder.Path);
        }
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Plans every eligible file in <paramref name="folder"/> for review (Organize now, the Explorer menu).</summary>
    public Task<PreviewPlan> CreatePreviewAsync(string folder, RunTrigger trigger = RunTrigger.Preview, CancellationToken ct = default)
    {
        var files = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).ToList()
            : [];
        return PreviewFilesAsync(folder, files, trigger, ct);
    }

    /// <summary>Plans the given files, all directly in <paramref name="folder"/>.</summary>
    public Task<PreviewPlan> PreviewFilesAsync(string folder, IReadOnlyList<string> files, RunTrigger trigger, CancellationToken ct = default) =>
        ExclusiveAsync(() => BuildPreview(folder, [.. files.Select(f => (f, f))], trigger, null), ct);

    /// <summary>
    /// Plans an extracted archive as if its files were in <paramref name="folder"/>: rules apply as
    /// they would there, and a file no rule matches goes to the folder itself. Never applied without approval.
    /// </summary>
    public Task<PreviewPlan> PreviewImportAsync(string folder, ExtractedArchive archive, CancellationToken ct = default)
    {
        var files = archive.Files.Select(f => (f, Path.Combine(folder, Path.GetFileName(f)))).ToList();
        return ExclusiveAsync(() => BuildPreview(folder, files, RunTrigger.ArchiveImport, archive), ct);
    }

    /// <summary>Drops a preview without applying it (Cancel), deleting an import's extracted files.</summary>
    public void Discard(Guid previewId)
    {
        PreviewPlan? preview;
        lock (_gate)
            _previews.Remove(previewId, out preview);
        if (preview?.Staging is { } staging)
            ArchiveImporter.DeleteStaging(staging, _logger);
    }

    /// <summary>
    /// Applies the selected items of a preview, once. Each is re-validated: a file that changed or a
    /// destination that got taken since the preview fails that item instead of being re-planned.
    /// </summary>
    public async Task<ApplyResult> ApproveAsync(Guid previewId, IReadOnlyCollection<int> selected, CancellationToken ct = default)
    {
        PreviewPlan? preview;
        lock (_gate)
            _previews.Remove(previewId, out preview);
        if (preview is null)
            throw new InvalidOperationException("That preview was already used.");
        try
        {
            if (preview.ExpiresAt <= _time.GetUtcNow())
                throw new InvalidOperationException("That preview expired. Organize again to see a fresh one.");
            return await ExclusiveAsync(() => ApplyPreview(preview, selected), ct).ConfigureAwait(false);
        }
        finally
        {
            // Unapproved files of an import go with the preview; it is single use. Undo of an
            // imported file never needs these: its history source is the folder it was imported into.
            if (preview.Staging is { } staging)
                ArchiveImporter.DeleteStaging(staging, _logger);
        }
    }

    /// <summary>Verified undo of one completed move or rename (architecture.md §3).</summary>
    public async Task<UndoResult> UndoAsync(long operationId, CancellationToken ct = default)
    {
        var result = await ExclusiveAsync(() => Undo(operationId), ct).ConfigureAwait(false);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Undoes every completed operation of a run, newest first. Each is verified on its own.</summary>
    public async Task<IReadOnlyList<UndoResult>> UndoRunAsync(long runId, CancellationToken ct = default)
    {
        var results = await ExclusiveAsync<IReadOnlyList<UndoResult>>(() => [.. _history.RunOperations(runId)
            .Where(o => o.State == OperationState.Completed && o.Kind is HistoryOperationKind.Move or HistoryOperationKind.Rename)
            .Reverse()
            .TakeWhile(_ => !_stop.IsCancellationRequested)
            .Select(o => Undo(o.Id))], ct).ConfigureAwait(false);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return results;
    }

    /// <summary>
    /// Stops watching and waits for whatever is moving files right now (the loop, an approval, an
    /// undo) to finish before returning, so the caller can close history safely. Work that has not
    /// started yet is refused with <see cref="ObjectDisposedException"/>. A batch stops between files.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var watcher in _watchers.Values)
                watcher.Dispose();
            _watchers.Clear();
        }
        _stop.Cancel();
        try
        {
            _loop?.Wait();
        }
        catch (AggregateException)
        {
            // The loop ends with cancellation.
        }
        _work.Wait(); // the current holder finishes its file; later callers see _closed
        _closed = true;
        _work.Release();
        _stop.Dispose();
    }

    /// <summary>Runs one pass of the loop now. Tests drive the engine with this and a fake clock.</summary>
    internal void ProcessDueNow()
    {
        _work.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            ProcessDue();
        }
        finally
        {
            _work.Release();
        }
    }

    /// <summary>Runs <paramref name="work"/> while holding the one right to move files.</summary>
    private async Task<T> ExclusiveAsync<T>(Func<T> work, CancellationToken ct)
    {
        await _work.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            return await Task.Run(work, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _work.Release();
        }
    }

    // ---- watching

    private FileSystemWatcher Watch(string folder)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };
        watcher.Created += (_, e) => Track(e.FullPath, arrived: true);
        watcher.Changed += (_, e) => Track(e.FullPath, arrived: true);
        watcher.Renamed += (_, e) =>
        {
            Drop(e.OldFullPath);
            Track(e.FullPath, arrived: true);
        };
        watcher.Deleted += (_, e) => Drop(e.FullPath);
        watcher.Error += (_, e) =>
        {
            // Overflow: events were lost, so look at everything again.
            _logger.LogWarning(e.GetException(), "Watcher overflow in {Folder}; rescanning", folder);
            Rescan(folder);
        };
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void Rescan(string folder)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
                Track(path, arrived: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not scan {Folder}", folder);
        }
    }

    /// <summary>
    /// Starts readiness checks for a file. A watcher event restarts its clock and marks it a new
    /// arrival (which earns a review notice); a rescan only picks up files not already tracked or listed.
    /// </summary>
    internal void Track(string path, bool arrived)
    {
        if (path.EndsWith(Executor.PartialSuffix, StringComparison.OrdinalIgnoreCase))
            return;
        var now = _time.GetUtcNow();
        bool changed;
        lock (_gate)
        {
            if (WatchedFolderOf(path) is not { } folder || folder.Mode == FolderMode.Paused)
                return;
            if (!arrived && (_tracked.ContainsKey(path) || _pending.ContainsKey(path)))
                return;
            _tracked[path] = new Tracked(path, folder, now, arrived) { NextCheck = now + Debounce };
            changed = _pending.Remove(path);
        }
        if (changed)
            PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Drop(string path)
    {
        bool changed;
        lock (_gate)
        {
            _tracked.Remove(path);
            changed = _pending.Remove(path);
        }
        if (changed)
            PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- the loop

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.Tick, _time, token).ConfigureAwait(false);
                await _work.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    ProcessDue();
                }
                finally
                {
                    _work.Release();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Organizer loop failed; continuing");
            }
        }
    }

    private void ProcessDue()
    {
        var now = _time.GetUtcNow();
        List<Tracked> due;
        lock (_gate)
            due = [.. _tracked.Values.Where(t => !t.GaveUp && t.NextCheck <= now)];
        if (due.Count == 0)
            return;

        var ready = new List<(Tracked Tracked, FileFacts Facts)>();
        foreach (var tracked in due)
        {
            var (state, reason, facts) = CheckReadiness(tracked, now);
            switch (state)
            {
                case Readiness.Excluded:
                    Forget(tracked);
                    break;
                case Readiness.Waiting:
                    Backoff(tracked, reason, now);
                    break;
                case Readiness.Ready:
                    Forget(tracked);
                    ready.Add((tracked, facts!));
                    break;
                default:
                    break; // not settled yet; the check set its next time
            }
        }

        var batch = new NoticeBatch(RunTrigger.Silent);
        foreach (var group in ready.GroupBy(r => r.Tracked.Folder.Path, StringComparer.OrdinalIgnoreCase))
            Route(group.First().Tracked.Folder, [.. group], batch);
        batch.Send(_notifier);
    }

    private enum Readiness
    {
        Ready,
        Settling,
        Waiting,
        Excluded,
    }

    private (Readiness, string?, FileFacts?) CheckReadiness(Tracked tracked, DateTimeOffset now)
    {
        var info = new FileInfo(tracked.Path);
        if (!info.Exists)
            return (Readiness.Excluded, null, null);
        var facts = Facts(info);

        switch (FileSafety.Exclusion(facts)?.Kind)
        {
            case WarningKind.EmptyFile:
                return (Readiness.Waiting, "Empty", facts); // browsers create the file before they write it
            case not null:
                return (Readiness.Excluded, null, facts); // partial download, system file, cloud placeholder
        }
        if (IgnorePatterns(tracked.Folder.Path) is not { } patterns)
            return (Readiness.Waiting, "Ignore file unreadable", facts);
        if (patterns.Any(p => IgnoreFile.Matches(facts.Name, p)))
            return (Readiness.Excluded, null, facts);
        if (_guard.IsOwnOutput(tracked.Path, facts.Bytes, facts.Modified))
            return (Readiness.Excluded, null, facts); // Chippi made this file: never organize it again

        // Stable: the same size and last write across two samples.
        if (tracked.LastBytes != facts.Bytes || tracked.LastWrite != facts.Modified)
        {
            tracked.LastBytes = facts.Bytes;
            tracked.LastWrite = facts.Modified;
            tracked.LastSample = now;
            tracked.NextCheck = now + _options.StabilityInterval;
            return (Readiness.Settling, null, facts);
        }
        if (now - tracked.LastSample < _options.StabilityInterval)
        {
            tracked.NextCheck = tracked.LastSample + _options.StabilityInterval;
            return (Readiness.Settling, null, facts);
        }

        // Silent waits out the grace period after the last write; Manual only needs the file settled.
        if (tracked.Folder.Mode == FolderMode.Silent && IsNew(tracked.Folder, facts))
        {
            var graceEnds = facts.Modified + TimeSpan.FromSeconds(Math.Max(0, tracked.Folder.GraceSeconds));
            if (now < graceEnds)
            {
                tracked.NextCheck = graceEnds;
                return (Readiness.Settling, null, facts);
            }
        }

        // Nobody else has it open. A failure can also mean access denied: logged, treated the same, retried.
        try
        {
            using var probe = new FileStream(tracked.Path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug("{File} is unavailable: {Reason}", tracked.Path, ex.Message);
            return (Readiness.Waiting, "In use", facts);
        }
        return (Readiness.Ready, null, facts);
    }

    private void Backoff(Tracked tracked, string? reason, DateTimeOffset now)
    {
        var delays = _options.Backoff;
        tracked.NextCheck = now + delays[Math.Min(tracked.Attempts, delays.Count - 1)];
        tracked.Attempts++;
        if (now - tracked.FirstSeen >= _options.GiveUpAfter)
        {
            tracked.GaveUp = true; // a later watcher event starts it again
            SetPending(new PendingFile(tracked.Folder.Path, tracked.Path, Path.GetFileName(tracked.Path), PendingStatus.Waiting, reason));
        }
    }

    /// <summary>
    /// The mode router. New files in a Silent folder are organized; everything else that a rule
    /// would move is listed for Organize now. Held files are listed either way; unmatched files are left alone.
    /// </summary>
    private void Route(WatchedFolder folder, IReadOnlyList<(Tracked Tracked, FileFacts Facts)> files, NoticeBatch batch)
    {
        IReadOnlyList<Rule> rules;
        lock (_gate)
            rules = _rules;
        if (IgnorePatterns(folder.Path) is not { } patterns)
        {
            // It became unreadable since the readiness check: look again later.
            foreach (var (tracked, _) in files)
                Track(tracked.Path, tracked.Arrived);
            return;
        }
        var context = new PlanContext(folder.Path, _time.GetUtcNow(), TimeZoneInfo.Local, []) { IgnorePatterns = patterns };
        var plans = Planner.PlanBatch(files.Select(f => f.Facts), rules, context);
        var names = rules.ToDictionary(r => r.Id, r => r.Name);
        long? runId = null;
        for (int i = 0; i < plans.Count && !_stop.IsCancellationRequested; i++) // stopping: the rest wait for the next start
        {
            var plan = plans[i];
            var (tracked, file) = files[i];
            switch (plan.Kind)
            {
                case OperationKind.Hold:
                    string reason = plan.HoldReason ?? plan.Warnings.LastOrDefault()?.Detail ?? "Held";
                    SetPending(new PendingFile(folder.Path, file.FullPath, file.Name, PendingStatus.Held, reason));
                    if (tracked.Arrived)
                        batch.Held.Add((file.Name, reason));
                    break;
                case OperationKind.Move or OperationKind.Rename when folder.Mode == FolderMode.Silent && IsNew(folder, file):
                    runId ??= _history.BeginRun(RunTrigger.Silent, folder.Path);
                    var result = _executor.Execute(runId.Value, new ExecutionRequest(
                        plan.Kind == OperationKind.Rename ? HistoryOperationKind.Rename : HistoryOperationKind.Move,
                        file.FullPath, plan.Destination!, plan.RuleId,
                        plan.RuleId is { } id && names.TryGetValue(id, out var name) ? name : null,
                        file.Bytes, file.Modified, AllowRenumber: true));
                    if (result.Succeeded)
                        batch.Organized.Add((file.Name, result.Destination, runId.Value, true));
                    else
                        batch.Failed.Add((file.Name, result.Error ?? "Failed", runId.Value));
                    break;
                case OperationKind.Move or OperationKind.Rename:
                    // Manual, or already there when the folder was added: only Organize now moves it.
                    SetPending(new PendingFile(folder.Path, file.FullPath, file.Name, PendingStatus.Ready, null));
                    if (tracked.Arrived && IsNew(folder, file))
                        batch.Review.Add(file.Name);
                    break;
                default:
                    break; // no rule matched: left where it is
            }
        }
        if (runId is { } run)
        {
            _history.EndRun(run);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- preview, approve, undo

    /// <param name="files">Each file's real path and the path it is planned as (they differ only for an import).</param>
    private PreviewPlan BuildPreview(string folder, IReadOnlyList<(string Real, string Planned)> files, RunTrigger trigger, ExtractedArchive? archive)
    {
        IReadOnlyList<Rule> rules;
        lock (_gate)
            rules = _rules;
        var patterns = IgnorePatterns(folder)
            ?? throw new InvalidOperationException($"{EngineOptions.IgnoreFileName} in this folder can't be read right now. Try again in a moment.");
        var facts = new List<FileFacts>();
        var real = new List<string>();
        foreach (var (path, planned) in files)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                continue;
            var fact = Facts(info) with { FullPath = planned };
            // Never listed: built-in exclusions, cloud placeholders, empty files and files Chippi
            // itself produced (or a rename would repeat on every run). Ignored names are listed as
            // held by the planner, so the person sees why they stay.
            if (FileSafety.Exclusion(fact) is not null || _guard.IsOwnOutput(path, fact.Bytes, fact.Modified))
                continue;
            facts.Add(fact);
            real.Add(path);
        }

        var context = new PlanContext(folder, _time.GetUtcNow(), TimeZoneInfo.Local, []) { IgnorePatterns = patterns, Preview = true };
        var first = Planner.PlanBatch(facts, rules, context);
        // Plan again knowing what already sits in each destination folder, so proposed names never collide.
        var occupied = first.Where(p => p.Destination is not null)
            .Select(p => Path.GetDirectoryName(p.Destination!)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFileSystemEntries)
            .ToList();
        var plans = occupied.Count == 0 ? first : Planner.PlanBatch(facts, rules, context with { OccupiedPaths = occupied });

        var names = rules.ToDictionary(r => r.Id, r => r.Name);
        var items = plans
            .Select((plan, i) => (Plan: archive is not null && plan.Kind == OperationKind.NoMatch
                // An imported file no rule wants still has to leave staging: it lands in the folder itself.
                ? plan with { Kind = OperationKind.Move, Destination = facts[i].FullPath }
                : plan, Fact: facts[i], Real: real[i]))
            .Select((p, i) => new PreviewItem( // no-match rows stay: they show where the file stays, unselectable
                i + 1, p.Real, p.Fact.Name, p.Fact.Bytes, p.Fact.Modified, p.Plan.Kind, p.Plan.Destination, p.Plan.RuleId,
                p.Plan.RuleId is { } id && names.TryGetValue(id, out var name) ? name : null, p.Plan.Warnings, p.Plan.HoldReason)
            {
                Version = FileVersion.Of(p.Real),
                ImportedAs = archive is null ? null : p.Fact.FullPath,
            })
            .ToList();
        var now = _time.GetUtcNow();
        var preview = new PreviewPlan(Guid.NewGuid(), folder, trigger, now, now + _options.PreviewLifetime, items)
        {
            Archive = archive?.Name,
            Staging = archive?.Staging,
        };
        lock (_gate)
        {
            foreach (var expired in _previews.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToList())
                _previews.Remove(expired);
            _previews[preview.Id] = preview;
        }
        return preview;
    }

    private ApplyResult ApplyPreview(PreviewPlan preview, IReadOnlyCollection<int> selected)
    {
        long runId = _history.BeginRun(preview.Trigger, preview.Folder);
        var results = new List<ItemResult>();
        var batch = new NoticeBatch(preview.Trigger) { Archive = preview.Archive };
        foreach (var item in preview.Items.Where(i => selected.Contains(i.Id)))
        {
            if (_stop.IsCancellationRequested)
            {
                results.Add(new ItemResult(item.Id, item.Source, null, false, "Chippi closed before this file."));
                continue;
            }
            if (item.Kind is not (OperationKind.Move or OperationKind.Rename) || item.Destination is null)
            {
                results.Add(new ItemResult(item.Id, item.Source, null, false, "Nothing to do for this file."));
                continue;
            }
            // An imported file is recorded as coming from the folder it was imported into, so undo
            // has somewhere real to put it back. One that lands right there is an Import: no undo.
            var kind = item.ImportedAs is { } importedAs && string.Equals(item.Destination, importedAs, StringComparison.OrdinalIgnoreCase)
                ? HistoryOperationKind.Import
                : item.Kind == OperationKind.Rename ? HistoryOperationKind.Rename : HistoryOperationKind.Move;
            var result = _executor.Execute(runId, new ExecutionRequest(
                kind, item.Source, item.Destination, item.RuleId, item.RuleName, item.Bytes, item.ModifiedUtc, AllowRenumber: false,
                ExpectedVersion: item.Version, RecordedSource: item.ImportedAs));
            results.Add(new ItemResult(item.Id, item.Source, result.Destination, result.Succeeded, result.Error) { OperationId = result.OperationId });
            if (result.Succeeded)
            {
                Drop(item.Source);
                batch.Organized.Add((item.Name, result.Destination, runId, kind != HistoryOperationKind.Import));
            }
            else
            {
                batch.Failed.Add((item.Name, result.Error ?? "Failed", runId));
            }
        }
        _history.EndRun(runId);
        // The person is looking at the result; only an archive import also announces itself.
        if (preview.Trigger == RunTrigger.ArchiveImport)
            batch.Send(_notifier);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        return new ApplyResult(runId, results);
    }

    private UndoResult Undo(long operationId)
    {
        var record = _history.Get(operationId);
        if (record is null || record.Kind == HistoryOperationKind.Restore)
            return new UndoResult(false, null, "Nothing to undo.");
        if (record.Kind == HistoryOperationKind.Import)
            return new UndoResult(false, null, "Imported straight into the folder, so there is nothing to undo.");
        if (record.State != OperationState.Completed)
            return new UndoResult(false, null, record.State == OperationState.Undone ? "Already undone." : "Only completed changes can be undone.");
        var current = new FileInfo(record.DestinationPath);
        if (!current.Exists)
            return new UndoResult(false, null, "The file is no longer where it was put.");
        if (record.Sha256 is null)
            return new UndoResult(false, null, Executor.ChangedSinceOrganized);
        if (File.Exists(record.SourcePath) || Directory.Exists(record.SourcePath))
            return new UndoResult(false, null, "Something already has the original name.");
        string originalFolder = Path.GetDirectoryName(record.SourcePath)!;
        if (!Directory.Exists(originalFolder))
            return new UndoResult(false, null, "The original folder is gone.");

        long runId = _history.BeginRun(RunTrigger.Undo, originalFolder);
        var result = _executor.Execute(runId, new ExecutionRequest(
            HistoryOperationKind.Restore, record.DestinationPath, record.SourcePath, record.RuleId, record.RuleName,
            current.Length, new DateTimeOffset(current.LastWriteTimeUtc, TimeSpan.Zero), AllowRenumber: false, UndoOf: record.Id,
            ExpectedSha256: record.Sha256)); // checked on the held handle, the bytes that actually move back
        _history.EndRun(runId);
        if (!result.Succeeded)
            return new UndoResult(false, result.OperationId, result.Error);
        _history.MarkUndone(record.Id);
        return new UndoResult(true, result.OperationId, null);
    }

    // ---- helpers

    private static bool IsNew(WatchedFolder folder, FileFacts file) => file.Created >= folder.BaselineUtc;

    private void SetPending(PendingFile file)
    {
        lock (_gate)
            _pending[file.Path] = file;
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Forget(Tracked tracked)
    {
        lock (_gate)
        {
            // Only if a newer event has not replaced it meanwhile.
            if (_tracked.TryGetValue(tracked.Path, out var current) && ReferenceEquals(current, tracked))
                _tracked.Remove(tracked.Path);
        }
    }

    /// <summary>The watched folder holding <paramref name="path"/> directly (top level only). Call under the gate.</summary>
    private WatchedFolder? WatchedFolderOf(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        return parent is null ? null : _settings.Folders.FirstOrDefault(f => Same(f.Path, parent));
    }

    /// <summary>
    /// The folder's ignore patterns, or null while its ignore file exists but cannot be read. Then
    /// nothing in the folder is organized: an unreadable ignore file must not read as "ignore nothing".
    /// </summary>
    private IReadOnlyList<string>? IgnorePatterns(string folder)
    {
        string file = Path.Combine(folder, EngineOptions.IgnoreFileName);
        DateTime stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
        lock (_gate)
        {
            if (_ignore.TryGetValue(folder, out var cached) && cached.Stamp == stamp)
                return cached.Patterns;
        }
        IReadOnlyList<string> patterns = [];
        if (stamp != DateTime.MinValue)
        {
            try
            {
                var parsed = IgnoreFile.Parse(File.ReadAllText(file));
                foreach (var error in parsed.Errors)
                    _logger.LogWarning("{File} line {Line} is not supported: {Pattern}", file, error.Line, error.Pattern);
                patterns = parsed.Patterns;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not read {File}; holding the folder until it can be read", file);
                return null; // not cached: the next look tries again
            }
        }
        lock (_gate)
            _ignore[folder] = (stamp, patterns);
        return patterns;
    }

    private List<Rule> ValidRules(IEnumerable<RuleSetting> settings)
    {
        // Order by the stored order, then renumber: a duplicate Order in settings.json must not disable every rule.
        var rules = settings.OrderBy(s => s.Order).Select((s, i) => s.ToRule() with { Order = i }).ToList();
        var bad = RuleValidator.Validate(rules).Select(e => e.RuleId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in bad)
            _logger.LogWarning("Rule {Id} is invalid and is skipped", id);
        return [.. rules.Where(r => !bad.Contains(r.Id))];
    }

    private static FileFacts Facts(FileInfo info) => new(
        info.Name,
        info.FullName,
        info.Length,
        new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
        info.Attributes);

    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private sealed class Tracked(string path, WatchedFolder folder, DateTimeOffset firstSeen, bool arrived)
    {
        public string Path { get; } = path;

        public WatchedFolder Folder { get; } = folder;

        public DateTimeOffset FirstSeen { get; } = firstSeen;

        /// <summary>Seen by the watcher, not found by a rescan: only arrivals are announced.</summary>
        public bool Arrived { get; } = arrived;

        public long? LastBytes { get; set; }

        public DateTimeOffset? LastWrite { get; set; }

        public DateTimeOffset LastSample { get; set; }

        public DateTimeOffset NextCheck { get; set; }

        public int Attempts { get; set; }

        public bool GaveUp { get; set; }
    }

    /// <summary>One pass's events turned into a few notices. Copy is state first and short.</summary>
    private sealed class NoticeBatch(RunTrigger trigger)
    {
        /// <param name="Undoable">A move or rename; an archive file that landed straight in the folder (Import) is not.</param>
        public List<(string Name, string Destination, long RunId, bool Undoable)> Organized { get; } = [];

        public List<(string Name, string Reason)> Held { get; } = [];

        public List<(string Name, string Error, long RunId)> Failed { get; } = [];

        public List<string> Review { get; } = [];

        /// <summary>Archive import: the archive's name, which titles the one notice.</summary>
        public string? Archive { get; init; }

        /// <summary>Undo only when the run has something undo can put back; History always.</summary>
        private static IReadOnlyList<NoticeAction> Buttons(long runId, bool undoable) =>
            undoable ? [DownloadsNotices.Undo(runId), DownloadsNotices.History(runId)] : [DownloadsNotices.History(runId)];

        public void Send(INotifier notifier)
        {
            if (trigger == RunTrigger.ArchiveImport)
            {
                if (Organized.Count + Failed.Count > 0)
                {
                    long runId = Organized.Count > 0 ? Organized[0].RunId : Failed[0].RunId;
                    string body = $"{Organized.Count} {(Organized.Count == 1 ? "file" : "files")} organized";
                    notifier.Show(new Notice(DownloadsNotices.ArchiveImported, $"{Archive ?? "Archive"} imported")
                    {
                        Body = Failed.Count == 0 ? body : $"{body} · {Failed.Count} failed",
                        Activation = DownloadsNotices.RunActivation(runId),
                        Actions = Buttons(runId, Organized.Any(o => o.Undoable)),
                        Celebrate = Failed.Count == 0,
                        NeedsAttention = Failed.Count > 0,
                    });
                }
                return;
            }
            // One notice per run, so its count, Undo and History all mean the same files
            // (two Silent folders in one pass are two runs).
            foreach (var run in Organized.GroupBy(o => o.RunId))
            {
                var files = run.ToList();
                notifier.Show(new Notice(DownloadsNotices.Organized, files.Count == 1 ? $"{files[0].Name} organized" : $"{files.Count} files organized")
                {
                    Body = files.Count == 1 ? Path.GetDirectoryName(files[0].Destination) : null,
                    Activation = DownloadsNotices.RunActivation(run.Key),
                    Actions = Buttons(run.Key, files.Any(o => o.Undoable)),
                    Celebrate = true,
                });
            }
            if (Held.Count > 0)
            {
                // A held file is not moved, so it has no history run: the body and Review open the
                // pending review, where held files are listed.
                notifier.Show(new Notice(DownloadsNotices.Held, Held.Count == 1 ? "1 file held" : $"{Held.Count} files held")
                {
                    Body = Held.Count == 1 ? $"{Held[0].Name} · {Held[0].Reason}" : null,
                    Activation = DownloadsNotices.ReviewActivation,
                    Actions = [DownloadsNotices.Review],
                    NeedsAttention = true,
                });
            }
            foreach (var run in Failed.GroupBy(o => o.RunId))
            {
                var files = run.ToList();
                notifier.Show(new Notice(DownloadsNotices.Failed, files.Count == 1 ? "1 file not moved" : $"{files.Count} files not moved")
                {
                    Body = files.Count == 1 ? $"{files[0].Name} · {files[0].Error}" : null,
                    Activation = DownloadsNotices.RunActivation(run.Key),
                    Actions = [DownloadsNotices.History(run.Key)],
                    NeedsAttention = true,
                });
            }
            if (Review.Count > 0)
            {
                // No run yet: nothing has moved.
                notifier.Show(new Notice(DownloadsNotices.ReadyToReview, Review.Count == 1 ? "1 file to review" : $"{Review.Count} files to review")
                {
                    Body = Review.Count == 1 ? Review[0] : null,
                    Activation = DownloadsNotices.ReviewActivation,
                    Actions = [DownloadsNotices.Review],
                });
            }
        }
    }
}
