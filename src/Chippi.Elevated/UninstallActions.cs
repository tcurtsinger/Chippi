// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated;

/// <summary>
/// What an HKLM Uninstall key says, read by name. IsLink: the key is a registry link. The other values are null when missing or not of the
/// expected kind. Nothing here is trusted until UninstallActions has checked it.
/// </summary>
public sealed record UninstallKey(bool IsLink, int? SystemComponent, string? ParentKeyName, int? WindowsInstaller, string? DisplayName, string? Publisher,
    string? InstallLocation, string? QuietUninstallString, string? UninstallString);

public interface IUninstallRegistry
{
    /// <summary>The key under HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall in the view (64 or 32), opened without following a link; null when it isn't there.</summary>
    UninstallKey? Read(int view, string keyName);
    bool Exists(int view, string keyName);
}

/// <summary>
/// The helper's identity for an app it uninstalled, kept under HKLM so no program running as the owner can plant or edit one. The next stage
/// (DeleteLeftovers) reads it back by id.
/// </summary>
public sealed record UninstallRecord(Guid Id, int View, string KeyName, string? DisplayName, string? Publisher, string? InstallLocation,
    string? ProductCode, string? UninstallerFolder, DateTime CreatedUtc);

public interface IRecordStore
{
    /// <summary>
    /// Creates HKLM\Software\Chippi\Elevated\UninstallRecords (64-bit view) with a protected DACL, Administrators and SYSTEM only, if it is missing.
    /// False when the key is a registry link, when the standard token could write it, or when an owner other than Administrators or SYSTEM holds it: nothing is then stored or read.
    /// </summary>
    bool Prepare();
    /// <summary>Every record, in any order. One that can't be read comes back with <see cref="DateTime.MinValue"/> as its time, so it expires.</summary>
    IReadOnlyList<UninstallRecord> List();
    /// <summary>The record under this id, or null; after a successful Prepare.</summary>
    UninstallRecord? Get(Guid id);
    void Write(UninstallRecord record);
    void Delete(Guid id);
}

public interface ILaunchedJob : IDisposable
{
    /// <summary>Waits until the job has no processes left, so an uninstaller that copies itself and restarts is still waited for; the exit code is the first process's.</summary>
    Task<int> WaitAsync(CancellationToken ct);
}

public interface IProcessLauncher
{
    /// <summary>
    /// Starts the application by its explicit name (CreateProcess's application name, never a search) in a job object, and returns once it has
    /// started. Throws when it can't.
    /// </summary>
    ILaunchedJob Start(string application, string commandLine, string workingDirectory);
}

public interface IUninstallHost
{
    string Expand(string text);
    bool FileExists(string path);
    /// <summary>The Windows System32 folder, where msiexec.exe lives.</summary>
    string SystemDirectory { get; }
}

public interface IUninstallActions { Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct); }

/// <summary>
/// Uninstall(view, keyName), as admin-helper.md specifies: the helper reads the key itself, builds or parses the command itself, checks the file
/// it would run, writes its record, runs it in a job and waits. Chippi sends only a view and a key name.
/// </summary>
public sealed class UninstallActions(IUninstallRegistry registry, IRecordStore records, IFsProbe probe, IProcessLauncher launcher, IUninstallHost host,
    TimeProvider? time = null) : IUninstallActions
{
    public static readonly TimeSpan RecordLifetime = TimeSpan.FromDays(30);
    public const int MaxRecords = 50;
    public const string FailedReason = "Admin helper failed", NotFoundReason = "Not found", LinkReason = "Registry link", SystemComponentReason = "System component",
        UpdateReason = "Update entry", NoCommandReason = "No uninstaller", CommandReason = "Unsafe command", RecordsReason = "Records unavailable",
        RecordFailedReason = "Record failed", StartFailedReason = "Start failed";
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct) => Task.Run(async () =>
    {
        try { return await RunCoreAsync(request, ct).ConfigureAwait(false); }
        catch (Exception)
        {
            // Any failure answers the request: an exception must never end the helper's session. Nothing from it is logged or sent.
            return new AdminReply(request.Id, Messages.UninstallFailed, Reason: FailedReason);
        }
    }, CancellationToken.None);

    /// <summary>Deletes records past their life and the oldest beyond the limit. Called each time the helper starts and before each new record.</summary>
    public void Maintain(int room = 0)
    {
        if (!records.Prepare()) return;
        var now = _time.GetUtcNow().UtcDateTime;
        var live = new List<UninstallRecord>();
        foreach (var record in records.List())
        {
            if (now - record.CreatedUtc > RecordLifetime) records.Delete(record.Id);
            else live.Add(record);
        }
        int excess = live.Count + room - MaxRecords;
        foreach (var oldest in live.OrderBy(r => r.CreatedUtc).Take(Math.Max(0, excess))) records.Delete(oldest.Id);
    }

    private async Task<AdminReply> RunCoreAsync(AdminRequest r, CancellationToken ct)
    {
        if (!Messages.RequestAllowed(r) || !Messages.IsUninstallVerb(r.Verb)) return Refuse(r, "Invalid request");
        int view = r.View!.Value;
        string keyName = r.KeyName!;
        var key = registry.Read(view, keyName);
        if (key is null) return Refuse(r, NotFoundReason);
        if (key.IsLink) return Refuse(r, LinkReason);
        if (key.SystemComponent == 1) return Refuse(r, SystemComponentReason);
        if (!string.IsNullOrEmpty(key.ParentKeyName)) return Refuse(r, UpdateReason);

        string application, commandLine;
        string? productCode = null;
        if (key.WindowsInstaller == 1 && UninstallCommand.IsProductCode(keyName))
        {
            productCode = keyName;
            application = Path.Combine(host.SystemDirectory, "msiexec.exe");
            commandLine = $"\"{application}\" /x {productCode} /passive /norestart";
        }
        else
        {
            string? text = string.IsNullOrWhiteSpace(key.QuietUninstallString) ? key.UninstallString : key.QuietUninstallString;
            if (string.IsNullOrWhiteSpace(text)) return Refuse(r, NoCommandReason);
            var parsed = UninstallCommand.Parse(text, host.Expand, host.FileExists);
            if (parsed is null) return Refuse(r, CommandReason);
            (application, commandLine) = parsed.Value;
        }

        ExecutionCheck check;
        try { check = ExecutionCheck.Open(application, probe); }
        catch (ExecutionRefusedException refusal) { return Refuse(r, refusal.Message); }
        using (check)
        {
            if (!records.Prepare()) return Refuse(r, RecordsReason);
            // The record comes first: the uninstaller usually deletes the key, so this is the only trusted identity left.
            var record = new UninstallRecord(Guid.NewGuid(), view, keyName, key.DisplayName, key.Publisher, key.InstallLocation, productCode,
                productCode is null ? Path.GetDirectoryName(application) : null, _time.GetUtcNow().UtcDateTime);
            try { Maintain(room: 1); records.Write(record); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
            { return new AdminReply(r.Id, Messages.UninstallFailed, Reason: RecordFailedReason); }

            ILaunchedJob job;
            try { job = launcher.Start(application, commandLine, Path.GetDirectoryName(application)!); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
            { return new AdminReply(r.Id, Messages.UninstallFailed, Reason: StartFailedReason, RecordId: record.Id, KeyGone: !registry.Exists(view, keyName)); }
            using (job)
            {
                // The file and its folders were held until the process started; from here nothing is left to swap.
                check.Dispose();
                int exit = await job.WaitAsync(ct).ConfigureAwait(false);
                bool gone = !registry.Exists(view, keyName);
                string state = exit == 0 ? Messages.UninstallDone : exit == Messages.RestartExitCode ? Messages.UninstallRestart : Messages.UninstallFailed;
                return new AdminReply(r.Id, state, ExitCode: exit, RecordId: record.Id, KeyGone: gone);
            }
        }
    }

    private static AdminReply Refuse(AdminRequest r, string reason) => new(r.Id, Messages.UninstallRefused, Reason: reason);
}
