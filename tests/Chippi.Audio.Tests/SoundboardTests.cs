// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio.Processing;
using Chippi.Audio.Soundboard;
using NAudio.Wave;
using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Audio.Tests;

/// <summary>No Windows audio calls: endpoint, voice pipeline, playback stream and clock are fakes.</summary>
public sealed class SoundboardTests
{
    private sealed class FakePlayback(SoundboardPlaybackRequest request) : ISoundboardPlayback
    {
        public SoundboardPlaybackRequest Request { get; } = request;
        public bool Started, Disposed;
        public bool FailStart;
        public bool FailDispose;
        public TimeSpan Position { get; set; }
        public (float Send, float Hear) Levels { get; private set; } = (request.Send, request.Hear);
        public event Action<Exception?>? Stopped;
        public void Start() { if (FailStart) throw new AudioFailure("Playback failed"); Started = true; }
        public void SetLevels(float send, float hear) => Levels = (send, hear);
        public void Finish(Exception? error = null) => Stopped?.Invoke(error);
        public void Dispose() { Disposed = true; if (FailDispose) throw new AudioFailure("Dispose failed"); }
    }

    private sealed class Clock : TimeProvider
    {
        public FakeTimer? Timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            Timer = new(callback, state);
    }

    private sealed class FakeTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Active, Disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) { Active = dueTime != Timeout.InfiniteTimeSpan; return true; }
        public void Tick() { if (Active && !Disposed) callback(state); }
        public void Dispose() { Disposed = true; Active = false; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public FakeDeviceSystem System { get; } = new();
        public FakePipelines Voices { get; } = new();
        public List<FakePlayback> Plays { get; } = [];
        public Clock Clock { get; } = new();
        public AudioService Audio { get; }
        public SoundboardPlayer Player { get; }
        public bool FailStart;
        public bool FailDispose;
        public Fixture(bool devices = true)
        {
            if (devices)
                System.With(Mic("Mic"), CableInput(), CableOutput(), Speaker("Speakers"), Speaker("Earbuds"))
                    .WithDefault("Mic").WithDefault("Speakers");
            Audio = new(() => System, null, options: new() { PipelineFactory = Voices.Factory, Debounce = TimeSpan.FromHours(1) });
            Player = new(Audio, factory: request =>
            {
                var playback = new FakePlayback(request) { FailStart = FailStart, FailDispose = FailDispose };
                Plays.Add(playback);
                return playback;
            }, time: Clock);
        }
        public Task Play(string id = "clip", bool preview = false) => Player.PlayAsync(id, "prepared.wav", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), preview);
        public async Task Flush() { await Audio.InvokeAsync(_ => true); await Audio.InvokeAsync(_ => true); }
        public ValueTask DisposeAsync() => Player.DisposeAsync();
    }

    [Fact]
    public async Task Initializes_unprocessed_voice_without_taking_defaults_or_marking_processing_on()
    {
        await using var f = new Fixture();
        await f.Player.InitializeAsync();
        var voice = Assert.Single(f.Voices.Created);
        Assert.False(voice.Settings.Suppression || voice.Settings.Gate || voice.Settings.Agc);
        Assert.Equal(("Mic", "CableIn"), (voice.Source, voice.Sink));
        Assert.Equal(ProcessingState.Off, f.Audio.Snapshot.Processing.State);
        Assert.Equal(ProcessingState.Live, f.Audio.Snapshot.SoundboardVoice.State);
        Assert.Empty(f.System.Writes);
        Assert.Empty(f.Plays);
    }

    [Fact]
    public async Task Processing_and_bypass_share_one_voice_pipeline_in_both_directions()
    {
        await using var f = new Fixture();
        await f.Player.InitializeAsync();
        var bypass = f.Voices.Live!;
        var settings = new MicSettings();
        await f.Audio.SetProcessingAsync(new(active => active[0].Id, settings, false, _ => { }));
        Assert.True(bypass.Disposed);
        Assert.Equal(settings, f.Voices.Live!.Settings);
        Assert.Single(f.Voices.Created, p => !p.Disposed);
        await f.Play();
        Assert.Equal(2, f.Voices.Created.Count); // A sound never creates a second capture chain.
        await f.Audio.SetProcessingAsync(null);
        Assert.False(f.Voices.Live!.Settings.Suppression);
        Assert.Single(f.Voices.Created, p => !p.Disposed);
        Assert.True(f.Player.State.IsPlaying);
        Assert.Equal(ProcessingState.Off, f.Audio.Snapshot.Processing.State);
        Assert.Empty(f.System.Writes);
    }

    [Fact]
    public async Task Removing_soundboard_does_not_stop_an_independent_processing_request()
    {
        await using var f = new Fixture();
        await using var processingLease = f.Audio.Acquire();
        await f.Player.InitializeAsync();
        await f.Audio.SetProcessingAsync(new(active => active[0].Id, new(), false, _ => { }));
        var voice = f.Voices.Live!;
        await f.Player.DisposeAsync();
        Assert.False(voice.Disposed);
        Assert.Equal(ProcessingState.Live, f.Audio.Snapshot.Processing.State);
    }

    [Fact]
    public async Task Disposal_releases_bypass_voice_and_timer_even_when_the_clip_disposal_fails()
    {
        await using var f = new Fixture();
        await using var otherModuleLease = f.Audio.Acquire();
        await f.Play();
        f.Plays[0].FailDispose = true;
        await Assert.ThrowsAsync<AudioFailure>(async () => await f.Player.DisposeAsync());
        Assert.True(f.Voices.Created[0].Disposed);
        Assert.Equal(ProcessingState.Off, f.Audio.Snapshot.SoundboardVoice.State);
        Assert.True(f.Clock.Timer!.Disposed);
        Assert.False(f.Player.State.IsPlaying);
        Assert.True(f.Audio.IsRunning);
    }

    [Fact]
    public async Task Next_clip_replaces_previous_and_old_completion_cannot_stop_new_clip()
    {
        await using var f = new Fixture();
        await f.Play("first");
        var first = f.Plays[0];
        await f.Play("second");
        Assert.True(first.Disposed);
        Assert.Equal("second", f.Player.State.ClipId);
        first.Finish(new AudioFailure("Late failure"));
        await f.Flush();
        Assert.True(f.Player.State.IsPlaying);
        Assert.Null(f.Player.State.Error);
        Assert.Equal("second", f.Player.State.ClipId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_and_replacement_publish_a_terminal_state_when_old_stream_disposal_fails(bool replace)
    {
        await using var f = new Fixture();
        await f.Play("first");
        var first = f.Plays[0];
        first.FailDispose = true;
        await Assert.ThrowsAsync<AudioFailure>(() => replace ? f.Play("second") : f.Player.StopAsync());
        Assert.False(f.Player.State.IsPlaying);
        Assert.Equal("Dispose failed", f.Player.State.Error);
        Assert.False(f.Clock.Timer!.Active);
        Assert.True(first.Disposed);
        Assert.Single(f.Plays);

        await f.Play("retry");
        first.Finish();
        await f.Flush();
        Assert.True(f.Player.State.IsPlaying);
        Assert.Equal("retry", f.Player.State.ClipId);
        Assert.Null(f.Player.State.Error);
    }

    [Fact]
    public async Task Failed_start_reports_terminal_state_even_when_partial_stream_disposal_also_fails()
    {
        await using var f = new Fixture { FailStart = true, FailDispose = true };
        await Assert.ThrowsAsync<AudioFailure>(() => f.Play());
        Assert.False(f.Player.State.IsPlaying);
        Assert.Equal("Playback failed", f.Player.State.Error);
        Assert.False(f.Clock.Timer!.Active);
        Assert.True(Assert.Single(f.Plays).Disposed);
    }

    [Fact]
    public async Task Send_and_hear_are_independent_and_stop_leaves_voice_connected()
    {
        await using var f = new Fixture();
        await f.Player.SetLevelsAsync(.2f, .7f);
        await f.Play();
        var play = Assert.Single(f.Plays);
        Assert.Equal(("CableIn", "Speakers"), (play.Request.SendDeviceId, play.Request.MonitorDeviceId));
        Assert.Equal((.2f, .7f), play.Levels);
        await f.Player.SetLevelsAsync(0, 1);
        Assert.Equal((0f, 1f), play.Levels);
        await f.Player.StopAsync();
        Assert.True(play.Disposed);
        Assert.False(f.Player.State.IsPlaying);
        Assert.NotNull(f.Voices.Live);
        Assert.False(f.Clock.Timer!.Active);
        Assert.Empty(f.System.Writes);
    }

    [Fact]
    public async Task Local_preview_works_without_a_driver_or_microphone_and_never_sends_to_mic()
    {
        await using var f = new Fixture(false);
        f.System.With(Speaker("Speakers")).WithDefault("Speakers");
        await f.Play(preview: true);
        Assert.Null(f.Plays[0].Request.SendDeviceId);
        Assert.Equal("Speakers", f.Plays[0].Request.MonitorDeviceId);
        Assert.True(f.Player.State.IsPreview);
        Assert.Empty(f.Voices.Created);
        Assert.Empty(f.System.Writes);
    }

    [Fact]
    public async Task Clips_can_send_without_a_physical_microphone()
    {
        await using var f = new Fixture(false);
        f.System.With(CableInput(), CableOutput(), Speaker("Speakers")).WithDefault("Speakers");
        await f.Play();
        Assert.Equal("CableIn", Assert.Single(f.Plays).Request.SendDeviceId);
        Assert.Equal(StopReason.NoMicrophone, f.Audio.Snapshot.SoundboardVoice.Reason);
    }

    [Fact]
    public async Task A_default_output_that_is_the_cable_is_never_used_for_monitoring()
    {
        await using var f = new Fixture();
        f.System.ChangeDefault("CableIn");
        await f.Play();
        Assert.Equal("CableIn", f.Plays[0].Request.SendDeviceId);
        Assert.Null(f.Plays[0].Request.MonitorDeviceId);
        await Assert.ThrowsAsync<AudioFailure>(() => f.Play(preview: true));
        Assert.False(f.Player.State.IsPlaying);
        Assert.Equal("No playback device", f.Player.State.Error);
    }

    [Theory]
    [InlineData("CableOut")]
    [InlineData("CableIn")]
    public async Task External_virtual_endpoint_mute_is_never_unmuted_by_play(string endpoint)
    {
        await using var f = new Fixture();
        f.System.ChangeLevel(endpoint, new(1, true));
        await Assert.ThrowsAsync<AudioFailure>(() => f.Play());
        Assert.True(f.System.GetLevel(endpoint).Muted);
        Assert.Empty(f.Plays);
        await f.Play(preview: true); // A local preview remains possible.
        Assert.Null(f.Plays[0].Request.SendDeviceId);
    }

    [Fact]
    public async Task Physical_mic_mute_silences_voice_without_stopping_clips_or_muting_virtual_audio()
    {
        await using var f = new Fixture();
        await f.Play();
        await f.Audio.ToggleMicrophoneMuteAsync();
        await f.Flush();
        Assert.True(f.System.GetLevel("Mic").Muted);
        Assert.True(f.Voices.Live!.Settings.VoiceMuted);
        Assert.False(f.System.GetLevel("CableOut").Muted);
        Assert.True(f.Player.State.IsPlaying);
        await f.Audio.ToggleMicrophoneMuteAsync();
        Assert.False(f.Voices.Live!.Settings.VoiceMuted);
    }

    [Fact]
    public async Task Output_switch_continues_at_current_position_without_undoing_Windows_choice()
    {
        await using var f = new Fixture();
        await f.Play();
        f.Plays[0].Position = TimeSpan.FromSeconds(1);
        f.System.ChangeDefault("Earbuds");
        await f.Audio.RefreshAsync();
        await f.Flush();
        Assert.Equal(2, f.Plays.Count);
        Assert.True(f.Plays[0].Disposed);
        Assert.Equal("Earbuds", f.Plays[1].Request.MonitorDeviceId);
        Assert.Equal(TimeSpan.FromSeconds(3), f.Plays[1].Request.Start);
        Assert.Equal(TimeSpan.FromSeconds(5), f.Plays[1].Request.End);
        Assert.Empty(f.System.Writes);
        Assert.True(f.Player.State.IsPlaying);
    }

    [Fact]
    public async Task Cable_removal_stops_clip_once_with_an_actionable_error()
    {
        await using var f = new Fixture();
        await f.Play();
        f.System.Remove("CableIn");
        await f.Audio.RefreshAsync();
        await f.Flush();
        Assert.False(f.Player.State.IsPlaying);
        Assert.NotNull(f.Player.State.Error);
        Assert.True(f.Plays[0].Disposed);
        Assert.Single(f.Plays);
    }

    [Fact]
    public async Task Playback_completion_stops_polling_and_reports_the_finished_duration()
    {
        await using var f = new Fixture();
        await f.Play();
        f.Plays[0].Position = TimeSpan.FromSeconds(1);
        f.Clock.Timer!.Tick();
        await f.Flush();
        Assert.Equal(TimeSpan.FromSeconds(1), f.Player.State.Position);
        f.Plays[0].Finish();
        await f.Flush();
        Assert.False(f.Player.State.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(3), f.Player.State.Position);
        Assert.False(f.Clock.Timer.Active);
        Assert.NotNull(f.Voices.Live);
    }

    [Fact]
    public async Task A_failed_start_disposes_the_partial_clip_and_can_be_retried()
    {
        await using var f = new Fixture { FailStart = true };
        await Assert.ThrowsAsync<AudioFailure>(() => f.Play());
        Assert.True(f.Plays[0].Disposed);
        Assert.Equal("Playback failed", f.Player.State.Error);
        f.FailStart = false;
        await f.Play();
        Assert.True(f.Player.State.IsPlaying);
        Assert.Null(f.Player.State.Error);
    }

    [Fact]
    public async Task Cancellation_and_invalid_ranges_never_start_streams()
    {
        await using var f = new Fixture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Player.PlayAsync("clip", "file.wav", TimeSpan.Zero, TimeSpan.FromSeconds(1), ct: new(true)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Player.PlayAsync("clip", "file.wav", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
        Assert.Empty(f.Plays);
        Assert.Empty(f.Voices.Created);
        Assert.False(f.Audio.IsRunning);
    }

    [Fact]
    public async Task A_later_stop_cancels_clips_waiting_for_initialization()
    {
        var system = new FakeDeviceSystem().With(Mic("Mic"), CableInput(), CableOutput(), Speaker("Speakers"))
            .WithDefault("Mic").WithDefault("Speakers");
        var voices = new FakePipelines();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var audio = new AudioService(() =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fixture release missing.");
            return system;
        }, null, options: new() { PipelineFactory = voices.Factory });
        int plays = 0;
        await using var player = new SoundboardPlayer(audio, factory: request => { plays++; return new FakePlayback(request); });
        var first = player.PlayAsync("first", "file.wav", TimeSpan.Zero, TimeSpan.FromSeconds(2));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = player.PlayAsync("second", "file.wav", TimeSpan.Zero, TimeSpan.FromSeconds(2));
        var stop = player.StopAsync();
        release.Set();
        await Task.WhenAll(first, second, stop);
        Assert.Equal(0, plays);
        Assert.False(player.State.IsPlaying);
        await player.PlayAsync("third", "file.wav", TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.Equal(1, plays);
        Assert.Equal("third", player.State.ClipId);
    }

    private sealed class ConstantSamples(int channels = 2) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, .5f, offset, count);
            return count;
        }
    }

    private sealed class FiniteSamples(int channels, float[] samples) : ISampleProvider
    {
        private int _position;
        public int LargestRead { get; private set; }
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
        public int Read(float[] buffer, int offset, int count)
        {
            LargestRead = Math.Max(LargestRead, count);
            int read = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, read);
            _position += read;
            return read;
        }
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(1, 8)]
    [InlineData(2, 6)]
    [InlineData(2, 8)]
    public void Surround_output_plays_clips_on_front_channels_and_leaves_the_rest_silent(int sourceChannels, int outputChannels)
    {
        float[] input = Enumerable.Range(1, sourceChannels * 4).Select(i => i / 10f).ToArray();
        var output = new FrontChannelsSampleProvider(new FiniteSamples(sourceChannels, input), outputChannels);
        var samples = Enumerable.Repeat(-1f, outputChannels * 4 + 5).ToArray();
        Assert.Equal(outputChannels, output.WaveFormat.Channels);
        Assert.Equal(48000, output.WaveFormat.SampleRate);
        Assert.Equal(outputChannels * 4, output.Read(samples, 2, outputChannels * 4 + 1));
        for (int frame = 0; frame < 4; frame++)
        {
            Assert.Equal(input[frame * sourceChannels], samples[2 + frame * outputChannels]);
            Assert.Equal(input[frame * sourceChannels + sourceChannels - 1], samples[3 + frame * outputChannels]);
            for (int channel = 2; channel < outputChannels; channel++)
                Assert.Equal(0f, samples[2 + frame * outputChannels + channel]);
        }
        Assert.Equal([-1f, -1f], samples[..2]);
        Assert.Equal([-1f, -1f, -1f], samples[(outputChannels * 4 + 2)..]);
        Assert.Equal(0, output.Read(samples, 0, samples.Length));
    }

    [Fact]
    public void Surround_mapping_streams_large_reads_with_bounded_scratch_space_and_stops_at_source_end()
    {
        var input = new FiniteSamples(2, Enumerable.Repeat(.5f, 2049 * 2).ToArray());
        var output = new FrontChannelsSampleProvider(input, 8);
        var samples = Enumerable.Repeat(-1f, 2050 * 8).ToArray();
        Assert.Equal(2049 * 8, output.Read(samples, 0, samples.Length));
        Assert.Equal(2048, input.LargestRead);
        Assert.Equal([.5f, .5f, 0f, 0f, 0f, 0f, 0f, 0f], samples[(2048 * 8)..(2049 * 8)]);
        Assert.All(samples[(2049 * 8)..], value => Assert.Equal(-1f, value));
        Assert.Equal(0, output.Read(samples, 0, samples.Length));
    }

    [Fact]
    public void Clip_reader_stops_at_trim_end_without_loading_or_reading_the_remaining_source()
    {
        var clip = new ClipSampleProvider(new ConstantSamples(), TimeSpan.FromMilliseconds(10));
        var samples = new float[1024];
        Assert.Equal(960, clip.Read(samples, 0, samples.Length));
        Assert.Equal(0, clip.Read(samples, 0, samples.Length));
        Assert.Equal(TimeSpan.FromMilliseconds(10), clip.Position);
    }

    [Fact]
    public void Voice_gate_clears_existing_audio_after_DSP_and_preserves_surrounding_samples()
    {
        var settings = new MicSettings { VoiceMuted = true };
        var voice = new VoiceMuteProvider(new ConstantSamples(1), settings);
        var samples = Enumerable.Repeat(1f, 8).ToArray();
        Assert.Equal(4, voice.Read(samples, 2, 4));
        Assert.Equal([1f, 1f, 0f, 0f, 0f, 0f, 1f, 1f], samples);
        settings.VoiceMuted = false;
        voice.Read(samples, 2, 4);
        Assert.Equal([1f, 1f, .5f, .5f, .5f, .5f, 1f, 1f], samples);
    }
}
