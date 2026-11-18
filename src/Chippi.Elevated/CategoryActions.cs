// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Runtime.InteropServices;
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated;

/// <summary>One fixed location of a category: a folder whose old files go, or a single file.</summary>
public sealed record CategoryTarget(string Path, bool IsFile = false);

/// <summary>
/// The folders behind each category name. The shipped helper builds this only from <see cref="ForWindows"/>: nothing on the command line, in the
/// protocol or in the environment can change it. Tests pass their own map to <see cref="CategoryActions"/>'s constructor.
/// </summary>
public static class CategoryRoots
{
    public static IReadOnlyDictionary<string, IReadOnlyList<CategoryTarget>> ForWindows()
    {
        string windows = WindowsDirectory();
        string data = KnownFolder(CommonAppData);
        string P(string a, params string[] rest) => System.IO.Path.Combine([a, .. rest]);
        return new Dictionary<string, IReadOnlyList<CategoryTarget>>(StringComparer.Ordinal)
        {
            [Messages.WindowsTemp] = [new(P(windows, "Temp"))],
            [Messages.WindowsUpdateDownloads] = [new(P(windows, "SoftwareDistribution", "Download"))],
            [Messages.DeliveryOptimization] = [new(P(windows, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"))],
            [Messages.SystemCrashDumps] =
            [
                new(P(windows, "Minidump")), new(P(windows, "MEMORY.DMP"), IsFile: true),
                new(P(data, "Microsoft", "Windows", "WER", "ReportArchive")), new(P(data, "Microsoft", "Windows", "WER", "ReportQueue")),
            ],
        };
    }

    private static readonly Guid CommonAppData = new("62AB5D82-FDC1-4DC3-A9DD-070D1D495D97");

    /// <summary>The Windows directory from the API, never from %SystemRoot%, which the process that started the helper could have set.</summary>
    private static unsafe string WindowsDirectory()
    {
        char* buffer = stackalloc char[520];
        uint length = GetSystemWindowsDirectoryW(buffer, 520);
        if (length is 0 or >= 520) throw new InvalidOperationException("Windows directory unavailable.");
        return new string(buffer, 0, (int)length);
    }

    private static string KnownFolder(Guid id)
    {
        int hr = SHGetKnownFolderPath(in id, 0, IntPtr.Zero, out var path);
        try
        {
            if (hr < 0 || path == IntPtr.Zero) throw new InvalidOperationException("Known folder unavailable.");
            return Marshal.PtrToStringUni(path) ?? throw new InvalidOperationException("Known folder unavailable.");
        }
        finally { if (path != IntPtr.Zero) CoTaskMemFree(path); }
    }

    [DllImport("kernel32.dll")] private static extern unsafe uint GetSystemWindowsDirectoryW(char* buffer, uint size);
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pointer);
}

public interface ICategoryActions { Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct); }

/// <summary>
/// MeasureCategory and CleanCategory (docs/phase-7/admin-helper.md). The rule: only files whose last write and creation are both over a day old;
/// a file something holds open is skipped and counted, never forced; no reparse point is followed, and a link inside a category is removed as a
/// link; deleted for good. Measure never changes anything. The category's own folder is never removed or followed if it is a link.
/// </summary>
public sealed class CategoryActions(IReadOnlyDictionary<string, IReadOnlyList<CategoryTarget>> roots, TimeProvider? time = null) : ICategoryActions
{
    public static readonly TimeSpan MinAge = TimeSpan.FromDays(1);
    private const int MaxDepth = 64;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private static readonly EnumerationOptions All = new() { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false };

    public Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct) => Task.Run(() =>
    {
        try { return Run(request, ct); }
        catch (Exception)
        {
            // Any failure answers the request: an exception must never end the helper's session. Nothing from it is logged or sent.
            return Reply(request, Messages.CategoryFailed, null, "Admin helper failed");
        }
    }, CancellationToken.None);

    private sealed class Tally { public long Bytes; public int Files, InUse, Removed; }

    internal AdminReply Run(AdminRequest r, CancellationToken ct)
    {
        if (!Messages.RequestAllowed(r) || !Messages.IsCategoryVerb(r.Verb) || !roots.TryGetValue(r.Category!, out var targets))
            return Reply(r, Messages.CategoryFailed, null, "Invalid request");
        bool clean = r.Verb == Messages.CleanCategory;
        var cutoff = _time.GetUtcNow().UtcDateTime - MinAge;
        var tally = new Tally();
        bool any = false, cancelled = false;
        foreach (var target in targets)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            if (target.IsFile)
            {
                var info = new FileInfo(target.Path);
                if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Attributes.HasFlag(FileAttributes.Directory)) continue;
                any = true;
                Visit(info, clean, cutoff, tally);
            }
            else
            {
                var dir = new DirectoryInfo(target.Path);
                if (!dir.Exists || dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                any = true;
                if (!Walk(dir, clean, cutoff, tally, 0, ct)) { cancelled = true; break; }
            }
        }
        if (!any && !cancelled) return Reply(r, Messages.CategoryUnavailable, null);
        return Reply(r, clean ? Messages.CategoryCleaned : Messages.CategoryMeasured, tally, cancelled ? Messages.CancelledReason : null);
    }

    /// <summary>False when cancelled. Folders are walked without following links; the folder passed in is the category root or a real subfolder.</summary>
    private bool Walk(DirectoryInfo dir, bool clean, DateTime cutoff, Tally tally, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth) return true;
        IEnumerable<FileSystemInfo> entries;
        try { entries = dir.EnumerateFileSystemInfos("*", All).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested) return false;
            bool link = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
            bool folder = entry.Attributes.HasFlag(FileAttributes.Directory);
            if (folder && !link)
            {
                int before = tally.Removed;
                if (!Walk((DirectoryInfo)entry, clean, cutoff, tally, depth + 1, ct)) return false;
                if (clean && tally.Removed > before) RemoveIfEmpty((DirectoryInfo)entry);
            }
            else Visit(entry, clean, cutoff, tally);
        }
        return true;
    }

    /// <summary>A file, or a link (never followed). A link counts for nothing but is removed as itself when it is old enough.</summary>
    private static void Visit(FileSystemInfo entry, bool clean, DateTime cutoff, Tally tally)
    {
        if (!Old(entry, cutoff)) return;
        bool link = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
        long size = 0;
        if (!link && entry is FileInfo file) { try { size = file.Length; } catch (IOException) { return; } }
        if (!clean)
        {
            if (!link) { tally.Bytes += size; tally.Files++; }
            return;
        }
        try
        {
            if (entry.Attributes.HasFlag(FileAttributes.Directory)) Directory.Delete(entry.FullName, recursive: false);   // RemoveDirectory on the link itself
            else
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(entry.FullName, entry.Attributes & ~FileAttributes.ReadOnly);
                File.Delete(entry.FullName);
            }
            tally.Removed++;
            if (!link) { tally.Bytes += size; tally.Files++; }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Held open (sharing violation) or access denied: skipped and counted, never forced.
            if (!link) tally.InUse++;
        }
    }

    private static bool Old(FileSystemInfo entry, DateTime cutoff)
    {
        try { return entry.LastWriteTimeUtc < cutoff && entry.CreationTimeUtc < cutoff; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>A subfolder this clean emptied goes too (never the category root, and never a folder that was already empty); one that isn't empty, or is held, stays.</summary>
    private static void RemoveIfEmpty(DirectoryInfo dir)
    {
        try
        {
            if (dir.EnumerateFileSystemInfos("*", All).Any()) return;
            Directory.Delete(dir.FullName, recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static AdminReply Reply(AdminRequest r, string state, Tally? tally, string? reason = null) =>
        new(r.Id, state, Reason: reason, Category: r.Category, Bytes: tally?.Bytes, Files: tally?.Files, InUse: tally?.InUse);
}
