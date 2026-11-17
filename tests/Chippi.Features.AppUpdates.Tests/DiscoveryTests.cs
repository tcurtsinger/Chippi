// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Chippi.Features.AppUpdates.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public async Task Opt_in_real_discovery_reads_only_and_reports_counts_and_timing()
    {
        string? report = Environment.GetEnvironmentVariable("CHIPPI_UPDATES_DISCOVERY");
        if (string.IsNullOrWhiteSpace(report)) return;
        var source = new WindowsPackageSource(allowUpdates: false);
        var watch = Stopwatch.StartNew();
        IReadOnlyList<PackageUpdate> packages;
        try { packages = await source.CheckAsync(includeUnknown: false); }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.GetFullPath(report), JsonSerializer.Serialize(new { milliseconds = watch.Elapsed.TotalMilliseconds, passed = false, hresult = error.HResult }));
            throw;
        }
        var output = new
        {
            milliseconds = watch.Elapsed.TotalMilliseconds, count = packages.Count,
            user = packages.Count(p => p.Scope == UpdateScope.User), machine = packages.Count(p => p.Scope == UpdateScope.Machine),
            unknownScope = packages.Count(p => p.Scope == UpdateScope.Unknown), unknownVersion = packages.Count(p => p.UnknownVersion),
            winget = packages.Count(p => p.Source == "winget"), store = packages.Count(p => p.IsStore), sourceWarning = source.Warning is not null,
            sourceErrors = source.SourceErrors
        };
        await File.WriteAllTextAsync(Path.GetFullPath(report), JsonSerializer.Serialize(output));
        Assert.True(output.count >= 0);
    }
}
