// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

/// <summary>A captured frame, stamped with its capture time on the performance counter. Whoever ends up holding
/// it disposes it: the writer once it's encoded, the recorder when it's dropped.</summary>
public interface IVideoFrame : IDisposable
{
    TimeSpan Time { get; }
}

/// <summary>Frames of one display, region or window, all at the size fixed when it was made.</summary>
public interface IVideoSource : IDisposable
{
    int Width { get; }

    int Height { get; }

    /// <summary>On the source's own thread.</summary>
    event Action<IVideoFrame>? FrameArrived;

    /// <summary>The source can't go on (the window closed, the display changed, the device was lost), with why.</summary>
    event Action<string>? Ended;

    void Start();
}

/// <summary>An audio input already at <see cref="AudioMixer.Rate"/> stereo float, each packet stamped with its
/// capture time on the performance counter.</summary>
public interface IAudioSource : IDisposable
{
    /// <summary>On the source's own thread: the capture time and interleaved stereo samples.</summary>
    event Action<TimeSpan, float[]>? PacketArrived;

    event Action<string>? Ended;

    void Start();
}

public sealed record VideoFormat(int Width, int Height, int Fps, bool HasAudio);

/// <summary>What the writer made of the file.</summary>
/// <param name="Playable">False when the file couldn't be finished and nothing in it plays.</param>
public sealed record WriteResult(long VideoFrames, bool Playable);

/// <summary>Encodes into one file. Its writes never block: it queues and encodes on its own thread.</summary>
public interface IMediaWriter : IDisposable
{
    /// <summary>Takes <paramref name="frame"/> and disposes it once encoded.</summary>
    void WriteVideo(IVideoFrame frame, TimeSpan at);

    void WriteAudio(AudioBlock block);

    /// <summary>Encoding failed (disk full, the encoder lost): the recorder stops and keeps what it can.</summary>
    event Action<Exception>? Failed;

    /// <summary>Writes what's queued and closes the file at <paramref name="duration"/>, the last frame held to it.</summary>
    Task<WriteResult> FinishAsync(TimeSpan duration);

    /// <summary>Stops without finishing; the file is left for the caller to delete.</summary>
    Task AbortAsync();
}

public interface IMediaWriterFactory
{
    IMediaWriter Create(string path, VideoFormat format);
}
