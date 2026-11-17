// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Chippi.Core.Admin;

namespace Chippi.Features.AppUpdates;

/// <summary>Discovery is read-only; RunAsync accepts only the exact snapshot the caller has already confirmed.</summary>
public sealed class AppUpdatesEngine
{
    private readonly IPackageSource _source;
    private readonly IAdminHelper? _admin;
    private readonly IAppUpdatesStore _store;
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private Task? _check;
    private Task? _run;
    private IReadOnlyList<PackageUpdate> _all = [];
    private readonly HashSet<VersionIdentity> _completedVersions = [];
    private readonly HashSet<string> _finishedBatch = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadFailed;
    private int _batchRevision;

    public AppUpdatesEngine(IPackageSource source, IAdminHelper? admin, IAppUpdatesStore store,
        Action<Action>? dispatch = null, TimeProvider? time = null)
    {
        _source = source; _admin = admin; _store = store;
        _dispatch = dispatch ?? (action => action()); _time = time ?? TimeProvider.System;
        try { Preferences = Normalize(store.Load()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { _loadFailed = true; Error = "Settings read failed"; }
    }

    public AppUpdatesPreferences Preferences { get; private set; } = new();
    public IReadOnlyList<PackageUpdate> Updates { get; private set; } = [];
    public IReadOnlyList<UpdateRow> Rows { get; private set; } = [];
    public IReadOnlyList<UpdateRow> BatchRows { get; private set; } = [];
    public bool IsChecking { get; private set; }
    public bool IsRunning { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? LastChecked { get; private set; }
    public int Completed => BatchRows.Count(r => r.State is UpdateState.Updated or UpdateState.RestartRequired);
    public int Total { get; private set; }
    public event Action? Changed;

    public Task CheckAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (IsRunning) return Task.CompletedTask;
            if (_check is { IsCompleted: false }) return _check;
            return _check = CheckCoreAsync(ct);
        }
    }

    private async Task CheckCoreAsync(CancellationToken ct)
    {
        int revision = 0;
        Change(() => { revision = _batchRevision; IsChecking = true; Error = _loadFailed ? "Settings read failed" : null; });
        try
        {
            // Cache metadata for unknown versions too; opting in only changes the visible, confirmable set.
            var found = await _source.CheckAsync(includeUnknown: true, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            Change(() =>
            {
                _all = Array.AsReadOnly(found.Where(p => !IsChippi(p)).DistinctBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
                LastChecked = _time.GetUtcNow();
                if (!_loadFailed) Error = (_source as IPackageSourceStatus)?.Warning;
                if (!IsRunning && revision == _batchRevision) RefreshRows(resetBatch: true);
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or System.Runtime.InteropServices.COMException or InvalidCastException or DllNotFoundException or TypeInitializationException)
        { Change(() => Error = ex is WingetUnavailableException ? "App updates need winget" : "Check failed"); }
        finally { Change(() => IsChecking = false); }
    }

    /// <summary>User/Store operations precede the machine group so one helper session preserves the reviewed order.</summary>
    public UpdateBatch CaptureBatch(IEnumerable<PackageUpdate>? packages = null)
    {
        if (IsRunning) return new(Array.Empty<PackageUpdate>());
        var selected = (packages ?? Updates).ToArray();
        var known = Updates.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
        var exact = selected.Where(p => known.TryGetValue(p.Key, out var current) && SameVersion(p, current))
            .DistinctBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => RequiresAdmin(p) ? 1 : 0).Take(256).ToArray();
        return new(Array.AsReadOnly(exact));
    }

    public Task RunAsync(UpdateBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (_gate)
        {
            if (_run is { IsCompleted: false }) return _run;
            var packages = batch.Packages.ToArray();
            if (packages.Length is 0 or > 256 || packages.Any(IsChippi) || packages.DistinctBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Count() != packages.Length)
                return Task.CompletedTask;
            return _run = RunCoreAsync(Array.AsReadOnly(packages), ct);
        }
    }

    private async Task RunCoreAsync(IReadOnlyList<PackageUpdate> packages, CancellationToken ct)
    {
        int revision = 0;
        Change(() =>
        {
            revision = ++_batchRevision; _finishedBatch.Clear(); IsRunning = true; Total = packages.Count; Error = null;
            BatchRows = Array.AsReadOnly(packages.Select(p => new UpdateRow(p, UpdateState.Queued)).ToArray());
            var selected = BatchRows.ToDictionary(r => r.Package.Key, StringComparer.OrdinalIgnoreCase);
            var rows = Rows.Select(row => selected.Remove(row.Package.Key, out var replacement) ? replacement : row).ToList();
            rows.AddRange(selected.Values);
            Rows = rows.AsReadOnly();
        });
        try
        {
            foreach (var package in packages.Where(p => !RequiresAdmin(p)))
            {
                if (ct.IsCancellationRequested) { Finish(package, new(UpdateState.Cancelled), revision); continue; }
                SetRow(package, UpdateState.Updating, revision);
                try
                {
                    var progress = new CallbackProgress<PackageInstallProgress>(p => SetRow(package, UpdateState.Updating, revision, p.Fraction, p.Stage));
                    // Cancellation belongs to the queue, not an installer that is already running.
                    var result = await _source.UpdateAsync(package, progress, ct).ConfigureAwait(false);
                    Finish(package, result, revision);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { Finish(package, new(UpdateState.Cancelled), revision); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or System.Runtime.InteropServices.COMException or InvalidCastException or DllNotFoundException or TypeInitializationException)
                { Finish(package, new(UpdateState.Failed, "Update failed"), revision); }
            }

            var machine = packages.Where(RequiresAdmin).ToArray();
            var supported = new List<PackageUpdate>();
            foreach (var package in machine)
            {
                if (ct.IsCancellationRequested) Finish(package, new(UpdateState.Cancelled), revision);
                else if (!package.Source.Equals("winget", StringComparison.OrdinalIgnoreCase)) Finish(package, new(UpdateState.Failed, "Update source isn’t supported for admin updates"), revision);
                else supported.Add(package);
            }
            if (supported.Count == 0) return;
            if (_admin is null)
            {
                foreach (var package in supported) Finish(package, new(UpdateState.NeedsAdmin, "Needs admin"), revision);
                return;
            }
            // Ids and versions the helper protocol would refuse fail here, one row each, instead of sinking the batch.
            foreach (var package in supported.Where(p => !AdminCanRun(p)).ToArray())
            {
                Finish(package, new(UpdateState.Failed, "Update failed"), revision);
                supported.Remove(package);
            }
            if (supported.Count == 0) return;
            var byId = supported.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            var adminProgress = new CallbackProgress<AdminUpdateProgress>(p =>
            {
                if (!byId.TryGetValue(p.PackageId, out var package)) return;
                if (p.Result is { } result) Finish(package, MapAdminResult(result), revision);
                else SetRow(package, UpdateState.Updating, revision);
            });
            var results = await _admin.UpdatePackagesAsync(supported.Select(p => new AdminUpdateItem(p.Id, p.AvailableVersion, p.UnknownVersion)).ToArray(), adminProgress, ct).ConfigureAwait(false);
            foreach (var package in supported)
            {
                var result = results.FirstOrDefault(r => r.PackageId.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
                Finish(package, result is null ? new(UpdateState.Failed, "Admin helper failed") : MapAdminResult(result), revision);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { FinishUnresolved(UpdateState.Cancelled, null, revision); }
        catch (Exception)
        { FinishUnresolved(UpdateState.Failed, "Admin helper failed", revision); }
        finally { Change(() => IsRunning = false); }
    }

    public bool Skip(PackageUpdate package, bool always) => Save(Preferences with
    { Skipped = [.. Preferences.Skipped.Where(s => !s.Key.Equals(package.Key, StringComparison.OrdinalIgnoreCase)), new(package.Key, package.Id, package.Name, always ? null : package.AvailableVersion)] });

    public bool Restore(SkippedUpdate skipped) => Save(Preferences with
    { Skipped = Preferences.Skipped.Where(s => s != skipped).ToArray() });

    public bool SetShowUnknownVersions(bool value) => Save(Preferences with { ShowUnknownVersions = value });

    private bool Save(AppUpdatesPreferences preferences)
    {
        bool saved = false;
        Change(() =>
        {
            if (_loadFailed) { Error = "Settings read failed"; return; }
            try
            {
                var next = Normalize(preferences); _store.Save(next);
                Preferences = next; Error = null; if (!IsRunning) RefreshRows(); saved = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            { Error = "Save failed"; }
        });
        return saved;
    }

    private IEnumerable<PackageUpdate> VisiblePackages() => _all.Where(p => (Preferences.ShowUnknownVersions || !p.UnknownVersion) && !Preferences.Skipped.Any(s =>
        s.Key.Equals(p.Key, StringComparison.OrdinalIgnoreCase) && (s.Version is null || s.Version == p.AvailableVersion)));

    private void RefreshUpdates() => Updates = Array.AsReadOnly(VisiblePackages().Where(p => !_completedVersions.Contains(Identity(p))).ToArray());

    private void RefreshRows(bool resetBatch = false)
    {
        RefreshUpdates();
        var existing = Rows.ToDictionary(r => r.Package.Key, StringComparer.OrdinalIgnoreCase);
        Rows = Array.AsReadOnly(VisiblePackages()
            .Where(p => !_completedVersions.Contains(Identity(p)) || (!resetBatch
                && existing.TryGetValue(p.Key, out var completed) && SameVersion(completed.Package, p)
                && completed.State is UpdateState.Updated or UpdateState.RestartRequired))
            .Select(p => !resetBatch && existing.TryGetValue(p.Key, out var row) && SameVersion(row.Package, p) ? row : new UpdateRow(p)).ToArray());
        if (resetBatch) { BatchRows = []; Total = 0; }
    }

    private void Finish(PackageUpdate package, PackageInstallResult result, int revision)
    {
        Change(() =>
        {
            if (revision != _batchRevision || !IsRunning || !_finishedBatch.Add(package.Key)) return;
            ApplyRow(package, result.State, reason: result.Reason);
            if (result.State is UpdateState.Updated or UpdateState.RestartRequired) _completedVersions.Add(Identity(package));
            RefreshUpdates();
            if (_loadFailed) return;
            // Serialize with Settings changes on the caller's dispatcher; a background result must not overwrite a skip.
            var entry = new UpdateHistoryEntry(package.Id, package.Name, package.InstalledVersion, package.AvailableVersion, _time.GetUtcNow(), result.State, result.Reason);
            var next = Preferences with { History = Preferences.History.Prepend(entry).Take(50).ToArray() };
            Preferences = next;
            try { _store.Save(next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { Error = "History save failed"; } // A persistence failure does not undo a successful install.
        });
    }

    private void FinishUnresolved(UpdateState state, string? reason, int revision)
    {
        foreach (var row in BatchRows.Where(r => r.State is UpdateState.Queued or UpdateState.Updating).ToArray())
            Finish(row.Package, new(state, reason), revision);
    }

    private void SetRow(PackageUpdate package, UpdateState state, int revision, double? progress = null, string? reason = null) => Change(() =>
    {
        if (revision != _batchRevision || !IsRunning || _finishedBatch.Contains(package.Key)) return;
        ApplyRow(package, state, progress, reason);
    });

    private void ApplyRow(PackageUpdate package, UpdateState state, double? progress = null, string? reason = null)
    {
        UpdateRow Update(UpdateRow row) => SameVersion(row.Package, package)
            ? row with { State = state, Progress = progress is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : null, Reason = reason } : row;
        Rows = Array.AsReadOnly(Rows.Select(Update).ToArray());
        BatchRows = Array.AsReadOnly(BatchRows.Select(Update).ToArray());
    }

    private readonly record struct VersionIdentity(string Key, string Version, string Channel, UpdateScope Scope);
    private static VersionIdentity Identity(PackageUpdate package) => new(package.Key.ToUpperInvariant(), package.AvailableVersion, package.Channel, package.Scope);

    private void Change(Action action) => _dispatch(() => { action(); Changed?.Invoke(); });
    internal static bool RequiresAdmin(PackageUpdate package) => !package.IsStore && package.Scope != UpdateScope.User;
    internal static bool SameVersion(PackageUpdate first, PackageUpdate second) => first.Key.Equals(second.Key, StringComparison.OrdinalIgnoreCase)
        && first.InstalledVersion == second.InstalledVersion && first.AvailableVersion == second.AvailableVersion && first.Scope == second.Scope && first.Channel == second.Channel;
    internal static bool IsChippi(PackageUpdate package) => package.Id.Equals("Chippi", StringComparison.OrdinalIgnoreCase)
        || package.Id.Equals("ChippiApp", StringComparison.OrdinalIgnoreCase) || package.Id.Equals("tcurtsinger.Chippi", StringComparison.OrdinalIgnoreCase);
    internal static bool AdminCanRun(PackageUpdate package) => Chippi.Elevated.Protocol.Messages.PackageIdAllowed(package.Id)
        && Chippi.Elevated.Protocol.Messages.VersionAllowed(package.AvailableVersion);

    internal static PackageInstallResult MapAdminResult(AdminUpdateResult result) => result.State switch
    {
        "Done" => new(UpdateState.Updated, Code: result.ExitCode),
        "NeedsAdmin" => new(UpdateState.NeedsAdmin, "Needs admin", result.ExitCode),
        "Cancelled" => new(UpdateState.Cancelled),
        _ when result.ExitCode is 3010 or 1641 || result.ExitCode == unchecked((int)0x8A150109) || result.ExitCode == unchecked((int)0x8A15010B) => new(UpdateState.RestartRequired, "Restart to finish", result.ExitCode),
        _ when result.ExitCode == unchecked((int)0x8A15002B) => new(UpdateState.Skipped, "No update available", result.ExitCode),
        _ when result.ExitCode == unchecked((int)0x8A15010C) => new(UpdateState.Cancelled, "Update cancelled", result.ExitCode),
        _ => new(UpdateState.Failed, AdminFailureReason(result), result.ExitCode)
    };

    private static string AdminFailureReason(AdminUpdateResult result) => result.Reason is "Version changed" or "Package update timed out" ? result.Reason : AdminExitReason(result);

    private static string AdminExitReason(AdminUpdateResult result) => result.ExitCode switch
    {
        unchecked((int)0x8A150008) => "Download failed",
        unchecked((int)0x8A150011) => "Installer verification failed",
        unchecked((int)0x8A150014) => "App not found",
        unchecked((int)0x8A15003A) => "Update blocked by policy",
        unchecked((int)0x8A150045) => "Update source unavailable",
        unchecked((int)0x8A150101) => "App is in use",
        unchecked((int)0x8A150102) => "Another installation is in progress",
        unchecked((int)0x8A150103) => "A file is in use",
        unchecked((int)0x8A150105) => "Not enough disk space",
        unchecked((int)0x8A150107) => "Network unavailable",
        unchecked((int)0x8A15010A) => "Restart, then try the update again",
        { } code => $"Update failed · 0x{code:X8}",
        _ => result.Reason is "Admin helper failed" ? result.Reason : "Update failed"
    };

    private static AppUpdatesPreferences Normalize(AppUpdatesPreferences preferences) => preferences with
    {
        Skipped = (preferences.Skipped ?? []).Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Key)).DistinctBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Take(10000).ToArray(),
        History = (preferences.History ?? []).Where(h => h is not null).Take(50).ToArray()
    };

    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}

public sealed class WingetUnavailableException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
