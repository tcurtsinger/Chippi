// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Audio;
using Chippi.Audio.Tests;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Volume;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Audio.Tests.FakeDeviceSystem;
using static Chippi.Features.Sound.Tests.RoutingFixture;

namespace Chippi.Features.Sound.Tests;

public sealed class VolumeFixture : IAsyncDisposable
{
    public FakeDeviceSystem System { get; } = new() { AppPolicyAvailable = true };
    public AudioService Audio { get; }
    public AppVolumeModule Module { get; }
    public MemorySettings<AppVolumeSettings> Settings { get; } = new();
    public RoutingUiFake Ui { get; } = new();
    public static AppKey Key { get; } = AppKey.ForExe(@"C:\Apps\Music.exe");
    public VolumeFixture()
    {
        System.With(Speaker("A"), Speaker("B")).WithDefault("A");
        System.WithSession(Session("one", "A", 123));
        Audio = new AudioService(()=>System,null,options:new AudioServiceOptions { Debounce = TimeSpan.FromMilliseconds(10) });
        Module = new(Audio);
    }
    public static AudioSessionInfo Session(string id, string endpoint, int pid, string path = @"C:\Apps\Music.exe", SessionState state = SessionState.Active) =>
        new(id,endpoint,pid,path,"Music",false,state,0.5f,false);
    public Task Start() => Module.StartAsync(new ModuleContext("app-volume",Settings,NullLogger.Instance,new IslandActivityHub().CreatePublisher("app-volume"),Ui),CancellationToken.None);
    public ValueTask DisposeAsync() => new(Module.StopAsync(CancellationToken.None));
}

public sealed class VolumeTests
{
    // The app closes while its output picker is open, then starts again.
    [Fact]
    public async Task A_missing_session_is_distinct_from_an_unsupported_policy_probe()
    {
        await using var f = new VolumeFixture(); await f.Start();
        f.System.EndSession("one"); await f.Audio.RefreshAsync();
        var missing = await f.Audio.GetAppOutputAsync(VolumeFixture.Key);
        Assert.False(missing.HasSession);
        f.System.WithSession(VolumeFixture.Session("again","A",456)); await f.Audio.RefreshAsync();
        f.System.AppPolicyAvailable = false;
        var unsupported = await f.Audio.GetAppOutputAsync(VolumeFixture.Key);
        Assert.True(unsupported.HasSession); Assert.False(unsupported.Available);
    }
    [Fact]
    public async Task App_closing_mid_pick_keeps_the_picker()
    {
        await using var f = new VolumeFixture();
        await f.Start();
        Assert.True(f.Module.CanPickOutput(VolumeFixture.Key));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = f.Audio.InvokeAsync(_ =>
        {
            f.System.EndSession("one"); // queue the session refresh ahead of the pick
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return true;
        });
        await entered.Task;
        Task pick;
        try { pick = f.Module.SetOutputAsync(VolumeFixture.Key, "B"); }
        finally { release.Set(); }
        await closing;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => pick);
        f.System.WithSession(VolumeFixture.Session("again", "A", 130));
        await f.Audio.RefreshAsync();
        await Task.Delay(300);
        Assert.True(f.Module.DevicePickerAvailable, "picker gone for the session after an app closed");
    }

    // The chosen output is unplugged between opening the picker and choosing it.
    [Fact]
    public async Task Output_unplugged_mid_pick_keeps_the_picker()
    {
        await using var f = new VolumeFixture();
        await f.Start();
        f.System.Remove("B");
        await f.Audio.RefreshAsync();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => f.Module.SetOutputAsync(VolumeFixture.Key, "B"));
        await Task.Delay(300);
        Assert.True(f.Module.DevicePickerAvailable, "picker gone for the session after an output was unplugged");
    }
    [Fact]
    public async Task Transport_endpoints_are_never_picker_targets()
    {
        await using var f = new VolumeFixture(); f.System.With(CableInput()).WithDefault("CableIn");
        await f.Start();
        Assert.DoesNotContain(f.Module.Outputs,e=>e.Id=="CableIn");
        Assert.Empty(f.System.Writes);
    }
    [Fact]
    public async Task Start_only_reads_audio_and_registers_shared_surfaces()
    {
        await using var f = new VolumeFixture();
        await f.Start();
        Assert.Empty(f.System.Writes);
        Assert.Empty(f.System.AppOutputWrites);
        Assert.Equal(0.5f,f.System.SessionById("one").Volume);
        var mixer = Assert.Single(f.Ui.Tiles);
        Assert.Equal((AppVolumeModule.TileKey,"App volume",IslandOrbit.Right),(mixer.Spec.Key,mixer.Spec.Name,mixer.Spec.Orbit));
    }

    [Fact]
    public async Task Volume_and_mute_apply_to_all_sessions_and_processes_but_not_other_apps()
    {
        await using var f = new VolumeFixture();
        f.System.WithSession(VolumeFixture.Session("two","B",124));
        f.System.WithSession(VolumeFixture.Session("other","A",125,@"C:\Apps\Other.exe"));
        await f.Start();
        await f.Module.SetVolumeAsync(VolumeFixture.Key,2);
        await f.Module.SetMutedAsync(VolumeFixture.Key,true);
        foreach(var id in new[]{"one","two"}) Assert.Equal((1f,true),(f.System.SessionById(id).Volume,f.System.SessionById(id).Muted));
        Assert.Equal((0.5f,false),(f.System.SessionById("other").Volume,f.System.SessionById("other").Muted));
    }

    [Fact]
    public async Task External_volume_and_mute_and_session_closure_are_reflected()
    {
        await using var f = new VolumeFixture(); await f.Start();
        f.System.SetSessionVolume("one",0.27f); f.System.SetSessionMute("one",true);
        await f.Audio.RefreshAsync();
        Assert.Equal((0.27f,true),(Assert.Single(f.Module.Apps).Volume,Assert.Single(f.Module.Apps).Muted));
        f.System.EndSession("one"); await f.Audio.RefreshAsync();
        Assert.Empty(f.Module.Apps);
    }

    [Fact]
    public async Task A_saved_device_is_reapplied_to_a_new_process_of_the_same_app()
    {
        await using var f = new VolumeFixture(); await f.Start();
        await f.Module.SetOutputAsync(VolumeFixture.Key,"B");
        Assert.Equal("B",f.Settings.Value.Outputs[VolumeFixture.Key.Value]);
        f.System.EndSession("one"); await f.Audio.RefreshAsync();
        await Until(()=>!f.Module.Apps.Any());
        f.System.WithSession(VolumeFixture.Session("new","A",456)); await f.Audio.RefreshAsync();
        await Until(()=>f.System.AppOutputs.GetValueOrDefault(456)=="B");
        Assert.Equal("A",f.System.DefaultOf(AudioFlow.Render,AudioRole.Multimedia));
    }

    [Fact]
    public async Task Device_loss_uses_windows_then_reapplies_the_remembered_output_on_return()
    {
        await using var f = new VolumeFixture(); await f.Start();
        await f.Module.SetOutputAsync(VolumeFixture.Key,"B");
        f.System.Disconnect("B"); await f.Audio.RefreshAsync();
        await Until(()=>f.System.AppOutputs[123] is null);
        Assert.Equal("B",f.Settings.Value.Outputs[VolumeFixture.Key.Value]);
        f.System.Connect(Speaker("B")); await f.Audio.RefreshAsync();
        await Until(()=>f.System.AppOutputs[123]=="B");
    }

    [Fact]
    public async Task Device_choice_writes_every_existing_process_without_claiming_a_live_stream_moved()
    {
        await using var f = new VolumeFixture(); f.System.WithSession(VolumeFixture.Session("two","A",124));
        await f.Start(); await f.Module.SetOutputAsync(VolumeFixture.Key,"B");
        Assert.Equal("B",f.System.AppOutputs[123]); Assert.Equal("B",f.System.AppOutputs[124]);
        Assert.Equal("A",Assert.Single(f.Module.Apps).EndpointIds.Single());
        Assert.Equal("B",f.Module.ChosenOutput(VolumeFixture.Key));
        await f.Module.SetOutputAsync(VolumeFixture.Key,null);
        Assert.Null(f.System.AppOutputs[123]); Assert.Empty(f.Settings.Value.Outputs);
    }

    [Fact]
    public async Task Unsupported_policy_hides_device_picker_but_volume_and_mute_work()
    {
        await using var f = new VolumeFixture(); f.System.AppPolicyAvailable=false; await f.Start();
        Assert.False(f.Module.DevicePickerAvailable);
        await f.Module.SetVolumeAsync(VolumeFixture.Key,0.3f); await f.Module.SetMutedAsync(VolumeFixture.Key,true);
        Assert.Equal((0.3f,true),(f.System.SessionById("one").Volume,f.System.SessionById("one").Muted));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>f.Module.SetOutputAsync(VolumeFixture.Key,"B"));
        Assert.Empty(f.System.AppOutputWrites);
    }

    [Fact]
    public async Task Failed_device_write_disables_picker_without_disabling_volume()
    {
        await using var f = new VolumeFixture(); await f.Start(); f.System.AppPolicyFailsWrite=true;
        await Assert.ThrowsAsync<AppOutputPolicyException>(()=>f.Module.SetOutputAsync(VolumeFixture.Key,"B"));
        Assert.False(f.Module.DevicePickerAvailable); Assert.Empty(f.Settings.Value.Outputs);
        await f.Module.SetVolumeAsync(VolumeFixture.Key,0.4f);
        Assert.Equal(0.4f,f.System.SessionById("one").Volume);
    }

    [Fact]
    public async Task Quiet_apps_and_remember_options_persist_without_changing_audio()
    {
        await using var f = new VolumeFixture(); f.System.EndSession("one");
        f.System.WithSession(VolumeFixture.Session("quiet","A",123,state:SessionState.Inactive));
        await f.Start(); Assert.Single(f.Module.Apps);
        await f.Module.SetShowQuietAsync(false); Assert.Empty(f.Module.Apps);
        await f.Module.SetRememberAsync(false);
        Assert.False(f.Settings.Value.ShowQuietApps); Assert.False(f.Settings.Value.RememberOutputs);
        Assert.Empty(f.System.AppOutputWrites);
    }
}