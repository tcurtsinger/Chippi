// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;
using Chippi.Features.Clipboard.History;

namespace Chippi.Features.Clipboard.Tests;

public sealed class ClipboardHistoryTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeHub _hub = new();
    private readonly ClipboardHistory _history;
    private int _opened;

    public ClipboardHistoryTests() => _history = Create();

    public void Dispose()
    {
        _history.Dispose();
        _folder.Dispose();
    }

    private ClipboardHistory Create(Func<string, string?>? autoClean = null) => new(_hub, () =>
    {
        _opened++;
        return HistoryStore.Open(_folder.File("history.db"), new TestKeys());
    }, autoClean);

    private async Task CopyAsync(string text, ClipPrivacy privacy = ClipPrivacy.None, string owner = @"C:\Apps\Editor.exe")
    {
        _hub.CopyText(text, privacy, owner);
        await _history.IdleAsync();
    }

    [Fact]
    public async Task Nothing_listens_or_is_created_until_recording_is_on_and_not_paused()
    {
        await _history.LoadAsync(exists: false);
        Assert.Equal(0, _hub.Leases);
        Assert.Equal(0, _opened);
        _history.Apply(recording: true, paused: false, []);
        Assert.Equal(1, _hub.Leases);
        _history.Apply(recording: true, paused: true, []);
        Assert.Equal(0, _hub.Leases);
        await CopyAsync("while paused");
        Assert.Empty(_history.Entries);
        _history.Apply(recording: false, paused: false, []);
        Assert.Equal(0, _hub.Leases);
        Assert.Equal(0, _opened);
    }

    [Fact]
    public async Task A_copy_past_seven_days_goes_without_another_copy_and_pins_stay()
    {
        var clock = new Clock();
        string path = _folder.File("age.db");
        using (var history = new ClipboardHistory(_hub, () => HistoryStore.Open(path, new TestKeys()), time: clock))
        {
            history.Apply(true, false, []);
            _hub.CopyText("kept by its pin");
            await history.IdleAsync();
            _hub.CopyText("old");
            await history.IdleAsync();
            await history.PinAsync(history.Entries.Single(e => e.Text == "kept by its pin").Id, true);
            var old = history.Entries.Single(e => e.Text == "old");

            clock.Now += TimeSpan.FromDays(8);
            Assert.Equal("kept by its pin", Assert.Single(history.Entries).Text);
            Assert.Equal(ClipStatus.Empty, await history.CopyAsync(old));
            Assert.Equal(PasteOutcome.NoText, await history.PasteAsync(old, new PasteTarget(1, 1)));
            Assert.Empty(_hub.Written);
            Assert.Empty(_hub.Pasted);
            await history.IdleAsync();
        }
        // Gone from the store too: read back as of before it expired, so this load prunes nothing.
        using var store = HistoryStore.Open(path, new TestKeys());
        Assert.Equal("kept by its pin", Assert.Single(store.Load(DateTimeOffset.UtcNow)).Text);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Copies_are_saved_and_a_copy_not_for_history_leaves_nothing()
    {
        _history.Apply(true, false, []);
        await CopyAsync("first");
        await CopyAsync("hunter2", ClipPrivacy.NoHistory);
        await CopyAsync("second");
        Assert.Equal(["second", "first"], _history.Entries.Select(e => e.Text));
    }

    [Fact]
    public async Task Apps_to_skip_are_matched_by_path_ignoring_case()
    {
        _history.Apply(true, false, [@"C:\Apps\Vault.exe"]);
        await CopyAsync("secret", owner: @"c:\apps\VAULT.EXE");
        await CopyAsync("ordinary");
        Assert.Equal("ordinary", Assert.Single(_history.Entries).Text);
    }

    [Fact]
    public async Task A_copy_over_a_limit_shows_until_the_next_copy()
    {
        _history.Apply(true, false, []);
        _hub.Copy(null, limit: ClipLimit.Image);
        await _history.IdleAsync();
        Assert.Equal(ClipLimit.Image, _history.Limit);
        Assert.Empty(_history.Entries);
        await CopyAsync("next");
        Assert.Equal(ClipLimit.None, _history.Limit);
    }

    [Fact]
    public async Task Automatic_cleaning_saves_only_the_cleaned_link()
    {
        using var history = Create(text => text.Contains("utm_source") ? "https://example.org/p" : null);
        history.Apply(true, false, []);
        _hub.CopyText("https://example.org/p?utm_source=a");
        await history.IdleAsync();
        var entry = Assert.Single(history.Entries);
        Assert.Equal("https://example.org/p", entry.Text);
        Assert.Equal(EntryKind.Link, entry.Kind);
    }

    [Fact]
    public async Task A_picture_is_kept_as_a_png_with_a_thumbnail_and_copies_back()
    {
        _history.Apply(true, false, []);
        var pixels = Enumerable.Range(0, 400 * 2).SelectMany(i => new byte[] { (byte)i, 20, 30, 255 }).ToArray();
        _hub.Copy(ClipContent.FromImage(new ClipImage(400, 2, pixels)));
        await _history.IdleAsync();
        var entry = Assert.Single(_history.Entries);
        Assert.Equal((EntryKind.Picture, 400, 2), (entry.Kind, entry.Width, entry.Height));
        Assert.Equal(Pictures.ThumbnailWidth, Pictures.Load(entry.Thumbnail!).PixelWidth);

        Assert.Equal(ClipStatus.Ok, await _history.CopyAsync(entry));
        var image = Assert.Single(_hub.Written).Image!;
        Assert.Equal(pixels, image.Bgra);
    }

    [Fact]
    public async Task A_copy_read_before_erase_all_is_not_saved_after_it()
    {
        _history.Apply(true, false, []);
        await CopyAsync("kept?");
        // Hold the worker so the next copy queues behind the erase.
        var held = _history.PictureAsync(-1);
        _hub.CopyText("in flight");
        await _history.EraseAllAsync();
        await _history.IdleAsync();
        await held;
        Assert.Empty(_history.Entries);
        await CopyAsync("after");
        Assert.Equal("after", Assert.Single(_history.Entries).Text);
    }

    [Fact]
    public async Task Pins_survive_clear_and_full_clears_when_a_pin_goes()
    {
        _history.Apply(true, false, []);
        await CopyAsync("pin me");
        await CopyAsync("recent");
        var pin = _history.Entries.First(e => e.Text == "pin me");
        await _history.PinAsync(pin.Id, true);
        await _history.ClearAsync([.. _history.Entries.Select(e => e.Id)]);
        Assert.Equal("pin me", Assert.Single(_history.Entries).Text);
        Assert.True(_history.Any());
    }

    [Fact]
    public async Task Files_with_one_missing_are_not_copied()
    {
        string here = _folder.File("here.txt");
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(here, "x");
        _history.Apply(true, false, []);
        _hub.Copy(ClipContent.FromFiles([here, _folder.File("gone.txt")]));
        await _history.IdleAsync();
        var entry = Assert.Single(_history.Entries);
        Assert.Equal(1, ClipboardHistory.MissingFiles(entry));
        Assert.Equal(ClipStatus.Empty, await _history.CopyAsync(entry));
        Assert.Empty(_hub.Written);
    }

    [Fact]
    public async Task Saved_history_comes_back_on_the_next_start()
    {
        _history.Apply(true, false, []);
        await CopyAsync("remembered");
        _history.Dispose();
        using var again = Create();
        await again.LoadAsync(exists: true);
        Assert.Equal("remembered", Assert.Single(again.Entries).Text);
    }
}
