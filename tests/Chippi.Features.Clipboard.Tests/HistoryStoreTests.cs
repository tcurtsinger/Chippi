// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text;
using Chippi.Features.Clipboard.History;

namespace Chippi.Features.Clipboard.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private readonly TempFolder _folder = new();
    private HistoryStore _store;

    public HistoryStoreTests() => _store = Open();

    public void Dispose()
    {
        _store.Dispose();
        _folder.Dispose();
    }

    private HistoryStore Open(long capacity = Core.Clipboard.ClipLimits.StoreBytes) =>
        HistoryStore.Open(_folder.File("history.db"), new TestKeys(), capacity);

    private HistoryEntry Add(string text, DateTimeOffset at) => _store.Add(HistoryCapture.FromText(text), at).Entry!;

    [Fact]
    public void Copies_come_back_newest_first_after_a_restart_with_nothing_plain_on_disk()
    {
        Add("invented first", T0);
        _store.Add(HistoryCapture.FromFiles([@"C:\Fake\a.txt", @"C:\Fake\b.txt"]), T0.AddMinutes(1));
        _store.Add(HistoryCapture.FromPicture(4, 3, [1, 2, 3], [9, 9, 9, 9]), T0.AddMinutes(2));
        _store.Dispose();

        foreach (var file in Directory.GetFiles(_folder.Path))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.DoesNotContain("invented first", Encoding.UTF8.GetString(bytes));
            Assert.DoesNotContain("a.txt", Encoding.UTF8.GetString(bytes));
        }

        _store = Open();
        var entries = _store.Load(T0.AddMinutes(3));
        Assert.Equal([EntryKind.Picture, EntryKind.Files, EntryKind.Text], entries.Select(e => e.Kind));
        Assert.Equal((4, 3), (entries[0].Width, entries[0].Height));
        Assert.Equal([1, 2, 3], entries[0].Thumbnail);
        Assert.Equal([9, 9, 9, 9], _store.Picture(entries[0].Id));
        Assert.Equal([@"C:\Fake\a.txt", @"C:\Fake\b.txt"], entries[1].Files);
        Assert.Equal("invented first", entries[2].Text);
    }

    [Fact]
    public void A_web_link_is_saved_as_a_link()
    {
        Assert.Equal(EntryKind.Link, Add("https://example.org/p?item=7", T0).Kind);
        Assert.Equal(EntryKind.Text, Add("example.org is a site", T0).Kind);
    }

    [Fact]
    public void Copying_the_same_again_moves_it_up_and_keeps_its_pin()
    {
        var first = Add("same", T0);
        Add("other", T0.AddMinutes(1));
        _store.SetPinned(first.Id, true, T0.AddMinutes(2));
        var (outcome, again) = _store.Add(HistoryCapture.FromText("same"), T0.AddMinutes(3));
        Assert.Equal(AddOutcome.Promoted, outcome);
        Assert.Equal(first.Id, again!.Id);
        Assert.True(again.Pinned);
        Assert.Equal(T0.AddMinutes(3), again.Recent);
        Assert.Equal(2, _store.Load(T0.AddMinutes(3)).Count);
    }

    [Fact]
    public void Recent_keeps_fifty_within_seven_days_and_pins_stay()
    {
        var pinned = Add("pinned", T0);
        _store.SetPinned(pinned.Id, true, T0);
        for (int i = 0; i < 60; i++)
            Add($"copy {i}", T0.AddMinutes(i + 1));
        var entries = _store.Load(T0.AddMinutes(61));
        Assert.Equal(51, entries.Count);
        Assert.DoesNotContain(entries, e => e.Text == "copy 9");
        Assert.Contains(entries, e => e.Text == "copy 10");

        entries = _store.Load(T0.AddDays(8));
        Assert.Equal("pinned", Assert.Single(entries).Text);
    }

    [Fact]
    public void Unpinning_counts_as_just_copied()
    {
        var old = Add("old", T0);
        _store.SetPinned(old.Id, true, T0);
        var unpinned = _store.SetPinned(old.Id, false, T0.AddDays(10))!;
        Assert.False(unpinned.Pinned);
        Assert.Equal(T0.AddDays(10), unpinned.Recent);
        Assert.Single(_store.Load(T0.AddDays(10)));
    }

    [Fact]
    public void At_the_cap_the_oldest_unpinned_go_first_and_pins_are_never_evicted()
    {
        _store.Dispose();
        _store = Open(capacity: 3 * (28 + 100) + 10);
        string Text(char c) => new(c, 100);
        var pin = Add(Text('p'), T0);
        _store.SetPinned(pin.Id, true, T0);
        Add(Text('a'), T0.AddMinutes(1));
        Add(Text('b'), T0.AddMinutes(2));
        Add(Text('c'), T0.AddMinutes(3));
        var texts = _store.Load(T0.AddMinutes(4)).Select(e => e.Text![0]).ToList();
        Assert.Equal(['c', 'b', 'p'], texts);

        foreach (var id in _store.Load(T0.AddMinutes(4)).Where(e => !e.Pinned).Select(e => e.Id))
            _store.SetPinned(id, true, T0.AddMinutes(4));
        var (outcome, entry) = _store.Add(HistoryCapture.FromText(Text('d')), T0.AddMinutes(5));
        Assert.Equal(AddOutcome.Full, outcome);
        Assert.Null(entry);
        Assert.Equal(3, _store.Load(T0.AddMinutes(5)).Count);
    }

    [Fact]
    public void Clear_removes_only_the_unpinned_copies_it_was_shown()
    {
        var pin = Add("pin", T0);
        _store.SetPinned(pin.Id, true, T0);
        var shown = Add("shown", T0.AddMinutes(1));
        var question = _store.Load(T0.AddMinutes(1)).Select(e => e.Id).ToList();
        Add("arrived while asking", T0.AddMinutes(2));
        _store.ClearUnpinned(question);
        Assert.Equal(["arrived while asking", "pin"], _store.Load(T0.AddMinutes(3)).Select(e => e.Text));
        Assert.DoesNotContain(shown.Id, _store.Load(T0.AddMinutes(3)).Select(e => e.Id));
    }

    [Fact]
    public void Erase_all_removes_pins_and_keeps_working()
    {
        var pin = Add("pin", T0);
        _store.SetPinned(pin.Id, true, T0);
        Assert.True(_store.Any());
        _store.EraseAll();
        Assert.False(_store.Any());
        Add("after", T0.AddMinutes(1));
        Assert.Equal("after", Assert.Single(_store.Load(T0.AddMinutes(1))).Text);
    }

    [Fact]
    public void Delete_removes_a_picture_with_its_image()
    {
        var picture = _store.Add(HistoryCapture.FromPicture(1, 1, [1], [2, 2]), T0).Entry!;
        _store.Delete(picture.Id);
        Assert.Null(_store.Picture(picture.Id));
        Assert.Equal(0, _store.Size());
    }
}
