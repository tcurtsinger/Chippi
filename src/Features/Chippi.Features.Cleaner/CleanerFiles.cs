// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.IO.Enumeration;

namespace Chippi.Features.Cleaner;

/// <summary>What one pass over a category's targets found or removed.</summary>
internal sealed class CleanerTally
{
    public long Bytes;
    public int Files, InUse, Removed;
    /// <summary>Targets that existed and could be read.</summary>
    public int Found;
    /// <summary>Targets that exist but couldn't be listed (access denied).</summary>
    public int Unreadable;
    public bool Cancelled;
    public readonly List<string> SkippedApps = [];
}

/// <summary>
/// Measures and deletes by the rule, as the standard user. Measure never changes anything and counts exactly what a clean would delete. The
/// day rule is last write and creation both over a day old (the helper's rule). A file something holds open is skipped and counted, never
/// forced. No reparse point is followed: one inside a target is removed as a link; a target that is a link counts as missing. Deleted for good.
/// </summary>
internal static class CleanerFiles
{
    private const int MaxDepth = 64;
    public static readonly TimeSpan MinAge = TimeSpan.FromDays(1);
    private static readonly EnumerationOptions All = new() { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
    private static readonly EnumerationOptions Strict = new() { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false, ReturnSpecialDirectories = false };

    /// <summary>A target whose app is running is skipped (named in <see cref="CleanerTally.SkippedApps"/>); every other target is walked.</summary>
    public static CleanerTally Run(IEnumerable<CleanerTarget> targets, bool clean, DateTime now, IReadOnlySet<string> running, CancellationToken ct)
    {
        var tally = new CleanerTally();
        var cutoff = now - MinAge;
        foreach (var target in targets)
        {
            if (ct.IsCancellationRequested) { tally.Cancelled = true; break; }
            DirectoryInfo root;
            try
            {
                root = new DirectoryInfo(target.Path);
                if (!root.Exists || root.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { tally.Unreadable++; continue; }
            if (target.Processes is { } processes && processes.Any(running.Contains))
            {
                if (target.App is { } app && !tally.SkippedApps.Contains(app)) tally.SkippedApps.Add(app);
                continue;
            }
            tally.Found++;
            bool ok = target.Kind == TargetKind.NamedFiles ? Named(root, target, clean, cutoff, tally, ct) : Walk(root, target, clean, cutoff, tally, 0, ct, strict: true);
            if (!ok && !tally.Cancelled) { tally.Found--; tally.Unreadable++; }
            if (tally.Cancelled) break;
        }
        return tally;
    }

    private static bool Named(DirectoryInfo dir, CleanerTarget target, bool clean, DateTime cutoff, CleanerTally tally, CancellationToken ct)
    {
        FileSystemInfo[] entries;
        try { entries = dir.EnumerateFileSystemInfos("*", Strict).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested) { tally.Cancelled = true; return true; }
            if (entry.Attributes.HasFlag(FileAttributes.Directory)) continue;
            if (!target.Patterns!.Any(p => Matches(entry.Name, p))) continue;
            Visit(entry, target.AgeRule, clean, cutoff, tally);
        }
        return true;
    }

    private static bool Matches(string name, string pattern) => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true);

    /// <summary>False only when the target's own folder can't be listed. Cancelled sets the tally's flag.</summary>
    private static bool Walk(DirectoryInfo dir, CleanerTarget target, bool clean, DateTime cutoff, CleanerTally tally, int depth, CancellationToken ct, bool strict = false)
    {
        if (depth > MaxDepth) return true;
        FileSystemInfo[] entries;
        try { entries = dir.EnumerateFileSystemInfos("*", strict ? Strict : All).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return !strict; }
        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested) { tally.Cancelled = true; return true; }
            if (CleanerTargets.IsProtected(entry.Name)) continue;
            bool link = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
            if (entry.Attributes.HasFlag(FileAttributes.Directory) && !link)
            {
                int before = tally.Removed;
                Walk((DirectoryInfo)entry, target, clean, cutoff, tally, depth + 1, ct);
                if (tally.Cancelled) return true;
                if (clean && tally.Removed > before) RemoveIfEmpty((DirectoryInfo)entry);
            }
            else Visit(entry, target.AgeRule, clean, cutoff, tally);
        }
        return true;
    }

    /// <summary>A file, or a link (never followed): a link counts for nothing but is removed as itself when it is selected.</summary>
    private static void Visit(FileSystemInfo entry, bool ageRule, bool clean, DateTime cutoff, CleanerTally tally)
    {
        if (ageRule && !Old(entry, cutoff)) return;
        bool link = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
        long size = 0;
        if (!link && entry is FileInfo file)
        {
            try { size = file.Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        }
        if (!clean)
        {
            if (!link) { tally.Bytes += size; tally.Files++; }
            return;
        }
        try
        {
            if (entry.Attributes.HasFlag(FileAttributes.Directory)) Directory.Delete(entry.FullName, recursive: false);   // the link itself, never its target
            else
            {
                if (!link && entry.Attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(entry.FullName, entry.Attributes & ~FileAttributes.ReadOnly);   // never through a link: it would change the target
                File.Delete(entry.FullName);
            }
            tally.Removed++;
            if (!link) { tally.Bytes += size; tally.Files++; }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!link) tally.InUse++;   // held open or denied: skipped and counted, never forced
        }
    }

    private static bool Old(FileSystemInfo entry, DateTime cutoff)
    {
        try { return entry.LastWriteTimeUtc < cutoff && entry.CreationTimeUtc < cutoff; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>A subfolder this clean emptied goes too (never a target's own folder, never one that was already empty).</summary>
    private static void RemoveIfEmpty(DirectoryInfo dir)
    {
        try
        {
            if (dir.EnumerateFileSystemInfos("*", All).Any()) return;
            Directory.Delete(dir.FullName, recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
