// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Security.Cryptography;
using Chippi.Clipboard.Storage;
using Chippi.Features.Clipboard.History;

namespace Chippi.Features.Clipboard.Tests;

public sealed class HistoryResilienceTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeHub _hub = new();

    public void Dispose() => _folder.Dispose();

    /// <summary>A key that can't be unwrapped (another profile, a reset password).</summary>
    private sealed class LostKeys : IKeyProtector
    {
        public byte[] Protect(byte[] key) => key;

        public byte[] Unprotect(byte[] wrapped) => throw new CryptographicException("Not this user");
    }

    [Fact]
    public void A_row_that_opens_but_is_garbled_is_dropped_and_the_rest_load()
    {
        string path = _folder.File("history.db");
        using (var store = HistoryStore.Open(path, new TestKeys()))
            store.Add(HistoryCapture.FromText("fine"), DateTimeOffset.UtcNow);
        using (var db = SealedDatabase.Open(path, new TestKeys()))
        {
            // A picture body must hold at least its two sizes: this one holds three bytes.
            using var insert = db.Connection.CreateCommand();
            insert.CommandText = "INSERT INTO entry (id, kind, recent, pinned, hash, bytes, body) VALUES (99, 2, 5, 0, 1, 0, $b)";
            insert.Parameters.AddWithValue("$b", db.Seal(new byte[3], "entry", 99));
            insert.ExecuteNonQuery();
        }

        using var again = HistoryStore.Open(path, new TestKeys());
        var entries = again.Load(DateTimeOffset.UtcNow);

        Assert.Equal("fine", Assert.Single(entries).Text);
        Assert.Single(again.Load(DateTimeOffset.UtcNow)); // and the bad row is gone for good
    }

    [Fact]
    public async Task Erase_all_starts_over_when_the_saved_history_cannot_be_unlocked()
    {
        string path = _folder.File("history.db");
        using (var store = HistoryStore.Open(path, new TestKeys()))
            store.Add(HistoryCapture.FromText("old"), DateTimeOffset.UtcNow);

        using var history = new ClipboardHistory(_hub, () => HistoryStore.Open(path, new LostKeys()),
            reset: () => SealedDatabase.Delete(path));
        history.Apply(true, false, []);
        _hub.CopyText("lost"); // opening fails: nothing can be recorded
        await history.IdleAsync();
        Assert.Empty(history.Entries);
        Assert.True(history.Unreadable);

        await history.EraseAllAsync();
        Assert.False(history.Unreadable);
        _hub.CopyText("fresh");
        await history.IdleAsync();
        Assert.Equal("fresh", Assert.Single(history.Entries).Text);
    }
}
