// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Media;

namespace Chippi.Media.Tests;

public sealed class RecorderTests : IDisposable
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);
    private readonly string _folder = Directory.CreateTempSubdirectory("chippi-media-tests-").FullName;
    private readonly FakeTime _time = new() { Now = T0 };
    private readonly FakeWriter _writer = new();
    private readonly FakeVideo _video = new();

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private string TempFile => Path.Combine(_folder, "recording.mp4");

    private Recorder StartRecorder(params IAudioSource[] audio)
    {
        var recorder = new Recorder(_writer, _time);
        Assert.Equal(StartResult.Started, recorder.Start(new RecordingRequest(_video, audio, TempFile)));
        return recorder;
    }

    [Fact]
    public void Starts_the_source_and_a_writer_at_the_source_size()
    {
        using var recorder = StartRecorder();
        Assert.True(_video.Started);
        Assert.Equal(new VideoFormat(640, 360, 30, false), _writer.Format);
        Assert.Equal(TempFile, _writer.Path);
        Assert.Equal(RecorderState.Recording, recorder.State);
    }

    [Fact]
    public void A_second_start_is_refused_and_changes_nothing()
    {
        using var recorder = StartRecorder();
        var other = new FakeVideo();
        Assert.Equal(StartResult.AlreadyRecording,
            recorder.Start(new RecordingRequest(other, [], Path.Combine(_folder, "other.mp4"))));
        Assert.False(other.Started);
        Assert.False(File.Exists(Path.Combine(_folder, "other.mp4")));
        Assert.Equal(TempFile, _writer.Path);
    }

    [Fact]
    public async Task Stop_with_no_recording_does_nothing()
    {
        using var recorder = new Recorder(_writer, _time);
        Assert.Null(await recorder.StopAsync());
        Assert.False(await recorder.DiscardAsync());
        Assert.False(recorder.Pause());
        Assert.Null(_writer.Path);
    }

    [Fact]
    public void Frames_are_thinned_to_30_fps_on_an_even_grid()
    {
        using var recorder = StartRecorder();
        // 165 Hz for one second.
        var dropped = new List<FakeFrame>();
        for (var i = 0; i < 165; i++)
        {
            var written = _writer.Video.Count;
            var frame = _video.Send(T0 + TimeSpan.FromTicks(i * TimeSpan.TicksPerSecond / 165));
            if (_writer.Video.Count == written) dropped.Add(frame);
        }
        Assert.Equal(30, _writer.Video.Count);
        Assert.Equal(TimeSpan.Zero, _writer.Video[0]);
        for (var i = 1; i < 30; i++)
            Assert.Equal(TimeSpan.FromTicks(i * TimeSpan.TicksPerSecond / 30), _writer.Video[i]);
        Assert.Equal(135, dropped.Count);
        Assert.All(dropped, f => Assert.True(f.Disposed));
    }

    [Fact]
    public async Task Pause_leaves_no_gap_in_the_timestamps()
    {
        using var recorder = StartRecorder();
        _video.Send(T0);
        _video.Send(T0 + Ms(500));
        _time.Now = T0 + Ms(1000);
        Assert.True(recorder.Pause());
        Assert.Equal(RecorderState.Paused, recorder.State);
        Assert.False(recorder.Pause());
        var during = _video.Send(T0 + Ms(3000));
        Assert.True(during.Disposed);
        _time.Now = T0 + Ms(11_000);
        Assert.Equal(Ms(1000), recorder.Elapsed);
        Assert.True(recorder.Resume());
        _video.Send(T0 + Ms(11_000));
        _video.Send(T0 + Ms(11_500));
        _time.Now = T0 + Ms(12_000);

        var result = await recorder.StopAsync();

        Assert.Equal([TimeSpan.Zero, Ms(500), Ms(1000), Ms(1500)], _writer.Video);
        Assert.NotNull(result);
        Assert.Equal(Ms(2000), result.Duration);
        Assert.Equal(Ms(2000), _writer.FinishedAt);
        Assert.Null(result.Stopped);
        Assert.Equal(RecorderState.Idle, recorder.State);
    }

    [Fact]
    public void A_frame_captured_before_pause_but_delivered_after_is_kept()
    {
        using var recorder = StartRecorder();
        _time.Now = T0 + Ms(1000);
        recorder.Pause();
        _video.Send(T0 + Ms(900));
        var paused = _video.Send(T0 + Ms(1100));
        Assert.Equal([Ms(900)], _writer.Video);
        Assert.True(paused.Disposed);
    }

    [Fact]
    public async Task Discard_removes_only_the_temporary_file()
    {
        var keep = Path.Combine(_folder, "saved.mp4");
        File.WriteAllText(keep, "earlier recording");
        using var recorder = StartRecorder();
        Assert.True(File.Exists(TempFile));

        Assert.True(await recorder.DiscardAsync());

        Assert.False(File.Exists(TempFile));
        Assert.True(File.Exists(keep));
        Assert.True(_writer.Aborted);
        Assert.True(_writer.Disposed);
        Assert.True(_video.Disposed);
        Assert.Null(_writer.FinishedAt);
        Assert.Equal(RecorderState.Idle, recorder.State);
    }

    [Fact]
    public async Task A_source_ending_stops_and_keeps_the_file()
    {
        using var recorder = StartRecorder();
        RecordingResult? stopped = null;
        recorder.StoppedItself += r => stopped = r;
        _video.Send(T0);
        _time.Now = T0 + Ms(4000);

        _video.End("Window closed");
        await WaitIdle(recorder);

        Assert.NotNull(stopped);
        Assert.Equal("Window closed", stopped.Stopped);
        Assert.Equal(Ms(4000), stopped.Duration);
        Assert.True(stopped.Playable);
        Assert.True(File.Exists(TempFile));
        Assert.Null(await recorder.StopAsync());
    }

    [Fact]
    public async Task A_writer_failure_stops_and_reports_what_was_kept()
    {
        var writer = new FakeWriter { FinishFails = true };
        using var recorder = new Recorder(writer, _time);
        recorder.Start(new RecordingRequest(_video, [], TempFile));
        RecordingResult? stopped = null;
        recorder.StoppedItself += r => stopped = r;
        _video.Send(T0);

        writer.Fail(new IOException("There is not enough space on the disk."));
        await WaitIdle(recorder);

        Assert.NotNull(stopped);
        Assert.False(stopped.Playable);
        Assert.Equal("There is not enough space on the disk.", stopped.Stopped);
        Assert.Equal(1, stopped.Frames);
    }

    [Fact]
    public void A_start_that_fails_leaves_the_recorder_idle_and_no_file()
    {
        var video = new FakeVideo { StartFails = new InvalidOperationException("Can't record this window") };
        using var recorder = new Recorder(_writer, _time);
        Assert.Equal(StartResult.Failed, recorder.Start(new RecordingRequest(video, [], TempFile)));
        Assert.Equal("Can't record this window", recorder.LastError);
        Assert.Equal(RecorderState.Idle, recorder.State);
        SpinWait.SpinUntil(() => !File.Exists(TempFile), 2000);
        Assert.False(File.Exists(TempFile));
        Assert.True(video.Disposed);
    }

    [Fact]
    public async Task System_sound_and_mic_are_mixed_on_the_shared_clock()
    {
        var system = new FakeAudio();
        var mic = new FakeAudio();
        using var recorder = StartRecorder(system, mic);
        Assert.True(_writer.Format!.HasAudio);
        // 100 ms each: system at 0.25 from the start, the mic at 0.5 from 50 ms in.
        system.Send(T0, 0.25f, 4800);
        mic.Send(T0 + Ms(50), 0.5f, 4800);
        _time.Now = T0 + Ms(200);

        await recorder.StopAsync();

        var all = _writer.AllAudio();
        Assert.Equal(0, _writer.Audio[0].Time.Ticks);
        Assert.Equal(200 * 48 * 2, all.Length);
        Assert.Equal(0.25f, all[(10 * 48 * 2) + 1]);   // 10 ms: system only
        Assert.Equal(0.75f, all[(75 * 48 * 2) + 1]);   // 75 ms: both
        Assert.Equal(0.5f, all[(120 * 48 * 2) + 1]);   // 120 ms: mic only
        Assert.Equal(0f, all[(180 * 48 * 2) + 1]);     // 180 ms: silence padded to the stop
    }

    [Fact]
    public void Silent_loopback_is_padded_behind_the_clock()
    {
        var system = new FakeAudio();
        var mic = new FakeAudio();
        using var recorder = StartRecorder(system, mic);
        mic.Send(T0, 0.5f, 48_000);
        Assert.Empty(_writer.Audio);
        _time.Now = T0 + Ms(1000);

        recorder.Tick();

        var frames = _writer.Audio.Sum(b => b.Frames);
        Assert.Equal(AudioMixer.FrameAt(Ms(1000) - Recorder.AudioLatency), frames);
    }

    [Fact]
    public async Task Audio_during_a_pause_is_dropped_and_the_rest_joins_up()
    {
        var mic = new FakeAudio();
        using var recorder = StartRecorder(mic);
        mic.Send(T0, 0.5f, 4800);
        _time.Now = T0 + Ms(100);
        recorder.Pause();
        mic.Send(T0 + Ms(100), 0.9f, 4800);
        _time.Now = T0 + Ms(5000);
        recorder.Resume();
        mic.Send(T0 + Ms(5000), 0.25f, 4800);
        _time.Now = T0 + Ms(5100);

        await recorder.StopAsync();

        var all = _writer.AllAudio();
        Assert.Equal(200 * 48 * 2, all.Length);
        Assert.DoesNotContain(0.9f, all);
        Assert.Equal(0.5f, all[(99 * 48 * 2) + 1]);
        Assert.Equal(0.25f, all[(101 * 48 * 2) + 1]);
    }

    private static async Task WaitIdle(Recorder recorder)
    {
        for (var i = 0; i < 200 && recorder.State != RecorderState.Idle; i++) await Task.Delay(10);
        Assert.Equal(RecorderState.Idle, recorder.State);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }
    }
}
