// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Buffers.Binary;
using System.Text;
using Chippi.Clipboard.Storage;
using Chippi.Core.Clipboard;
using Microsoft.Data.Sqlite;

namespace Chippi.Features.Clipboard.History;

/// <summary>How saving a copy went.</summary>
public enum AddOutcome
{
    /// <summary>A new entry.</summary>
    Added,

    /// <summary>The same copy was already saved: it moved to the top, keeping its pin.</summary>
    Promoted,

    /// <summary>Pins fill the store: nothing saved (notes #13).</summary>
    Full,
}

/// <summary>
/// Saved copies in a <see cref="SealedDatabase"/> (architecture §4). Only the kind, times, pin, size and a
/// keyed hash are plain; the text, file names and thumbnail are sealed in the entry, a picture's PNG in
/// <c>blob</c>. Unpinned entries keep the newest <see cref="RecentCount"/> within <see cref="RecentAge"/>;
/// pins stay until removed. Everything counts toward <see cref="ClipLimits.StoreBytes"/>, and at the cap
/// the oldest unpinned entries go first; a pin is never evicted. Not thread-safe: one caller at a time.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    public const int RecentCount = 50;
    public static readonly TimeSpan RecentAge = TimeSpan.FromDays(7);

    private const string Entries = "entry", Blobs = "blob";
    private readonly SealedDatabase _db;
    private readonly long _capacity;
    private SqliteTransaction? _tx;

    private HistoryStore(SealedDatabase db, long capacity)
    {
        _db = db;
        _capacity = capacity;
        CreateTables();
    }

    /// <param name="capacity">The byte cap; tests pass a small one.</param>
    public static HistoryStore Open(string path, IKeyProtector protector, long capacity = ClipLimits.StoreBytes) =>
        new(SealedDatabase.Open(path, protector), capacity);

    /// <summary>Every entry, newest first, after dropping what has aged out. A row that won't open is dropped.</summary>
    public IReadOnlyList<HistoryEntry> Load(DateTimeOffset now)
    {
        Prune(now);
        var entries = new List<HistoryEntry>();
        var broken = new List<long>();
        using (var cmd = Command("SELECT id, kind, recent, pinned, bytes, body FROM entry ORDER BY recent DESC, id DESC"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                long id = reader.GetInt64(0);
                try
                {
                    entries.Add(Decode(id, (EntryKind)reader.GetInt32(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), (byte[])reader[5]));
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    broken.Add(id);
                }
            }
        }
        foreach (long id in broken)
            Delete(id);
        return entries;
    }

    /// <summary>Whether anything is saved, pins included (the uninstall question, notes #56).</summary>
    public bool Any()
    {
        using var cmd = Command("SELECT EXISTS (SELECT 1 FROM entry)");
        return Convert.ToInt64(cmd.ExecuteScalar()) != 0;
    }

    /// <summary>
    /// Saves a copy as the newest entry. The same copy already saved moves to the top instead. Makes room
    /// by dropping the oldest unpinned entries; when pins alone leave no room, nothing changes.
    /// </summary>
    public (AddOutcome Outcome, HistoryEntry? Entry) Add(HistoryCapture capture, DateTimeOffset now)
    {
        byte[] body = Body(capture);
        long hash = _db.Hash([(byte)capture.Kind, .. capture.Png ?? body]);
        using var tx = Begin();
        if (Find(capture.Kind, hash, body) is { } existing)
        {
            Execute("UPDATE entry SET recent = $r WHERE id = $id", ("$r", now.ToUnixTimeMilliseconds()), ("$id", existing));
            tx.Commit();
            return (AddOutcome.Promoted, Get(existing));
        }
        long bytes = SealedDatabase.Overhead + body.Length + (capture.Png is { } png ? SealedDatabase.Overhead + png.Length : 0);
        if (!MakeRoom(bytes))
            return (AddOutcome.Full, null);
        Execute("INSERT INTO entry (kind, recent, pinned, hash, bytes, body) VALUES ($k, $r, 0, $h, $b, x'')",
            ("$k", (int)capture.Kind), ("$r", now.ToUnixTimeMilliseconds()), ("$h", hash), ("$b", bytes));
        long id = LastId();
        Execute("UPDATE entry SET body = $body WHERE id = $id", ("$body", _db.Seal(body, Entries, id)), ("$id", id));
        if (capture.Png is { } picture)
            Execute("INSERT INTO blob (id, data) VALUES ($id, $d)", ("$id", id), ("$d", _db.Seal(picture, Blobs, id)));
        tx.Commit();
        Prune(now);
        return (AddOutcome.Added, Get(id));
    }

    /// <summary>Pins, or unpins with a fresh recent time (it counts as just copied).</summary>
    public HistoryEntry? SetPinned(long id, bool pinned, DateTimeOffset now)
    {
        long time = now.ToUnixTimeMilliseconds();
        if (pinned)
            Execute("UPDATE entry SET pinned = $t WHERE id = $id AND pinned = 0", ("$t", time), ("$id", id));
        else
            Execute("UPDATE entry SET pinned = 0, recent = $t WHERE id = $id AND pinned <> 0", ("$t", time), ("$id", id));
        Prune(now);
        return Get(id);
    }

    public void Delete(long id) => Execute("DELETE FROM entry WHERE id = $id", ("$id", id));

    /// <summary>Clear history: the unpinned entries among <paramref name="shown"/> (a copy saved while the question was open stays).</summary>
    public void ClearUnpinned(IReadOnlyCollection<long> shown)
    {
        using (var tx = Begin())
        {
            foreach (long id in shown)
                Execute("DELETE FROM entry WHERE id = $id AND pinned = 0", ("$id", id));
            tx.Commit();
        }
        _db.Compact();
    }

    /// <summary>Erase all: every entry and pin, and the file with its key (architecture §4).</summary>
    public void EraseAll()
    {
        _db.Erase();
        CreateTables();
    }

    /// <summary>A picture's full PNG, or null when it's gone.</summary>
    public byte[]? Picture(long id)
    {
        using var cmd = Command("SELECT data FROM blob WHERE id = $id");
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is byte[] data ? _db.Unseal(data, Blobs, id) : null;
    }

    /// <summary>Bytes saved, pins included.</summary>
    public long Size()
    {
        using var cmd = Command("SELECT COALESCE(SUM(bytes), 0) FROM entry");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Dispose() => _db.Dispose();

    // ---- Rows ----

    private void CreateTables() => Execute(
        "CREATE TABLE IF NOT EXISTS entry (id INTEGER PRIMARY KEY, kind INTEGER NOT NULL, recent INTEGER NOT NULL, "
        + "pinned INTEGER NOT NULL, hash INTEGER NOT NULL, bytes INTEGER NOT NULL, body BLOB NOT NULL);"
        + "CREATE INDEX IF NOT EXISTS entry_hash ON entry (hash);"
        + "CREATE TABLE IF NOT EXISTS blob (id INTEGER PRIMARY KEY REFERENCES entry (id) ON DELETE CASCADE, data BLOB NOT NULL);");

    /// <summary>Unpinned entries past the age, then beyond the count.</summary>
    private void Prune(DateTimeOffset now)
    {
        Execute("DELETE FROM entry WHERE pinned = 0 AND recent < $old", ("$old", (now - RecentAge).ToUnixTimeMilliseconds()));
        Execute("DELETE FROM entry WHERE pinned = 0 AND id NOT IN (SELECT id FROM entry WHERE pinned = 0 ORDER BY recent DESC, id DESC LIMIT $n)",
            ("$n", RecentCount));
    }

    private bool MakeRoom(long bytes)
    {
        long used = Size();
        if (used + bytes <= _capacity)
            return true;
        var oldest = new List<(long Id, long Bytes)>();
        using (var cmd = Command("SELECT id, bytes FROM entry WHERE pinned = 0 ORDER BY recent ASC, id ASC"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                oldest.Add((reader.GetInt64(0), reader.GetInt64(1)));
        }
        int drop = 0;
        while (used + bytes > _capacity && drop < oldest.Count)
            used -= oldest[drop++].Bytes;
        if (used + bytes > _capacity)
            return false;
        foreach (var (id, _) in oldest.Take(drop))
            Delete(id);
        return true;
    }

    private long? Find(EntryKind kind, long hash, byte[] body)
    {
        var candidates = new List<(long Id, byte[] Sealed)>();
        using (var cmd = Command("SELECT id, body FROM entry WHERE hash = $h AND kind = $k"))
        {
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$k", (int)kind);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                candidates.Add((reader.GetInt64(0), (byte[])reader[1]));
        }
        // The hash is 8 bytes: a picture's PNG is compared through it alone, text and files by content too.
        foreach (var (id, sealedBody) in candidates)
            if (kind == EntryKind.Picture || _db.Unseal(sealedBody, Entries, id).AsSpan().SequenceEqual(body))
                return id;
        return null;
    }

    private HistoryEntry? Get(long id)
    {
        using var cmd = Command("SELECT kind, recent, pinned, bytes, body FROM entry WHERE id = $id");
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Decode(id, (EntryKind)reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), (byte[])reader[4]) : null;
    }

    private HistoryEntry Decode(long id, EntryKind kind, long recent, long pinned, long bytes, byte[] sealedBody)
    {
        byte[] body = _db.Unseal(sealedBody, Entries, id);
        var at = DateTimeOffset.FromUnixTimeMilliseconds(recent);
        DateTimeOffset? pinnedAt = pinned == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(pinned);
        return kind switch
        {
            EntryKind.Files => new(id, kind, at, pinnedAt, null, Encoding.UTF8.GetString(body).Split('\n'), 0, 0, null, bytes),
            EntryKind.Picture => new(id, kind, at, pinnedAt, null, null,
                BinaryPrimitives.ReadInt32LittleEndian(body), BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4)), body[8..], bytes),
            _ => new(id, kind, at, pinnedAt, Encoding.UTF8.GetString(body), null, 0, 0, null, bytes),
        };
    }

    /// <summary>The sealed part of an entry: the text, the paths one per line (no path holds a line break), or a picture's size and thumbnail.</summary>
    private static byte[] Body(HistoryCapture capture)
    {
        switch (capture.Kind)
        {
            case EntryKind.Files:
                return Encoding.UTF8.GetBytes(string.Join('\n', capture.Files ?? []));
            case EntryKind.Picture:
                var thumbnail = capture.Thumbnail ?? [];
                var body = new byte[8 + thumbnail.Length];
                BinaryPrimitives.WriteInt32LittleEndian(body, capture.Width);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), capture.Height);
                thumbnail.CopyTo(body, 8);
                return body;
            default:
                return Encoding.UTF8.GetBytes(capture.Text ?? "");
        }
    }

    private long LastId()
    {
        using var cmd = Command("SELECT last_insert_rowid()");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private Transaction Begin() => new(this);

    /// <summary>A transaction every command joins until it ends (Microsoft.Data.Sqlite requires it on each command).</summary>
    private sealed class Transaction : IDisposable
    {
        private readonly HistoryStore _store;
        private readonly SqliteTransaction _tx;

        public Transaction(HistoryStore store)
        {
            _store = store;
            _tx = store._tx = store._db.Connection.BeginTransaction();
        }

        public void Commit()
        {
            _tx.Commit();
            _store._tx = null;
        }

        public void Dispose()
        {
            _store._tx = null;
            _tx.Dispose();
        }
    }

    private SqliteCommand Command(string sql)
    {
        var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _tx;
        return cmd;
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(sql);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
