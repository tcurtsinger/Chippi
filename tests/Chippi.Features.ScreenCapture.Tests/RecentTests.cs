// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Features.ScreenCapture;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class RecentTests
{
    [Fact]
    public void Fifty_entries_reopen_thumbnails_deduplicate_and_never_delete_saved_files()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "chippi-recent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string external = Path.Combine(folder, "saved.png"); File.WriteAllText(external, "owner file");
            try
            {
                var pixels = BitmapSource.Create(400, 300, 96, 96, PixelFormats.Bgra32, null, new byte[400 * 300 * 4], 1600);
                Guid first = Guid.NewGuid(), last = Guid.NewGuid();
                using (var store = new RecentCaptureStore(folder))
                {
                    store.Put(first, pixels, DateTimeOffset.Now, external);
                    for (int i = 0; i < 49; i++) store.Put(Guid.NewGuid(), pixels, DateTimeOffset.Now, null);
                    store.Put(last, pixels, DateTimeOffset.Now, external);
                    Assert.Equal(50, store.Read().Count);
                    Assert.DoesNotContain(store.Read(), x => x.Id == first);
                    store.Put(last, pixels, DateTimeOffset.Now, null);
                    Assert.Equal(50, store.Read().Count);
                    Assert.Equal(external, store.Read()[0].SavedFile);
                    Assert.NotEmpty(store.Thumbnail(last));
                }
                using (var store = new RecentCaptureStore(folder))
                {
                    Assert.Equal(50, store.Read().Count);
                    Assert.NotEmpty(store.Thumbnail(last));
                    var reopened = store.Load(last);
                    Assert.Equal(400, reopened.PixelWidth);
                    Assert.Equal(300, reopened.PixelHeight);
                    Assert.True(reopened.IsFrozen);
                    Assert.False(new CaptureDraft(reopened).Undo());
                    store.Remove(last); Assert.Equal(49, store.Read().Count);
                    Assert.Throws<FileNotFoundException>(() => store.Load(last));
                    // Loading releases the file, so removal succeeds while its pixels remain usable.
                    Assert.Equal(400, reopened.PixelWidth);
                    store.Clear(); Assert.Empty(store.Read());
                }
                Assert.Equal("owner file", File.ReadAllText(external));
                Assert.Single(Directory.GetFiles(folder, "*.png"));
            }
            catch (Exception ex) { error = ex; }
            finally { Directory.Delete(folder, true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
