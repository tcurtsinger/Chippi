// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio.Soundboard;
using Chippi.Features.Sound.Soundboard.Library;

namespace Chippi.Features.Sound.Soundboard.Views;

/// <summary>The soundboard view reads immutable snapshots; every mutation belongs to the module.</summary>
public interface ISoundboardController
{
    SoundboardLibrary Library { get; }
    SoundboardPlaybackState Playback { get; }
    string? Error { get; }
    string? SendUnavailableReason { get; }
    event Action? Changed;

    string? ImagePath(SoundClip clip);
    IDisposable HoldOpen();
    void SelectCollection(string id);
    Task PlayAsync(string clipId);
    Task StopAsync();
    Task SetLevelsAsync(float send, float hear);
    Task<string> CreateCollectionAsync(string name);
    Task RenameCollectionAsync(string id, string name);
    /// <summary>Only empty collections can be deleted; sounds must be moved first.</summary>
    Task DeleteCollectionAsync(string id);
    Task SaveClipAsync(SoundClip clip, string? newImagePath = null, SoundClip? expected = null);
    Task DeleteClipAsync(string id);
    Task ReorderClipsAsync(string collectionId, IReadOnlyList<string> ids);

    Task<SoundImportDraft> PrepareAsync(SoundImportSource source, IProgress<SoundImportProgress>? progress, CancellationToken ct);
    Task<string> PreviewAsync(SoundImportDraft draft, double startSeconds, double endSeconds, CancellationToken ct);
    Task<string> PreviewClipAsync(string clipId, double startSeconds, double endSeconds, CancellationToken ct);
    Task AddAsync(SoundImportDraft draft, SoundClip details, string? imagePath, CancellationToken ct);
    void OpenSetup();
}
