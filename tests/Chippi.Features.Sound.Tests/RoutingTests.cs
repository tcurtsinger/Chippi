// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;
using Chippi.Audio;
using Chippi.Features.Sound.Routing;
using static Chippi.Audio.Tests.FakeDeviceSystem;
using static Chippi.Features.Sound.Tests.RoutingFixture;

namespace Chippi.Features.Sound.Tests;

public sealed class RoutingTests
{
    [Fact]
    public async Task Seeds_independent_lists_once_default_first_and_startup_is_quiet()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B"), Mic("M1"), Mic("M2")).WithDefault("B").WithDefault("M2");
        await f.Start();
        Assert.Equal(["B", "A"], f.Module.Settings.Output.Devices.Select(d => d.Id));
        Assert.Equal(["M2", "M1"], f.Module.Settings.Microphone.Devices.Select(d => d.Id));
        Assert.True(f.Module.Settings.Output.Seeded);
        Assert.Empty(f.Notices.Sent);
        Assert.Equal([SoundTile.Key, SoundTile.Key], f.Ui.Tiles.Select(t => t.Spec.Key));
    }

    [Fact]
    public async Task Startup_applies_saved_priority_for_all_roles_without_notice()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("a"), Speaker("B"), Mic("M")).WithDefault("B");
        f.Settings.Value = new() { Output = Saved("A", "B") };
        await f.Start();
        foreach (var role in Enum.GetValues<AudioRole>()) Assert.Equal("a", f.System.DefaultOf(AudioFlow.Render, role));
        Assert.Empty(f.Notices.Sent);
        Assert.Equal("a", f.Ui.Tiles[0].Status, ignoreCase: true);
        Assert.Equal(ApartmentState.MTA, await f.Audio.InvokeAsync(_ => Thread.CurrentThread.GetApartmentState()));
    }

    [Fact]
    public async Task Higher_device_reconnects_and_unplug_falls_back_to_next_listed_not_former_default()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("B"), Speaker("C")).WithDefault("C");
        f.Settings.Value = new() { Output = Saved("A", "B", "C") };
        await f.Start();
        f.System.Connect(Speaker("A"));
        await Until(() => f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia) == "A");
        f.System.Disconnect("A", windowsPicks: "C");
        await Until(() => f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia) == "B");
        await Until(() => f.Notices.Sent.Count >= 2);
        Assert.All(f.Notices.Sent, n => Assert.Equal("output-switched", n.Kind));
    }

    [Fact]
    public async Task Nothing_listed_available_leaves_windows_choice()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("Windows")).WithDefault("A");
        f.Settings.Value = new() { Output = Saved("A"), Microphone = new() { Seeded = true } };
        await f.Start();
        // Windows is explicitly removed so it cannot be appended.
        await f.Module.RemoveAsync(AudioFlow.Render, "Windows");
        f.System.Disconnect("A", "Windows");
        await f.Audio.RefreshAsync();
        Assert.Equal("Windows", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
        Assert.Equal("Windows", f.Module.CurrentName(AudioFlow.Render));
    }

    [Fact]
    public async Task Emptying_list_does_not_reseed_and_removed_devices_stay_removed_until_add()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B")).WithDefault("A");
        await f.Start();
        await f.Module.RemoveAsync(AudioFlow.Render, "A");
        await f.Module.RemoveAsync(AudioFlow.Render, "B");
        f.System.Disconnect("A", "B");
        f.System.Connect(Speaker("A"));
        await f.Audio.RefreshAsync();
        await Task.Delay(80);
        Assert.Empty(f.Module.Settings.Output.Devices);
        await f.Module.AddAsync(AudioFlow.Render, "A");
        Assert.Equal("A", Assert.Single(f.Module.Settings.Output.Devices).Id);
        Assert.DoesNotContain(f.Module.Settings.Output.Removed, d => d.Id == "A");
    }

    [Fact]
    public async Task New_devices_append_and_edited_order_persists_immediately()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B")).WithDefault("A");
        await f.Start();
        f.System.Connect(Speaker("C"));
        await Until(() => f.Module.Settings.Output.Devices.Count == 3);
        Assert.Equal(["A", "B", "C"], f.Module.Settings.Output.Devices.Select(d => d.Id));
        await f.Module.MoveAsync(AudioFlow.Render, "C", 1);
        Assert.Equal(["A", "C", "B"], f.Settings.Value.Output.Devices.Select(d => d.Id));
        await f.Module.UseAsync(AudioFlow.Render, "B");
        Assert.Equal(["A", "C", "B"], f.Settings.Value.Output.Devices.Select(d => d.Id)); // a pick leaves the order alone
        Assert.Equal("B", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
    }

    [Fact]
    public async Task A_failed_pick_with_switching_off_leaves_the_device_to_windows()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B")).WithDefault("A");
        await f.Start();
        await f.Module.SetEnabledAsync(AudioFlow.Render, false);
        f.System.ReadsBeforeFailure = 0; // the pick's claim fails at once; its release then passes
        await Assert.ThrowsAnyAsync<Exception>(() => f.Module.UseAsync(AudioFlow.Render, "B"));
        Assert.Null(f.System.ReadsBeforeFailure);
        await f.Audio.RefreshAsync();
        Assert.Equal("A", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia)); // Routing let go of the roles
    }

    [Theory]
    [InlineData(AudioFlow.Render)]
    [InlineData(AudioFlow.Capture)]
    public async Task Turning_calls_off_releases_only_communications_and_master_off_releases_flow(AudioFlow flow)
    {
        await using var f = new RoutingFixture();
        f.System.With(new("A", "A", flow, EndpointState.Active), new("B", "B", flow, EndpointState.Active)).WithDefault("A");
        await f.Start();
        await f.Module.SetCallsAsync(flow, false);
        f.System.ChangeDefault("B");
        await f.Audio.RefreshAsync();
        Assert.Equal("A", f.System.DefaultOf(flow, AudioRole.Multimedia));
        Assert.Equal("B", f.System.DefaultOf(flow, AudioRole.Communications));
        await f.Module.SetEnabledAsync(flow, false);
        f.System.ChangeDefault("B");
        await f.Audio.RefreshAsync();
        Assert.Equal("B", f.System.DefaultOf(flow, AudioRole.Multimedia));
        await f.Module.SetEnabledAsync(flow, true);
        Assert.Equal("A", f.System.DefaultOf(flow, AudioRole.Multimedia));
    }

    [Fact]
    public async Task Repairs_role_only_drift_without_output_notice_and_optional_mic_notice_has_quiet_default()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B"), Mic("Real"), Mic("Headset")).WithDefault("A").WithDefault("Real");
        await f.Start();
        f.System.ChangeDefault("B", AudioRole.Communications);
        await f.Audio.RefreshAsync();
        Assert.Empty(f.Notices.Sent);
        f.System.ChangeDefault("Headset", AudioRole.Communications);
        await f.Audio.RefreshAsync();
        await Until(() => f.Notices.Sent.Any(n => n.Kind == "mic-pinned"));
        Assert.Equal(Chippi.Core.Notifications.NoticePlaces.None, f.Notices.PlacesFor("mic-pinned"));
        var count = f.Notices.Sent.Count;
        await f.Audio.RefreshAsync();
        Assert.Equal(count, f.Notices.Sent.Count);
    }

    [Fact]
    public async Task Mic_lists_remain_independent_and_processing_owns_capture_until_it_stops()
    {
        await using var f = new RoutingFixture(e => e.Id == "Transport");
        f.System.With(Speaker("Output"), Mic("Real"), Mic("Transport"), Mic("Backup")).WithDefault("Real");
        await f.Start();
        Assert.DoesNotContain(f.Module.Settings.Microphone.Devices, d => d.Id == "Transport");
        await f.Audio.ClaimRolesAsync(RoleOwner.Processing, AudioFlow.Capture, Enum.GetValues<AudioRole>(), _ => "Transport");
        await f.Module.UseAsync(AudioFlow.Capture, "Backup");
        Assert.Equal("Transport", f.System.DefaultOf(AudioFlow.Capture, AudioRole.Multimedia));
        Assert.Equal("Backup", f.Module.WinningMicrophone);
        await f.Audio.ReleaseRolesAsync(RoleOwner.Processing, AudioFlow.Capture);
        Assert.Equal("Backup", f.System.DefaultOf(AudioFlow.Capture, AudioRole.Multimedia));
        Assert.Equal("Output", Assert.Single(f.Module.Settings.Output.Devices).Id);
    }

    [Fact]
    public async Task Stopping_routing_drops_only_its_ownership_and_stops_engine_with_last_lease()
    {
        var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B"), Mic("Real"), Mic("Transport")).WithDefault("A").WithDefault("Real");
        await f.Start();
        await using var processingLease = f.Audio.Acquire();
        await f.Audio.ClaimRolesAsync(RoleOwner.Processing, AudioFlow.Capture, Enum.GetValues<AudioRole>(), _ => "Transport");
        await f.Module.StopAsync(CancellationToken.None);
        Assert.True(f.Audio.IsRunning);
        Assert.Equal(RoleOwner.Processing, f.Audio.Snapshot.Owner(AudioFlow.Capture, AudioRole.Multimedia));
        Assert.Equal(RoleOwner.None, f.Audio.Snapshot.Owner(AudioFlow.Render, AudioRole.Multimedia));
    }

    [Fact]
    public void Repair_requires_one_name_match_and_cannot_steal_another_list_entry()
    {
        var old = new RoutingList { Seeded = true, Devices = [new("old", "Desk")] };
        var repaired = old.Refresh([Speaker("new", "Desk")], "new");
        Assert.Equal("new", repaired.Devices[0].Id);
        var ambiguous = old.Refresh([Speaker("new1", "Desk"), Speaker("new2", "Desk")], "new1");
        Assert.Equal("old", ambiguous.Devices[0].Id);
        var claimed = old with { Devices = [new("old", "Desk"), new("new", "Desk")] };
        Assert.Equal("old", claimed.Refresh([Speaker("new", "Desk")], "new").Devices[0].Id);
    }

    [Fact]
    public void Settings_round_trip_keeps_exclusions_and_seeding_state()
    {
        var settings = new RoutingSettings { Output = new() { Seeded = true, Removed = [new("id", "Desk")] } };
        var read = JsonSerializer.Deserialize<RoutingSettings>(JsonSerializer.Serialize(settings))!;
        read.Validate();
        Assert.True(read.Output.Seeded);
        Assert.Empty(read.Output.Devices);
        Assert.Equal("id", Assert.Single(read.Output.Removed).Id);
        Assert.Throws<ArgumentException>(() => (read with { Output = read.Output with { Devices = [new("A", "A"), new("a", "A")] } }).Validate());
    }

    [Fact]
    public async Task Relevant_property_changes_refresh_saved_labels_without_resetting_order()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A", "Old label"), Speaker("B")).WithDefault("A");
        await f.Start();
        f.System.With(Speaker("A", "New label"));
        f.System.Raise(DeviceChange.Devices);
        await Until(() => f.Module.Settings.Output.Devices[0].Name == "New label");
        Assert.Equal(["A", "B"], f.Module.Settings.Output.Devices.Select(d => d.Id));
        Assert.Equal("New label", f.Module.CurrentName(AudioFlow.Render));
    }

    [Fact]
    public async Task Startup_failure_disposes_lease_and_does_not_register_working_views()
    {
        var audio = new AudioService(() => throw new InvalidOperationException("Unavailable"), null);
        var f = new RoutingFixture();
        var module = new AudioRoutingModule(audio);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => module.StartAsync(f.Context, CancellationToken.None));
        Assert.False(audio.IsRunning);
        Assert.Empty(f.Ui.Tiles);
        await module.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Removing_a_device_remembers_its_unambiguously_repaired_identity()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("new", "Desk"), Speaker("Other")).WithDefault("Other");
        f.Settings.Value = new() { Output = new() { Seeded = true, Removed = [new("old", "Desk")] } };
        await f.Start();
        Assert.DoesNotContain(f.Module.Settings.Output.Devices, e => e.Id == "new");
        Assert.Equal("new", Assert.Single(f.Module.Settings.Output.Removed).Id);
    }
    [Fact]
    public async Task Bluetooth_burst_emits_one_visible_switch_notice()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("B")).WithDefault("B");
        f.Settings.Value = new() { Output = Saved("A", "B") };
        await f.Start();
        f.System.Connect(Speaker("A"));
        for (var i = 0; i < 20; i++) f.System.Raise(DeviceChange.Devices);
        await Until(() => f.Notices.Sent.Count > 0);
        await Task.Delay(100);
        Assert.Equal("A", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
        Assert.Equal("output-switched", Assert.Single(f.Notices.Sent).Kind);
    }
    [Fact]
    public async Task A_pick_holds_until_a_device_comes_or_goes_and_then_the_list_decides_for_good()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("Headset"), Speaker("Speakers")).WithDefault("Headset");
        await f.Start();
        await f.Module.UseAsync(AudioFlow.Render, "Speakers");
        Assert.Equal("Speakers", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
        Assert.Equal(["Headset", "Speakers"], f.Module.Settings.Output.Devices.Select(d => d.Id)); // the order stays
        f.System.ChangeDefault("Headset"); // Windows drifts: switching puts the pick back
        await f.Audio.RefreshAsync();
        Assert.Equal("Speakers", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));

        f.System.Disconnect("Headset", windowsPicks: "Speakers");
        await Until(() => f.Module.Snapshot.Find("Headset")?.IsActive == false);
        f.System.Connect(Speaker("Headset")); // plugged back in: the list decides, so the headset takes over
        await Until(() => f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia) == "Headset");
        Assert.Equal("Headset", f.Module.InUse(AudioFlow.Render));
    }

    [Fact]
    public async Task Editing_the_list_drops_a_pick_and_the_list_decides_at_once()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B"), Speaker("C")).WithDefault("A");
        await f.Start();
        await f.Module.UseAsync(AudioFlow.Render, "C");
        Assert.Equal("C", f.Module.Order(AudioFlow.Render)[0].Id);
        await f.Module.MoveAsync(AudioFlow.Render, "B", 0);
        Assert.Equal("B", f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia));
        Assert.Equal(["B", "A", "C"], f.Module.Order(AudioFlow.Render).Select(e => e.Id));
    }

    [Fact]
    public async Task A_mic_pick_leads_the_order_mic_processing_reads_until_a_mic_comes_or_goes()
    {
        await using var f = new RoutingFixture(e => e.Id == "Transport");
        f.System.With(Mic("Real"), Mic("Headset"), Mic("Transport")).WithDefault("Real");
        await f.Start();
        await f.Audio.ClaimRolesAsync(RoleOwner.Processing, AudioFlow.Capture, Enum.GetValues<AudioRole>(), _ => "Transport");
        await f.Module.UseAsync(AudioFlow.Capture, "Headset");
        Assert.Equal(["Headset", "Real"], f.Module.Order(AudioFlow.Capture).Select(e => e.Id));
        Assert.Equal("Headset", f.Module.InUse(AudioFlow.Capture)); // the real mic, while the cable holds the default
        f.System.Connect(Mic("Desk"));
        await Until(() => f.Module.Order(AudioFlow.Capture)[0].Id == "Real");
        Assert.Equal("Real", f.Module.InUse(AudioFlow.Capture));
    }

    [Fact]
    public async Task The_output_shortcut_is_unset_until_recorded_then_steps_through_the_connected_outputs()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B"), Speaker("C"), Speaker("Gone")).WithDefault("A");
        await f.Start();
        f.System.Disconnect("Gone");
        await Until(() => f.Module.Snapshot.Find("Gone")?.IsActive == false);
        Assert.Null(f.Hotkey.Current); // nothing registered by default
        var keys = new Mute.MicShortcut(Mute.ShortcutModifiers.Control | Mute.ShortcutModifiers.Alt, 0x4F);
        Assert.True(f.Module.ChangeShortcut(keys));
        Assert.Equal(keys, f.Settings.Value.SwitchShortcut);

        foreach (var next in new[] { "B", "C", "A" }) // past the last connected one, back to the first
        {
            int notices = f.Notices.Sent.Count(n => n.Kind == "output-switched");
            f.Hotkey.Pressed!();
            await Until(() => f.System.DefaultOf(AudioFlow.Render, AudioRole.Multimedia) == next && f.Notices.Sent.Count(n => n.Kind == "output-switched") > notices);
            await Task.Delay(50);
            var said = f.Notices.Sent.Where(n => n.Kind == "output-switched").Skip(notices).ToList();
            Assert.Equal(next, Assert.Single(said).Body); // the bar says which, once
        }
        Assert.Equal(["A", "B", "C", "Gone"], f.Module.Settings.Output.Devices.Select(d => d.Id));

        f.Module.RemoveShortcut();
        Assert.True(f.Hotkey.Disposed);
        Assert.Null(f.Settings.Value.SwitchShortcut);
    }

    [Fact]
    public async Task A_saved_output_shortcut_registers_at_start_and_says_when_its_keys_are_taken()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B")).WithDefault("A");
        f.Settings.Value = new() { SwitchShortcut = new(Mute.ShortcutModifiers.Control | Mute.ShortcutModifiers.Alt, 0x4F) };
        f.Hotkey.Available = false;
        await f.Start();
        Assert.Equal("Shortcut in use", f.Module.ShortcutError);
        f.Hotkey.Available = true;
        Assert.True(f.Module.ChangeShortcut(new(Mute.ShortcutModifiers.Control | Mute.ShortcutModifiers.Alt, 0x50)));
        Assert.Null(f.Module.ShortcutError);
    }

    [Fact]
    public async Task Errors_say_what_failed_then_why()
    {
        await using var f = new RoutingFixture();
        f.System.With(Speaker("A"), Speaker("B")).WithDefault("A");
        await f.Start();
        await Assert.ThrowsAnyAsync<Exception>(() => f.Module.UseAsync(AudioFlow.Render, "Nowhere"));
        Assert.Equal("Switch failed · Device unavailable", f.Module.Error);
        Assert.Equal("Mute failed", AudioRoutingModule.Failed("Mute", new AudioFailure("Mute failed"))); // never "Mute failed · Mute failed"
    }

    private static RoutingList Saved(params string[] ids) => new() { Seeded = true, Devices = ids.Select(id => new PriorityEntry(id, id)).ToList() };
}