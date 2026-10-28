// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Audio.Processing;
using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Audio.Tests;

public sealed class ProcessingTests : IAsyncDisposable
{
    private static readonly AudioRole[] All = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];

    private readonly FakeDeviceSystem _sys = new();
    private readonly FakePipelines _pipelines = new();
    private readonly ConcurrentQueue<ProcessingMarker?> _markers = new();
    private readonly AudioService _service;
    private readonly IAsyncDisposable _lease;

    public ProcessingTests()
    {
        _service = new AudioService(() => _sys, null, options: new AudioServiceOptions
        {
            Debounce = TimeSpan.FromMilliseconds(10),
            PipelineFactory = _pipelines.Factory,
        });
        _lease = _service.Acquire();
    }

    public async ValueTask DisposeAsync() => await _lease.DisposeAsync();

    private ProcessingMarker? LastMarker => _markers.LastOrDefault();

    private ProcessingRequest Request(bool setDefault = true, params string[] pinned) => new(
        active => PriorityPolicy.Resolve(pinned.Select(id => new PriorityEntry(id, id)).ToList(), active) ?? active.FirstOrDefault()?.Id,
        new MicSettings(), setDefault, _markers.Enqueue);

    private string? DefaultMic(AudioRole role = AudioRole.Multimedia) => _sys.DefaultOf(AudioFlow.Capture, role);

    private ProcessingStatus Status => _service.Snapshot.Processing;

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Live_processing_cleans_the_real_mic_into_the_cable_and_makes_cable_output_the_default()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");

        var status = await _service.SetProcessingAsync(Request());

        Assert.Equal(new ProcessingStatus(ProcessingState.Live, SourceId: "Real"), status);
        var pipeline = Assert.Single(_pipelines.Created);
        Assert.Equal(("Real", "CableIn", true), (pipeline.Source, pipeline.Sink, pipeline.Started));
        Assert.All(All, r => Assert.Equal("CableOut", DefaultMic(r)));
        Assert.Equal(RoleOwner.Processing, _service.Snapshot.Owner(AudioFlow.Capture, AudioRole.Console));
        var marker = Assert.IsType<ProcessingMarker>(Assert.Single(_markers));
        Assert.Equal("CableOut", marker.CableCaptureId);
        Assert.All(All, r => Assert.Equal("Real", marker.Replaced[r]));
    }

    [Fact]
    public async Task The_cable_is_never_the_source_even_as_the_only_or_first_mic()
    {
        _sys.With(CableOutput(), CableInput(), Mic("Real"));
        await _service.SetProcessingAsync(Request());
        Assert.Equal("Real", Assert.Single(_pipelines.Created).Source);
    }

    [Fact]
    public async Task A_real_mic_named_like_the_cable_is_still_a_mic()
    {
        _sys.With(Mic("Room", "Cable Room Mic"), CableInput(), CableOutput());
        await _service.SetProcessingAsync(Request());
        Assert.Equal("Room", Assert.Single(_pipelines.Created).Source);
    }

    [Fact]
    public async Task Without_set_as_default_the_roles_stay_with_windows()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await _service.SetProcessingAsync(Request(setDefault: false));
        Assert.Equal("Real", DefaultMic());
        Assert.Empty(_markers);
    }

    [Fact]
    public async Task The_pipeline_follows_the_winning_mic()
    {
        _sys.With(Mic("Desk"), CableInput(), CableOutput()).WithDefault("Desk");
        await _service.SetProcessingAsync(Request(true, "Headset", "Desk"));

        _sys.Connect(Mic("Headset"));

        await WaitUntil(() => _pipelines.Live?.Source == "Headset");
        Assert.True(_pipelines.Created[0].Disposed);
        Assert.Equal("CableOut", DefaultMic()); // a rebuild never hands the roles back
        Assert.Single(_markers);
    }

    [Fact]
    public async Task No_mic_stops_and_gives_the_roles_back()
    {
        _sys.With(Mic("Real"), Speaker("Spk"), CableInput(), CableOutput()).WithDefault("Real");
        await _service.SetProcessingAsync(Request());

        _sys.Remove("Real");

        await WaitUntil(() => Status.Reason == StopReason.NoMicrophone);
        Assert.Equal(ProcessingState.Stopped, Status.State);
        Assert.True(_pipelines.Created[0].Disposed);
        Assert.NotNull(LastMarker); // nothing real to put back yet: the marker waits for a mic
        Assert.NotEqual(RoleOwner.Processing, _service.Snapshot.Owner(AudioFlow.Capture, AudioRole.Console));
    }

    [Fact]
    public async Task A_failed_pipeline_stays_stopped_until_its_mic_returns()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await _service.SetProcessingAsync(Request());

        _pipelines.Created[0].Fail("Device invalidated");

        await WaitUntil(() => Status.Reason == StopReason.Failed);
        Assert.Equal("Device invalidated", Status.Error);
        Assert.All(All, r => Assert.Equal("Real", DefaultMic(r)));
        await _service.RefreshAsync();
        Assert.Single(_pipelines.Created); // no retry loop

        _sys.Disconnect("Real", windowsPicks: "CableOut");
        await WaitUntil(() => Status.Reason == StopReason.NoMicrophone);
        _sys.Connect(Mic("Real"));

        await WaitUntil(() => Status.State == ProcessingState.Live);
        Assert.Equal(2, _pipelines.Created.Count);
    }

    [Fact]
    public async Task A_pipeline_that_cannot_start_never_takes_the_roles()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        _pipelines.FailOnStart.Add("Real");

        var status = await _service.SetProcessingAsync(Request());

        Assert.Equal((ProcessingState.Stopped, StopReason.Failed, "Device in use"), (status.State, status.Reason, status.Error));
        Assert.True(_pipelines.Created[0].Disposed);
        Assert.Equal("Real", DefaultMic());
        Assert.Empty(_markers);
    }

    [Fact]
    public async Task Switching_on_again_retries_after_a_failure()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput());
        _pipelines.FailOnStart.Add("Real");
        await _service.SetProcessingAsync(Request());
        _pipelines.FailOnStart.Clear();

        var status = await _service.SetProcessingAsync(Request());

        Assert.Equal(ProcessingState.Live, status.State);
    }

    [Fact]
    public async Task Vb_cable_removed_while_running_stops_and_restores_the_real_mic()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await _service.SetProcessingAsync(Request());

        _sys.Remove("CableIn", "CableOut");

        await WaitUntil(() => Status.Reason == StopReason.NoDriver);
        Assert.True(_pipelines.Created[0].Disposed);
        Assert.All(All, r => Assert.Equal("Real", DefaultMic(r)));
        Assert.Null(LastMarker);
    }

    [Fact]
    public async Task A_disabled_cable_reads_as_driver_disabled()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput() with { State = EndpointState.Disabled });
        var status = await _service.SetProcessingAsync(Request());
        Assert.Equal(StopReason.DriverDisabled, status.Reason);
        Assert.Empty(_pipelines.Created);
    }

    [Fact]
    public async Task Two_cables_read_as_more_than_one_cable_not_no_driver()
    {
        const string second = @"{2}.\\?\root#media#0001#{6994ad04-93ef-11d0-a3cc-00a0c9223196}";
        _sys.With(Mic("Real"), CableInput(), CableOutput(), CableInput("In2", second), CableOutput("Out2", second));
        var status = await _service.SetProcessingAsync(Request());
        Assert.Equal(StopReason.MoreThanOneCable, status.Reason);
        Assert.Empty(_pipelines.Created);
    }

    [Fact]
    public async Task One_install_with_cable_in_16ch_goes_live_on_cable_input()
    {
        _sys.With(Mic("Real"), CableIn16(), CableInput(), CableOutput()).WithDefault("Real");
        var status = await _service.SetProcessingAsync(Request());
        Assert.Equal(ProcessingState.Live, status.State);
        Assert.Single(_pipelines.Created);
    }

    [Fact]
    public async Task Without_vb_cable_nothing_starts()
    {
        _sys.With(Mic("Real"));
        var status = await _service.SetProcessingAsync(Request());
        Assert.Equal(StopReason.NoDriver, status.Reason);
        Assert.Empty(_pipelines.Created);
    }

    [Fact]
    public async Task Off_restores_what_processing_replaced_when_routing_isnt_running()
    {
        _sys.With(Mic("Real"), Mic("Other"), CableInput(), CableOutput()).WithDefault("Real").WithDefault("Other", AudioRole.Communications);
        await _service.SetProcessingAsync(Request());

        var status = await _service.SetProcessingAsync(null);

        Assert.Equal(ProcessingStatus.Off, status);
        Assert.Equal("Real", DefaultMic(AudioRole.Console));
        Assert.Equal("Other", DefaultMic(AudioRole.Communications));
        Assert.True(_pipelines.Created[0].Disposed);
        Assert.Null(LastMarker);
    }

    [Fact]
    public async Task Off_hands_the_capture_roles_back_to_routing()
    {
        _sys.With(Mic("Pinned"), Mic("Other"), CableInput(), CableOutput()).WithDefault("Other");
        await _service.ClaimRolesAsync(RoleOwner.Routing, AudioFlow.Capture, All, active => active.Any(a => a.Id == "Pinned") ? "Pinned" : null);
        await _service.SetProcessingAsync(Request(true, "Pinned"));
        Assert.Equal("CableOut", DefaultMic());

        await _service.SetProcessingAsync(null);

        Assert.All(All, r => Assert.Equal("Pinned", DefaultMic(r)));
    }

    [Fact]
    public async Task Stopping_the_engine_stops_processing_and_restores_the_mic()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await _service.SetProcessingAsync(Request());

        await _lease.DisposeAsync();

        Assert.True(_pipelines.Created[0].Disposed);
        Assert.Equal("Real", DefaultMic());
        Assert.Null(LastMarker);
    }

    [Fact]
    public async Task Recovery_after_a_crash_puts_back_roles_still_on_the_cable()
    {
        _sys.With(Mic("Real"), Mic("Phone"), CableInput(), CableOutput())
            .WithDefault("CableOut", AudioRole.Console, AudioRole.Multimedia)
            .WithDefault("Phone", AudioRole.Communications);
        var marker = new ProcessingMarker("CableOut", All.ToDictionary(r => r, _ => "Real"));

        var restored = await _service.RecoverProcessingAsync(marker);

        Assert.True(restored);
        Assert.Equal("Real", DefaultMic(AudioRole.Console));
        Assert.Equal("Real", DefaultMic(AudioRole.Multimedia));
        Assert.Equal("Phone", DefaultMic(AudioRole.Communications)); // the user changed it since: left alone
    }

    [Fact]
    public async Task Recovery_leaves_roles_the_cable_no_longer_holds()
    {
        _sys.With(Mic("Real"), Mic("Other"), CableInput(), CableOutput()).WithDefault("Other");
        var restored = await _service.RecoverProcessingAsync(new("CableOut", All.ToDictionary(r => r, _ => "Real")));
        Assert.True(restored);
        Assert.Equal("Other", DefaultMic());
    }

    // Karl's S3 review: the replaced mic is gone, another real one is there.
    [Fact]
    public async Task Off_moves_the_roles_to_another_real_mic_when_the_replaced_one_is_gone()
    {
        _sys.With(Mic("Desk"), Mic("Headset"), CableInput(), CableOutput()).WithDefault("Desk");
        await _service.SetProcessingAsync(Request(true, "Headset"));
        _sys.Remove("Desk");
        await WaitUntil(() => _service.Snapshot.Endpoints.All(e => e.Id != "Desk"));

        await _service.SetProcessingAsync(null);

        Assert.All(All, r => Assert.Equal("Headset", DefaultMic(r)));
        Assert.Null(LastMarker);
    }

    [Fact]
    public async Task Recovery_uses_another_real_mic_when_the_replaced_one_is_gone()
    {
        _sys.With(Mic("Headset"), CableInput(), CableOutput()).WithDefault("CableOut");
        var restored = await _service.RecoverProcessingAsync(new("CableOut", All.ToDictionary(r => r, _ => "Desk")));
        Assert.True(restored);
        Assert.All(All, r => Assert.Equal("Headset", DefaultMic(r)));
    }

    [Fact]
    public async Task Recovery_with_no_real_mic_reports_unsettled_and_leaves_the_cable()
    {
        _sys.With(CableInput(), CableOutput()).WithDefault("CableOut");
        var restored = await _service.RecoverProcessingAsync(new("CableOut", All.ToDictionary(r => r, _ => "Desk")));
        Assert.False(restored);
        Assert.Equal("CableOut", DefaultMic());
    }

    // Karl's 30b56a5 review: relaunch with the mic still unplugged, processing off, then plug it in.
    [Fact]
    public async Task Unsettled_startup_recovery_finishes_when_a_real_mic_arrives()
    {
        _sys.With(CableInput(), CableOutput()).WithDefault("CableOut");
        var restored = await _service.RecoverProcessingAsync(new("CableOut", All.ToDictionary(r => r, _ => "Desk")), _markers.Enqueue);
        Assert.False(restored);

        _sys.Connect(Mic("Desk"));

        await WaitUntil(() => All.All(r => DefaultMic(r) == "Desk"));
        await WaitUntil(() => _markers.Count == 1 && LastMarker is null);
    }

    // Karl's 30b56a5 review: Audio routing installed, its mic list empty or unplugged.
    [Fact]
    public async Task Off_restores_the_mic_when_routing_claims_but_has_no_target()
    {
        _sys.With(Mic("Desk"), CableInput(), CableOutput()).WithDefault("Desk");
        await _service.ClaimRolesAsync(RoleOwner.Routing, AudioFlow.Capture, All, _ => null);
        await _service.SetProcessingAsync(Request());
        Assert.Equal("CableOut", DefaultMic());

        await _service.SetProcessingAsync(null);

        Assert.All(All, r => Assert.Equal("Desk", DefaultMic(r)));
        Assert.Null(LastMarker);
    }

    [Fact]
    public async Task With_no_real_mic_left_the_marker_stays_until_one_returns()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await _service.SetProcessingAsync(Request());
        _sys.SetState("Real", EndpointState.Unplugged);
        await WaitUntil(() => Status.Reason == StopReason.NoMicrophone);
        await _service.SetProcessingAsync(null);
        Assert.Equal("CableOut", DefaultMic());
        Assert.NotNull(LastMarker);

        _sys.SetState("Real", EndpointState.Active);

        await WaitUntil(() => DefaultMic() == "Real");
        Assert.All(All, r => Assert.Equal("Real", DefaultMic(r)));
        await WaitUntil(() => LastMarker is null);
    }

    [Fact]
    public async Task Meters_carry_the_pipeline_levels_while_live()
    {
        _sys.With(Mic("Real"), CableInput(), CableOutput());
        await _service.SetProcessingAsync(Request());
        var readings = new ConcurrentQueue<MeterReadings>();
        _service.MetersChanged += (_, r) => readings.Enqueue(r);

        using var watch = _service.WatchMeters([], []);

        await WaitUntil(() => !readings.IsEmpty);
        Assert.Equal((0.3f, 0.2f), (readings.First().ProcessingInput, readings.First().ProcessingOutput));
    }
}
