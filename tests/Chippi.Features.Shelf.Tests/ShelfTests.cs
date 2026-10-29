// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Clipboard.Storage;
using Chippi.Core.Clipboard;
using Chippi.Features.Clipboard.Tests;

namespace Chippi.Features.Shelf.Tests;

public sealed class ShelfTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeHub _hub = new();
    private readonly ShelfService _shelf;

    public ShelfTests() => _shelf = Create();

    public void Dispose()
    {
        _shelf.Dispose();
        _folder.Dispose();
    }

    private ShelfService Create(long capacity = ClipLimits.StoreBytes) =>
        new(_hub, () => ShelfStore.Open(_folder.File("shelf.db"), new TestKeys(), capacity));

    [Fact]
    public async Task One_drop_is_one_item_newest_first_and_comes_back_on_the_next_start()
    {
        string a = _folder.Touch("a.pdf", 2100), b = _folder.Touch("b.txt", 10), c = _folder.Touch("c.txt", 10);
        await _shelf.AddFilesAsync([a]);
        await _shelf.AddFilesAsync([b, c]);
        await _shelf.AddTextAsync("Call Dana about the venue\nBring the plan");
        await _shelf.AddTextAsync("https://example.org/docs/setup");
        Assert.Equal([ShelfKind.Link, ShelfKind.Text, ShelfKind.Files, ShelfKind.Files], _shelf.Items.Select(i => i.Kind));
        Assert.Equal([b, c], _shelf.Items[2].Files);

        _shelf.Dispose();
        using var again = Create();
        await again.LoadAsync(exists: true);
        Assert.Equal(4, again.Items.Count);
    }

    [Fact]
    public async Task Items_are_sealed_on_disk()
    {
        await _shelf.AddTextAsync("unmistakable-shelf-secret");
        _shelf.Dispose();
        foreach (var file in Directory.GetFiles(_folder.Path))
            Assert.DoesNotContain("unmistakable-shelf-secret", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
    }

    [Fact]
    public async Task Over_100_files_is_refused_and_the_line_stays_until_the_next_drop()
    {
        var files = Enumerable.Range(0, 101).Select(i => _folder.File($"f{i}.txt")).ToList();
        await _shelf.AddFilesAsync(files);
        Assert.Empty(_shelf.Items);
        Assert.Equal(ShelfLimit.Files, _shelf.Limit);
        await _shelf.AddTextAsync("next");
        Assert.Equal(ShelfLimit.None, _shelf.Limit);
    }

    [Fact]
    public async Task A_picture_is_kept_as_png_and_copies_back()
    {
        await _shelf.AddPictureAsync(Bitmap(4, 3));
        var item = Assert.Single(_shelf.Items);
        Assert.Equal((ShelfKind.Picture, 4, 3), (item.Kind, item.Width, item.Height));
        Assert.Equal(ClipStatus.Ok, await _shelf.CopyAsync(item));
        Assert.Equal(4, Assert.Single(_hub.Written).Image!.Width);
        Assert.Single(_shelf.Items); // Copy keeps it (notes #39)
    }

    [Fact]
    public async Task A_picture_over_the_pixel_limit_is_refused()
    {
        await _shelf.AddPictureAsync(Bitmap(5000, 3400));
        Assert.Empty(_shelf.Items);
        Assert.Equal(ShelfLimit.Picture, _shelf.Limit);
    }

    [Fact]
    public async Task At_the_cap_a_drop_is_refused_nothing_is_evicted_and_files_count_nothing()
    {
        using var small = Create(capacity: 200);
        await small.AddTextAsync(new string('x', 120));
        await small.AddTextAsync(new string('y', 120));
        Assert.Single(small.Items);
        Assert.Equal(ShelfLimit.Full, small.Limit);
        await small.AddFilesAsync([_folder.Touch("big.bin", 10_000)]);
        Assert.Equal(2, small.Items.Count);
        Assert.Equal(ShelfLimit.None, small.Limit);
        await small.AddTextAsync(new string('z', 120));
        Assert.Equal(ShelfLimit.Full, small.Limit);
        await small.RemoveAsync(small.Items.Single(i => i.Kind == ShelfKind.Text).Id);
        Assert.Equal(ShelfLimit.None, small.Limit);
    }

    [Fact]
    public async Task At_40_items_the_shelf_is_full_and_refuses_drops_until_one_is_removed()
    {
        for (int i = 0; i < ShelfService.MaxItems; i++)
            await _shelf.AddTextAsync($"item {i}");
        await _shelf.AddTextAsync("one more");
        Assert.Equal(40, _shelf.Items.Count); // nothing thrown away
        Assert.DoesNotContain(_shelf.Items, i => i.Text == "one more");
        Assert.Equal("Shelf full", ShelfFormat.Limit(_shelf.Limit));
        await _shelf.RemoveAsync(_shelf.Items[^1].Id);
        Assert.Equal(ShelfLimit.None, _shelf.Limit);
        await _shelf.AddTextAsync("one more");
        Assert.Equal("one more", _shelf.Items[0].Text);
    }

    [Fact]
    public async Task Pin_and_remove()
    {
        await _shelf.AddTextAsync("keep");
        var item = Assert.Single(_shelf.Items);
        await _shelf.PinAsync(item.Id, true);
        Assert.True(Assert.Single(_shelf.Items).Pinned);
        await _shelf.RemoveAsync(item.Id);
        Assert.Empty(_shelf.Items);
    }

    [Fact]
    public async Task A_missing_file_is_not_copied()
    {
        string here = _folder.Touch("here.txt", 1);
        await _shelf.AddFilesAsync([here, _folder.File("gone.txt")]);
        var item = Assert.Single(_shelf.Items);
        Assert.Equal(1, ShelfService.Missing(item));
        Assert.Equal(ClipStatus.Empty, await _shelf.CopyAsync(item));
        Assert.Empty(_hub.Written);
    }

    [Fact]
    public void Links_are_one_web_link_with_nothing_around_it()
    {
        Assert.True(ShelfDrop.IsLink(" https://example.org/a?b=1 "));
        Assert.False(ShelfDrop.IsLink("see https://example.org"));
        Assert.False(ShelfDrop.IsLink("ftp://example.org/a"));
        Assert.False(ShelfDrop.IsLink("example.org"));
    }

    internal static BitmapSource Bitmap(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)(i % 251), 80, 160, 255);
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }
}

/// <summary>Stands in for DPAPI.</summary>
internal sealed class TestKeys : IKeyProtector
{
    public byte[] Protect(byte[] key) => [.. key.Select(b => (byte)(b ^ 0x5A))];

    public byte[] Unprotect(byte[] wrapped) => [.. wrapped.Select(b => (byte)(b ^ 0x5A))];
}

/// <summary>A folder of its own per test, deleted afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chippi-shelf-tests", Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>A file of <paramref name="bytes"/> bytes, invented content.</summary>
    public string Touch(string name, int bytes)
    {
        string path = File(name);
        System.IO.File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
