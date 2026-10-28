// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;
using Chippi.Clipboard.Storage;

using System.IO;

namespace Chippi.Features.Scratchpad;

public interface IScratchpadStore : IDisposable
{
    ScratchpadDocument Load();
    void Save(ScratchpadDocument document);
}

/// <summary>Use on the Scratchpad worker only. Failed loads cannot authorize a later overwrite.</summary>
public sealed class ScratchpadStore : IScratchpadStore
{
    private readonly SealedDatabase _database;
    private bool _loaded;

    public ScratchpadStore(string path, IKeyProtector protector)
    {
        _database = SealedDatabase.Open(path, protector);
        try
        {
            using var command = _database.Connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS scratchpad (id INTEGER PRIMARY KEY CHECK(id=1), payload BLOB NOT NULL)";
            command.ExecuteNonQuery();
        }
        catch
        {
            _database.Dispose();
            throw;
        }
    }

    public ScratchpadDocument Load()
    {
        _loaded = false;
        using var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT payload FROM scratchpad WHERE id=1";
        var document = command.ExecuteScalar() is byte[] sealedBytes
            ? JsonSerializer.Deserialize<ScratchpadDocument>(_database.Unseal(sealedBytes, "scratchpad", 1))
                ?? throw new InvalidDataException("Missing notes document")
            : ScratchpadDocument.Empty();
        document.Validate();
        _loaded = true;
        return document with { Notes = Array.AsReadOnly(document.Notes.ToArray()) };
    }

    public void Save(ScratchpadDocument document)
    {
        if (!_loaded)
            throw new InvalidOperationException("Notes have not loaded");
        document.Validate();
        var sealedBytes = _database.Seal(JsonSerializer.SerializeToUtf8Bytes(document), "scratchpad", 1);
        using var command = _database.Connection.CreateCommand();
        command.CommandText = "INSERT INTO scratchpad(id,payload) VALUES(1,$body) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
        command.Parameters.AddWithValue("$body", sealedBytes);
        command.ExecuteNonQuery();
    }

    public void Dispose() => _database.Dispose();
}
