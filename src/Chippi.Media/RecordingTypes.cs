// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

public enum RecordingTarget { Window, Screen }
public enum RecordingPhase { Idle, Countdown, Starting, Recording, Paused, Stopping }

public sealed record RecordingSource(RecordingTarget Target, nint Handle, string Name, int Width, int Height);

public sealed record RecordingOptions
{
    internal bool AllowSoftwareForFixture { get; init; }
    public bool SystemAudio { get; init; } = true;
    public bool MicAudio { get; init; }
    public bool OriginalSize { get; init; }
    public int FrameRate { get; init; } = 30;
    public bool IncludeCursor { get; init; } = true;

    public void Validate()
    {
        if (FrameRate is not (30 or 60)) throw new ArgumentOutOfRangeException(nameof(FrameRate));
    }

    public (int Width, int Height) OutputSize(int width, int height)
    {
        if (width < 2 || height < 2) throw new ArgumentOutOfRangeException(nameof(width));
        double ratio = OriginalSize ? 1 : Math.Min(1, 1080d / height);
        return (Math.Max(2, (int)(width * ratio) & ~1), Math.Max(2, (int)(height * ratio) & ~1));
    }
}

public sealed record RecordingResult(string Path, TimeSpan Duration, int Width, int Height, long Frames, long DroppedFrames, string Encoder, string? Warning)
{
    public string? AudioDiagnostics { get; init; }
}
public sealed record RecordingDeviceNames(string? Output, string? Input);

public interface IRecordingSession : IAsyncDisposable
{
    TimeSpan Elapsed { get; }
    bool IsPaused { get; }
    Task<RecordingResult> Completion { get; }
    event Action<string>? Warning;
    event Action? SourceClosed;
    void Pause();
    void Resume();
    Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default);
}

public interface IRecordingBackend
{
    IReadOnlyList<RecordingSource> Sources(RecordingTarget target);
    RecordingDeviceNames Devices();
    Task<IRecordingSession> StartAsync(RecordingSource source, RecordingOptions options, string partialPath, CancellationToken cancellationToken);
}
