// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Buffers.Binary;
using System.Text;
using Chippi.Clipboard.Storage;
using Chippi.Core.Clipboard;
using Microsoft.Data.Sqlite;

namespace Chippi.Features.Shelf;

/// <summary>
/// Shelf items in their own <see cref="SealedDatabase"/>, apart from History's (architecture §4). Only the
/// kind, time, pin and size are plain; text, paths and thumbnails are sealed in the item, a picture's PNG
/// in <c>blob</c>. Text and pictures count toward <see cref="ClipLimits.StoreBytes"/>; files are references
/// and count nothing (notes #45). Nothing is ever evicted: at the cap a drop is refused. Not thread-safe.
/// </summary>
public sealed class ShelfStore : IDisposable
{
    private const string Items = "item", Blobs = "blob";
    private readonly SealedDatabase _db;
    private readonly long _capacity;
    private SqliteTransaction? _tx;

    private ShelfStore(SealedDatabase db, long capacity)
    {
        _db = db;
        _capacity = capacity;
        Execute("CREATE TABLE IF NOT EXISTS item (id INTEGER PRIMARY KEY, kind INTEGER NOT NULL, added INTEGER NOT NULL, "
            + "pinned INTEGER NOT NULL, bytes INTEGER NOT NULL, body BLOB NOT NULL);"
            + "CREATE TABLE IF NOT EXISTS blob (id INTEGER PRIMARY KEY REFERENCES item (id) ON DELETE CASCADE, data BLOB NOT NULL);");
    }

    /// <param name="capacity">The byte cap; tests pass a small one.</param>
    public static ShelfStore Open(string path, IKeyProtector protector, long capacity = ClipLimits.StoreBytes) =>
        new(SealedDatabase.Open(path, protector), capacity);

    /// <summary>Every item, newest first. A row that won't open is dropped.</summary>
    public IReadOnlyList<ShelfItem> Load()
    {
        var items = new List<ShelfItem>();
        var broken = new List<long>();
        using (var cmd = Command("SELECT id, kind, added, pinned, bytes, body FROM item ORDER BY added DESC, id DESC"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                long id = reader.GetInt64(0);
                try
                {
                    items.Add(Decode(id, (ShelfKind)reader.GetInt32(1), reader.GetInt64(2), reader.GetInt64(3) != 0, reader.GetInt64(4), (byte[])reader[5]));
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    broken.Add(id);
                }
            }
        }
        foreach (long id in broken)
            Remove(id);
        return items;
    }

    /// <summary>Keeps a drop as the newest item, or null when it doesn't fit under the cap.</summary>
    public ShelfItem? Add(ShelfDrop drop, DateTimeOffset now)
    {
        byte[] body = Body(drop);
        long bytes = drop.Kind == ShelfKind.Files ? 0 : SealedDatabase.Overhead + body.Length + (drop.Png is { } png ? SealedDatabase.Overhead + png.Length : 0);
        if (Size() + bytes > _capacity)
            return null;
        using var tx = new Transaction(this);
        Execute("INSERT INTO item (kind, added, pinned, bytes, body) VALUES ($k, $a, 0, $b, x'')",
            ("$k", (int)drop.Kind), ("$a", now.ToUnixTimeMilliseconds()), ("$b", bytes));
        long id = LastId();
        Execute("UPDATE item SET body = $body WHERE id = $id", ("$body", _db.Seal(body, Items, id)), ("$id", id));
        if (drop.Png is { } picture)
            Execute("INSERT INTO blob (id, data) VALUES ($id, $d)", ("$id", id), ("$d", _db.Seal(picture, Blobs, id)));
        tx.Commit();
        return Get(id);
    }

    public void SetPinned(long id, bool pinned) => Execute("UPDATE item SET pinned = $p WHERE id = $id", ("$p", pinned ? 1 : 0), ("$id", id));

    public void Remove(long id) => Execute("DELETE FROM item WHERE id = $id", ("$id", id));

    /// <summary>A picture's full PNG, or null when it's gone.</summary>
    public byte[]? Picture(long id)
    {
        using var cmd = Command("SELECT data FROM blob WHERE id = $id");
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is byte[] data ? _db.Unseal(data, Blobs, id) : null;
    }

    /// <summary>Bytes kept for text and pictures.</summary>
    public long Size()
    {
        using var cmd = Command("SELECT COALESCE(SUM(bytes), 0) FROM item");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Dispose() => _db.Dispose();

    private ShelfItem? Get(long id)
    {
        using var cmd = Command("SELECT kind, added, pinned, bytes, body FROM item WHERE id = $id");
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Decode(id, (ShelfKind)reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2) != 0, reader.GetInt64(3), (byte[])reader[4]) : null;
    }

    private ShelfItem Decode(long id, ShelfKind kind, long added, bool pinned, long bytes, byte[] sealedBody)
    {
        byte[] body = _db.Unseal(sealedBody, Items, id);
        var at = DateTimeOffset.FromUnixTimeMilliseconds(added);
        return kind switch
        {
            ShelfKind.Files => new(id, kind, at, pinned, null, Encoding.UTF8.GetString(body).Split('\n'), 0, 0, null, bytes),
            ShelfKind.Picture => new(id, kind, at, pinned, null, null,
                BinaryPrimitives.ReadInt32LittleEndian(body), BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(4)), body[8..], bytes),
            _ => new(id, kind, at, pinned, Encoding.UTF8.GetString(body), null, 0, 0, null, bytes),
        };
    }

    /// <summary>The sealed part: the text, the paths one per line (no path holds a line break), or a picture's size and thumbnail.</summary>
    private static byte[] Body(ShelfDrop drop)
    {
        switch (drop.Kind)
        {
            case ShelfKind.Files:
                return Encoding.UTF8.GetBytes(string.Join('\n', drop.Files ?? []));
            case ShelfKind.Picture:
                var thumbnail = drop.Thumbnail ?? [];
                var body = new byte[8 + thumbnail.Length];
                BinaryPrimitives.WriteInt32LittleEndian(body, drop.Width);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), drop.Height);
                thumbnail.CopyTo(body, 8);
                return body;
            default:
                return Encoding.UTF8.GetBytes(drop.Text ?? "");
        }
    }

    private long LastId()
    {
        using var cmd = Command("SELECT last_insert_rowid()");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>A transaction every command joins until it ends (Microsoft.Data.Sqlite requires it on each command).</summary>
    private sealed class Transaction : IDisposable
    {
        private readonly ShelfStore _store;
        private readonly SqliteTransaction _tx;

        public Transaction(ShelfStore store)
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
