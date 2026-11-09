// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Audio;
using Chippi.Audio.Processing;
using Chippi.Audio.Soundboard;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Processing;
using Chippi.Features.Sound.Soundboard.Library;
using Chippi.Features.Sound.Soundboard.Views;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Sound.Soundboard;

/// <summary>Persistent sound collections and one shared player across the widget and Settings.</summary>
public sealed class SoundboardModule : IFeatureModule, ISoundboardController
{
    public const string Id = "soundboard";
    public const string TileKey = "sound.board";
    private readonly string _directory;
    private readonly Func<IReadOnlyList<PriorityEntry>> _voiceOrder;
    private readonly SoundboardStore _store;
    private ISoundboardImporter? _importer;
    private readonly bool _ownsImporter;
    private readonly Func<ISoundboardPlayer>? _createPlayer;
    private readonly Func<string?>? _sendUnavailable;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _importsGate = new();
    private readonly HashSet<Task> _imports = [];
    private readonly object _playbackGate = new();
    private readonly HashSet<string> _deletingClips = new(StringComparer.Ordinal);
    private string? _playbackClipId;
    private string? _previewPath;
    private AudioService? _audio;
    private ISoundboardPlayer? _player;
    private ModuleContext? _context;
    private CancellationTokenSource? _lifetime;
    private SoundboardPage? _page;
    private volatile bool _stopping;
    private SoundboardLibrary _library = new();
    private string? _error;

    public SoundboardModule(string? dataDirectory = null, Func<IReadOnlyList<PriorityEntry>>? voiceOrder = null,
        SoundboardStore? store = null, ISoundboardImporter? importer = null, Func<ISoundboardPlayer>? createPlayer = null,
        Func<string?>? sendUnavailable = null)
    {
        _directory = dataDirectory ?? Path.Combine(ModuleServices.DefaultDataRoot, Id);
        _voiceOrder = voiceOrder ?? (() => []);
        _store = store ?? new(_directory);
        _importer = importer;
        _ownsImporter = importer is null;
        _createPlayer = createPlayer;
        _sendUnavailable = sendUnavailable;
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Soundboard", "Play your sound collections through the virtual mic", FeatureGroup.Sound)
    {
        Icon = "Music",
        Presets = PresetMembership.Audio | PresetMembership.PowerUser,
        Requires = [Capability.VbCable],
        // Local imports and previews work without the driver. Setup is available inside the page.
        Badges = [FeatureBadge.Needs("VB-CABLE for mic playback")],
    };

    public SoundboardLibrary Library => _library;
    public SoundboardPlaybackState Playback => _player?.State ?? SoundboardPlaybackState.Idle;
    public string? Error => _error ?? Playback.Error ?? VoiceWarning(_audio?.Snapshot.SoundboardVoice);
    internal static string? VoiceWarning(ProcessingStatus? status) => status is { State: ProcessingState.Stopped, Reason: StopReason.Failed }
        ? "Voice capture failed · sounds can still play. Check your microphone in Devices."
        : null;
    public event Action? Changed;
    public string? SendUnavailableReason => _sendUnavailable is not null ? _sendUnavailable() : _audio is null ? null
        : VbCable.Find(_audio.Snapshot.Endpoints).State switch
        {
            CableState.Missing => "Set up the virtual mic to send sounds",
            CableState.Disabled => "Virtual mic is disabled",
            CableState.Ambiguous => "Choose one VB-CABLE pair in Windows",
            _ => null,
        };

    public async Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _library = await Task.Run(_store.Load, ct);
            _importer ??= new SoundboardImporter(_directory);
            _error = _store.RecoveryNotice;
            _context = context;
            _stopping = false;
            _lifetime = new();
            if (_createPlayer is null)
            {
                _audio = SoundAudio.Get(context.Logger);
                _player = new SoundboardPlayer(_audio, active => PriorityPolicy.Resolve(_voiceOrder(), active)
                    ?? active.FirstOrDefault(e => string.Equals(e.Id, _audio.Snapshot.Default(AudioFlow.Capture, AudioRole.Multimedia), StringComparison.OrdinalIgnoreCase))?.Id
                    ?? active.FirstOrDefault()?.Id);
                _audio.SnapshotChanged += SnapshotChanged;
            }
            else _player = _createPlayer();
            _player.Changed += PlayerChanged;
            await _player.InitializeAsync(ct);
            await _player.SetLevelsAsync(_library.SendVolume, _library.HearVolume, ct);
            context.Ui.AddIslandTile(new(TileKey, "Soundboard", "Music", 21, 0, () => Page())
            {
                HeaderHeight = 32,
                InitialFocus = () => _page?.InitialFocus,
                HandleBack = () => _page?.HandleBack() == true,
                HandleEscape = () => _page?.HandleEscape() == true,
            });
            context.Ui.SetSettingsPage(section => section == "setup" ? new SoundboardSetupView(this) : new SoundboardPage(this));
            context.Ui.AddCommands(() => [new ChippiCommand("soundboard/stop", "Stop sound", "Square", StopAsync)
            {
                Words = "soundboard stop playback",
            }]);
        }
        catch
        {
            // Preserve the original startup failure even if native stream teardown also fails.
            try { await ReleaseAsync(); }
            catch (Exception ex) { context.Logger.LogWarning(ex, "Soundboard startup cleanup failed"); }
            throw;
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    private SoundboardPage Page() => _page ??= new(this);

    public async Task StopAsync(CancellationToken ct)
    {
        _stopping = true;
        _lifetime?.Cancel();
        await _gate.WaitAsync(CancellationToken.None);
        try { await ReleaseAsync(); }
        finally { _gate.Release(); Changed?.Invoke(); }
    }

    private async Task ReleaseAsync()
    {
        _stopping = true;
        if (_audio is not null) _audio.SnapshotChanged -= SnapshotChanged;
        _audio = null;
        var player = _player;
        _player = null;
        if (player is not null) player.Changed -= PlayerChanged;
        try { if (player is not null) await player.DisposeAsync(); }
        finally
        {
            _lifetime?.Cancel();
            Task[] imports;
            lock (_importsGate) imports = [.. _imports];
            // Import completions include cancellation cleanup and draft disposal. Keep the owned
            // downloader alive until its helpers have exited, including during tray Quit.
            await Task.WhenAll(imports);
            _lifetime?.Dispose(); _lifetime = null;
            try { DisposeImporter(); }
            finally { _context = null; _page = null; }
        }
    }

    private void SnapshotChanged(object? sender, AudioSnapshot snapshot) => Changed?.Invoke();
    private void PlayerChanged() => Changed?.Invoke();
    public string? ImagePath(SoundClip clip) => _store.ImagePath(clip);
    public IDisposable HoldOpen() => _context?.HoldIslandOpen() ?? new EmptyScope();
    public void OpenSetup() => _context?.OpenSettings("setup");
    internal IFeatureRequirement CreateRequirement()
    {
        var audio = _audio ?? throw new SoundboardException("Audio is unavailable");
        return new VbCableRequirement(audio, Path.Combine(_directory, "driver"),
            () => VbCablePackage.Prepare(VbCablePackage.VendorFolder, Path.Combine(_directory, "driver", "package")),
            new ElevatedDriverSetup(), VbCableRequirement.OpenWindowsSoundSettings);
    }

    public void SelectCollection(string id) => _ = Observe(() => Change(() =>
    {
        if (id != SoundboardLibrary.FavoritesId && !_library.Collections.Any(c => c.Id == id)) return Task.CompletedTask;
        return Save(_library with { SelectedCollectionId = id });
    }));

    public Task PlayAsync(string clipId) => PlaybackChange(async player =>
    {
        var clip = Find(clipId);
        if (SendUnavailableReason is { } reason) throw new SoundboardException(reason);
        await PlayTracked(player, clip.Id, clip.Id, _store.AudioPath(clip), clip.StartSeconds, clip.EndSeconds, false, _lifetime!.Token);
    });

    public Task StopAsync() => PlaybackChange(StopPlayback);

    public Task SetLevelsAsync(float send, float hear) => Change(async () =>
    {
        if (!float.IsFinite(send) || !float.IsFinite(hear)) throw new SoundboardException("Choose a valid volume");
        var next = _library with { SendVolume = Math.Clamp(send, 0, 1), HearVolume = Math.Clamp(hear, 0, 1) };
        await _player!.SetLevelsAsync(next.SendVolume, next.HearVolume, _lifetime!.Token);
        await Save(next);
    });

    public async Task<string> CreateCollectionAsync(string name)
    {
        string id = Guid.NewGuid().ToString("N");
        await Change(() =>
        {
            string clean = CollectionName(name);
            return Save(_library with { Collections = [.. _library.Collections, new(id, clean)], SelectedCollectionId = id });
        });
        return id;
    }

    public Task RenameCollectionAsync(string id, string name) => Change(() =>
    {
        RequireCollection(id);
        string clean = CollectionName(name, id);
        return Save(_library with { Collections = [.. _library.Collections.Select(c => c.Id == id ? c with { Name = clean } : c)] });
    });

    public Task DeleteCollectionAsync(string id) => Change(() =>
    {
        RequireCollection(id);
        if (_library.Collections.Count == 1) throw new SoundboardException("Keep at least one collection");
        if (_library.Clips.Any(c => c.CollectionId == id)) throw new SoundboardException("Move the sounds before deleting this collection");
        var remaining = _library.Collections.Where(c => c.Id != id).ToList();
        return Save(_library with { Collections = remaining, SelectedCollectionId = _library.SelectedCollectionId == id ? remaining[0].Id : _library.SelectedCollectionId });
    });

    public Task SaveClipAsync(SoundClip clip, string? newImagePath = null, SoundClip? expected = null) => Change(async () =>
    {
        var current = Find(clip.Id);
        // Check inside the mutation gate: a form or menu may have waited behind another view's save.
        if (expected is not null && (expected.Id != current.Id || expected.Name != current.Name
            || expected.CollectionId != current.CollectionId || expected.StartSeconds != current.StartSeconds
            || expected.EndSeconds != current.EndSeconds || expected.Favorite != current.Favorite || expected.ImageFile != current.ImageFile))
            throw new SoundboardException("Sound changed in another view. Reopen it before saving.");
        RequireCollection(clip.CollectionId);
        SoundboardStore.ValidateRange(current.DurationSeconds, clip.StartSeconds, clip.EndSeconds);
        // A view can edit presentation/trim only; the stored source and media identity stay authoritative.
        var updated = current with { Name = SoundboardStore.CleanName(clip.Name), CollectionId = clip.CollectionId,
            StartSeconds = clip.StartSeconds, EndSeconds = clip.EndSeconds, Favorite = clip.Favorite,
            ImageFile = clip.ImageFile is null ? null : current.ImageFile };
        await StopClipPlayback(_player!, clip.Id);
        if (newImagePath is not null) updated = updated with { ImageFile = await Task.Run(() => _store.ImportCover(newImagePath)) };
        try { await Save(_library with { Clips = [.. _library.Clips.Select(c => c.Id == clip.Id ? updated : c)] }); }
        catch
        {
            if (updated.ImageFile != current.ImageFile) _store.DeleteCover(updated.ImageFile);
            throw;
        }
        if (updated.ImageFile != current.ImageFile) _store.DeleteCover(current.ImageFile);
    });

    public Task DeleteClipAsync(string id) => Change(async () =>
    {
        var clip = Find(id);
        lock (_playbackGate) _deletingClips.Add(id);
        try
        {
            // State may still be idle while a play waits for the audio engine. Fence the
            // recorded intent now and reject further plays until metadata is removed.
            await StopClipPlayback(_player!, id);
            await Save(_library with { Clips = [.. _library.Clips.Where(c => c.Id != id)] });
            await Task.Run(() => _store.DeleteMedia(clip));
        }
        finally { lock (_playbackGate) _deletingClips.Remove(id); }
    });

    public Task ReorderClipsAsync(string collectionId, IReadOnlyList<string> ids) => Change(() =>
    {
        bool Included(SoundClip c) => collectionId == SoundboardLibrary.FavoritesId ? c.Favorite : c.CollectionId == collectionId;
        var current = _library.Clips.Where(Included).ToDictionary(c => c.Id, StringComparer.Ordinal);
        // A different view may have added or moved a clip while editing. Keep those edits, and append new arrivals.
        var ordered = new Queue<SoundClip>(ids.Distinct(StringComparer.Ordinal).Where(current.ContainsKey).Select(id => current[id])
            .Concat(current.Values.Where(c => !ids.Contains(c.Id))));
        return Save(_library with { Clips = [.. _library.Clips.Select(c => Included(c) ? ordered.Dequeue() : c)] });
    });

    public async Task<SoundImportDraft> PrepareAsync(SoundImportSource source, IProgress<SoundImportProgress>? progress, CancellationToken ct)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource linked;
        ISoundboardImporter importer;
        lock (_importsGate)
        {
            RequireRunning();
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime!.Token);
            importer = _importer!;
            _imports.Add(completed.Task);
        }
        try
        {
            var draft = await importer.PrepareAsync(source, progress, linked.Token);
            if (linked.IsCancellationRequested)
            {
                draft.Dispose();
                linked.Token.ThrowIfCancellationRequested();
            }
            return draft;
        }
        finally
        {
            linked.Dispose();
            lock (_importsGate) _imports.Remove(completed.Task);
            completed.SetResult();
        }
    }

    public async Task<string> PreviewAsync(SoundImportDraft draft, double startSeconds, double endSeconds, CancellationToken ct)
    {
        string playbackId = "@preview/" + Guid.NewGuid().ToString("N");
        await PlaybackChange(async player =>
        {
            SoundboardStore.ValidateRange(draft.DurationSeconds, startSeconds, endSeconds);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime!.Token);
            await PlayTracked(player, null, playbackId, draft.LocalAudioPath, startSeconds, endSeconds, true, linked.Token);
        });
        return playbackId;
    }

    public async Task<string> PreviewClipAsync(string clipId, double startSeconds, double endSeconds, CancellationToken ct)
    {
        string playbackId = "@preview/" + Guid.NewGuid().ToString("N");
        await PlaybackChange(async player =>
        {
            var clip = Find(clipId);
            SoundboardStore.ValidateRange(clip.DurationSeconds, startSeconds, endSeconds);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime!.Token);
            await PlayTracked(player, clip.Id, playbackId, _store.AudioPath(clip), startSeconds, endSeconds, true, linked.Token);
        });
        return playbackId;
    }

    public Task AddAsync(SoundImportDraft draft, SoundClip details, string? imagePath, CancellationToken ct) => Change(async () =>
    {
        ct.ThrowIfCancellationRequested();
        RequireCollection(details.CollectionId);
        Task stopped;
        lock (_playbackGate)
            stopped = string.Equals(_previewPath, draft.LocalAudioPath, StringComparison.OrdinalIgnoreCase) ? StopPlayback(_player!) : Task.CompletedTask;
        await stopped;
        var clip = await Task.Run(() => _store.CommitDraft(draft, details.Name, details.CollectionId,
            details.StartSeconds, details.EndSeconds, imagePath, details.Favorite), ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            _lifetime!.Token.ThrowIfCancellationRequested();
            await Save(_library with { Clips = [.. _library.Clips, clip], SelectedCollectionId = clip.CollectionId });
        }
        catch { _store.DeleteMedia(clip); throw; }
    });

    private SoundClip Find(string id) => _library.Clips.FirstOrDefault(c => c.Id == id) ?? throw new SoundboardException("Sound is no longer available");
    private Task PlayTracked(ISoundboardPlayer player, string? clipId, string playbackId, string path,
        double start, double end, bool preview, CancellationToken ct)
    {
        lock (_playbackGate)
        {
            if (clipId is not null)
            {
                if (_deletingClips.Contains(clipId)) throw new SoundboardException("Sound is being deleted");
                Find(clipId);
            }
            ct.ThrowIfCancellationRequested();
            _playbackClipId = clipId;
            _previewPath = preview ? path : null;
            // Only command submission is locked, never file work or an awaited operation.
            return player.PlayAsync(playbackId, path, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), preview, ct);
        }
    }
    private Task StopPlayback(ISoundboardPlayer player)
    {
        lock (_playbackGate)
        {
            _playbackClipId = null;
            _previewPath = null;
            return player.StopAsync();
        }
    }
    private Task StopClipPlayback(ISoundboardPlayer player, string clipId)
    {
        lock (_playbackGate)
            // A replacement may already be pending while State still names the old sound.
            // Only the latest intent can be stopped without cancelling that replacement.
            return _playbackClipId == clipId ? StopPlayback(player) : Task.CompletedTask;
    }
    private void RequireCollection(string id)
    {
        if (!_library.Collections.Any(c => c.Id == id)) throw new SoundboardException("Choose a collection");
    }
    private string CollectionName(string name, string? except = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new SoundboardException("Enter a collection name");
        string clean = SoundboardStore.CleanName(name);
        if (_library.Collections.Any(c => c.Id != except && string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
            throw new SoundboardException("A collection with this name already exists");
        return clean;
    }
    private async Task Save(SoundboardLibrary next)
    {
        next = SoundboardStore.Normalize(next);
        await Task.Run(() => _store.Save(next));
        _library = next;
    }
    private void RequireRunning()
    {
        if (_context is null || _stopping || _player is null) throw new SoundboardException("Soundboard is not running");
    }
    private async Task Change(Func<Task> action)
    {
        await _gate.WaitAsync();
        try { RequireRunning(); _error = null; await action(); }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException) _error = ex is SoundboardException or AudioFailure ? ex.Message : "Change failed";
            throw;
        }
        finally { _gate.Release(); Changed?.Invoke(); }
    }
    // Playback intent must reach the player's ordering fence immediately. In particular, Stop
    // cannot queue behind copying a large import or saving library metadata.
    private async Task PlaybackChange(Func<ISoundboardPlayer, Task> action)
    {
        try { RequireRunning(); _error = null; await action(_player!); }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                _error = ex is SoundboardException or AudioFailure ? ex.Message : "Playback failed";
                _context?.Logger.LogWarning(ex, "Soundboard playback failed");
            }
            throw;
        }
        finally { Changed?.Invoke(); }
    }
    private async Task Observe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _context?.Logger.LogWarning(ex, "Soundboard selection failed"); }
    }
    private void DisposeImporter()
    {
        if (!_ownsImporter) return;
        (_importer as IDisposable)?.Dispose();
        _importer = null;
    }
    private sealed class EmptyScope : IDisposable { public void Dispose() { } }
}
