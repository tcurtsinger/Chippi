// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.SystemIntegration;
using Chippi.Features.Downloads.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Chippi.Features.Downloads.Tests;

public sealed class ExplorerLifetimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chippi-explorer-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = @"Software\Chippi.Explorer.ModuleTest-" + Guid.NewGuid().ToString("N");
    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_key, false);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Explorer_defaults_on_when_settings_omit_the_switch()
    {
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.SetModuleSettings("downloads-organizer", new { initialized = true });
        Assert.True(config.GetModuleSettings<DownloadsSettings>("downloads-organizer").OrganizeInExplorer);
    }

    [Fact]
    public async Task Switch_stop_start_reapply_and_uninstall_have_distinct_lifetimes()
    {
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.SetModuleSettings("downloads-organizer", new DownloadsSettings { Initialized = true, OrganizeInExplorer = false });
        var registration = new ExplorerRegistration("test", _key, @"C:\Tools\Chippi.exe");
        var module = new DownloadsModule(registration);
        var registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance,
            new ModuleServices { DataRoot = Path.Combine(_root, "data") });
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            Assert.True(registry.Find("downloads-organizer")!.IsRunning);
            AssertKeys(registration, false);
            var on = module.Settings.Clone(); on.OrganizeInExplorer = true; module.SaveSettings(on);
            AssertKeys(registration, true);
            Assert.True(config.GetModuleSettings<DownloadsSettings>("downloads-organizer").OrganizeInExplorer);
            await registry.StopAllAsync();
            AssertKeys(registration, true); // quitting must leave the cold-launch verb available
            using (var key = Registry.CurrentUser.OpenSubKey(registration.KeyPaths[0], true)!) key.SetValue("MUIVerb", "Stale");
            await registry.StartAsync();
            using (var key = Registry.CurrentUser.OpenSubKey(registration.KeyPaths[0])!) Assert.Equal("Organize", key.GetValue("MUIVerb"));
            var off = module.Settings.Clone(); off.OrganizeInExplorer = false; module.SaveSettings(off);
            AssertKeys(registration, false);
            on = module.Settings.Clone(); on.OrganizeInExplorer = true; module.SaveSettings(on);
            await registry.SetInstalledAsync("downloads-organizer", false);
            AssertKeys(registration, false);
        }
        finally { await registry.StopAllAsync(); registration.SetEnabled(false); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_reconciles_stale_keys_without_removing_installed_or_other_profiles(bool installed)
    {
        var registration = new ExplorerRegistration("test", _key, @"C:\Tools\Chippi.exe");
        var neighbor = new ExplorerRegistration("other", _key, @"C:\Tools\Chippi.exe");
        registration.SetEnabled(true);
        neighbor.SetEnabled(true);
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.SetModuleSettings("downloads-organizer", new DownloadsSettings { Initialized = true });
        // Represents the installed list after an offline edit or interrupted uninstall.
        config.SetSection("features", new { preset = "custom", installed = installed ? new[] { "downloads-organizer" } : Array.Empty<string>() });
        var registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance,
            new ModuleServices { DataRoot = Path.Combine(_root, "data") });
        var module = new DownloadsModule(registration);
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            AssertKeys(registration, installed);
            AssertKeys(neighbor, true);
            Assert.Equal(installed, registry.Find("downloads-organizer")!.IsRunning);
            if (!installed) Assert.Null(module.Engine);
            await registry.StopAllAsync();
            AssertKeys(registration, installed); // normal exit still preserves installed entries
        }
        finally { await registry.StopAllAsync(); }
    }
    private static void AssertKeys(ExplorerRegistration registration, bool present)
    {
        foreach (var path in registration.KeyPaths) { using var key = Registry.CurrentUser.OpenSubKey(path); Assert.Equal(present, key is not null); }
    }
}

[CollectionDefinition("DownloadsProfileEnvironment", DisableParallelization = true)]
public sealed class DownloadsProfileEnvironmentCollection;

[Collection("DownloadsProfileEnvironment")]
public sealed class InvalidExplorerProfileTests
{
    [Fact]
    public async Task Invalid_environment_profile_does_not_fail_construction_start_or_uninstall()
    {
        string? before = Environment.GetEnvironmentVariable("CHIPPI_DEV_PROFILE");
        string root = Path.Combine(Path.GetTempPath(), "chippi-explorer-" + Guid.NewGuid().ToString("N"));
        ModuleRegistry? registry = null;
        try
        {
            Environment.SetEnvironmentVariable("CHIPPI_DEV_PROFILE", "p2.views");
            var module = new DownloadsModule();
            var config = new ConfigStore(Path.Combine(root, "settings.json"));
            config.SetModuleSettings("downloads-organizer", new DownloadsSettings { Initialized = true });
            registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance,
                new ModuleServices { DataRoot = Path.Combine(root, "data") });
            registry.Register(module);
            await registry.StartAsync();
            Assert.True(registry.Find("downloads-organizer")!.IsRunning);
            Assert.NotNull(module.Engine);
            var off = module.Settings.Clone(); off.OrganizeInExplorer = false; module.SaveSettings(off);
            await registry.StopAllAsync();
            await registry.SetInstalledAsync("downloads-organizer", false);
        }
        finally
        {
            if (registry is not null) await registry.StopAllAsync();
            Environment.SetEnvironmentVariable("CHIPPI_DEV_PROFILE", before);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}