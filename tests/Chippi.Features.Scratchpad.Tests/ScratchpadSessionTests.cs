// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Clipboard.Tests;

namespace Chippi.Features.Scratchpad.Tests;

public sealed class ScratchpadSessionTests
{
    private sealed class MemoryStore : IScratchpadStore
    {
        public ScratchpadDocument Saved { get; private set; } = ScratchpadDocument.Empty();
        public int Writes { get; private set; }
        public bool FailRead { get; set; }
        public bool FailWrite { get; set; }
        public bool Disposed { get; private set; }
        public List<int> Threads { get; } = [];

        public ScratchpadDocument Load()
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            if (FailRead) throw new IOException("Invented read failure");
            return Saved;
        }
        public void Save(ScratchpadDocument document)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            if (FailWrite) throw new IOException("Invented write failure");
            Saved = document;
            Writes++;
        }
        public void Dispose() { Disposed = true; }
    }

    [Fact]
    public async Task Typing_resets_the_800ms_debounce_and_uses_one_store_worker()
    {
        var store = new MemoryStore();
        var clock = new ManualTime();
        await using var session = new ScratchpadSession(() => store, clock);
        Assert.True(await session.LoadAsync());
        await session.EditAsync("First draft");
        clock.Advance(TimeSpan.FromMilliseconds(500));
        await session.EditAsync("Latest draft\t\u4e2d\r\nline");
        clock.Advance(TimeSpan.FromMilliseconds(799));
        await session.EditAsync("Latest draft\t\u4e2d\r\nline"); // worker barrier, no new edit
        Assert.Equal(0, store.Writes);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await session.EditAsync("Latest draft\t\u4e2d\r\nline");
        Assert.Equal(1, store.Writes);
        Assert.Equal("Latest draft\t\u4e2d\r\nline", store.Saved.Selected.Text);
        Assert.Single(store.Threads.Distinct());
        Assert.False(session.State.HasChanges);
    }

    [Fact]
    public async Task Failed_save_keeps_the_draft_and_navigation_retries_it()
    {
        var store = new MemoryStore { FailWrite = true };
        await using var session = new ScratchpadSession(() => store, new ManualTime());
        await session.LoadAsync();
        await session.EditAsync("Keep this draft");
        Assert.False(await session.FlushAsync());
        Assert.True(session.State.SaveFailed);
        Assert.Equal("Keep this draft", session.State.Document!.Selected.Text);
        store.FailWrite = false;
        Assert.True(await session.ChangeAsync(d => d.Add()));
        Assert.Equal("Keep this draft", store.Saved.Notes[0].Text);
        Assert.Equal(2, store.Saved.SelectedId);
        Assert.False(session.State.SaveFailed);
    }

    [Fact]
    public async Task Failed_load_has_no_editable_document_and_retry_opens_again()
    {
        var failed = new MemoryStore { FailRead = true };
        var good = new MemoryStore();
        var opens = 0;
        await using var session = new ScratchpadSession(() => ++opens == 1 ? failed : good, new ManualTime());
        Assert.False(await session.LoadAsync());
        Assert.True(session.State.LoadFailed);
        Assert.Null(session.State.Document);
        await session.EditAsync("Must not overwrite anything");
        Assert.False(await session.FlushAsync());
        Assert.Equal(0, failed.Writes);
        Assert.True(await session.LoadAsync());
        Assert.True(failed.Disposed);
        Assert.False(session.State.LoadFailed);
        Assert.Equal("Note 1", session.State.Document!.Selected.Name);
    }

    [Fact]
    public async Task Reopening_a_loaded_page_does_not_discard_a_pending_draft()
    {
        var store = new MemoryStore();
        await using var session = new ScratchpadSession(() => store, new ManualTime());
        await session.LoadAsync();
        await session.EditAsync("Pending draft");
        Assert.True(await session.LoadAsync());
        Assert.Equal("Pending draft", session.State.Document!.Selected.Text);
        Assert.True(session.State.HasChanges);
    }

    [Fact]
    public async Task Stop_flushes_the_latest_queued_edit_and_cancels_future_timer_saves()
    {
        var store = new MemoryStore();
        var clock = new ManualTime();
        var session = new ScratchpadSession(() => store, clock);
        await session.LoadAsync();
        var edit = session.EditAsync("Final edit before stop");
        var stop = session.DisposeAsync().AsTask();
        Assert.Same(stop, session.DisposeAsync().AsTask());
        await edit;
        await stop;
        Assert.Equal("Final edit before stop", store.Saved.Selected.Text);
        Assert.True(store.Disposed);
        var writes = store.Writes;
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(writes, store.Writes);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.EditAsync("Too late"));
    }
}
