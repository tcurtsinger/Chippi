// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using Chippi.Core.Admin;
using Chippi.Features.Cleaner;

namespace Chippi.Features.Cleaner.Tests;

/// <summary>
/// A fixture tree under the OS temp folder that this test created, standing in for every category's folders: nothing here ever points at the
/// real %TEMP%, caches, Windows folders or browser profiles.
/// </summary>
internal sealed class Fixture : IDisposable
{
    public readonly string Base = Path.Combine(Path.GetTempPath(), "chippi-cleaner-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];
    public readonly CleanerRoots Roots;
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Fixture()
    {
        Directory.CreateDirectory(Base);
        Roots = new CleanerRoots(Path.Combine(Base, "temp"), Path.Combine(Base, "local"), Path.Combine(Base, "roaming"), Path.Combine(Base, "windows"));
    }

    public string Temp(string rel = "") => Path.Combine(Roots.Temp, rel);
    public string Local(params string[] parts) => Path.Combine([Roots.LocalAppData, .. parts]);
    public string Roaming(params string[] parts) => Path.Combine([Roots.AppData, .. parts]);
    public string Windows(params string[] parts) => Path.Combine([Roots.WindowsDirectory, .. parts]);
    public string Outside(string rel) => Path.Combine(Base, "outside", rel);

    /// <summary>A file of <paramref name="size"/> bytes whose last write and creation are <paramref name="ageDays"/> old.</summary>
    public string File(string path, int size = 100, double ageDays = 2)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[size]);
        var when = DateTime.UtcNow - TimeSpan.FromDays(ageDays);
        System.IO.File.SetCreationTimeUtc(path, when);
        System.IO.File.SetLastWriteTimeUtc(path, when);
        return path;
    }

    public CleanerEngine Engine(IAdminHelper? admin = null, CleanerSettings? settings = null, Action<CleanerSettings>? save = null) =>
        new(Roots, admin, settings, save, () => Running);

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

    public bool Exists(string path) => System.IO.File.Exists(path) || Directory.Exists(path);

    public void Dispose()
    {
        foreach (var link in _junctions)
            try { if (Directory.Exists(link)) Directory.Delete(link, recursive: false); } catch (IOException) { }
        try { Directory.Delete(Base, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>An admin helper that never starts anything: its calls are recorded and its answers set by the test.</summary>
internal sealed class FakeAdmin : IAdminHelper
{
    public List<string> Calls { get; } = [];
    public Func<AdminCategory, AdminCategoryResult> Measure { get; set; } = c => new(c, AdminCategoryState.Measured, 1000, 3);
    public Func<AdminCategory, AdminCategoryResult> Clean { get; set; } = c => new(c, AdminCategoryState.Cleaned, 1000, 3);
    public Exception? MeasureThrows { get; set; }
    /// <summary>Called with each category before it is cleaned; may throw to fail the helper mid-run.</summary>
    public Action<AdminCategory>? BeforeClean { get; set; }
    public Action? OnClean { get; set; }

    public Task<IReadOnlyList<AdminUpdateResult>> UpdatePackagesAsync(IReadOnlyList<AdminUpdateItem> items, IProgress<AdminUpdateProgress>? progress = null, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<AdminPortResult> CloseProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AdminPortResult> EndProcessAsync(AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AdminPortResult> StopServiceAsync(string serviceName, AdminEndpoint endpoint, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<AdminCategoryResult>> MeasureCategoriesAsync(IReadOnlyList<AdminCategory> names, CancellationToken ct = default)
    {
        Calls.Add("measure " + string.Join(",", names));
        if (MeasureThrows is { } ex) throw ex;
        return Task.FromResult<IReadOnlyList<AdminCategoryResult>>([.. names.Select(Measure)]);
    }

    public Task<IReadOnlyList<AdminCategoryResult>> CleanCategoriesAsync(IReadOnlyList<AdminCategory> names, IProgress<AdminCategoryProgress>? progress = null, CancellationToken ct = default)
    {
        Calls.Add("clean " + string.Join(",", names));
        OnClean?.Invoke();
        var results = new List<AdminCategoryResult>();
        for (int i = 0; i < names.Count; i++)
        {
            progress?.Report(new(names[i], i, names.Count));
            BeforeClean?.Invoke(names[i]);
            var r = Clean(names[i]);
            results.Add(r);
            progress?.Report(new(names[i], i, names.Count, r));
        }
        return Task.FromResult<IReadOnlyList<AdminCategoryResult>>(results);
    }

    public Task EndSessionAsync() { Calls.Add("end"); return Task.CompletedTask; }
}
