// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using Chippi.Core.Admin;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.Cleaner;

/// <summary>The last size of a category, remembered so the list opens with it (dimmed, "from earlier") while a fresh measure runs.</summary>
public sealed record CleanerSavedSize(long Bytes, bool LowerBound);

/// <summary>
/// The module's settings section. <see cref="Ticks"/> is one value: the list's checkboxes and Settings' switches. A category with no entry
/// takes its default (User temp files and Thumbnail and icon caches on, the rest off).
/// </summary>
public sealed record CleanerSettings
{
    public Dictionary<string, bool> Ticks { get; init; } = [];
    public Dictionary<string, CleanerSavedSize> Sizes { get; init; } = [];
    public Dictionary<string, KeyShortcut?> Shortcuts { get; init; } = [];
}

/// <summary>
/// The list behind Actions › Apps › Clean junk files and Settings › Cleaner. Measure is read-only and runs off the UI thread, one category at a
/// time, each applied on the caller's context as it finishes. A clean is two steps, <see cref="PrepareCleanAsync"/> (measures; with an admin
/// category ticked the helper measures it first, which is the one UAC prompt) and, after the confirm, <see cref="CleanAsync"/>; nothing is ever
/// deleted before the caller says the question was answered yes. Deleted for good, files in use skipped and counted, never forced.
/// </summary>
public sealed class CleanerEngine : IDisposable
{
    private readonly CleanerRoots _roots;
    private readonly IAdminHelper? _admin;
    private readonly Action<CleanerSettings>? _save;
    private readonly Func<IReadOnlySet<string>> _running;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Data> _data = [];
    private CleanerSettings _settings;
    private SynchronizationContext? _ui;
    private Task? _measure;
    private bool _preparing, _cleaning, _disposed;
    private int _runId;
    private CleanerRow[] _rows = [];

    private sealed class Data
    {
        public CleanerRowState State;
        public long? Bytes;
        public int Files, InUse;
        public bool LowerBound, FromEarlier;
        public IReadOnlyList<string>? Skipped;
    }

    public CleanerEngine(CleanerRoots roots, IAdminHelper? admin, CleanerSettings? settings = null, Action<CleanerSettings>? save = null,
        Func<IReadOnlySet<string>>? runningProcesses = null, TimeProvider? time = null)
    {
        _roots = roots;
        _admin = admin;
        _save = save;
        _running = runningProcesses ?? RunningProcesses;
        _time = time ?? TimeProvider.System;
        _settings = Normalize(settings ?? new());
        foreach (var category in CleanerCatalog.All)
        {
            var d = new Data { State = CleanerRowState.Unmeasured };
            if (_settings.Sizes.TryGetValue(category.Id, out var saved))
                (d.State, d.Bytes, d.LowerBound, d.FromEarlier) = (CleanerRowState.Measured, saved.Bytes, saved.LowerBound, true);
            _data[category.Id] = d;
        }
        _rows = Snapshot();
    }

    private static CleanerSettings Normalize(CleanerSettings s) =>
        s with { Ticks = s.Ticks ?? [], Sizes = s.Sizes ?? [], Shortcuts = s.Shortcuts ?? [] };

    /// <summary>Raised on the context that started the work, when a row, a tick or the state changed.</summary>
    public event Action? Changed;

    /// <summary>A clean finished (or stopped): the module shows its result in the island's bar.</summary>
    public event Action<CleanerRunResult>? RunCompleted;

    /// <summary>Every category, in the list's order. Replaced whole, so any thread may read it.</summary>
    public IReadOnlyList<CleanerRow> Rows => _rows;

    public bool IsMeasuring => _measure is { IsCompleted: false };
    /// <summary>A clean is deleting. The app asks before quitting while this is true.</summary>
    public bool IsCleaning => _cleaning;
    /// <summary>The measure a clean starts with is running (including the prompt): the screens show Measuring or Waiting for admin.</summary>
    public bool IsPreparing => _preparing;
    /// <summary>The helper's prompt is open: Waiting for admin.</summary>
    public bool WaitsForAdmin { get; private set; }
    /// <summary>The category being deleted now, null between categories and outside a run; the other rows of a run are queued.</summary>
    public string? RunningId { get; private set; }

    /// <summary>The header's second part after a run: "Admin helper failed"; cleared by the next measure.</summary>
    public string? Meta { get; private set; }

    public CleanerRunResult? LastRun { get; private set; }

    public bool IsTicked(string id) => _settings.Ticks.TryGetValue(id, out var on) ? on : CleanerCatalog.Find(id).DefaultTicked;
    public bool AnyAdminTicked => CleanerCatalog.All.Any(c => c.NeedsAdmin && IsTicked(c.Id));

    /// <summary>Ticks a category. False when settings.json could not be saved; the tick then stays as it was.</summary>
    public bool SetTicked(string id, bool on)
    {
        if (_disposed || _cleaning || !CleanerCatalog.All.Any(c => c.Id == id)) return false;
        if (IsTicked(id) == on) return true;
        var next = _settings with { Ticks = new(_settings.Ticks) { [id] = on } };
        if (!TrySave(next)) return false;
        _settings = next;
        Republish();
        return true;
    }

    /// <summary>The ticked categories' size; "+" when a ticked size is only a lower bound or can't be read yet.</summary>
    public (long Bytes, bool LowerBound) Total
    {
        get
        {
            long bytes = 0;
            bool lower = false;
            foreach (var row in _rows.Where(r => r.Ticked))
            {
                if (row.Bytes is { } b && row.State is CleanerRowState.Measured or CleanerRowState.Measuring or CleanerRowState.Cleaned) { bytes += b; lower |= row.LowerBound; }
                else if (row.Category.NeedsAdmin && row.State == CleanerRowState.Unmeasured) lower = true;
            }
            return (bytes, lower);
        }
    }

    /// <summary>"1.0 GB", "2.6 GB+".</summary>
    public string TotalText { get { var (bytes, lower) = Total; return CleanerText.Size(bytes, lower); } }

    /// <summary>Some size is known (measured now or remembered), so the Apps menu can show the last total.</summary>
    public bool HasSizes => _rows.Any(r => r.Bytes is not null);

    /// <summary>
    /// Measures every category the owner's account can read, read-only and off the UI thread, applying each as it finishes. A measure already
    /// running is awaited, not repeated. Windows temp and Delivery Optimization can't be read without admin: they show no size until a clean
    /// brings up the prompt.
    /// </summary>
    public Task MeasureAsync(CancellationToken ct = default)
    {
        if (_disposed || _cleaning) return Task.CompletedTask;
        if (_measure is { IsCompleted: false } running) return running;
        return _measure = MeasureCoreAsync(ct);
    }

    private async Task MeasureCoreAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, ct);
        var token = linked.Token;
        lock (_gate)
        {
            Meta = null;
            foreach (var category in CleanerCatalog.All)
            {
                var d = _data[category.Id];
                d.FromEarlier = d.Bytes is not null;
                if (CleanerTargets.For(category.Id, _roots).Count > 0) d.State = CleanerRowState.Measuring;
                else if (d.State is not CleanerRowState.Measured) d.State = CleanerRowState.Unmeasured;
            }
        }
        Republish();
        try
        {
            foreach (var category in CleanerCatalog.All)
            {
                var targets = CleanerTargets.For(category.Id, _roots);
                if (targets.Count == 0) continue;
                token.ThrowIfCancellationRequested();
                var tally = await Task.Run(() => CleanerFiles.Run(targets, clean: false, _time.GetUtcNow().UtcDateTime, _running(), token), CancellationToken.None);
                if (token.IsCancellationRequested) break;
                ApplyMeasure(category, tally);
                Republish();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_gate)
                foreach (var d in _data.Values.Where(d => d.State == CleanerRowState.Measuring)) { d.State = d.Bytes is null ? CleanerRowState.Unmeasured : CleanerRowState.Measured; }
        }
        RememberSizes();
        Republish();
    }

    private void ApplyMeasure(CleanerCategory category, CleanerTally t)
    {
        lock (_gate)
        {
            var d = _data[category.Id];
            d.FromEarlier = false;
            d.Files = t.Files; d.InUse = 0; d.Skipped = t.SkippedApps.Count > 0 ? [.. t.SkippedApps] : null;
            d.LowerBound = false;
            if (t.Found == 0)
            {
                d.Bytes = null;
                // Windows' own folders: a standard account may not see them, which is not the same as missing. The helper decides.
                d.State = t.SkippedApps.Count > 0 ? CleanerRowState.InUse
                    : category.Id == CleanerCatalog.WindowsUpdate ? CleanerRowState.Unmeasured
                    : category.Id == CleanerCatalog.Dumps ? CleanerRowState.Measured
                    : CleanerRowState.Unavailable;
                if (category.Id == CleanerCatalog.Dumps) { d.Bytes = 0; d.LowerBound = true; }
                return;
            }
            d.State = CleanerRowState.Measured;
            d.Bytes = t.Bytes;
            d.LowerBound = category.NeedsAdmin;   // what the owner can read is part of what the helper would find
        }
    }

    /// <summary>
    /// Step one of Clean: measures the ticked categories fresh and returns what the confirm asks, or null with nothing ticked. With an admin
    /// category ticked the helper measures it first (the prompt) and stays open for <see cref="CleanAsync"/>; a declined prompt leaves those rows
    /// Needs admin and the plan covers the rest. <paramref name="only"/> limits it to those ids (a row's Retry or Clean).
    /// </summary>
    public async Task<CleanerPlan?> PrepareCleanAsync(IReadOnlyList<string>? only = null, CancellationToken ct = default)
    {
        if (_disposed || _cleaning || _preparing) return null;
        var ticked = CleanerCatalog.All.Where(c => only is null ? IsTicked(c.Id) : only.Contains(c.Id)).ToList();
        if (ticked.Count == 0) return null;
        _preparing = true;
        Republish();
        bool declined = false, helperFailed = false;
        try
        {
            await MeasureAsync(ct);
            ct.ThrowIfCancellationRequested();
            var admin = ticked.Where(c => c.NeedsAdmin).ToList();
            if (admin.Count > 0) (declined, helperFailed) = await MeasureAdminAsync(admin, ct);
            Republish();
            var items = new List<CleanerPlanItem>();
            foreach (var category in ticked)
            {
                var d = Copy(category.Id);
                if (d.State == CleanerRowState.Measured && d.Bytes is { } bytes) items.Add(new(category, bytes, d.Files, d.LowerBound));
            }
            Meta = helperFailed ? "Admin helper failed" : null;
            if (admin.Count > 0 && !items.Any(i => i.Category.NeedsAdmin)) await EndSessionAsync();   // nothing for the open session to do
            return new CleanerPlan(items, declined, helperFailed, [.. ticked.Select(c => c.Id)]);
        }
        catch (OperationCanceledException)
        {
            await EndSessionAsync();
            Republish();
            return null;
        }
        finally { _preparing = false; }
    }

    private async Task<(bool Declined, bool HelperFailed)> MeasureAdminAsync(List<CleanerCategory> admin, CancellationToken ct)
    {
        IReadOnlyList<AdminCategoryResult> results;
        if (_admin is null) { SetAll(admin, CleanerRowState.NeedsAdmin); return (true, false); }
        WaitsForAdmin = true;
        Republish();
        try { results = await _admin.MeasureCategoriesAsync([.. admin.Select(c => c.Admin!.Value)], ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            SetAll(admin, CleanerRowState.Failed);
            return (false, true);
        }
        finally { WaitsForAdmin = false; }
        bool anyNeedsAdmin = false;
        lock (_gate)
        {
            foreach (var category in admin)
            {
                var d = _data[category.Id];
                var r = results.FirstOrDefault(x => x.Category == category.Admin);
                d.FromEarlier = false;
                if (r is null) { d.State = CleanerRowState.Failed; continue; }
                switch (r.State)
                {
                    case AdminCategoryState.Measured:
                        // The helper sees everything the owner's account saw (Windows Update); the user dump folders are added to its system part.
                        long user = 0; int userFiles = 0;
                        if (category.Id == CleanerCatalog.Dumps && d.State == CleanerRowState.Measured) { user = d.Bytes ?? 0; userFiles = d.Files; }
                        d.State = CleanerRowState.Measured; d.Bytes = r.Bytes + user; d.Files = r.Files + userFiles;
                        d.LowerBound = false; d.InUse = r.InUse;
                        break;
                    case AdminCategoryState.NeedsAdmin: d.State = CleanerRowState.NeedsAdmin; anyNeedsAdmin = true; break;
                    case AdminCategoryState.Unavailable: d.State = CleanerRowState.Unavailable; d.Bytes = null; break;
                    case AdminCategoryState.Cancelled: d.State = CleanerRowState.Cancelled; break;
                    default: d.State = CleanerRowState.Failed; break;
                }
            }
        }
        return (anyNeedsAdmin, false);
    }

    private void SetAll(IEnumerable<CleanerCategory> categories, CleanerRowState state)
    {
        lock (_gate) foreach (var c in categories) { _data[c.Id].State = state; _data[c.Id].FromEarlier = false; }
    }

    /// <summary>The question was answered yes: deletes the plan, admin categories first (the helper session the measure opened is still warm), then the rest.</summary>
    public async Task<CleanerRunResult> CleanAsync(CleanerPlan plan, IProgress<CleanerRunProgress>? progress = null, CancellationToken ct = default)
    {
        if (_disposed || _cleaning) return new CleanerRunResult([], 0, 0, false, false);
        _ui = SynchronizationContext.Current;
        _cleaning = true;
        int run = ++_runId;
        bool helperFailed = false, cancelled = false;
        var ids = plan.Asked.ToHashSet();
        lock (_gate) foreach (var item in plan.Items) { var d = _data[item.Category.Id]; d.State = CleanerRowState.Cleaning; d.InUse = 0; }
        Republish();
        try
        {
            var admin = plan.Items.Where(i => i.Category.NeedsAdmin).Select(i => i.Category).ToList();
            RunningId = admin.Count > 0 ? admin[0].Id : plan.Items.FirstOrDefault()?.Category.Id;
            Republish();
            if (admin.Count > 0) helperFailed = await CleanAdminAsync(admin, progress, run, ct);
            foreach (var item in plan.Items.Where(i => !i.Category.NeedsAdmin))
            {
                if (ct.IsCancellationRequested) { MarkCancelled(item.Category.Id); cancelled = true; continue; }
                RunningId = item.Category.Id;
                Republish();
                try
                {
                    var tally = await Task.Run(() => CleanerFiles.Run(CleanerTargets.For(item.Category.Id, _roots), clean: true, _time.GetUtcNow().UtcDateTime, _running(), ct), CancellationToken.None);
                    ApplyClean(item.Category, tally);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (_gate) { _data[item.Category.Id].State = CleanerRowState.Failed; _data[item.Category.Id].Bytes = null; }
                }
                Republish();
                progress?.Report(new(item.Category.Id, Copy(item.Category.Id).Bytes ?? 0));
            }
            cancelled |= ct.IsCancellationRequested;
        }
        finally
        {
            _cleaning = false;
            RunningId = null;
            _runId++;
        }
        long freed = 0; int inUse = 0;
        CleanerRow[] rows;
        lock (_gate)
        {
            foreach (var d in _data.Values)
                if (d.State == CleanerRowState.Cleaning) d.State = CleanerRowState.Failed;
            rows = Snapshot().Where(r => ids.Contains(r.Category.Id)).ToArray();
        }
        foreach (var row in rows.Where(r => r.State is CleanerRowState.Cleaned or CleanerRowState.Cancelled)) { freed += row.Bytes ?? 0; inUse += row.InUseFiles; }
        Meta = helperFailed ? "Admin helper failed" : null;
        var result = LastRun = new CleanerRunResult(rows, freed, inUse, helperFailed, cancelled);
        RememberSizes();
        Republish();
        RunCompleted?.Invoke(result);
        return result;
    }

    /// <summary>True when the helper failed. Results arrive per category; one that never came is Failed, so Retry offers it again.</summary>
    private async Task<bool> CleanAdminAsync(List<CleanerCategory> admin, IProgress<CleanerRunProgress>? progress, int run, CancellationToken ct)
    {
        var names = admin.Select(c => c.Admin!.Value).ToList();
        IReadOnlyList<AdminCategoryResult> results;
        if (_admin is null) { SetAll(admin, CleanerRowState.NeedsAdmin); return false; }
        var reporter = new DirectProgress<AdminCategoryProgress>(p =>
        {
            if (p.Result is null)
            {
                if (admin.FirstOrDefault(c => c.Admin == p.Category) is { } starting) { RunningId = starting.Id; RepublishFromAnyThread(); }
                return;
            }
            lock (_gate)
            {
                if (run != _runId) return;
                var category = admin.FirstOrDefault(c => c.Admin == p.Category);
                if (category is not null) ApplyAdminResult(category, p.Result);
            }
            RepublishFromAnyThread();
            if (admin.FirstOrDefault(c => c.Admin == p.Category) is { } done) progress?.Report(new(done.Id, Copy(done.Id).Bytes ?? 0));
        });
        try { results = await _admin.CleanCategoriesAsync(names, reporter, ct); }
        catch (OperationCanceledException)
        {
            foreach (var c in admin) if (Copy(c.Id).State == CleanerRowState.Cleaning) MarkCancelled(c.Id);
            return false;
        }
        catch (Exception)
        {
            lock (_gate)
                foreach (var c in admin)
                    if (_data[c.Id].State == CleanerRowState.Cleaning) _data[c.Id].State = CleanerRowState.Failed;
            return true;
        }
        lock (_gate)
            foreach (var category in admin)
                if (results.FirstOrDefault(r => r.Category == category.Admin) is { } r) ApplyAdminResult(category, r);
                else _data[category.Id].State = CleanerRowState.Failed;
        // The user-owned dump folders go with the system part, for a category the helper really cleaned.
        foreach (var category in admin.Where(c => c.Id == CleanerCatalog.Dumps))
        {
            if (Copy(category.Id).State is not (CleanerRowState.Cleaned or CleanerRowState.Cancelled) || ct.IsCancellationRequested) continue;
            var tally = await Task.Run(() => CleanerFiles.Run(CleanerTargets.For(category.Id, _roots), clean: true, _time.GetUtcNow().UtcDateTime, _running(), ct), CancellationToken.None);
            lock (_gate) { var d = _data[category.Id]; d.Bytes = (d.Bytes ?? 0) + tally.Bytes; d.Files += tally.Files; d.InUse += tally.InUse; }
        }
        return false;
    }

    private void ApplyAdminResult(CleanerCategory category, AdminCategoryResult r)
    {
        var d = _data[category.Id];
        d.InUse = r.InUse;
        d.Files = r.Files;
        d.LowerBound = false;
        switch (r.State)
        {
            case AdminCategoryState.Cleaned: d.State = CleanerRowState.Cleaned; d.Bytes = r.Bytes; break;
            case AdminCategoryState.Cancelled: d.State = CleanerRowState.Cancelled; d.Bytes = r.Bytes; break;
            case AdminCategoryState.Unavailable: d.State = CleanerRowState.Unavailable; d.Bytes = null; break;
            case AdminCategoryState.NeedsAdmin: d.State = CleanerRowState.NeedsAdmin; d.Bytes = null; break;
            default: d.State = CleanerRowState.Failed; d.Bytes = null; break;
        }
    }

    private void ApplyClean(CleanerCategory category, CleanerTally t)
    {
        lock (_gate)
        {
            var d = _data[category.Id];
            d.LowerBound = false;
            d.Files = t.Files; d.InUse = t.InUse;
            d.Skipped = t.SkippedApps.Count > 0 ? [.. t.SkippedApps] : null;
            if (t.Cancelled) { d.State = CleanerRowState.Cancelled; d.Bytes = t.Bytes; }
            else if (t.Found == 0) { d.State = t.SkippedApps.Count > 0 ? CleanerRowState.InUse : CleanerRowState.Unavailable; d.Bytes = null; }
            else { d.State = CleanerRowState.Cleaned; d.Bytes = t.Bytes; }
        }
    }

    private void MarkCancelled(string id)
    {
        lock (_gate) { var d = _data[id]; d.State = CleanerRowState.Cancelled; d.Bytes ??= 0; }
    }

    /// <summary>The confirm was cancelled, or its page closed: the helper session the measure left open ends, with nothing deleted.</summary>
    public async Task CancelPlanAsync() => await EndSessionAsync();

    private async Task EndSessionAsync()
    {
        if (_admin is null) return;
        try { await _admin.EndSessionAsync(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or OperationCanceledException) { }
    }

    private Data Copy(string id)
    {
        lock (_gate)
        {
            var d = _data[id];
            return new Data { State = d.State, Bytes = d.Bytes, Files = d.Files, InUse = d.InUse, LowerBound = d.LowerBound, FromEarlier = d.FromEarlier, Skipped = d.Skipped };
        }
    }

    /// <summary>What the list opens with next time: measured sizes, and 0 for a category a clean emptied (one with files in use is unknown until measured).</summary>
    private void RememberSizes()
    {
        Dictionary<string, CleanerSavedSize> sizes = [];
        lock (_gate)
            foreach (var (id, d) in _data)
            {
                if (d.State == CleanerRowState.Measured && d.Bytes is { } bytes) sizes[id] = new(bytes, d.LowerBound);
                else if (d.State == CleanerRowState.Cleaned && d.InUse == 0) sizes[id] = new(0, false);
            }
        if (sizes.Count == _settings.Sizes.Count && sizes.All(p => _settings.Sizes.TryGetValue(p.Key, out var o) && o == p.Value)) return;
        var next = _settings with { Sizes = sizes };
        if (TrySave(next)) _settings = next;
    }

    private bool TrySave(CleanerSettings next)
    {
        if (_save is null) return true;
        try { _save(next); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return false; }
    }

    private CleanerRow[] Snapshot()
    {
        lock (_gate)
            return [.. CleanerCatalog.All.Select(c =>
            {
                var d = _data[c.Id];
                return new CleanerRow(c, IsTicked(c.Id), d.State, d.Bytes, d.Files, d.InUse, d.LowerBound, d.FromEarlier, d.Skipped);
            })];
    }

    private void Republish()
    {
        _rows = Snapshot();
        Changed?.Invoke();
    }

    /// <summary>For the helper's progress, which arrives on its own thread: the event is raised on the context the work started on.</summary>
    private void RepublishFromAnyThread()
    {
        _rows = Snapshot();
        if (_ui is { } ui && SynchronizationContext.Current != ui) ui.Post(_ => Changed?.Invoke(), null);
        else Changed?.Invoke();
    }

    /// <summary>Names of the processes running now (lower case, no ".exe"); read off the UI thread.</summary>
    private static IReadOnlySet<string> RunningProcesses()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try { names.Add(process.ProcessName); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        return names;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}

/// <summary>A category finished during a clean.</summary>
public sealed record CleanerRunProgress(string Id, long Bytes);
