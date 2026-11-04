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
        public async Task<SoundImportDraft> PrepareAsync(SoundImportSource source, IProgress<SoundImportProgress>? progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Cancelled import completed unexpectedly");
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
        public Task InitializeAsync(CancellationToken ct = default)
        {
            Initializations++; Disposed = false;
            if (FailInitialize) throw new InvalidOperationException("Initialize failed");
            return Task.CompletedTask;
        }
        public Task PlayAsync(string clipId, string path, TimeSpan start, TimeSpan end, bool preview = false, CancellationToken ct = default)
        {
            Plays.Add((path, start, end, preview));
            State = new(clipId, true, preview, TimeSpan.Zero, end - start);
            Changed?.Invoke();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct = default) { Stops++; State = SoundboardPlaybackState.Idle; Changed?.Invoke(); return Task.CompletedTask; }
        public Task SetLevelsAsync(float send, float hear, CancellationToken ct = default) { Levels = (send, hear); return Task.CompletedTask; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (FailDispose) throw new IOException("Dispose failed");
            return ValueTask.CompletedTask;
        }
    }
}
