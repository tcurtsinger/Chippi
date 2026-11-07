// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Media;

namespace Chippi.Media.Tests;

/// <summary>A clock the test moves; its timers never fire (tests call the recorder's tick themselves).</summary>
internal sealed class FakeTime : TimeProvider
{
    public TimeSpan Now { get; set; } = TimeSpan.FromSeconds(100);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Now.Ticks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();

    private sealed class Timer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class FakeFrame(TimeSpan time) : IVideoFrame
{
    public TimeSpan Time { get; } = time;
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

internal sealed class FakeVideo(int width = 640, int height = 360) : IVideoSource
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }
    public Exception? StartFails { get; init; }
    public event Action<IVideoFrame>? FrameArrived;
    public event Action<string>? Ended;

    public FakeFrame Send(TimeSpan at)
    {
        var frame = new FakeFrame(at);
        FrameArrived?.Invoke(frame);
        return frame;
    }

    public void End(string because) => Ended?.Invoke(because);

    public void Start()
    {
        if (StartFails is not null) throw StartFails;
        Started = true;
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeAudio : IAudioSource
{
    public bool Disposed { get; private set; }
    public event Action<TimeSpan, float[]>? PacketArrived;
    public event Action<string>? Ended;

    public void Send(TimeSpan at, float value, int frames)
    {
        var samples = new float[frames * AudioMixer.Channels];
        Array.Fill(samples, value);
        PacketArrived?.Invoke(at, samples);
    }

    public void End(string because) => Ended?.Invoke(because);

    public void Start() { }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeWriter : IMediaWriter, IMediaWriterFactory
{
    public List<TimeSpan> Video { get; } = [];
    public List<AudioBlock> Audio { get; } = [];
    public string? Path { get; private set; }
    public VideoFormat? Format { get; private set; }
    public TimeSpan? FinishedAt { get; private set; }
    public bool Aborted { get; private set; }
    public bool Disposed { get; private set; }
    public bool FinishFails { get; init; }
    public event Action<Exception>? Failed;

    public IMediaWriter Create(string path, VideoFormat format)
    {
        Path = path;
        Format = format;
        File.WriteAllText(path, "partial");
        return this;
    }

    public void WriteVideo(IVideoFrame frame, TimeSpan at)
    {
        Video.Add(at);
        frame.Dispose();
    }

    public void WriteAudio(AudioBlock block) => Audio.Add(block);

    public void Fail(Exception ex) => Failed?.Invoke(ex);

    public Task<WriteResult> FinishAsync(TimeSpan duration)
    {
        if (FinishFails) throw new IOException("There is not enough space on the disk.");
        FinishedAt = duration;
        return Task.FromResult(new WriteResult(Video.Count, true));
    }

    public Task AbortAsync()
    {
        Aborted = true;
        return Task.CompletedTask;
    }

    public void Dispose() => Disposed = true;

    /// <summary>All mixed audio, as one buffer.</summary>
    public float[] AllAudio() => [.. Audio.SelectMany(b => b.Samples)];
}
