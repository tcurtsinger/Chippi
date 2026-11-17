// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Clipboard.Storage;
using Microsoft.Data.Sqlite;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class RecentRecoveryTests
{
    private sealed class Protector(bool canOpen = true, byte mask = 0x5A) : IKeyProtector
    {
        public byte[] Protect(byte[] key) => [.. key.Select(b => (byte)(b ^ mask))];
        public byte[] Unprotect(byte[] wrapped) => canOpen ? [.. wrapped.Select(b => (byte)(b ^ mask))] : throw new CryptographicException("key changed");
    }

    private static BitmapSource Pixels() => BitmapSource.Create(40, 30, 96, 96, PixelFormats.Bgra32, null, new byte[40 * 30 * 4], 160);

    private static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ex; }
            finally { SqliteConnection.ClearAllPools(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static string Folder() => Path.Combine(Path.GetTempPath(), "chippi-recovery-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void An_unreadable_entry_is_skipped_noted_and_leaves_every_file() => Sta(() =>
    {
        string folder = Folder();
        try
        {
            Guid good = Guid.NewGuid();
            using (var store = new RecentCaptureStore(folder, new Protector()))
            {
                store.Put(Guid.NewGuid(), Pixels(), DateTimeOffset.Now, null);
                store.Put(good, Pixels(), DateTimeOffset.Now, null);
            }
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(folder, "recent.db")};Pooling=False"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE captures SET entry = X'0102' WHERE id = (SELECT MIN(id) FROM captures)";
                Assert.Equal(1, cmd.ExecuteNonQuery());
            }
            using (var store = new RecentCaptureStore(folder, new Protector()))
            {
                Assert.True(store.HasUnreadable);
                Assert.Equal(good, Assert.Single(store.Read()).Id);
                Assert.NotEmpty(store.Thumbnail(good));
                Assert.Equal(2, Directory.GetFiles(folder, "*.png").Length); // Nothing was deleted as an orphan.
                store.Put(Guid.NewGuid(), Pixels(), DateTimeOffset.Now, null); // Saving to Recent still works.
                Assert.Equal(2, store.Read().Count);
                store.Remove(good);
                store.Clear();
                Assert.False(store.HasUnreadable);
            }
        }
        finally { Directory.Delete(folder, true); }
    });

    [Fact]
    public void A_changed_key_starts_an_empty_index_and_keeps_the_old_files() => Sta(() =>
    {
        string folder = Folder();
        try
        {
            using (var store = new RecentCaptureStore(folder, new Protector()))
                store.Put(Guid.NewGuid(), Pixels(), DateTimeOffset.Now, null);
            using (var store = new RecentCaptureStore(folder, new Protector(canOpen: false)))
            {
                Assert.True(store.HasUnreadable);
                Assert.Empty(store.Read());
                Assert.Single(Directory.GetFiles(folder, "*.png"));
                Assert.NotEmpty(Directory.GetFiles(folder, "recent.unreadable-*.db"));
                store.Put(Guid.NewGuid(), Pixels(), DateTimeOffset.Now, null);
                Assert.Single(store.Read());
            }
            // A later start opens the new index and still doesn't sweep the old image as an orphan.
            using (var store = new RecentCaptureStore(folder, new Protector()))
            {
                Assert.Single(store.Read());
                Assert.Equal(2, Directory.GetFiles(folder, "*.png").Length);
            }
        }
        finally { Directory.Delete(folder, true); }
    });

    [Fact]
    public void Later_starts_sweep_new_orphans_but_not_images_from_before_the_set_aside() => Sta(() =>
    {
        string folder = Folder();
        try
        {
            using (var store = new RecentCaptureStore(folder, new Protector()))
                store.Put(Guid.NewGuid(), Pixels(), DateTimeOffset.Now, null);
            string old = Directory.GetFiles(folder, "*.png").Single();
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-5));
            using (new RecentCaptureStore(folder, new Protector(canOpen: false))) { }
            string orphan = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(orphan, [1, 2, 3]);
            File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddMinutes(5));
            using (new RecentCaptureStore(folder, new Protector())) { }
            Assert.True(File.Exists(old));
            Assert.False(File.Exists(orphan));
        }
        finally { Directory.Delete(folder, true); }
    });

    [Theory]
    [InlineData("shot.png", 2, CaptureFileType.Png)]
    [InlineData("shot.PNG", 2, CaptureFileType.Png)]
    [InlineData("shot.jpg", 1, CaptureFileType.Jpeg)]
    [InlineData("shot.jpeg", 1, CaptureFileType.Jpeg)]
    [InlineData("shot", 2, CaptureFileType.Jpeg)]
    [InlineData("shot", 1, CaptureFileType.Png)]
    public void Save_as_follows_the_typed_extension_before_the_filter(string name, int filter, CaptureFileType expected)
        => Assert.Equal(expected, CaptureFiles.TypeFor(name, filter));

    [Theory]
    [InlineData(typeof(CryptographicException))]
    [InlineData(typeof(InvalidDataException))]
    public async Task A_damaged_recent_index_reports_recent_failed_instead_of_throwing(Type failure)
    {
        var pixels = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        pixels.Freeze();
        var output = new CaptureOutput(pixels, _ => Task.FromResult(true), _ => Task.FromResult("x.png"),
            (_, _) => throw (Exception)Activator.CreateInstance(failure)!);
        var result = await output.FinishAsync(EnterAction.Copy);
        Assert.True(result.RecentFailed);
        Assert.True(result.Copied);
    }
}
