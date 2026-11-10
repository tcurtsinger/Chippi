// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Text.Json;
using Chippi.Clipboard.Storage;

namespace Chippi.Features.Recording;

public sealed record RecordingEntry(Guid Id, string Path, string Source, DateTimeOffset Created, TimeSpan Duration, int Width, int Height, long Bytes, DateTime LastWriteUtc);

public interface IRecordingStore : IDisposable
{
    IReadOnlyList<RecordingEntry> Read();
    void Add(RecordingEntry entry, byte[] poster);
    byte[]? Poster(Guid id);
}

/// <summary>The recent index and posters are sealed; saved videos remain ordinary files owned by the user.</summary>
internal sealed class RecordingStore : IRecordingStore
{
    private readonly SealedDatabase _database;
    public RecordingStore(string folder)
    {
        Directory.CreateDirectory(folder);
        _database = SealedDatabase.Open(System.IO.Path.Combine(folder, "recordings.db"), DpapiKeyProtector.Instance);
        using var command = _database.Connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS recordings(id INTEGER PRIMARY KEY, entry BLOB NOT NULL, poster BLOB NOT NULL)";
        command.ExecuteNonQuery();
    }
    public IReadOnlyList<RecordingEntry> Read()
    {
        using var command = _database.Connection.CreateCommand(); command.CommandText = "SELECT id,entry FROM recordings ORDER BY id DESC LIMIT 50";
        using var reader = command.ExecuteReader(); List<RecordingEntry> entries = [];
        while (reader.Read()) entries.Add(JsonSerializer.Deserialize<RecordingEntry>(_database.Unseal((byte[])reader[1], "recordings", reader.GetInt64(0))) ?? throw new InvalidDataException("Recording index invalid"));
        return entries;
    }
    public void Add(RecordingEntry entry, byte[] poster)
    {
        using var transaction = _database.Connection.BeginTransaction();
        using var insert = _database.Connection.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO recordings(entry,poster) VALUES (X'',X''); SELECT last_insert_rowid()";
        long row = (long)insert.ExecuteScalar()!;
        using var update = _database.Connection.CreateCommand(); update.Transaction = transaction;
        update.CommandText = "UPDATE recordings SET entry=$entry,poster=$poster WHERE id=$id";
        update.Parameters.AddWithValue("$entry", _database.Seal(JsonSerializer.SerializeToUtf8Bytes(entry), "recordings", row));
        update.Parameters.AddWithValue("$poster", _database.Seal(poster, "recording-posters", row)); update.Parameters.AddWithValue("$id", row); update.ExecuteNonQuery();
        using var prune = _database.Connection.CreateCommand(); prune.Transaction = transaction;
        prune.CommandText = "DELETE FROM recordings WHERE id NOT IN (SELECT id FROM recordings ORDER BY id DESC LIMIT 50)"; prune.ExecuteNonQuery();
        transaction.Commit();
    }
    public byte[]? Poster(Guid id)
    {
        using var command = _database.Connection.CreateCommand(); command.CommandText = "SELECT id,entry,poster FROM recordings ORDER BY id DESC LIMIT 50";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            long row = reader.GetInt64(0);
            if (JsonSerializer.Deserialize<RecordingEntry>(_database.Unseal((byte[])reader[1], "recordings", row))?.Id == id)
                return _database.Unseal((byte[])reader[2], "recording-posters", row);
        }
        return null;
    }
    public void Dispose() => _database.Dispose();
}
