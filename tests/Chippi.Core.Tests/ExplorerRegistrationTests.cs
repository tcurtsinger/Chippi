// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.SystemIntegration;
using Microsoft.Win32;

namespace Chippi.Core.Tests;

public sealed class ExplorerRegistrationTests : IDisposable
{
    private readonly string _root = @"Software\Chippi.Explorer.Test-" + Guid.NewGuid().ToString("N");
    private const string Exe = @"C:\Program Files\Chippi\Chippi.exe";
    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, false);

    [Fact]
    public void Both_folder_keys_have_the_exact_label_icon_and_quoted_command()
    {
        var registration = new ExplorerRegistration(classesRoot: _root, executable: Exe);
        registration.SetEnabled(true);
        Assert.Equal("ChippiOrganize", registration.VerbName);
        Assert.Equal(2, registration.KeyPaths.Count);
        foreach (var path in registration.KeyPaths)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path)!;
            Assert.Equal("Organize", key.GetValue("MUIVerb"));
            Assert.Equal($"\"{Exe}\",0", key.GetValue("Icon"));
            using var command = key.OpenSubKey("command")!;
            Assert.Equal($"\"{Exe}\" --organize \"%V\"", command.GetValue(""));
        }
    }

    [Fact]
    public void Reapply_repairs_values_and_remove_is_idempotent_without_touching_neighbors()
    {
        var registration = new ExplorerRegistration("test", _root, Exe);
        registration.SetEnabled(true);
        using (var key = Registry.CurrentUser.OpenSubKey(registration.KeyPaths[0], true)!) key.SetValue("MUIVerb", "Stale");
        using (var command = Registry.CurrentUser.OpenSubKey(registration.KeyPaths[1] + @"\command", true)!) command.SetValue("", "Stale");
        registration.SetEnabled(true);
        foreach (var path in registration.KeyPaths)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path)!;
            Assert.Equal("Organize", key.GetValue("MUIVerb"));
            using var command = key.OpenSubKey("command")!;
            Assert.Equal($"\"{Exe}\" --organize \"%V\" --dev-profile \"test\"", command.GetValue(""));
        }
        string neighbor = _root + @"\Directory\shell\AnotherApp";
        using (var key = Registry.CurrentUser.CreateSubKey(neighbor)) key.SetValue("", "Keep");
        registration.SetEnabled(false);
        registration.SetEnabled(false);
        Assert.All(registration.KeyPaths, path => { using var key = Registry.CurrentUser.OpenSubKey(path); Assert.Null(key); });
        using var other = Registry.CurrentUser.OpenSubKey(neighbor);
        Assert.NotNull(other);
    }

    [Fact]
    public void Installed_paths_use_current_and_portable_paths_stay_portable()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChippiApp");
        Assert.Equal(Path.Combine(root, "current", "Chippi.exe"), ExplorerRegistration.StableExecutable(Path.Combine(root, "app-0.1.2", "Chippi.exe")));
        Assert.Equal(Exe, ExplorerRegistration.StableExecutable(Exe));
    }

    [Theory]
    [InlineData("bad/profile")]
    [InlineData("..")]
    [InlineData("bad\"profile")]
    public void Dev_profile_cannot_escape_its_key_or_command(string profile) =>
        Assert.Throws<ArgumentException>(() => new ExplorerRegistration(profile, _root, Exe));
}

[CollectionDefinition("ExplorerProfileEnvironment", DisableParallelization = true)]
public sealed class ExplorerProfileEnvironmentCollection;

[Collection("ExplorerProfileEnvironment")]
public sealed class ExplorerProfileArgumentTests
{
    [Fact]
    public void Explicit_profile_survives_a_shell_launch_without_an_inherited_environment()
    {
        string? before = Environment.GetEnvironmentVariable("CHIPPI_DEV_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("CHIPPI_DEV_PROFILE", null);
            Assert.Equal("test-123", ExplorerRegistration.ResolveDevProfile(["--organize", @"C:\Folder", "--dev-profile", "test-123"]));
            Assert.Equal("test-123", Environment.GetEnvironmentVariable("CHIPPI_DEV_PROFILE"));
            Assert.Equal("test-123", ExplorerRegistration.ResolveDevProfile([]));
            Assert.Throws<ArgumentException>(() => ExplorerRegistration.ResolveDevProfile(["--dev-profile"]));
        }
        finally { Environment.SetEnvironmentVariable("CHIPPI_DEV_PROFILE", before); }
    }
}
