// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using Chippi.Core.Admin;
using Chippi.Features.Uninstaller;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Uninstaller.Tests;

internal sealed class FakeRegistry : IAppRegistry
{
    public Dictionary<AppSource, List<RawApp>> Keys { get; } = new() { [AppSource.Machine64] = [], [AppSource.Machine32] = [], [AppSource.User] = [] };
    public IReadOnlyList<RawApp> Read(AppSource source) => source == AppSource.Store ? [] : [.. Keys[source]];
    public RawApp? ReadOne(AppSource source, string key) => source == AppSource.Store ? null : Keys[source].FirstOrDefault(k => k.Key == key);
    public void Remove(AppSource source, string key) => Keys[source].RemoveAll(k => k.Key == key);

    public RawApp Add(AppSource source, string key, string name, string publisher = "", string version = "1.0", string? location = null, string? uninstall = null,
        string? quiet = null, IEnumerable<(string Name, object Value)>? extra = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DisplayName"] = name, ["Publisher"] = publisher, ["DisplayVersion"] = version, ["InstallLocation"] = location, ["UninstallString"] = uninstall,
            ["QuietUninstallString"] = quiet,
        };
        foreach (var (n, v) in extra ?? []) values[n] = v;
        var raw = new RawApp(key, values);
        Keys[source].Add(raw);
        return raw;
    }
}

internal sealed class FakePackages : IPackageSource, IPackageRemover
{
    public List<RawPackage> Packages { get; } = [];
    public List<string> Removed { get; } = [];
    public bool RemoveOk { get; set; } = true;
    public IReadOnlyList<RawPackage> List() => [.. Packages];
    public Task<bool> RemoveAsync(string fullName, CancellationToken ct)
    {
        Removed.Add(fullName);
        if (RemoveOk) Packages.RemoveAll(p => p.FullName == fullName);
        return Task.FromResult(RemoveOk);
    }
}

internal sealed class FakeProcesses : IAppProcesses
{
    public HashSet<string> RunningFolders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Reads;
    public IReadOnlyList<string> Images() { Reads++; return [.. RunningFolders.Select(r => System.IO.Path.Combine(r, "app.exe"))]; }
}

internal sealed class FakeUserProcess(List<string> log) : IUserProcess
{
    public List<(string App, string Args)> Calls { get; } = [];
    public Func<string, string, int?> Run { get; set; } = (_, _) => 0;
    public bool ExportOk { get; set; } = true;
    public Task<int?> RunAsync(string application, string arguments, TimeSpan limit, CancellationToken ct)
    {
        Calls.Add((application, arguments));
        if (Path.GetFileName(application).Equals("reg.exe", StringComparison.OrdinalIgnoreCase))
        {
            // export "HKCU\Software\X" "<file>" /y
            var parts = arguments.Split('"');
            log.Add("export " + parts[1]);
            if (!ExportOk) return Task.FromResult<int?>(1);
            Directory.CreateDirectory(Path.GetDirectoryName(parts[3])!);
            File.WriteAllText(parts[3], "Windows Registry Editor Version 5.00");
            return Task.FromResult<int?>(0);
        }
        log.Add("run " + Path.GetFileName(application));
        return Task.FromResult(Run(application, arguments));
    }
}

internal sealed class FakeRecycler(List<string> log) : IRecycler
{
    public HashSet<string> TooBig { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Held { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Recycled { get; } = [];
    public List<(int Thread, ApartmentState Apartment)> Threads { get; } = [];
    public bool CanRecycle(string path, long bytes) => !TooBig.Contains(path);
    public RecycleResult Recycle(string path)
    {
        if (Held.Contains(path)) return RecycleResult.InUse;
        // Whatever the real Shell does, no link may be left inside the folder: it would be followed out.
        if (Directory.Exists(path) && new DirectoryInfo(path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Any(e => e.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            throw new InvalidOperationException("a link was left in the folder");
        Threads.Add((Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        log.Add("recycle " + path);
        Recycled.Add(path);
        if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path);
        return RecycleResult.Recycled;
    }
}

internal sealed class FakeShortcuts : IShortcutReader
{
    public Dictionary<string, string?> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Target(string lnkPath) => Targets.GetValueOrDefault(lnkPath);
}

internal sealed class FakeLeftoverRegistry(List<string> log) : ILeftoverRegistry
{
    public List<(RegHive Hive, string Path)> Keys { get; } = [];
    public bool DeleteOk { get; set; } = true;
    public void Add(RegHive hive, string path) => Keys.Add((hive, path));
    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public bool Exists(RegHive hive, string path) => Keys.Any(k => k.Hive == hive && Eq(k.Path, path));
    public IReadOnlyList<string> SubKeys(RegHive hive, string path)
    {
        string prefix = path + "\\";
        return [.. Keys.Where(k => k.Hive == hive && k.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(k => k.Path[prefix.Length..].Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase)];
    }
    public bool DeleteTree(RegHive hive, string path)
    {
        log.Add("delete " + hive + " " + path);
        if (!DeleteOk) return false;
        Keys.RemoveAll(k => k.Hive == hive && (Eq(k.Path, path) || k.Path.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)));
        return true;
    }
}

internal sealed class FakeAdmin : IAdminHelper
{
    public List<string> Calls { get; } = [];
    public Func<AdminUninstallItem, AdminUninstallResult>? Uninstall { get; set; }
    public Func<AdminLeftoverItem, AdminLeftoverResult>? Leftover { get; set; }
    public List<AdminUninstallItem> Uninstalled { get; } = [];
    public List<(Guid Id, IReadOnlyList<AdminLeftoverItem> Items)> LeftoverCalls { get; } = [];
    public List<Guid> Dropped { get; } = [];
    public Action<AdminUninstallItem>? OnUninstall { get; set; }

    public Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items, IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default) =>
        throw new NotSupportedException("update");
    public Task<AdminPortResult> CloseProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException("close");
    public Task<AdminPortResult> EndProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException("end");
    public Task<AdminPortResult> StopServiceAsync(string serviceName, AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException("stop");
    public Task<IReadOnlyList<AdminCategoryResult>> MeasureCategoriesAsync(IReadOnlyList<AdminCategory> names, CancellationToken ct = default) => throw new NotSupportedException("measure");
    public Task<IReadOnlyList<AdminCategoryResult>> CleanCategoriesAsync(IReadOnlyList<AdminCategory> names, IProgress<AdminCategoryProgress>? progress = null, CancellationToken ct = default) =>
        throw new NotSupportedException("clean");

    public Task<IReadOnlyList<AdminUninstallResult>> UninstallAsync(IReadOnlyList<AdminUninstallItem> items, IProgress<AdminUninstallProgress>? progress = null, CancellationToken ct = default)
    {
        Calls.Add("uninstall " + items.Count);
        var results = new List<AdminUninstallResult>();
        for (int i = 0; i < items.Count; i++)
        {
            progress?.Report(new(items[i], i + 1, items.Count));
            Uninstalled.Add(items[i]);
            OnUninstall?.Invoke(items[i]);
            var r = Uninstall?.Invoke(items[i]) ?? new AdminUninstallResult(items[i], AdminUninstallState.Uninstalled, 0, true, Guid.NewGuid());
            results.Add(r);
            progress?.Report(new(items[i], i + 1, items.Count, r));
        }
        return Task.FromResult<IReadOnlyList<AdminUninstallResult>>(results);
    }

    public Task<IReadOnlyList<AdminLeftoverResult>> DeleteLeftoversAsync(Guid recordId, IReadOnlyList<AdminLeftoverItem> items, CancellationToken ct = default)
    {
        Calls.Add("leftovers " + items.Count);
        LeftoverCalls.Add((recordId, items));
        return Task.FromResult<IReadOnlyList<AdminLeftoverResult>>([.. items.Select(i => Leftover?.Invoke(i) ?? new AdminLeftoverResult(i, AdminLeftoverState.Removed))]);
    }

    public Task<bool> DropRecordAsync(Guid recordId, CancellationToken ct = default) { Calls.Add("drop"); Dropped.Add(recordId); return Task.FromResult(true); }
    public Task EndSessionAsync() { Calls.Add("end"); return Task.CompletedTask; }
}

internal sealed class CaptureLog : ILogger
{
    public List<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception) + (exception?.ToString() ?? ""));
}

/// <summary>A fixture tree under the OS temp folder that this test created: nothing here points at the real Program Files, AppData or registry.</summary>
internal sealed class Fixture : IDisposable
{
    public readonly string Base = Path.Combine(Path.GetTempPath(), "chippi-uninstaller-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];
    public readonly List<string> Log = [];
    public readonly LeftoverRoots Roots;
    public readonly FakeRegistry Registry = new();
    public readonly FakePackages Packages = new();
    public readonly FakeProcesses Processes = new();
    public readonly FakeUserProcess User;
    public readonly FakeRecycler Recycler;
    public readonly FakeShortcuts Shortcuts = new();
    public readonly FakeLeftoverRegistry Keys;
    public readonly FakeAdmin Admin = new();
    public readonly CaptureLog Logger = new();
    public readonly string RecordsPath, BackupFolder;
    public DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    public Fixture()
    {
        string profile = Path.Combine(Base, "Users", "Travis");
        string programFiles = Path.Combine(Base, "Program Files"), programFiles86 = Path.Combine(Base, "Program Files (x86)");
        Roots = new LeftoverRoots(Path.Combine(profile, "AppData", "Roaming"), Path.Combine(profile, "AppData", "Local"), Path.Combine(Base, "ProgramData"),
            [Path.Combine(profile, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs"), Path.Combine(Base, "ProgramData", "Microsoft", "Windows", "Start Menu", "Programs")],
            [Path.Combine(profile, "Desktop"), Path.Combine(Base, "Users", "Public", "Desktop")],
            Path.Combine(Base, "Windows"), [programFiles, programFiles86], profile,
            [Path.Combine(profile, "AppData", "Roaming", "Chippi"), Path.Combine(profile, "AppData", "Local", "Chippi")]);
        Directory.CreateDirectory(Roots.AppData);
        Directory.CreateDirectory(Roots.LocalAppData);
        Directory.CreateDirectory(Roots.ProgramData);
        Directory.CreateDirectory(programFiles);
        Directory.CreateDirectory(Roots.WindowsDirectory);
        RecordsPath = Path.Combine(Base, "data", "records.json");
        BackupFolder = Path.Combine(Base, "data", "backups");
        User = new FakeUserProcess(Log);
        Recycler = new FakeRecycler(Log);
        Keys = new FakeLeftoverRegistry(Log);
    }

    public UninstallerSystem Sys => new(Registry, Packages, Packages, Processes, User, Recycler, Shortcuts, Keys);

    public RecordStore Records() => new(RecordsPath, new FixedTime(this));

    public UninstallerEngine Engine(IAdminHelper? admin = null, bool withAdmin = true, string? cache = null) =>
        new(Sys, withAdmin ? admin ?? Admin : null, Records(), Roots, BackupFolder, cache, Logger, new FixedTime(this), (_, _) => Task.CompletedTask, TimeSpan.Zero, _ => false);

    public string ProgramFiles(params string[] parts) => Path.Combine([Roots.ProgramFiles[0], .. parts]);
    public string Roaming(params string[] parts) => Path.Combine([Roots.AppData, .. parts]);
    public string Local(params string[] parts) => Path.Combine([Roots.LocalAppData, .. parts]);
    public string Data(params string[] parts) => Path.Combine([Roots.ProgramData, .. parts]);
    public string Windows(params string[] parts) => Path.Combine([Roots.WindowsDirectory, .. parts]);
    public string Outside(params string[] parts) => Path.Combine([Base, "outside", .. parts]);

    public string WriteFile(string path, int size = 100)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    public string Folder(string path) { Directory.CreateDirectory(path); return path; }

    public string Junction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using var p = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
        })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        _junctions.Add(link);
        return link;
    }

    public void Dispose()
    {
        foreach (string link in _junctions)
            try { if (Directory.Exists(link)) Directory.Delete(link, false); } catch (IOException) { }
        try { if (Directory.Exists(Base)) Directory.Delete(Base, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

internal sealed class FixedTime(Fixture f) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => f.Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
