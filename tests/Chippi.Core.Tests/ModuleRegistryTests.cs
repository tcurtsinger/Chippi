// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Core.Modules;

namespace Chippi.Core.Tests;

public sealed class ModuleRegistryTests : IDisposable
{
    private readonly RegistryFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task First_run_applies_Essentials()
    {
        var essential = FakeModule.Create("essential", PresetMembership.Essentials);
        var audio = FakeModule.Create("audio", PresetMembership.Audio);
        _f.Registry.Register(essential);
        _f.Registry.Register(audio);

        await _f.Registry.StartAsync();

        Assert.Equal(FeaturePreset.Essentials, _f.Registry.Preset);
        Assert.True(_f.Registry.Find("essential")!.IsRunning);
        Assert.False(_f.Registry.Find("audio")!.IsInstalled);
        Assert.Equal(1, essential.Starts);
        Assert.Equal(0, audio.Starts);
    }

    [Fact]
    public async Task Uninstalled_module_is_never_started()
    {
        var module = FakeModule.Create("m");
        _f.Registry.Register(module);
        await _f.Registry.StartAsync();

        Assert.False(_f.Registry.Find("m")!.IsInstalled);
        Assert.Equal(0, module.Starts);
    }

    [Fact]
    public async Task Install_and_uninstall_start_and_stop_live()
    {
        var module = FakeModule.Create("m");
        _f.Registry.Register(module);
        await _f.Registry.StartAsync();

        await _f.Registry.SetInstalledAsync("m", true);
        Assert.Equal(1, module.Starts);
        Assert.True(_f.Registry.Find("m")!.IsRunning);
        Assert.Equal(1, _f.Registry.InstalledCount);

        await _f.Registry.SetInstalledAsync("m", false);
        Assert.Equal(1, module.Stops);
        Assert.False(_f.Registry.Find("m")!.IsRunning);
        Assert.Equal(0, _f.Registry.InstalledCount);
    }

    [Fact]
    public async Task Install_uninstall_reinstall_restores_module_settings()
    {
        var module = FakeModule.Create("m");
        module.OnStart = ctx =>
        {
            var s = ctx.Settings.Get<FakeSettings>();
            if (s.Value == 0)
                ctx.Settings.Save(new FakeSettings { Value = 42 });
        };
        _f.Registry.Register(module);
        await _f.Registry.StartAsync();

        await _f.Registry.SetInstalledAsync("m", true);
        await _f.Registry.SetInstalledAsync("m", false);
        Assert.True(_f.Config.HasModuleSettings("m")); // kept after uninstall

        await _f.Registry.SetInstalledAsync("m", true);
        Assert.Equal(42, module.Context!.Settings.Get<FakeSettings>().Value);
        Assert.Equal(2, module.Starts);
    }

    [Fact]
    public async Task Installed_set_and_settings_survive_a_restart()
    {
        _f.Registry.Register(FakeModule.Create("a"));
        _f.Registry.Register(FakeModule.Create("b"));
        await _f.Registry.StartAsync();
        await _f.Registry.SetInstalledAsync("b", true);
        _f.Config.SetModuleSettings("b", new FakeSettings { Value = 7 });

        var (config, registry) = _f.Restart();
        var a = FakeModule.Create("a");
        var b = FakeModule.Create("b");
        registry.Register(a);
        registry.Register(b);
        await registry.StartAsync();

        Assert.Equal(FeaturePreset.Custom, registry.Preset);
        Assert.Equal(0, a.Starts);
        Assert.Equal(1, b.Starts);
        Assert.Equal(7, config.GetModuleSettings<FakeSettings>("b").Value);
    }

    [Fact]
    public async Task Presets_install_exactly_their_members()
    {
        _f.Registry.Register(FakeModule.Create("ess", PresetMembership.Essentials | PresetMembership.PowerUser));
        _f.Registry.Register(FakeModule.Create("aud", PresetMembership.Audio));
        _f.Registry.Register(FakeModule.Create("pow", PresetMembership.PowerUser));
        _f.Registry.Register(FakeModule.Create("none"));
        _f.Registry.RegisterComingSoon(new FeatureDescriptor("soon", "Soon", "Later", FeatureGroup.Files) { Presets = PresetMembership.Essentials });
        await _f.Registry.StartAsync();

        await _f.Registry.ApplyPresetAsync(FeaturePreset.Audio);
        Assert.Equal(["aud"], Installed());

        await _f.Registry.ApplyPresetAsync(FeaturePreset.PowerUser);
        Assert.Equal(["ess", "pow"], Installed());

        await _f.Registry.ApplyPresetAsync(FeaturePreset.Everything);
        Assert.Equal(["ess", "aud", "pow", "none"], Installed()); // never the placeholder
        Assert.Equal(FeaturePreset.Everything, _f.Registry.Preset);

        await _f.Registry.ApplyPresetAsync(FeaturePreset.Essentials);
        Assert.Equal(["ess"], Installed());
    }

    [Fact]
    public async Task Toggling_one_feature_switches_the_preset_to_Custom()
    {
        _f.Registry.Register(FakeModule.Create("ess", PresetMembership.Essentials));
        _f.Registry.Register(FakeModule.Create("other"));
        await _f.Registry.StartAsync();
        Assert.Equal(FeaturePreset.Essentials, _f.Registry.Preset);

        await _f.Registry.SetInstalledAsync("other", true);

        Assert.Equal(FeaturePreset.Custom, _f.Registry.Preset);
        Assert.Equal(["ess", "other"], Installed());
    }

    [Fact]
    public async Task Choosing_Custom_keeps_the_current_set()
    {
        _f.Registry.Register(FakeModule.Create("ess", PresetMembership.Essentials));
        _f.Registry.Register(FakeModule.Create("other"));
        await _f.Registry.StartAsync();

        await _f.Registry.ApplyPresetAsync(FeaturePreset.Custom);

        Assert.Equal(FeaturePreset.Custom, _f.Registry.Preset);
        Assert.Equal(["ess"], Installed());
    }

    [Fact]
    public async Task Coming_soon_placeholders_cannot_be_installed()
    {
        _f.Registry.RegisterComingSoon(new FeatureDescriptor("soon", "Soon", "Later", FeatureGroup.Files));
        await _f.Registry.StartAsync();

        var entry = _f.Registry.Find("soon")!;
        Assert.True(entry.IsComingSoon);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _f.Registry.SetInstalledAsync("soon", true));
        Assert.False(entry.IsInstalled);
        Assert.Single(_f.Registry.Features);
    }

    [Fact]
    public async Task A_module_that_fails_to_start_stays_installed_and_reports_the_error()
    {
        var module = FakeModule.Create("bad");
        module.FailOnStart = new InvalidOperationException("boom");
        _f.Registry.Register(module);
        await _f.Registry.StartAsync();

        await _f.Registry.SetInstalledAsync("bad", true);

        var entry = _f.Registry.Find("bad")!;
        Assert.True(entry.IsInstalled);
        Assert.False(entry.IsRunning);
        Assert.Equal("boom", entry.LastError!.Message);
    }

    [Fact]
    public async Task Stopping_removes_island_activities_and_ui_contributions()
    {
        var module = FakeModule.Create("m");
        module.OnStart = ctx =>
        {
            ctx.Island.Publish(new IslandActivity("on", IslandActivityKind.Timer, "Keeping awake", "Keep awake"));
            ctx.Ui.AddToolTile(new ToolTileModel("Keep awake", "Coffee", _ => Task.CompletedTask));
            ctx.Ui.SetSettingsPage(() => new object());
        };
        _f.Registry.Register(module);
        await _f.Registry.StartAsync();

        await _f.Registry.SetInstalledAsync("m", true);
        var entry = _f.Registry.Find("m")!;
        Assert.Single(entry.ToolTiles);
        Assert.NotNull(entry.SettingsPage);
        Assert.Equal("m", _f.Island.Current!.ModuleId);

        await _f.Registry.SetInstalledAsync("m", false);
        Assert.Empty(entry.ToolTiles);
        Assert.Null(entry.SettingsPage);
        Assert.Null(_f.Island.Current);
    }

    [Fact]
    public async Task StopAll_stops_running_modules_but_keeps_them_installed()
    {
        var module = FakeModule.Create("m", PresetMembership.Essentials);
        _f.Registry.Register(module);
        await _f.Registry.StartAsync();

        await _f.Registry.StopAllAsync();

        Assert.Equal(1, module.Stops);
        Assert.True(_f.Registry.Find("m")!.IsInstalled);
        var (_, registry) = _f.Restart();
        registry.Register(FakeModule.Create("m", PresetMembership.Essentials));
        await registry.StartAsync();
        Assert.True(registry.Find("m")!.IsRunning);
    }

    [Fact]
    public async Task Reload_after_import_starts_and_stops_to_match()
    {
        var a = FakeModule.Create("a", PresetMembership.Essentials);
        var b = FakeModule.Create("b");
        _f.Registry.Register(a);
        _f.Registry.Register(b);
        await _f.Registry.StartAsync();
        var export = Path.Combine(Path.GetDirectoryName(_f.SettingsPath)!, "export.json");
        await _f.Registry.SetInstalledAsync("b", true);
        await _f.Registry.SetInstalledAsync("a", false);
        _f.Config.Export(export);
        await _f.Registry.ApplyPresetAsync(FeaturePreset.Essentials);
        Assert.Equal(["a"], Installed());

        _f.Config.Import(export);
        await _f.Registry.ReloadAsync();

        Assert.Equal(["b"], Installed());
        Assert.Equal(FeaturePreset.Custom, _f.Registry.Preset);
        Assert.True(_f.Registry.Find("b")!.IsRunning);
        Assert.False(_f.Registry.Find("a")!.IsRunning);
    }

    [Fact]
    public async Task Installed_ids_from_a_newer_build_are_kept()
    {
        File.WriteAllText(_f.SettingsPath, """
            { "version": 1, "features": { "preset": "custom", "installed": ["m", "from-the-future"] } }
            """);
        _f.Config.Load();
        _f.Registry.Register(FakeModule.Create("m"));
        _f.Registry.Register(FakeModule.Create("n"));
        await _f.Registry.StartAsync();

        await _f.Registry.SetInstalledAsync("n", true);

        var (config, _) = _f.Restart();
        Assert.Contains("from-the-future", File.ReadAllText(config.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registering_after_start_or_twice_is_rejected()
    {
        _f.Registry.Register(FakeModule.Create("m"));
        Assert.Throws<ArgumentException>(() => _f.Registry.Register(FakeModule.Create("m")));
        await _f.Registry.StartAsync();
        Assert.Throws<InvalidOperationException>(() => _f.Registry.Register(FakeModule.Create("x")));
    }

    private string[] Installed() => [.. _f.Registry.Features.Where(e => e.IsInstalled).Select(e => e.Id)];
}
