// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.Uninstaller;

/// <summary>One Uninstall key as read: its name and its values. Values are strings, ints or null; nothing is expanded.</summary>
public sealed record RawApp(string Key, IReadOnlyDictionary<string, object?> Values)
{
    public string? Str(string name) => Values.TryGetValue(name, out var v) ? v?.ToString() : null;
    public int? Int(string name) => Values.TryGetValue(name, out var v) ? v switch { int i => i, long l when l is >= int.MinValue and <= int.MaxValue => (int)l, string s when int.TryParse(s, out int n) => n, _ => null } : null;
}

/// <summary>The Uninstall keys of one source, read-only. Tests pass a fake: nothing in the tests reads or writes the real registry.</summary>
public interface IAppRegistry
{
    /// <summary>Every key under the source's Uninstall key (Machine64, Machine32 or User). A key that can't be read is skipped.</summary>
    IReadOnlyList<RawApp> Read(AppSource source);

    /// <summary>One key again, or null when it's gone: the re-read after an uninstaller ran.</summary>
    RawApp? ReadOne(AppSource source, string key);
}

public sealed record RawPackage(string FullName, string FamilyName, string Name, string DisplayName, string Publisher, string Version,
    string? InstallLocation, DateTime? Installed, bool IsFramework, bool IsResource, bool IsSystem);

/// <summary>The Store and MSIX packages of the current user.</summary>
public interface IPackageSource
{
    IReadOnlyList<RawPackage> List();
}

/// <summary>Removes a Store package for the current user. No admin.</summary>
public interface IPackageRemover
{
    Task<bool> RemoveAsync(string fullName, CancellationToken ct);
}

/// <summary>The running programs whose file is inside one of the folders ("Close Notepad++ first"). Never ends one.</summary>
public interface IAppProcesses
{
    IReadOnlyList<string> RunningIn(IReadOnlyList<string> folders);
}

/// <summary>Starts a program as the standard user, never elevated, and waits for it. The exit code, or null when it couldn't start.</summary>
public interface IUserProcess
{
    Task<int?> RunAsync(string application, string arguments, TimeSpan limit, CancellationToken ct);
}

public enum RecycleResult { Recycled, InUse, Failed }

/// <summary>The Recycle Bin. A file is never deleted for good by Chippi: what can't be recycled stays and says so.</summary>
public interface IRecycler
{
    /// <summary>Whether this item can go to the Recycle Bin: false for a drive without one and for an item bigger than it holds.</summary>
    bool CanRecycle(string path, long bytes);
    RecycleResult Recycle(string path);
}

public interface IShortcutReader
{
    /// <summary>Where a .lnk points, or null when it can't be read.</summary>
    string? Target(string lnkPath);
}

/// <summary>The registry keys the scan reads and removal deletes, as the standard user.</summary>
public interface ILeftoverRegistry
{
    bool Exists(RegHive hive, string path);
    IReadOnlyList<string> SubKeys(RegHive hive, string path);
    /// <summary>Deletes the key and everything under it. False when it couldn't be (access denied, in use).</summary>
    bool DeleteTree(RegHive hive, string path);
}

/// <summary>Everything the engine reaches Windows through. The module passes the real set; tests pass fakes.</summary>
public sealed record UninstallerSystem(IAppRegistry Registry, IPackageSource Packages, IPackageRemover Remover, IAppProcesses Processes,
    IUserProcess UserProcess, IRecycler Recycler, IShortcutReader Shortcuts, ILeftoverRegistry LeftoverRegistry)
{
    public static UninstallerSystem ForWindows() => new(new WindowsAppRegistry(), new WindowsPackages(), new WindowsPackages(), new WindowsAppProcesses(),
        new WindowsUserProcess(), new WindowsRecycler(), new WindowsShortcutReader(), new WindowsLeftoverRegistry());
}
