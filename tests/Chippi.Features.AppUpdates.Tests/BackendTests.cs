// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Microsoft.Management.Deployment;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Chippi.Features.AppUpdates.Tests;

public sealed class BackendTests
{
    [Fact]
    public void Winrt_vector_views_are_read_without_requesting_an_iterable_interface() =>
        Assert.Equal(new[] { 4, 7 }, WindowsPackageSource.CopyVector(new IndexedView()));

    [Fact]
    public void The_official_native_factory_metadata_and_notices_reach_the_consuming_app()
    {
        string root = AppContext.BaseDirectory;
        Assert.True(File.Exists(Path.Combine(root, "Microsoft.Management.Deployment.winmd")));
        Assert.True(File.Exists(Path.Combine(root, "licenses", "WinGet-NOTICE.txt")));
        using var stream = File.OpenRead(Path.Combine(root, "Microsoft.Management.Deployment.dll"));
        using var image = new PEReader(stream);
        Assert.Equal(RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Machine.Arm64 : Machine.Amd64, image.PEHeaders.CoffHeader.Machine);
    }

    [Theory]
    [InlineData("user", UpdateScope.User)]
    [InlineData("Machine", UpdateScope.Machine)]
    [InlineData("System", UpdateScope.Machine)]
    [InlineData("unknown", UpdateScope.Unknown)]
    [InlineData(null, UpdateScope.Unknown)]
    public void Scope_comes_from_installed_metadata(string? metadata, UpdateScope expected) => Assert.Equal(expected, WindowsPackageSource.ParseScope(metadata));

    [Fact]
    public async Task Explicit_read_only_source_refuses_before_native_activation()
    {
        var result = await new WindowsPackageSource(allowUpdates: false).UpdateAsync(new("No.App", "No app", "1", "2", "winget", UpdateScope.User));
        Assert.Equal(UpdateState.Failed, result.State); Assert.Equal("Updates are unavailable in this profile", result.Reason);
    }

    [Theory]
    [InlineData(InstallResultStatus.Ok, false, UpdateState.Updated)]
    [InlineData(InstallResultStatus.Ok, true, UpdateState.RestartRequired)]
    [InlineData(InstallResultStatus.NoApplicableUpgrade, false, UpdateState.Skipped)]
    [InlineData(InstallResultStatus.DownloadError, false, UpdateState.Failed)]
    [InlineData(InstallResultStatus.BlockedByPolicy, false, UpdateState.Failed)]
    public void Com_results_keep_reboot_and_no_longer_applicable_distinct(InstallResultStatus status, bool reboot, UpdateState expected) =>
        Assert.Equal(expected, WindowsPackageSource.TranslateResult(status, reboot, null, 0).State);

    [Fact]
    public void Progress_is_per_stage_and_unmeasurable_installs_stay_indeterminate()
    {
        var download = WindowsPackageSource.TranslateProgress(new() { State = PackageInstallProgressState.Downloading, DownloadProgress = .5 });
        Assert.Equal(.5, download.Fraction); Assert.Equal("Downloading", download.Stage);
        var install = WindowsPackageSource.TranslateProgress(new() { State = PackageInstallProgressState.Installing, InstallationProgress = 0 });
        Assert.Null(install.Fraction); Assert.Equal("Installing", install.Stage);
    }

    [Theory]
    [InlineData(PackageInstallerScope.User, ElevationRequirement.ElevationProhibited, PackageInstallerType.Exe, true)]
    [InlineData(PackageInstallerScope.User, ElevationRequirement.Unknown, PackageInstallerType.Exe, true)]
    [InlineData(PackageInstallerScope.User, ElevationRequirement.ElevatesSelf, PackageInstallerType.Exe, false)]
    [InlineData(PackageInstallerScope.Unknown, ElevationRequirement.Unknown, PackageInstallerType.Exe, false)]
    [InlineData(PackageInstallerScope.System, ElevationRequirement.ElevationProhibited, PackageInstallerType.Msi, false)]
    [InlineData(PackageInstallerScope.Unknown, ElevationRequirement.Unknown, PackageInstallerType.MSStore, true)]
    [InlineData(PackageInstallerScope.User, ElevationRequirement.ElevationRequired, PackageInstallerType.Msix, false)]
    public void Confirmed_user_updates_preserve_scope_and_refuse_explicit_self_elevation(PackageInstallerScope scope, ElevationRequirement elevation, PackageInstallerType type, bool expected) =>
        Assert.Equal(expected, WindowsPackageSource.UserInstallerAllowed(scope, elevation, type));

    private sealed class IndexedView : IReadOnlyList<int>
    {
        public int Count => 2;
        public int this[int index] => index switch { 0 => 4, 1 => 7, _ => throw new IndexOutOfRangeException() };
        public IEnumerator<int> GetEnumerator() => throw new InvalidCastException("No iterable interface");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
