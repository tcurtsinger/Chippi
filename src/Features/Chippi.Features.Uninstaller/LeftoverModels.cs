// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;

namespace Chippi.Features.Uninstaller;

public enum LeftoverKind { File, Folder, Key }

/// <summary>
/// Safe: verified ownership (inside the app's recorded install folder, named by its own uninstall entry, or its package). Check first: a name
/// match only. Shared: another app's folder or key is, contains or sits inside it; never offered for removal.
/// </summary>
public enum LeftoverClass { Safe, CheckFirst, Shared }

/// <summary>
/// One thing the scan found. <see cref="Path"/> is what the review shows: a file system path, or a key as "HKCU\Software\X" / "HKLM\SOFTWARE\X". A key's
/// <see cref="KeyPath"/> is the path under its hive. <see cref="Reason"/> is a short word for the engine and tests ("Install folder", "App folder");
/// the review draws none. <see cref="SharedWith"/> is the other app's name for a Shared item.
/// </summary>
public sealed record LeftoverItem(string Id, LeftoverKind Kind, string Path, RegHive? Hive, string? KeyPath, LeftoverClass Class, string Reason, string? SharedWith,
    long? Bytes, bool InUse, bool NoRecycleBin, bool NeedsAdmin, bool IsLink)
{
    /// <summary>Safe items start ticked, except one in use or too big for the Recycle Bin; Check first and Shared never do.</summary>
    public bool TickedByDefault => Class == LeftoverClass.Safe && !InUse && !NoRecycleBin;

    /// <summary>The review offers a checkbox: not for a Shared item.</summary>
    public bool CanTick => Class != LeftoverClass.Shared;

    public static string IdFor(LeftoverKind kind, string path) => (kind == LeftoverKind.Key ? "k:" : "f:") + path.ToUpperInvariant();
}

/// <summary>The scan's result for one app. <see cref="HasRecord"/> false means only Check first items were offered.</summary>
public sealed record LeftoverScan(AppRecord Record, IReadOnlyList<LeftoverItem> Items, bool HasRecord)
{
    public IReadOnlyList<LeftoverItem> Safe => [.. Items.Where(i => i.Class == LeftoverClass.Safe)];
    public IReadOnlyList<LeftoverItem> CheckFirst => [.. Items.Where(i => i.Class == LeftoverClass.CheckFirst)];
    public IReadOnlyList<LeftoverItem> Shared => [.. Items.Where(i => i.Class == LeftoverClass.Shared)];
    public IReadOnlyList<LeftoverItem> Removable => [.. Items.Where(i => i.CanTick)];
}

/// <summary>Where the scan looks. The fixed folders are read from Windows once; tests pass a fixture tree.</summary>
public sealed record LeftoverRoots(string AppData, string LocalAppData, string ProgramData, IReadOnlyList<string> StartMenuPrograms, IReadOnlyList<string> Desktops,
    string WindowsDirectory, IReadOnlyList<string> ProgramFiles, string UserProfile, IReadOnlyList<string> ProtectedTrees)
{
    /// <summary>The folders whose items the helper removes: everything an admin owns.</summary>
    public IReadOnlyList<string> AdminRoots => [.. ProgramFiles, ProgramData, .. StartMenuPrograms.Where(p => !Under(UserProfile, p)), .. Desktops.Where(p => !Under(UserProfile, p))];

    /// <summary>Roots a leftover never is, never contains and is never the whole of: the scan lists only what is inside them.</summary>
    public IEnumerable<string> Containers()
    {
        yield return AppData; yield return LocalAppData; yield return ProgramData; yield return UserProfile; yield return WindowsDirectory;
        foreach (var p in ProgramFiles) { yield return p; yield return System.IO.Path.Combine(p, "Common Files"); }
        foreach (var p in StartMenuPrograms) yield return p;
        foreach (var p in Desktops) yield return p;
        yield return System.IO.Path.Combine(LocalAppData, "Programs");
        yield return System.IO.Path.Combine(LocalAppData, "Packages");
        yield return System.IO.Path.Combine(AppData, "Microsoft");
        yield return System.IO.Path.Combine(LocalAppData, "Microsoft");
        yield return System.IO.Path.Combine(ProgramData, "Microsoft");
        yield return System.IO.Path.Combine(ProgramData, "Packages");
        yield return System.IO.Path.GetDirectoryName(UserProfile) ?? UserProfile;
        yield return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UserProfile) ?? UserProfile, "Public");
        foreach (string known in new[] { "Documents", "Downloads", "Pictures", "Music", "Videos", "Desktop" }) yield return System.IO.Path.Combine(UserProfile, known);
    }

    public static LeftoverRoots ForWindows()
    {
        string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        string[] programFiles = [.. new[] { F(Environment.SpecialFolder.ProgramFiles), F(Environment.SpecialFolder.ProgramFilesX86) }.Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        string appData = F(Environment.SpecialFolder.ApplicationData), local = F(Environment.SpecialFolder.LocalApplicationData);
        return new LeftoverRoots(appData, local, F(Environment.SpecialFolder.CommonApplicationData),
            [System.IO.Path.Combine(F(Environment.SpecialFolder.StartMenu), "Programs"), System.IO.Path.Combine(F(Environment.SpecialFolder.CommonStartMenu), "Programs")],
            [F(Environment.SpecialFolder.DesktopDirectory), F(Environment.SpecialFolder.CommonDesktopDirectory)],
            F(Environment.SpecialFolder.Windows), programFiles, F(Environment.SpecialFolder.UserProfile),
            [System.IO.Path.Combine(appData, "Chippi"), System.IO.Path.Combine(appData, "Chippi-dev"), System.IO.Path.Combine(local, "Chippi"), System.IO.Path.Combine(local, "Chippi-dev"),
             System.IO.Path.Combine(F(Environment.SpecialFolder.CommonApplicationData), "Chippi")]);
    }

    public static bool Under(string parent, string child) => PathRules.Contains(parent, child);
}

/// <summary>Whole-segment path compares: "App" is not inside "AppData".</summary>
public static class PathRules
{
    public static string Norm(string path)
    {
        try { path = System.IO.Path.GetFullPath(path); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        return path.Length > 3 ? path.TrimEnd('\\', '/') : path;
    }

    /// <summary>True when <paramref name="child"/> is <paramref name="parent"/> or inside it, by whole segments.</summary>
    public static bool Contains(string parent, string child)
    {
        string p = Norm(parent), c = Norm(child);
        if (string.Equals(p, c, StringComparison.OrdinalIgnoreCase)) return true;
        string prefix = p.EndsWith('\\') ? p : p + "\\";
        return c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Strictly(string parent, string child) => Contains(parent, child) && !string.Equals(Norm(parent), Norm(child), StringComparison.OrdinalIgnoreCase);

    public static bool Same(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
}
