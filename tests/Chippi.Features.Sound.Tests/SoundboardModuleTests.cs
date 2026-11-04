// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Audio.Processing;
using Chippi.Audio.Soundboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Soundboard;
using Chippi.Features.Sound.Soundboard.Library;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Sound.Tests;

/// <summary>Module integration with temporary files and a fake player; no endpoints, windows or input.</summary>
public sealed class SoundboardModuleTests
{
    [Fact]
    public async Task Starting_registers_a_third_audio_page_without_playing_anything()
    {
        await using var f = await Fixture.Start();
        var tile = Assert.Single(f.Ui.Tiles);
        Assert.Equal(SoundboardModule.TileKey, tile.Spec.Key);
        Assert.Equal(32, tile.Spec.HeaderHeight);
        Assert.Empty(f.Player.Plays);
        Assert.Equal(1, f.Player.Initializations);
        Assert.Empty(f.Module.Library.Clips);
        Assert.Equal("Sounds", Assert.Single(f.Module.Library.Collections).Name);
    }

    [Fact]
    public async Task Imported_media_is_owned_and_trim_reaches_the_player()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        await f.Module.AddAsync(draft, new() { Name = "Air horn", StartSeconds = 1, EndSeconds = 3 }, null, default);
        var clip = Assert.Single(f.Module.Library.Clips);
        await f.Module.PlayAsync(clip.Id);
        var played = Assert.Single(f.Player.Plays);
        Assert.Equal(TimeSpan.FromSeconds(1), played.Start);
        Assert.Equal(TimeSpan.FromSeconds(3), played.End);
        Assert.False(played.Preview);
        Assert.NotEqual(draft.LocalAudioPath, played.Path);
        Assert.Equal(File.ReadAllBytes(draft.LocalAudioPath), File.ReadAllBytes(played.Path));
        await f.Module.DeleteClipAsync(clip.Id);
        Assert.Empty(f.Module.Library.Clips);
        Assert.False(File.Exists(played.Path));
        Assert.True(File.Exists(draft.LocalAudioPath));
        Assert.Equal(1, f.Player.Stops);
    }

    [Fact]
    public async Task Local_preview_works_while_sending_is_unavailable()
    {
        await using var f = await Fixture.Start("Set up the virtual mic");
        using var draft = f.Draft();
        await f.Module.PreviewAsync(draft, 0, 2, default);
        Assert.True(Assert.Single(f.Player.Plays).Preview);
        await f.Module.AddAsync(draft, new() { EndSeconds = 2 }, null, default);
        await Assert.ThrowsAsync<SoundboardException>(() => f.Module.PlayAsync(f.Module.Library.Clips[0].Id));
        Assert.Equal("Set up the virtual mic", f.Module.Error);
        Assert.Single(f.Player.Plays);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deleting_fences_pending_play_and_rejects_replay_until_removal(bool preview)
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        await f.Module.AddAsync(draft, new() { EndSeconds = 2 }, null, default);
        var clip = Assert.Single(f.Module.Library.Clips);
        var playReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Player.PendingPlay = playReleased.Task;
        f.Player.PendingStop = stopReleased.Task;
        Task playing = preview ? f.Module.PreviewClipAsync(clip.Id, 0, 2, default) : f.Module.PlayAsync(clip.Id);
        Assert.False(f.Module.Playback.IsPlaying);
        var deleting = f.Module.DeleteClipAsync(clip.Id);
        try
        {
            Assert.Equal(1, f.Player.Stops);
            Assert.False(deleting.IsCompleted);
            await Assert.ThrowsAsync<SoundboardException>(() => f.Module.PlayAsync(clip.Id));
            await Assert.ThrowsAsync<SoundboardException>(() => f.Module.PreviewClipAsync(clip.Id, 0, 2, default));
            Assert.Single(f.Player.Plays);
        }
        finally { stopReleased.TrySetResult(); playReleased.TrySetResult(); }
        await deleting;
        await playing;
        Assert.False(f.Module.Playback.IsPlaying);
        Assert.Empty(f.Module.Library.Clips);
        Assert.False(File.Exists(f.Store.AudioPath(clip)));
    }

    [Fact]
    public async Task Deleting_another_sound_keeps_the_current_sound_playing()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        await f.Module.AddAsync(draft, new() { Name = "First", EndSeconds = 2 }, null, default);
        await f.Module.AddAsync(draft, new() { Name = "Second", EndSeconds = 2 }, null, default);
        var clips = f.Module.Library.Clips.ToArray();
        await f.Module.PlayAsync(clips[1].Id);

        await f.Module.DeleteClipAsync(clips[0].Id);

        Assert.Equal(clips[1].Id, f.Module.Playback.ClipId);
        Assert.True(f.Module.Playback.IsPlaying);
        Assert.Equal(0, f.Player.Stops);
    }

    [Fact]
    public async Task Deleting_the_previous_sound_preserves_a_pending_replacement()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        await f.Module.AddAsync(draft, new() { Name = "First", EndSeconds = 2 }, null, default);
        await f.Module.AddAsync(draft, new() { Name = "Second", EndSeconds = 2 }, null, default);
        var clips = f.Module.Library.Clips.ToArray();
        await f.Module.PlayAsync(clips[0].Id);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Player.PendingPlay = released.Task;
        var replacement = f.Module.PlayAsync(clips[1].Id);
        try
        {
            Assert.Equal(clips[0].Id, f.Module.Playback.ClipId);
            await f.Module.DeleteClipAsync(clips[0].Id);
            Assert.Equal(0, f.Player.Stops);
        }
        finally { released.TrySetResult(); }

        await replacement;

        Assert.Equal(clips[1].Id, f.Module.Playback.ClipId);
        Assert.True(f.Module.Playback.IsPlaying);
        Assert.Equal(clips[1].Id, Assert.Single(f.Module.Library.Clips).Id);
    }

    [Fact]
    public async Task Preview_commands_have_unique_ownership_and_saved_clip_edits_stop_their_preview()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        string firstDraft = await f.Module.PreviewAsync(draft, 0, 2, default);
        string secondDraft = await f.Module.PreviewAsync(draft, 0, 2, default);
        Assert.NotEqual(firstDraft, secondDraft);
        Assert.Equal(secondDraft, f.Module.Playback.ClipId);
        await f.Module.AddAsync(draft, new() { EndSeconds = 2 }, null, default);
        Assert.Equal(1, f.Player.Stops);
        var clip = Assert.Single(f.Module.Library.Clips);
        string firstClip = await f.Module.PreviewClipAsync(clip.Id, 0, 2, default);
        string secondClip = await f.Module.PreviewClipAsync(clip.Id, 0, 2, default);
        Assert.NotEqual(firstClip, secondClip);
        Assert.NotEqual(clip.Id, secondClip);

        await f.Module.SaveClipAsync(clip with { Name = "Renamed" });

        Assert.Equal(2, f.Player.Stops);
        Assert.False(f.Module.Playback.IsPlaying);
    }

    [Fact]
    public async Task Adding_a_different_draft_preserves_another_views_preview()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        string otherPath = Path.Combine(f.Directory, "other.wav");
        File.Copy(draft.LocalAudioPath, otherPath);
        using var other = new SoundImportDraft(otherPath, "Other", "File", 4, []);
        string previewId = await f.Module.PreviewAsync(draft, 0, 2, default);

        await f.Module.AddAsync(other, new() { EndSeconds = 2 }, null, default);

        Assert.Equal(previewId, f.Module.Playback.ClipId);
        Assert.True(f.Module.Playback.IsPlaying);
        Assert.Equal(0, f.Player.Stops);
    }

    [Fact]
    public async Task Editing_cannot_replace_media_identity_and_persists_presentation()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        await f.Module.AddAsync(draft, new() { EndSeconds = 4 }, null, default);
        var original = Assert.Single(f.Module.Library.Clips);
        string collection = await f.Module.CreateCollectionAsync("Games");
        await f.Module.SaveClipAsync(original with { Name = "  Win\n sound  ", CollectionId = collection, Favorite = true,
            AudioFile = "outside.wav", DurationSeconds = 1000, Source = "replaced", StartSeconds = 1, EndSeconds = 3 });
        var updated = Assert.Single(f.Module.Library.Clips);
        Assert.Equal("Win sound", updated.Name);
        Assert.Equal(original.AudioFile, updated.AudioFile);
        Assert.Equal(original.DurationSeconds, updated.DurationSeconds);
        Assert.Equal(original.Source, updated.Source);
        Assert.Equal(collection, updated.CollectionId);
        Assert.True(updated.Favorite);
        Assert.Equal(updated, Assert.Single(f.Store.Load().Clips) with { Waveform = updated.Waveform });
    }

    [Fact]
    public async Task Queued_stale_edits_cannot_overwrite_a_newer_save()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        await f.Module.AddAsync(draft, new() { Name = "Original", EndSeconds = 4 }, null, default);
        var original = Assert.Single(f.Module.Library.Clips);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Player.PendingLevels = released.Task;
        var levels = f.Module.SetLevelsAsync(.4f, .5f); // Occupies the metadata gate deterministically.
        var rename = f.Module.SaveClipAsync(original with { Name = "Renamed" }, expected: original);
        var staleFavorite = f.Module.SaveClipAsync(original with { Favorite = true }, expected: original);
        released.SetResult();
        await levels;
        await rename;
        var error = await Assert.ThrowsAsync<SoundboardException>(() => staleFavorite);
        Assert.Contains("changed in another view", error.Message);
        var saved = Assert.Single(f.Store.Load().Clips);
        Assert.Equal("Renamed", saved.Name);
        Assert.False(saved.Favorite);
        // Unrelated level changes and normalization do not create a false edit conflict.
        var current = Assert.Single(f.Module.Library.Clips);
        await f.Module.SetLevelsAsync(.6f, .7f);
        await f.Module.SaveClipAsync(current with { Favorite = true }, expected: current);
        Assert.True(Assert.Single(f.Module.Library.Clips).Favorite);
    }

    [Fact]
    public async Task Collections_reject_duplicate_names_and_nonempty_deletion()
    {
        await using var f = await Fixture.Start();
        await Assert.ThrowsAsync<SoundboardException>(() => f.Module.CreateCollectionAsync(" sounds "));
        string games = await f.Module.CreateCollectionAsync("Games");
        using var draft = f.Draft();
        await f.Module.AddAsync(draft, new() { CollectionId = games, EndSeconds = 2 }, null, default);
        await Assert.ThrowsAsync<SoundboardException>(() => f.Module.DeleteCollectionAsync(games));
        await f.Module.RenameCollectionAsync(games, "Reactions");
        var clip = f.Module.Library.Clips[0];
        await f.Module.SaveClipAsync(clip with { CollectionId = SoundboardLibrary.DefaultCollectionId });
        await f.Module.DeleteCollectionAsync(games);
        Assert.Single(f.Module.Library.Collections);
        Assert.Equal(SoundboardLibrary.DefaultCollectionId, f.Module.Library.SelectedCollectionId);
    }

    [Fact]
    public async Task Stale_reorder_keeps_new_sounds_and_other_collections()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        foreach (string name in new[] { "One", "Two", "Three" })
            await f.Module.AddAsync(draft, new() { Name = name, EndSeconds = 2 }, null, default);
        var original = f.Module.Library.Clips.ToArray();
        string other = await f.Module.CreateCollectionAsync("Other");
        await f.Module.SaveClipAsync(original[1] with { CollectionId = other });
        await f.Module.AddAsync(draft, new() { Name = "Arrived later", EndSeconds = 2 }, null, default);
        await f.Module.ReorderClipsAsync(SoundboardLibrary.DefaultCollectionId, [original[2].Id, original[1].Id, original[0].Id, "missing"]);
        Assert.Equal(["Three", "Two", "One", "Arrived later"], f.Module.Library.Clips.Select(c => c.Name));
        Assert.Equal(other, f.Module.Library.Clips[1].CollectionId);
    }

    [Fact]
    public async Task Levels_are_saved_and_invalid_values_do_not_reach_the_player()
    {
        await using var f = await Fixture.Start();
        await f.Module.SetLevelsAsync(.35f, .7f);
        Assert.Equal((.35f, .7f), f.Player.Levels);
        Assert.Equal(.35f, f.Store.Load().SendVolume);
        Assert.Equal(.7f, f.Store.Load().HearVolume);
        await Assert.ThrowsAsync<SoundboardException>(() => f.Module.SetLevelsAsync(float.NaN, .2f));
        Assert.Equal((.35f, .7f), f.Player.Levels);
    }

    [Fact]
    public async Task Stopping_cancels_an_import_and_disposes_the_shared_player()
    {
        await using var f = await Fixture.Start();
        var preparing = f.Module.PrepareAsync(new("https://example.invalid/sound.mp3", true), null, default);
        await f.Importer.Started.Task;
        await f.Module.StopAsync(CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparing);
        Assert.True(f.Player.Disposed);
        await Assert.ThrowsAsync<SoundboardException>(() => f.Module.StopAsync());
    }

    [Fact]
    public async Task Stopping_waits_for_import_cleanup_and_discards_a_cancelled_late_draft()
    {
        await using var f = await Fixture.Start();
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Importer.Cleanup = cleanup.Task;
        using var draft = new SoundImportDraft(Path.Combine(f.Directory, "late.wav"), "Late", "File", 4, []);
        f.Importer.ResultAfterCancellation = draft;
        var preparing = f.Module.PrepareAsync(new("https://example.invalid/sound.mp3", true), null, default);
        await f.Importer.Started.Task;
        var stopping = f.Module.StopAsync(CancellationToken.None);
        try
        {
            await f.Importer.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(stopping.IsCompleted);
            Assert.False(draft.IsDisposed);
        }
        finally { cleanup.TrySetResult(); }
        await stopping;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparing);
        Assert.True(draft.IsDisposed);
    }

    [Fact]
    public async Task A_cancelled_add_leaves_no_clip_or_copied_media()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Module.AddAsync(draft, new() { EndSeconds = 2 }, null, cancelled.Token));
        Assert.Empty(f.Module.Library.Clips);
        Assert.False(Directory.Exists(Path.Combine(f.Directory, "media")));
    }

    [Fact]
    public async Task A_teardown_failure_still_detaches_the_player_and_allows_restart()
    {
        await using var f = await Fixture.Start();
        using var draft = f.Draft();
        await f.Module.PreviewAsync(draft, 0, 2, default);
        f.Player.FailDispose = true;
        await Assert.ThrowsAsync<IOException>(() => f.Module.StopAsync(CancellationToken.None));
        Assert.False(f.Module.Playback.IsPlaying);
        await Assert.ThrowsAsync<SoundboardException>(() => f.Module.PreviewAsync(draft, 0, 2, default));
        f.Player.FailDispose = false;
        await f.StartAgain();
        await f.Module.PreviewAsync(draft, 0, 2, default);
        Assert.True(f.Module.Playback.IsPlaying);
    }

    [Fact]
    public async Task Startup_cleanup_preserves_the_initial_failure_and_detaches_the_player()
    {
        await using var f = await Fixture.Start();
        await f.Module.StopAsync(CancellationToken.None);
        f.Player.FailInitialize = f.Player.FailDispose = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(f.StartAgain);
        Assert.Equal("Initialize failed", error.Message);
        Assert.True(f.Player.Disposed);
        Assert.False(f.Module.Playback.IsPlaying);
        await Assert.ThrowsAsync<SoundboardException>(() => f.Module.StopAsync());
        f.Player.FailInitialize = f.Player.FailDispose = false;
        await f.StartAgain();
        Assert.Equal(3, f.Player.Initializations);
    }

    [Fact]
    public void Voice_capture_failure_has_a_nonblocking_warning()
    {
        Assert.Contains("sounds can still play", SoundboardModule.VoiceWarning(new(ProcessingState.Stopped, StopReason.Failed)));
        Assert.Null(SoundboardModule.VoiceWarning(new(ProcessingState.Live)));
        Assert.Null(SoundboardModule.VoiceWarning(new(ProcessingState.Stopped, StopReason.NoMicrophone)));
        Assert.Null(SoundboardModule.VoiceWarning(ProcessingStatus.Off));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "chippi-soundboard-module-" + Guid.NewGuid().ToString("N"));
        public SoundboardStore Store { get; }
        public FakePlayer Player { get; } = new();
        public WaitingImporter Importer { get; } = new();
        public RoutingUiFake Ui { get; } = new();
        public SoundboardModule Module { get; }
        private Fixture(string? unavailable)
        {
            Store = new(Directory);
            Module = new(Directory, store: Store, importer: Importer, createPlayer: () => Player, sendUnavailable: () => unavailable);
        }
        public static async Task<Fixture> Start(string? unavailable = null)
        {
            var f = new Fixture(unavailable);
            await f.StartAgain();
            return f;
        }
        public Task StartAgain() => Module.StartAsync(new(SoundboardModule.Id, new RoutingMemorySettings(), NullLogger.Instance,
            new IslandActivityHub().CreatePublisher(SoundboardModule.Id), Ui), default);
        public SoundImportDraft Draft()
        {
            System.IO.Directory.CreateDirectory(Directory);
            string path = Path.Combine(Directory, "original.wav");
            File.WriteAllBytes(path, [1, 2, 3, 4]); // The fake player never decodes or plays this fixture.
            return new(path, "Fixture", "File", 4, [.1f, .4f]);
        }
        public async ValueTask DisposeAsync()
        {
            await Module.StopAsync(CancellationToken.None);
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
    private sealed class WaitingImporter : ISoundboardImporter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Cleanup { get; set; } = Task.CompletedTask;
        public SoundImportDraft? ResultAfterCancellation { get; set; }
        public async Task<SoundImportDraft> PrepareAsync(SoundImportSource source, IProgress<SoundImportProgress>? progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("Cancelled import completed unexpectedly");
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                if (ResultAfterCancellation is { } draft) return draft;
                throw;
            }
            finally { await Cleanup; }
        }
    }
    private sealed class FakePlayer : ISoundboardPlayer
    {
        public SoundboardPlaybackState State { get; private set; } = SoundboardPlaybackState.Idle;
        public event Action? Changed;
        public List<(string Path, TimeSpan Start, TimeSpan End, bool Preview)> Plays { get; } = [];
        public int Initializations, Stops;
        public bool Disposed, FailDispose, FailInitialize;
        public (float Send, float Hear) Levels;
        public Task? PendingLevels;
        public Task? PendingPlay, PendingStop;
        private long _revision;
        public Task InitializeAsync(CancellationToken ct = default)
        {
            Initializations++; Disposed = false;
            if (FailInitialize) throw new InvalidOperationException("Initialize failed");
            return Task.CompletedTask;
        }
        public async Task PlayAsync(string clipId, string path, TimeSpan start, TimeSpan end, bool preview = false, CancellationToken ct = default)
        {
            long revision = Interlocked.Increment(ref _revision);
            Plays.Add((path, start, end, preview));
            if (PendingPlay is { } pending) await pending;
            ct.ThrowIfCancellationRequested();
            if (revision != Interlocked.Read(ref _revision)) return;
            State = new(clipId, true, preview, TimeSpan.Zero, end - start);
            Changed?.Invoke();
        }
        public async Task StopAsync(CancellationToken ct = default)
        {
            long revision = Interlocked.Increment(ref _revision);
            Stops++;
            if (PendingStop is { } pending) await pending;
            if (revision != Interlocked.Read(ref _revision)) return;
            State = SoundboardPlaybackState.Idle;
            Changed?.Invoke();
        }
        public Task SetLevelsAsync(float send, float hear, CancellationToken ct = default) { Levels = (send, hear); return PendingLevels ?? Task.CompletedTask; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (FailDispose) throw new IOException("Dispose failed");
            return ValueTask.CompletedTask;
        }
    }
}
