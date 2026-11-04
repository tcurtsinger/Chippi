// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Features.Sound.Soundboard.Library;

namespace Chippi.Features.Sound.Tests;

public sealed class SoundboardLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Chippi-tests", "soundboard-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_new_library_has_one_empty_collection_and_independent_levels()
    {
        var library = new SoundboardStore(_root).Load();
        Assert.Equal("Sounds", Assert.Single(library.Collections).Name);
        Assert.Empty(library.Clips);
        Assert.Equal(.8f, library.SendVolume);
        Assert.Equal(.6f, library.HearVolume);
    }

    [Fact]
    public void Commit_copies_media_and_atomic_metadata_preserves_order_trim_and_favorite()
    {
        Directory.CreateDirectory(_root);
        string original = Path.Combine(_root, "original.wav");
        File.WriteAllBytes(original, [1, 2, 3]);
        var store = new SoundboardStore(Path.Combine(_root, "library"));
        using var draft = new SoundImportDraft(original, "Air horn", "File", 12, [.2f, .7f]);
        var clip = store.CommitDraft(draft, "  Air\r\nhorn  ", "reactions", 2, 5, favorite: true);
        var library = new SoundboardLibrary
        {
            Collections = [new("reactions", "Reactions"), new("games", "Games")], Clips = [clip],
            SelectedCollectionId = SoundboardLibrary.FavoritesId, SendVolume = .2f, HearVolume = .7f,
        };
        store.Save(library);
        var read = store.Load();
        Assert.Equal(["Reactions", "Games"], read.Collections.Select(c => c.Name));
        Assert.Equal(2, Assert.Single(read.Clips).StartSeconds);
        Assert.Equal(5, read.Clips[0].EndSeconds);
        Assert.Equal("Air horn", read.Clips[0].Name);
        Assert.True(read.Clips[0].Favorite);
        Assert.Equal(SoundboardLibrary.FavoritesId, read.SelectedCollectionId);
        Assert.Equal(.7f, read.HearVolume);
        Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(store.AudioPath(clip)));
        store.Save(read with { Clips = [] });
        store.DeleteMedia(clip);
        Assert.False(File.Exists(store.AudioPath(clip)));
        Assert.True(File.Exists(original));
    }

    [Fact]
    public void Corrupt_primary_recovers_backup_without_destroying_it_on_next_save()
    {
        var store = new SoundboardStore(_root);
        store.Save(new() { HearVolume = .25f });
        store.Save(new() { HearVolume = .5f });
        File.WriteAllText(Path.Combine(_root, "library.json"), "{broken");
        var recovered = store.Load();
        Assert.Equal(.25f, recovered.HearVolume);
        Assert.NotNull(store.RecoveryNotice);
        store.Save(recovered);
        Assert.Equal(.25f, store.Load().HearVolume);
        Assert.Contains("0.25", File.ReadAllText(Path.Combine(_root, "library.json.bak")));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void Corrupt_library_without_backup_is_not_silently_replaced()
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, "library.json");
        File.WriteAllText(file, "{broken");
        Assert.Throws<SoundboardException>(() => new SoundboardStore(_root).Load());
        Assert.Equal("{broken", File.ReadAllText(file));
    }

    [Theory]
    [InlineData("../../outside.wav")]
    [InlineData("C:\\outside.wav")]
    [InlineData("clip.wav")]
    [InlineData("00000000000000000000000000000000.exe")]
    public void Metadata_cannot_delete_or_reference_arbitrary_files(string path)
    {
        var store = new SoundboardStore(_root);
        var clip = new SoundClip { AudioFile = path, DurationSeconds = 3, EndSeconds = 3 };
        Assert.Throws<SoundboardException>(() => store.AudioPath(clip));
        Assert.Throws<SoundboardException>(() => store.DeleteMedia(clip));
        Assert.Throws<SoundboardException>(() => store.Save(new() { Clips = [clip] }));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Validation_clamps_levels_recovers_orphans_and_rejects_invalid_trim()
    {
        var clip = new SoundClip { AudioFile = Guid.NewGuid().ToString("N") + ".wav", CollectionId = "missing", DurationSeconds = 3,
            EndSeconds = 3, Waveform = [float.NaN, 2, -1] };
        var normalized = SoundboardStore.Normalize(new() { Clips = [clip], SendVolume = 2, HearVolume = float.NaN });
        Assert.Equal(SoundboardLibrary.DefaultCollectionId, normalized.Clips[0].CollectionId);
        Assert.Equal([0f, 1f, 0f], normalized.Clips[0].Waveform);
        Assert.Equal(1, normalized.SendVolume);
        Assert.Equal(.6f, normalized.HearVolume);
        Assert.Throws<SoundboardException>(() => SoundboardStore.Normalize(normalized with { Clips = [clip with { StartSeconds = 4 }] }));
        Assert.Throws<SoundboardException>(() => SoundboardStore.ValidateRange(3, 0, double.NaN));
        Assert.Throws<SoundboardException>(() => SoundboardStore.ValidateRange(3, 0, 4));
    }

    [Fact]
    public Task Cover_thumbnail_bounds_both_axes_and_deletion_preserves_the_original() => SoundUiThread.Sta(() =>
    {
        Directory.CreateDirectory(_root);
        string original = Path.Combine(_root, "tall.png");
        var pixels = BitmapSource.Create(20, 2000, 96, 96, PixelFormats.Bgra32, null, new byte[20 * 2000 * 4], 20 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(pixels));
        using (var output = File.Create(original)) encoder.Save(output);
        var store = new SoundboardStore(Path.Combine(_root, "library"));
        string owned = store.ImportCover(original);
        string copy = store.ImagePath(new SoundClip { ImageFile = owned })!;
        using (var input = File.OpenRead(copy))
        {
            var decoded = BitmapDecoder.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.InRange(decoded.PixelWidth, 1, 256);
            Assert.Equal(256, decoded.PixelHeight);
        }
        store.DeleteCover(owned);
        Assert.False(File.Exists(copy));
        Assert.True(File.Exists(original));
        Assert.Throws<SoundboardException>(() => store.DeleteCover("../../tall.png"));
    });

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
