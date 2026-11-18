// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;

namespace Chippi.Features.Cleaner;

/// <summary>
/// The four folders everything hangs off. The shipped module uses <see cref="ForWindows"/>; tests and dev runs pass fixture folders, so no
/// category ever points at the real %TEMP%, caches or Windows folders outside the owner's own run.
/// </summary>
public sealed record CleanerRoots(string Temp, string LocalAppData, string AppData, string WindowsDirectory)
{
    public static CleanerRoots ForWindows() => new(
        Path.GetTempPath(),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows));
}

/// <summary>How a target's files are chosen.</summary>
internal enum TargetKind
{
    /// <summary>Everything under the folder, recursively; the folder itself is never removed.</summary>
    Folder,
    /// <summary>Only files in the folder (not below it) whose name matches one of <see cref="CleanerTarget.Patterns"/>.</summary>
    NamedFiles,
}

/// <summary>One fixed place. <see cref="AgeRule"/>: only files whose last write and creation are both over a day old. <see cref="App"/> names the app
/// whose running process (one of <see cref="Processes"/>) makes the target In use.</summary>
internal sealed record CleanerTarget(string Path, TargetKind Kind, bool AgeRule, IReadOnlyList<string>? Patterns = null,
    string? App = null, IReadOnlyList<string>? Processes = null);

/// <summary>
/// The folders behind each category, in code. Nothing here reads a setting or a request. An app's cache is only the named cache subfolders of
/// its profile: never Cookies, Login Data, History, Web Data, Local Storage, IndexedDB or anything else beside them.
/// </summary>
internal static class CleanerTargets
{
    public static readonly string[] ThumbnailPatterns = ["thumbcache_*.db", "iconcache_*.db"];
    private static readonly string[] CacheFolders = ["Cache", "Code Cache", "GPUCache"];

    /// <summary>Names the cleaner never deletes, wherever they sit under a target (a second guard behind the fixed folder list).</summary>
    private static readonly string[] ProtectedPrefixes = ["Cookies", "Login Data", "History", "Web Data"];
    private static readonly string[] ProtectedNames = ["Local Storage", "IndexedDB"];

    public static bool IsProtected(string name)
    {
        foreach (var prefix in ProtectedPrefixes) if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var exact in ProtectedNames) if (name.Equals(exact, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The targets of a standard category, or of the user part of a Windows one. Browser profiles are listed now, not followed as links.</summary>
    public static IReadOnlyList<CleanerTarget> For(string id, CleanerRoots roots) => id switch
    {
        CleanerCatalog.Temp => [new(roots.Temp, TargetKind.Folder, AgeRule: true)],
        CleanerCatalog.Thumbnails =>
            [new(P(roots.LocalAppData, "Microsoft", "Windows", "Explorer"), TargetKind.NamedFiles, AgeRule: false, ThumbnailPatterns)],
        CleanerCatalog.Apps => AppCaches(roots),
        // Readable by the owner, so a size can be shown before the prompt; the helper reads and cleans the rest. The day rule keeps it a lower bound.
        CleanerCatalog.WindowsUpdate => [new(P(roots.WindowsDirectory, "SoftwareDistribution", "Download"), TargetKind.Folder, AgeRule: true)],
        CleanerCatalog.Dumps =>
        [
            new(P(roots.LocalAppData, "CrashDumps"), TargetKind.Folder, AgeRule: true),
            new(P(roots.LocalAppData, "Microsoft", "Windows", "WER", "ReportArchive"), TargetKind.Folder, AgeRule: true),
            new(P(roots.LocalAppData, "Microsoft", "Windows", "WER", "ReportQueue"), TargetKind.Folder, AgeRule: true),
        ],
        _ => [],   // Windows temp and Delivery Optimization can't be read without admin: the helper does both
    };

    private static IReadOnlyList<CleanerTarget> AppCaches(CleanerRoots roots)
    {
        var list = new List<CleanerTarget>();
        Chromium(list, "Chrome", ["chrome"], P(roots.LocalAppData, "Google", "Chrome", "User Data"));
        Chromium(list, "Edge", ["msedge"], P(roots.LocalAppData, "Microsoft", "Edge", "User Data"));
        Chromium(list, "Brave", ["brave"], P(roots.LocalAppData, "BraveSoftware", "Brave-Browser", "User Data"));
        Electron(list, "Discord", ["discord"], P(roots.AppData, "discord"));
        Electron(list, "Slack", ["slack"], P(roots.AppData, "Slack"));
        Firefox(list, P(roots.LocalAppData, "Mozilla", "Firefox", "Profiles"));
        return list;
    }

    private static void Chromium(List<CleanerTarget> list, string app, string[] processes, string userData)
    {
        foreach (var profile in Subfolders(userData))
        {
            string name = System.IO.Path.GetFileName(profile);
            if (name != "Default" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            foreach (var cache in CacheFolders) list.Add(new(P(profile, cache), TargetKind.Folder, false, null, app, processes));
        }
    }

    private static void Electron(List<CleanerTarget> list, string app, string[] processes, string root)
    {
        foreach (var cache in CacheFolders) list.Add(new(P(root, cache), TargetKind.Folder, false, null, app, processes));
    }

    private static void Firefox(List<CleanerTarget> list, string profiles)
    {
        foreach (var profile in Subfolders(profiles)) list.Add(new(P(profile, "cache2"), TargetKind.Folder, false, null, "Firefox", ["firefox"]));
    }

    /// <summary>Real subfolders only: a profile that is a link is not followed.</summary>
    private static IEnumerable<string> Subfolders(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists || dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return [];
            return dir.EnumerateDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint)).Select(d => d.FullName).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static string P(params string[] parts) => System.IO.Path.Combine(parts);
}
