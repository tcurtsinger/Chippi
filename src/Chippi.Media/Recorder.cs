// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

public enum RecorderState
{
    Idle,
    Recording,
    Paused,
    Stopping,
}

public enum StartResult
{
    Started,
    AlreadyRecording,
    Failed,
}

/// <param name="Path">The file the recording writes to: a temporary MP4 the feature saves or discards.</param>
/// <param name="Audio">System sound, the mic, both or neither; each is off unless switched on for this recording.</param>
public sealed record RecordingRequest(IVideoSource Video, IReadOnlyList<IAudioSource> Audio, string Path, int Fps = 30);

/// <param name="Stopped">Why it stopped by itself (the window closed, disk full); null after Stop.</param>
public sealed record RecordingResult(string Path, TimeSpan Duration, long Frames, bool Playable, string? Stopped);

/// <summary>
/// One recording at a time (screen-recording.md, While recording): starts the sources and the writer, places each
/// frame and packet on the timeline, thins frames to the file's rate, mixes audio, pauses without a gap, and stops,
/// discards or keeps what it can when a source or the writer fails. Sources call in on their own threads.
/// </summary>
public sealed class Recorder : IDisposable
{
    /// <summary>How far behind the clock audio is mixed, so packets still in flight land before their slot is filled.</summary>
    public static readonly TimeSpan AudioLatency = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly IMediaWriterFactory _writers;
    private readonly TimeProvider _time;
    private readonly ITimer _pad;
    private Session? _session;

    public Recorder(IMediaWriterFactory writers, TimeProvider? time = null)
    {
        _writers = writers;
        _time = time ?? TimeProvider.System;
        _pad = _time.CreateTimer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public RecorderState State { get { lock (_gate) return _session?.State ?? RecorderState.Idle; } }

    /// <summary>The file's length so far; it stands still while paused.</summary>
    public TimeSpan Elapsed { get { lock (_gate) return _session?.Timeline.Elapsed(Now) ?? TimeSpan.Zero; } }

    /// <summary>The last start's failure, for Settings.</summary>
    public string? LastError { get; private set; }

    /// <summary>Raised once when a recording stops by itself, after its file is closed. On a source's or the writer's thread.</summary>
    public event Action<RecordingResult>? StoppedItself;

    /// <summary>Raised on every state change, on the thread that made it.</summary>
    public event Action<RecorderState>? StateChanged;

    private TimeSpan Now => TimeSpan.FromTicks((long)(_time.GetTimestamp() * ((double)TimeSpan.TicksPerSecond / _time.TimestampFrequency)));

    public StartResult Start(RecordingRequest request)
    {
        Session session;
        lock (_gate)
        {
            if (_session is not null) return StartResult.AlreadyRecording;
            IMediaWriter writer;
            try
            {
                writer = _writers.Create(request.Path,
                    new VideoFormat(request.Video.Width, request.Video.Height, request.Fps, request.Audio.Count > 0));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return StartResult.Failed;
            }
            session = new Session(this, request, writer);
            _session = session;
            session.Timeline.Begin(Now);
        }
        try
        {
            session.Attach();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            lock (_gate) _session = null;
            session.Detach();
            _ = session.Writer.AbortAsync().ContinueWith(_ => { session.Writer.Dispose(); TryDelete(request.Path); }, TaskScheduler.Default);
            return StartResult.Failed;
        }
        LastError = null;
        _pad.Change(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        StateChanged?.Invoke(RecorderState.Recording);
        return StartResult.Started;
    }

    public bool Pause()
    {
        lock (_gate)
        {
            if (_session is not { State: RecorderState.Recording } session || !session.Timeline.Pause(Now)) return false;
            session.State = RecorderState.Paused;
        }
        StateChanged?.Invoke(RecorderState.Paused);
        return true;
    }

    public bool Resume()
    {
        lock (_gate)
        {
            if (_session is not { State: RecorderState.Paused } session || !session.Timeline.Resume(Now)) return false;
            session.State = RecorderState.Recording;
        }
        StateChanged?.Invoke(RecorderState.Recording);
        return true;
    }

    /// <returns>The finished file, or null when nothing was recording ("Not recording").</returns>
    public Task<RecordingResult?> StopAsync() => End(null);

    /// <summary>Stops and deletes the temporary file, and only that.</summary>
    /// <returns>False when nothing was recording.</returns>
    public async Task<bool> DiscardAsync()
    {
        Session? session;
        lock (_gate)
        {
            session = Claim();
            if (session is null) return false;
        }
        StateChanged?.Invoke(RecorderState.Stopping);
        session.Detach();
        try { await session.Writer.AbortAsync().ConfigureAwait(false); }
        finally
        {
            session.Writer.Dispose();
            TryDelete(session.Request.Path);
            lock (_gate) _session = null;
            StateChanged?.Invoke(RecorderState.Idle);
        }
        return true;
    }

    /// <summary>Mixes audio up to just behind the clock, so a silent loopback still advances. The pad timer calls it.</summary>
    internal void Tick()
    {
        lock (_gate)
        {
            if (_session is not { State: RecorderState.Recording } session || !session.HasAudio) return;
            session.Write(session.Mixer!.Pad(session.Timeline.Elapsed(Now) - AudioLatency));
        }
    }

    private Session? Claim()
    {
        if (_session is not { State: not RecorderState.Stopping } session) return null;
        session.State = RecorderState.Stopping;
        _pad.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return session;
    }

    private async Task<RecordingResult?> End(string? because)
    {
        Session? session;
        TimeSpan duration;
        lock (_gate)
        {
            session = Claim();
            if (session is null) return null;
            duration = session.Timeline.Elapsed(Now);
        }
        StateChanged?.Invoke(RecorderState.Stopping);
        session.Detach();
        RecordingResult result;
        try
        {
            lock (_gate)
            {
                if (session.HasAudio) session.Write(session.Mixer!.Pad(duration));
            }
            var written = await session.Writer.FinishAsync(duration).ConfigureAwait(false);
            result = new RecordingResult(session.Request.Path, duration, written.VideoFrames, written.Playable, because);
        }
        catch (Exception ex)
        {
            result = new RecordingResult(session.Request.Path, duration, session.Pacer.Kept, false, because ?? ex.Message);
        }
        finally
        {
            session.Writer.Dispose();
            lock (_gate) _session = null;
        }
        StateChanged?.Invoke(RecorderState.Idle);
        if (because is not null) StoppedItself?.Invoke(result);
        return result;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _pad.Dispose();
        _ = DiscardAsync();
    }

    private sealed class Session(Recorder owner, RecordingRequest request, IMediaWriter writer)
    {
        private readonly List<Action> _detach = [];

        public RecordingRequest Request { get; } = request;
        public IMediaWriter Writer { get; } = writer;
        public RecordingTimeline Timeline { get; } = new();
        public FramePacer Pacer { get; } = new(request.Fps);
        public AudioMixer? Mixer { get; } = request.Audio.Count > 0 ? new AudioMixer(request.Audio.Count) : null;
        public bool HasAudio => Mixer is not null;
        public RecorderState State { get; set; } = RecorderState.Recording;

        public void Attach()
        {
            Action<IVideoFrame> frame = OnFrame;
            Action<string> ended = OnEnded;
            Action<Exception> failed = OnFailed;
            Request.Video.FrameArrived += frame;
            Request.Video.Ended += ended;
            Writer.Failed += failed;
            _detach.Add(() => { Request.Video.FrameArrived -= frame; Request.Video.Ended -= ended; Writer.Failed -= failed; });
            for (var track = 0; track < Request.Audio.Count; track++)
            {
                var index = track;
                var source = Request.Audio[track];
                Action<TimeSpan, float[]> packet = (at, samples) => OnAudio(index, at, samples);
                source.PacketArrived += packet;
                source.Ended += ended;
                _detach.Add(() => { source.PacketArrived -= packet; source.Ended -= ended; });
            }
            Request.Video.Start();
            foreach (var source in Request.Audio) source.Start();
        }

        public void Detach()
        {
            foreach (var undo in _detach) undo();
            _detach.Clear();
            Request.Video.Dispose();
            foreach (var source in Request.Audio) source.Dispose();
        }

        public void Write(IReadOnlyList<AudioBlock> blocks)
        {
            foreach (var block in blocks) Writer.WriteAudio(block);
        }

        private void OnFrame(IVideoFrame frame)
        {
            lock (owner._gate)
            {
                if (owner._session == this && State is RecorderState.Recording or RecorderState.Paused
                    && Timeline.Map(frame.Time) is { } mapped && Pacer.Take(mapped) is { } at)
                {
                    Writer.WriteVideo(frame, at);
                    return;
                }
            }
            frame.Dispose();
        }

        private void OnAudio(int track, TimeSpan time, float[] samples)
        {
            lock (owner._gate)
            {
                if (owner._session != this || State is not (RecorderState.Recording or RecorderState.Paused) || Timeline.Map(time) is not { } at) return;
                Write(Mixer!.Add(track, at, samples));
            }
        }

        private void OnEnded(string because) => _ = owner.End(because);

        private void OnFailed(Exception ex) => _ = owner.End(ex.Message);
    }
}
