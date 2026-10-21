// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;

namespace Chippi.Core.Tests;

public sealed class PermissionsTests : IDisposable
{
    private readonly RegistryFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Lists_each_capability_with_the_installed_modules_using_it()
    {
        _f.Registry.Register(FakeModule.Create("routing", PresetMembership.None, Capability.VbCable));
        _f.Registry.Register(FakeModule.Create("temps", PresetMembership.None, Capability.Admin));
        _f.Registry.Register(FakeModule.Create("fans", PresetMembership.None, Capability.Admin));
        await _f.Registry.StartAsync();
        await _f.Registry.SetInstalledAsync("temps", true);
        await _f.Registry.SetInstalledAsync("fans", true);

        var admin = Usage(Capability.Admin);
        Assert.Equal(["temps", "fans"], admin.UsedBy.Select(d => d.Id));
        Assert.False(admin.IsUnused);
        var cable = Usage(Capability.VbCable);
        Assert.Empty(cable.UsedBy);
    }

    [Fact]
    public async Task A_capability_never_used_is_not_flagged_unused()
    {
        _f.Registry.Register(FakeModule.Create("routing", PresetMembership.None, Capability.VbCable));
        _f.Registry.RegisterComingSoon(new FeatureDescriptor("ocr", "OCR", "Later", FeatureGroup.Capture) { Requires = [Capability.ScreenCapture] });
        await _f.Registry.StartAsync();

        Assert.False(Usage(Capability.VbCable).IsUnused);
        Assert.False(Usage(Capability.ScreenCapture).IsUnused); // placeholders are listed too
    }

    [Fact]
    public async Task Uninstalling_the_last_user_flags_the_capability_unused()
    {
        _f.Registry.Register(FakeModule.Create("temps", PresetMembership.None, Capability.Admin));
        _f.Registry.Register(FakeModule.Create("fans", PresetMembership.None, Capability.Admin));
        await _f.Registry.StartAsync();
        await _f.Registry.SetInstalledAsync("temps", true);
        await _f.Registry.SetInstalledAsync("fans", true);

        await _f.Registry.SetInstalledAsync("temps", false);
        Assert.False(Usage(Capability.Admin).IsUnused); // fans still needs it

        await _f.Registry.SetInstalledAsync("fans", false);
        Assert.True(Usage(Capability.Admin).IsUnused);

        await _f.Registry.SetInstalledAsync("fans", true);
        Assert.False(Usage(Capability.Admin).IsUnused);
    }

    [Fact]
    public async Task Unused_flag_survives_a_restart_and_can_be_dismissed()
    {
        _f.Registry.Register(FakeModule.Create("routing", PresetMembership.None, Capability.VbCable));
        await _f.Registry.StartAsync();
        await _f.Registry.SetInstalledAsync("routing", true);
        await _f.Registry.SetInstalledAsync("routing", false);

        var (_, registry) = _f.Restart();
        registry.Register(FakeModule.Create("routing", PresetMembership.None, Capability.VbCable));
        await registry.StartAsync();
        Assert.True(registry.GetPermissions().Single().IsUnused);

        registry.DismissUnusedCapability(Capability.VbCable.Id);
        Assert.False(registry.GetPermissions().Single().IsUnused);
    }

    [Fact]
    public async Task Dismissing_a_capability_still_in_use_does_nothing()
    {
        _f.Registry.Register(FakeModule.Create("routing", PresetMembership.None, Capability.VbCable));
        await _f.Registry.StartAsync();
        await _f.Registry.SetInstalledAsync("routing", true);

        _f.Registry.DismissUnusedCapability(Capability.VbCable.Id);
        await _f.Registry.SetInstalledAsync("routing", false);

        Assert.True(Usage(Capability.VbCable).IsUnused);
    }

    private Permissions.CapabilityUsage Usage(Capability capability) =>
        _f.Registry.GetPermissions().Single(u => u.Capability == capability);
}
