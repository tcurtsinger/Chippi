// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.Sound.Soundboard.Library;

public sealed record SoundCollection(string Id, string Name);

public sealed record SoundClip
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Sound";
    public string CollectionId { get; init; } = SoundboardLibrary.DefaultCollectionId;
    public string AudioFile { get; init; } = "";
    public string? ImageFile { get; init; }
    public string? Source { get; init; }
    public double DurationSeconds { get; init; }
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }
    public bool Favorite { get; init; }
    public float[] Waveform { get; init; } = [];
}

public sealed record SoundboardLibrary
{
    public const string DefaultCollectionId = "default";
    public const string FavoritesId = "@favorites";
    public int Version { get; init; } = 1;
    public List<SoundCollection> Collections { get; init; } = [new(DefaultCollectionId, "Sounds")];
    public List<SoundClip> Clips { get; init; } = [];
    public string? SelectedCollectionId { get; init; } = DefaultCollectionId;
    public float SendVolume { get; init; } = .8f;
    public float HearVolume { get; init; } = .6f;
}

public sealed class SoundboardException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record SoundImportSource(string Value, bool IsUrl = false);
public sealed record SoundImportProgress(string Stage, double? Fraction = null);
public sealed record SoundDecodedAudio(double DurationSeconds, float[] Waveform);

/// <summary>An app-owned prepared WAV. Disposing a draft only removes its own temporary directory.</summary>
public sealed class SoundImportDraft : IDisposable
{
    private readonly Action? _cleanup;
    private int _disposed;

    public SoundImportDraft(string localAudioPath, string title, string source, double durationSeconds, float[] waveform, Action? cleanup = null)
    {
        LocalAudioPath = localAudioPath;
        Title = title;
        Source = source;
        DurationSeconds = durationSeconds;
        Waveform = waveform;
        _cleanup = cleanup;
    }

    public string LocalAudioPath { get; }
    public string Title { get; }
    public string Source { get; }
    public double DurationSeconds { get; }
    public float[] Waveform { get; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _cleanup?.Invoke();
    }
}

public interface ISoundboardImporter
{
    Task<SoundImportDraft> PrepareAsync(SoundImportSource source, IProgress<SoundImportProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Decoding never opens a capture/render endpoint. Tests replace it with a file-only fake.</summary>
public interface ISoundboardDecoder
{
    Task<SoundDecodedAudio> DecodeAsync(string inputPath, string wavePath, IProgress<SoundImportProgress>? progress, CancellationToken cancellationToken);
}

public sealed record SoundDownload(string LocalPath, string Title, string Source);

public interface ISoundboardLinkDownloader
{
    Task<SoundDownload> DownloadAsync(Uri url, string directory, IProgress<SoundImportProgress>? progress, CancellationToken cancellationToken);
}
