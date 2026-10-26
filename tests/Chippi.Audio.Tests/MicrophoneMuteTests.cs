// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio.Processing;
using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Audio.Tests;

public sealed class MicrophoneMuteTests : IAsyncDisposable
{
    private readonly FakeDeviceSystem _system = new();
    private readonly FakePipelines _pipelines = new();
    private readonly AudioService _audio;
    private readonly IAsyncDisposable _lease;

    public MicrophoneMuteTests()
    {
        _audio = new(() => _system, null, options: new AudioServiceOptions { Debounce = TimeSpan.FromMilliseconds(10), PipelineFactory = _pipelines.Factory });
        _lease = _audio.Acquire();
    }
    public ValueTask DisposeAsync() => _lease.DisposeAsync();
    private bool Muted(string id) => _system.GetLevel(id).Muted;

    [Fact]
    public async Task Toggle_mutes_every_capture_including_cable_but_never_speakers_then_unmutes_all()
    {
        _system.With(Mic("A"), Mic("B"), CableOutput(), Speaker("Output"));
        await _audio.ToggleMicrophoneMuteAsync();
        Assert.All(new[] { "A", "B", "CableOut" }, id => Assert.True(Muted(id)));
        Assert.False(Muted("Output"));
        Assert.Equal(MicrophoneMuteState.Muted, _audio.Snapshot.MicrophoneMute.State);
        await _audio.ToggleMicrophoneMuteAsync();
        Assert.All(new[] { "A", "B", "CableOut" }, id => Assert.False(Muted(id)));
        Assert.False(_audio.Snapshot.MicrophoneMute.Latched);
    }

    [Fact]
    public async Task Mixed_state_mutes_all_and_external_changes_update_the_snapshot()
    {
        _system.With(Mic("A"), Mic("B"));
        _system.ChangeLevel("A", new(.5f, true));
        await _audio.RefreshAsync();
        Assert.Equal(MicrophoneMuteState.Mixed, _audio.Snapshot.MicrophoneMute.State);
        await _audio.ToggleMicrophoneMuteAsync();
        Assert.True(Muted("B"));
        _system.ChangeLevel("B", new(.5f, false));
        await _audio.RefreshAsync();
        Assert.Equal(MicrophoneMuteState.Mixed, _audio.Snapshot.MicrophoneMute.State);
        Assert.False(Muted("B")); // do not fight an external unmute
    }

    [Fact]
    public async Task Releasing_unmutes_only_mics_Chippi_muted_and_releases_the_latch()
    {
        _system.With(Mic("Already"), Mic("Ours"));
        _system.ChangeLevel("Already", new(.5f, true));
        await _audio.ToggleMicrophoneMuteAsync();
        await _audio.ReleaseMicrophoneMuteAsync();
        Assert.True(Muted("Already"));
        Assert.False(Muted("Ours"));
        _system.Connect(Mic("New"));
        await _audio.RefreshAsync();
        Assert.False(Muted("New"));
    }

    [Fact]
    public async Task New_and_returning_mics_join_the_latch()
    {
        _system.With(Mic("A"));
        await _audio.ToggleMicrophoneMuteAsync();
        _system.Connect(Mic("B"));
        await _audio.RefreshAsync();
        Assert.True(Muted("B"));
        _system.Disconnect("A");
        await _audio.RefreshAsync();
        _system.Connect(Mic("A"));
        await _audio.RefreshAsync();
        Assert.True(Muted("A"));
    }

    [Fact]
    public async Task Partial_failure_is_visible_and_a_later_toggle_can_recover()
    {
        _system.With(Mic("Good"), Mic("Bad"));
        _system.Failing.Add("Bad");
        await _audio.ToggleMicrophoneMuteAsync();
        // Good's mute queued a level refresh that retries Bad. Let it run while Bad still fails,
        // or it mutes Bad after Failing.Clear() and the next toggle unmutes both.
        await _audio.RefreshAsync();
        Assert.Equal(MicrophoneMuteState.Mixed, _audio.Snapshot.MicrophoneMute.State);
        Assert.Contains("Bad", _audio.Snapshot.MicrophoneMute.Failed);
        _system.Failing.Clear();
        await _audio.ToggleMicrophoneMuteAsync();
        Assert.Equal(MicrophoneMuteState.Muted, _audio.Snapshot.MicrophoneMute.State);
        Assert.Empty(_audio.Snapshot.MicrophoneMute.Failed);
    }

    [Fact]
    public async Task No_microphones_does_not_claim_all_muted()
    {
        await _audio.ToggleMicrophoneMuteAsync();
        Assert.Equal(MicrophoneMuteState.NoMicrophone, _audio.Snapshot.MicrophoneMute.State);
    }

    [Fact]
    public async Task Pipeline_rebuild_does_not_unmute_source_or_transport()
    {
        _system.With(Mic("A"), CableInput(), CableOutput()).WithDefault("A");
        await _audio.ToggleMicrophoneMuteAsync();
        var request = new ProcessingRequest(active => active.Last().Id, new(), false, _ => { });
        await _audio.SetProcessingAsync(request);
        _system.Connect(Mic("B"));
        await _audio.RefreshAsync();
        Assert.Equal("B", _audio.Snapshot.Processing.SourceId);
        Assert.Equal(2, _pipelines.Created.Count);
        Assert.True(Muted("A"));
        Assert.True(Muted("B"));
        Assert.True(Muted("CableOut"));
    }

    [Fact]
    public async Task Last_lease_restores_only_owned_mics()
    {
        _system.With(Mic("A"), Mic("PreMuted"));
        _system.ChangeLevel("PreMuted", new(.5f, true));
        await _audio.ToggleMicrophoneMuteAsync();
        await _lease.DisposeAsync();
        Assert.False(Muted("A"));
        Assert.True(Muted("PreMuted"));
    }
}