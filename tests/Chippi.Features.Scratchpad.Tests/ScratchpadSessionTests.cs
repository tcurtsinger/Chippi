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
        public Action? BeforeWrite { get; set; }
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
            BeforeWrite?.Invoke();
            if (FailWrite) throw new IOException("Invented write failure");
            Saved = document;
            Writes++;
        }
        public void Dispose() { Disposed = true; }
    }

    [Fact]
    public async Task Two_views_editing_one_note_see_each_keystroke_while_a_save_is_blocked()
    {
        var store = new MemoryStore();
        await using var session = new ScratchpadSession(() => store, new ManualTime());
        await session.LoadAsync();
        await session.EditAsync(1, "x");
        var note = session.State.Document!.Selected;

        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        store.BeforeWrite = () => { saving.TrySetResult(); release.Wait(); };
        var slowSave = session.FlushAsync();
        string settingsText = note.Text;
        void RefreshSettings(ScratchpadState state) => settingsText = state.Document!.Current(ScratchpadSurface.Settings).Text;
        Task widgetEdit;
        Task settingsEdit;
        try
        {
            await saving.Task.WaitAsync(TimeSpan.FromSeconds(10));
            session.Changed += RefreshSettings;
            widgetEdit = session.EditAsync(1, "x1");
            Assert.Equal("x1", settingsText);
            Assert.True(session.State.HasChanges);
            settingsEdit = session.EditAsync(1, settingsText + "2");
            Assert.Equal("x12", settingsText);
        }
        finally
        {
            session.Changed -= RefreshSettings;
            release.Set();
        }
        Assert.True(await slowSave);
        await Task.WhenAll(widgetEdit, settingsEdit);
        Assert.True(session.State.HasChanges); // Only the old snapshot reached disk.
        Assert.Equal("x12", session.State.Document!.Selected.Text);
        Assert.True(await session.FlushAsync());
        Assert.Equal("x12", store.Saved.Selected.Text);
    }

    [Fact]
    public async Task Concurrent_creates_share_the_last_slot_while_disk_is_blocked()
    {
        var store = new MemoryStore();
        await using var session = new ScratchpadSession(() => store, new ManualTime());
        await session.LoadAsync();
        await session.ChangeAsync(d =>
        {
            while (d.Notes.Count < ScratchpadDocument.MaximumNotes - 1) d = d.Add();
            return d;
        });
        var before = session.State.Document!;
        await session.EditAsync(before.SelectedId, "Old note");
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        store.BeforeWrite = () => { saving.TrySetResult(); release.Wait(); };
        var slowSave = session.FlushAsync();
        Task<bool>[] attempts;
        try
        {
            await saving.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<Task<bool>> Create(ScratchpadSurface surface)
            {
                await start.Task;
                return session.ChangeAsync(d => d.Add(surface));
            }
            var widget = Create(ScratchpadSurface.Widget);
            var settings = Create(ScratchpadSurface.Settings);
            start.SetResult();
            attempts = await Task.WhenAll(widget, settings);

            var after = session.State.Document!;
            Assert.Equal(ScratchpadDocument.MaximumNotes, after.Notes.Count);
            long createdId = after.Notes[^1].Id;
            Assert.True(createdId > before.Notes.Max(n => n.Id));
            var winner = after.SelectedId == createdId ? ScratchpadSurface.Widget : ScratchpadSurface.Settings;
            var other = winner == ScratchpadSurface.Widget ? ScratchpadSurface.Settings : ScratchpadSurface.Widget;
            Assert.Equal(createdId, after.Current(winner).Id);
            Assert.Equal(before.Current(other).Id, after.Current(other).Id);
        }
        finally { release.Set(); }
        Assert.True(await slowSave);
        Assert.All(await Task.WhenAll(attempts), saved => Assert.True(saved));
        Assert.Equal(ScratchpadDocument.MaximumNotes, store.Saved.Notes.Count);
    }

    [Fact]
    public async Task Edit_during_a_slow_save_stays_with_its_editor_after_navigation()
    {
        var store = new MemoryStore();
        await using var session = new ScratchpadSession(() => store, new ManualTime());
        await session.LoadAsync();
        await session.ChangeAsync(d => d.Add(ScratchpadSurface.Settings).Edit(2, "Settings draft"));
        await session.EditAsync(1, "Widget draft");

        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        store.BeforeWrite = () => { saving.TrySetResult(); release.Wait(); };
        var slowSave = session.FlushAsync();
        Task<bool> navigation;
        Task lateEdit;
        Task<bool> close;
        try
        {
            await saving.Task.WaitAsync(TimeSpan.FromSeconds(10));
            navigation = session.ChangeAsync(d => d.Select(ScratchpadSurface.Widget, 2));
            lateEdit = session.EditAsync(1, "Final widget keystroke");
            close = session.FlushAsync();
        }
        finally { release.Set(); }
        Assert.True(await slowSave);
        Assert.True(await navigation);
        await lateEdit;
        Assert.True(await close);
        Assert.Equal("Final widget keystroke", store.Saved.Notes[0].Text);
        Assert.Equal("Settings draft", store.Saved.Current(ScratchpadSurface.Widget).Text);
        Assert.False(session.State.HasChanges);
    }

    [Fact]
    public async Task Queued_actions_for_a_deleted_last_note_leave_its_replacement_untouched()
    {
        var store = new MemoryStore();
        await using var session = new ScratchpadSession(() => store, new ManualTime());
        await session.LoadAsync();
        var deleted = session.ChangeAsync(d => d.Delete(1));
        var lateEdit = session.EditAsync(1, "Old editor text");
        var lateRename = session.ChangeAsync(d => d.Rename(1, "Old name"));
        var lateDelete = session.ChangeAsync(d => d.Delete(1));
        await Task.WhenAll(deleted, lateEdit, lateRename, lateDelete);
        Assert.True(await session.FlushAsync());
        Assert.NotEqual(1, store.Saved.Selected.Id);
        Assert.Equal(store.Saved.Selected, store.Saved.Current(ScratchpadSurface.Settings));
        Assert.Equal("Note 1", store.Saved.Selected.Name);
        Assert.Empty(store.Saved.Selected.Text);
        Assert.Equal(1, store.Writes);
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
    public async Task Retrying_a_failed_save_writes_the_latest_draft_and_clears_both_flags()
    {
        var store = new MemoryStore { FailWrite = true };
        await using var session = new ScratchpadSession(() => store, new ManualTime());
        await session.LoadAsync();
        await session.EditAsync(1, "Draft");
        Assert.False(await session.FlushAsync());
        Assert.True(session.State.SaveFailed);
        Assert.True(session.State.HasChanges);
        await session.EditAsync(1, "Revised draft");
        store.FailWrite = false;
        Assert.True(await session.FlushAsync());
        Assert.Equal("Revised draft", store.Saved.Selected.Text);
        Assert.False(session.State.HasChanges);
        Assert.False(session.State.SaveFailed);
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
