// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Clipboard;
using Chippi.Features.Clipboard.History;
using Chippi.Features.Clipboard.Views;
using Chippi.Island.Tests;

namespace Chippi.Features.Clipboard.Tests;

/// <summary>Rows for one image file show its picture, read from a fake disk that counts every open.</summary>
public sealed class FileThumbnailTests
{
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    private static readonly byte[] Png = Pictures.Encode(new ClipImage(2, 1, [255, 0, 0, 255, 0, 0, 255, 255]))!.Value.Png;

    private readonly Dictionary<string, FileAttributes> _disk = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _opened = [];

    private FileThumbnails Files => new(
        path => _disk.TryGetValue(path, out var attributes) ? attributes : null,
        path =>
        {
            lock (_opened)
                _opened.Add(path);
            return new MemoryStream(_bytes[path]);
        });

    private void Put(string path, byte[] bytes, FileAttributes attributes = FileAttributes.Normal)
    {
        _disk[path] = attributes;
        _bytes[path] = bytes;
    }

    private static HistoryEntry Row(long id, params string[] files) =>
        new(id, EntryKind.Files, DateTimeOffset.UtcNow, null, null, files, 0, 0, null, 0);

    [Fact]
    public void One_image_file_is_read_and_drawn()
    {
        Put(@"C:\Pics\test.png", Png);
        var picture = Files.Read(@"C:\Pics\test.png");
        Assert.NotNull(picture);
        Assert.Equal(2, picture.PixelWidth);
        Assert.Equal(@"C:\Pics\test.PNG", FileThumbnails.Picture(Row(1, @"C:\Pics\test.PNG")));
    }

    [Fact]
    public void The_real_disk_reads_a_picture_and_leaves_the_file_free()
    {
        using var folder = new TempFolder();
        string path = folder.File("real.png");
        Directory.CreateDirectory(folder.Path);
        File.WriteAllBytes(path, Png);
        Assert.NotNull(FileThumbnails.Disk.Read(path));
        File.Delete(path); // not held open
        Assert.Null(FileThumbnails.Disk.Read(path));
    }

    [Fact]
    public void An_online_only_file_is_never_opened()
    {
        Put(@"C:\OneDrive\cloud.png", Png, FileAttributes.Archive | RecallOnDataAccess);
        Put(@"C:\OneDrive\offline.jpg", Png, FileAttributes.Offline);
        Assert.Null(Files.Read(@"C:\OneDrive\cloud.png"));
        Assert.Null(Files.Read(@"C:\OneDrive\offline.jpg"));
        Assert.Empty(_opened);
    }

    [Fact]
    public void A_missing_file_is_never_opened()
    {
        Assert.Null(Files.Read(@"C:\Pics\gone.png"));
        Assert.Empty(_opened);
    }

    [Fact]
    public void Other_files_and_several_files_get_no_picture()
    {
        Assert.Null(FileThumbnails.Picture(Row(1, @"C:\Docs\note.txt")));
        Assert.Null(FileThumbnails.Picture(Row(2, @"C:\Pics\a.png", @"C:\Pics\b.png")));
        Assert.Null(FileThumbnails.Picture(Row(3, @"C:\Pics\folder.png\")));
        Assert.Null(FileThumbnails.Picture(new HistoryEntry(4, EntryKind.Text, DateTimeOffset.UtcNow, null, "a.png", null, 0, 0, null, 0)));
    }

    [Fact]
    public void A_file_that_wont_decode_or_is_over_the_limit_gets_no_picture()
    {
        Put(@"C:\Pics\broken.png", [1, 2, 3]);
        Put(@"C:\Pics\huge.png", new byte[ClipLimits.ImageEncodedBytes + 1]);
        Assert.Null(Files.Read(@"C:\Pics\broken.png"));
        Assert.Null(Files.Read(@"C:\Pics\huge.png"));
    }

    [Fact]
    public void The_page_reads_off_the_ui_thread_and_a_reused_row_id_never_keeps_the_old_picture()
    {
        Put(@"C:\Pics\first.png", Png);
        Put(@"C:\Pics\second.png", Png);
        WpfTestThread.Run(() =>
        {
            var view = new ClipboardView(new ClipboardFeatures()) { Files = Files };
            Assert.Null(view.FileThumbnail(Row(7, @"C:\Pics\first.png"))); // not read yet
            view.Reading.Wait();
            Flush(view);
            var first = view.FileThumbnail(Row(7, @"C:\Pics\first.png"));
            Assert.NotNull(first);

            // Same id, another file: nothing until the new file is read, then its own picture.
            Assert.Null(view.FileThumbnail(Row(7, @"C:\Pics\second.png")));
            view.Reading.Wait();
            Flush(view);
            var second = view.FileThumbnail(Row(7, @"C:\Pics\second.png"));
            Assert.NotNull(second);
            Assert.NotSame(first, second);
            Assert.Same(second, view.FileThumbnail(Row(7, @"C:\Pics\second.png")));
        });
        Assert.Equal([@"C:\Pics\first.png", @"C:\Pics\second.png"], _opened);
    }

    // Runs what's queued on the page's thread, the read's result included.
    private static void Flush(System.Windows.Threading.DispatcherObject on)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        on.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => frame.Continue = false);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
