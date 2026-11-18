// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Text.Json;

namespace Chippi.Features.Uninstaller;

/// <summary>
/// Chippi's own record of each app it uninstalled, kept 30 days (at most 50): what the entry said before the uninstaller deleted it, and for a
/// machine app the id of the helper's record. A file in the module's data folder; a file that can't be read starts empty, and a save that fails
/// keeps the records in memory for this run.
/// </summary>
public sealed class RecordStore(string path, TimeProvider? time = null)
{
    public const int Max = 50;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private List<AppRecord>? _records;

    public IReadOnlyList<AppRecord> List()
    {
        lock (_gate)
        {
            var records = Load();
            int before = records.Count;
            Prune(records);
            if (records.Count != before) Save(records);
            return [.. records.OrderByDescending(r => r.CreatedUtc)];
        }
    }

    public AppRecord? Get(Guid id) { lock (_gate) return Load().FirstOrDefault(r => r.Id == id); }

    /// <summary>Adds the record (or replaces the one with its id). False when the file could not be written; the record is kept for this run.</summary>
    public bool Put(AppRecord record)
    {
        lock (_gate)
        {
            var records = Load();
            records.RemoveAll(r => r.Id == record.Id);
            records.Add(record);
            Prune(records);
            return Save(records);
        }
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            var records = Load();
            return records.RemoveAll(r => r.Id == id) == 0 || Save(records);
        }
    }

    private void Prune(List<AppRecord> records)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        records.RemoveAll(r => now - r.CreatedUtc > AppRecord.Lifetime);
        while (records.Count > Max) records.Remove(records.MinBy(r => r.CreatedUtc)!);
    }

    private List<AppRecord> Load()
    {
        if (_records is not null) return _records;
        try
        {
            if (File.Exists(path))
                return _records = JsonSerializer.Deserialize<List<AppRecord>>(File.ReadAllBytes(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        return _records = [];
    }

    private bool Save(List<AppRecord> records)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(records));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
