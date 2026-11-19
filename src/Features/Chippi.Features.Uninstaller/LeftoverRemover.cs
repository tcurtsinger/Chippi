// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using Chippi.Core.Admin;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Uninstaller;

public enum LeftoverState { Removed, Kept, InUse, Failed, NeedsAdmin, Cancelled }

/// <summary>"Removed", the reason for Kept ("Kept: shared with Thunderbird"), "In use", "Remove failed", "Needs admin", "Cancelled".</summary>
public sealed record LeftoverResult(LeftoverItem Item, LeftoverState State, string? Reason = null)
{
    public string Text => State switch
    {
        LeftoverState.Removed => "Removed",
        LeftoverState.Kept => Reason ?? "Kept",
        LeftoverState.InUse => "In use",
        LeftoverState.NeedsAdmin => "Needs admin",
        LeftoverState.Cancelled => "Cancelled",
        _ => "Remove failed",
    };

    public bool Ok => State == LeftoverState.Removed;
}

/// <summary>What a removal did. <see cref="BackupFolder"/> is where the .reg backups went, or null when no key was touched.</summary>
public sealed record LeftoverRun(IReadOnlyList<LeftoverResult> Results, string? BackupFolder)
{
    public int Removed => Results.Count(r => r.Ok);
    public int NotRemoved => Results.Count(r => !r.Ok);

    /// <summary>
    /// This run with a later one (a retry of what failed) laid over it: an item the later run did again takes its new result in its place, and the
    /// backup folder the first run named stays (else the later one's). What the first run removed stays removed.
    /// </summary>
    public LeftoverRun Merge(LeftoverRun later)
    {
        var again = later.Results.ToDictionary(r => r.Item.Id);
        var merged = Results.Select(r => again.Remove(r.Item.Id, out var fresh) ? fresh : r).Concat(again.Values).ToList();
        return new LeftoverRun(merged, BackupFolder ?? later.BackupFolder);   // the first named folder stays: Chippi's own before the helper's
    }
}

/// <summary>
/// Removes the ticked leftovers. Items under Program Files, ProgramData and HKLM go to the admin helper with the id of its record, which works the
/// ownership rule out again itself; what it returns as Yours, and every other item, is removed here as the standard user. Files and folders go to
/// the Recycle Bin (never deleted for good: one that can't be recycled stays and says "Remove failed"); a link inside is removed as a link; a key
/// is exported with reg.exe before it is deleted, and a failed export keeps the key. A Shared item is never removed. Nothing is removed that wasn't ticked.
/// </summary>
public sealed class LeftoverRemover(UninstallerSystem system, IAdminHelper? admin, string backupFolder, TimeProvider? time = null, ILogger? logger = null)
{
    private static readonly TimeSpan ExportLimit = TimeSpan.FromMinutes(2);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Where the admin helper puts its .reg exports (NativeLeftovers.cs): the owner's local app data, fixed in the helper.</summary>
    public static string HelperBackupFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi", "leftovers-backup");

    public async Task<LeftoverRun> RemoveAsync(LeftoverScan scan, IReadOnlyCollection<string> ticked, IProgress<LeftoverResult>? progress = null, CancellationToken ct = default)
    {
        var results = new Dictionary<string, LeftoverResult>();
        var selected = scan.Items.Where(i => ticked.Contains(i.Id)).ToList();
        void Done(LeftoverResult r) { results[r.Item.Id] = r; progress?.Report(r); }

        var standard = new List<LeftoverItem>();
        var helper = new List<LeftoverItem>();
        foreach (var item in selected)
        {
            if (item.Class == LeftoverClass.Shared) Done(new(item, LeftoverState.Kept, "Kept: shared with " + item.SharedWith));
            else if (item.NeedsAdmin && scan.Record.HelperRecordId is not null && admin is not null) helper.Add(item);
            else if (item.NeedsAdmin && scan.Record.Source is AppSource.Machine64 or AppSource.Machine32 && scan.Record.HelperRecordId is null && item.Kind == LeftoverKind.Key && item.Hive != RegHive.User)
                Done(new(item, LeftoverState.Kept, "Kept: no uninstall record"));   // a machine key is never touched without the helper's record
            else standard.Add(item);
        }

        bool helperKey = false;
        if (helper.Count > 0)
        {
            var sent = helper.Select(ToAdmin).ToArray();
            IReadOnlyList<AdminLeftoverResult> answers;
            try { answers = await admin!.DeleteLeftoversAsync(scan.Record.HelperRecordId!.Value, sent, ct); }
            catch (OperationCanceledException) { answers = [.. sent.Select(s => new AdminLeftoverResult(s, AdminLeftoverState.Cancelled))]; }
            for (int i = 0; i < helper.Count; i++)
            {
                var answer = i < answers.Count ? answers[i] : new AdminLeftoverResult(sent[i], AdminLeftoverState.Failed);
                switch (answer.State)
                {
                    case AdminLeftoverState.Yours: standard.Add(helper[i]); break;
                    case AdminLeftoverState.Removed: Done(new(helper[i], LeftoverState.Removed)); helperKey |= helper[i].Kind == LeftoverKind.Key; break;
                    case AdminLeftoverState.Kept: Done(new(helper[i], LeftoverState.Kept, answer.Text)); break;
                    case AdminLeftoverState.InUse: Done(new(helper[i], LeftoverState.InUse)); break;
                    case AdminLeftoverState.NeedsAdmin: Done(new(helper[i], LeftoverState.NeedsAdmin)); break;
                    case AdminLeftoverState.Cancelled: Done(new(helper[i], LeftoverState.Cancelled)); break;
                    default: Done(new(helper[i], LeftoverState.Failed)); break;
                }
            }
        }

        // Files, folders, the Recycle Bin and reg.exe run on one worker of their own (STA, as the Shell wants), never on the caller's thread: a large
        // folder must not hold the window, and its progress must be able to paint. Each result reaches the caller through the progress it captured.
        string? usedBackup = standard.Count == 0 ? null : await OnWorker(() =>
        {
            string? used = null;
            int backups = 0;
            foreach (var item in standard)
            {
                if (ct.IsCancellationRequested) { Done(new(item, LeftoverState.Cancelled)); continue; }
                LeftoverResult result;
                try
                {
                    result = item.Kind == LeftoverKind.Key ? RemoveKeyAsync(scan.Record.Name, item, () => ++backups, ct).GetAwaiter().GetResult() : RemovePath(item);
                    if (item.Kind == LeftoverKind.Key && result.Ok && backups > 0) used = backupFolder;
                }
                catch (OperationCanceledException) { result = new(item, LeftoverState.Cancelled); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { result = new(item, LeftoverState.Failed); }
                Done(result);
            }
            return used;
        });
        var ordered = selected.Select(i => results[i.Id]).ToList();
        logger?.LogInformation("Removed leftovers of {App}: {Removed} of {Count}", scan.Record.Name, ordered.Count(r => r.Ok), ordered.Count);
        // The helper exports a machine key to its own fixed folder before deleting it; the results name that folder when Chippi made no backup itself.
        return new LeftoverRun(ordered, usedBackup ?? (helperKey ? HelperBackupFolder : null));
    }

    private static Task<T> OnWorker<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "Leftover removal" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
    private static AdminLeftoverItem ToAdmin(LeftoverItem item) => new(
        item.Kind switch { LeftoverKind.File => AdminLeftoverKind.File, LeftoverKind.Folder => AdminLeftoverKind.Folder, _ => AdminLeftoverKind.RegistryKey },
        item.Kind == LeftoverKind.Key ? item.KeyPath! : item.Path,
        item.Class == LeftoverClass.Safe ? AdminLeftoverClass.Safe : AdminLeftoverClass.CheckFirst, true,
        item.Kind == LeftoverKind.Key ? (item.Hive == RegHive.Machine32 ? AdminRegistryView.Registry32 : AdminRegistryView.Registry64) : null);

    private LeftoverResult RemovePath(LeftoverItem item)
    {
        string path = item.Path;
        bool isDirectory = Directory.Exists(path), isFile = File.Exists(path), link = IsLink(path);
        if (!isDirectory && !isFile && !link) return new(item, LeftoverState.Removed);   // already gone
        if (link)
        {
            // A link is removed as a link: its target is never touched.
            if (File.GetAttributes(path).HasFlag(FileAttributes.Directory)) Directory.Delete(path, recursive: false); else File.Delete(path);
            return new(item, LeftoverState.Removed);
        }
        if (item.NoRecycleBin || (item.Bytes is { } bytes && !system.Recycler.CanRecycle(path, bytes))) return new(item, LeftoverState.Failed);
        if (isDirectory) RemoveInnerLinks(path);
        return system.Recycler.Recycle(path) switch
        {
            RecycleResult.Recycled => new(item, LeftoverState.Removed),
            RecycleResult.InUse => new(item, LeftoverState.InUse),
            _ => new(item, LeftoverState.Failed),
        };
    }

    /// <summary>A link that is really there. A path that is gone is not one (its attributes read -1, every flag set).</summary>
    internal static bool IsLink(string path)
    {
        try { return File.GetAttributes(path) is var a && a != (FileAttributes)(-1) && a.HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }
    /// <summary>The Recycle Bin must never follow a junction out of the folder: each link inside is removed as itself first.</summary>
    private static void RemoveInnerLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    if (entry is DirectoryInfo) Directory.Delete(entry.FullName, recursive: false); else File.Delete(entry.FullName);
                }
                else if (entry is DirectoryInfo sub) pending.Push(sub.FullName);
            }
        }
    }

    private async Task<LeftoverResult> RemoveKeyAsync(string appName, LeftoverItem item, Func<int> nextBackup, CancellationToken ct)
    {
        var hive = item.Hive!.Value;
        string path = item.KeyPath!;
        if (!system.LeftoverRegistry.Exists(hive, path)) return new(item, LeftoverState.Removed);
        string folder = backupFolder;
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, Safe(appName) + "-" + _time.GetLocalNow().ToString("yyyy-MM-dd") + "-" + nextBackup() + ".reg");
        string reg = Path.Combine(Environment.ExpandEnvironmentVariables("%SystemRoot%"), "System32", "reg.exe");
        string root = hive == RegHive.User ? "HKCU" : "HKLM";
        string view = hive switch { RegHive.Machine32 => " /reg:32", RegHive.Machine64 => " /reg:64", _ => "" };
        int? exit = await system.UserProcess.RunAsync(reg, $"export \"{root}\\{path}\" \"{file}\" /y{view}", ExportLimit, ct).ConfigureAwait(false);
        if (exit != 0 || !File.Exists(file)) return new(item, LeftoverState.Failed);   // no backup, no delete
        return system.LeftoverRegistry.DeleteTree(hive, path) ? new(item, LeftoverState.Removed) : new(item, LeftoverState.Failed);
    }

    private static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        string text = new([.. name.Select(c => bad.Contains(c) ? '-' : c)]);
        text = text.Trim().Trim('.');
        return text.Length == 0 ? "Uninstaller" : text.Length > 60 ? text[..60] : text;
    }
}
