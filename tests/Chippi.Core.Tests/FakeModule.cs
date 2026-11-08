// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Core.Tests;

internal sealed class FakeSettings
{
    public int Value { get; set; }
}

internal sealed class FakeModule(FeatureDescriptor descriptor) : IFeatureModule
{
    public FeatureDescriptor Descriptor { get; } = descriptor;

    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public int Uninstalls { get; private set; }

    public int DataErases { get; private set; }

    public ModuleContext? Context { get; private set; }

    public Exception? FailOnStart { get; set; }

    /// <summary>Runs inside StartAsync, after the context is captured.</summary>
    public Action<ModuleContext>? OnStart { get; set; }

    /// <summary>Stands in for a handle or execution state: taken at the start of StartAsync, released by StopAsync.</summary>
    public bool HoldsResource { get; private set; }

    public Exception? FailOnStop { get; set; }

    /// <summary>Returned by StartAsync: work that finishes on another thread, as Audio routing's and Clipboard history's starts do.</summary>
    public Func<Task>? StartWork { get; set; }

    /// <summary>Returned by StopAsync: work that finishes on another thread, as the File organizer's stop does.</summary>
    public Func<Task>? StopWork { get; set; }

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        Starts++;
        Context = context;
        HoldsResource = true;
        OnStart?.Invoke(context);
        if (FailOnStart is not null)
            throw FailOnStart;
        return StartWork?.Invoke() ?? Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        Stops++;
        HoldsResource = false;
        if (FailOnStop is not null)
            throw FailOnStop;
        return StopWork?.Invoke() ?? Task.CompletedTask;
    }

    public Task UninstallAsync(CancellationToken ct)
    {
        Uninstalls++;
        return Task.CompletedTask;
    }

    public Task EraseDataAsync(CancellationToken ct)
    {
        DataErases++;
        return Task.CompletedTask;
    }

    public static FakeModule Create(string id, PresetMembership presets = PresetMembership.None, params Capability[] requires) =>
        new(new FeatureDescriptor(id, id, "Test feature", FeatureGroup.System) { Presets = presets, Requires = requires });
}

/// <summary>A registry on a temp settings file, with the hub it publishes to.</summary>
internal sealed class RegistryFixture : IDisposable
{
    private readonly TempDir _dir = new();

    public RegistryFixture()
    {
        Config = new ConfigStore(_dir.File("settings.json"));
        Config.Load();
        Registry = new ModuleRegistry(Config, Island, NullLoggerFactory.Instance, new ModuleServices { DataRoot = _dir.File("data") });
    }

    public string SettingsPath => Config.FilePath;

    public ConfigStore Config { get; }

    public IslandActivityHub Island { get; } = new();

    public ModuleRegistry Registry { get; }

    /// <summary>A second registry over the same settings file, as after an app restart.</summary>
    public (ConfigStore Config, ModuleRegistry Registry) Restart()
    {
        var config = new ConfigStore(SettingsPath);
        config.Load();
        return (config, new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance, new ModuleServices { DataRoot = _dir.File("data") }));
    }

    public void Dispose() => _dir.Dispose();
}
