// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Clipboard.Storage;

namespace Chippi.Features.ScreenCapture;

public sealed record RecentCapture(Guid Id, string Image, DateTimeOffset Time, int Width, int Height, string? SavedFile, CaptureFileIdentity? SavedIdentity = null);

/// <summary>Single-worker store. Index and thumbnails are sealed; retained PNGs are ordinary local files.</summary>
public sealed class RecentCaptureStore : IDisposable
{
    private readonly object _gate = new();
    private readonly string _folder;
    private readonly SealedDatabase _database;
    public RecentCaptureStore(string folder)
    {
        _folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(_folder);
        _database = SealedDatabase.Open(Path.Combine(_folder, "recent.db"), DpapiKeyProtector.Instance);
        using var cmd = _database.Connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS captures(id INTEGER PRIMARY KEY, entry BLOB NOT NULL, thumbnail BLOB NOT NULL)";
        cmd.ExecuteNonQuery();
        // Authenticate every entry before attempting recovery. Corruption must leave files untouched.
        var live = Read().Select(x => x.Image).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(_folder, "*.png"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) && !live.Contains(Path.GetFileName(file))) File.Delete(file);
    }

    public IReadOnlyList<RecentCapture> Read() { lock (_gate) return ReadCore(); }
    private IReadOnlyList<RecentCapture> ReadCore()
    {
        using var cmd = _database.Connection.CreateCommand(); cmd.CommandText = "SELECT id,entry FROM captures ORDER BY id DESC";
        using var reader = cmd.ExecuteReader();
        var entries = new List<RecentCapture>();
        while (reader.Read())
        {
            var entry = JsonSerializer.Deserialize<RecentCapture>(_database.Unseal((byte[])reader[1], "captures", reader.GetInt64(0))) ?? throw new InvalidDataException("Recent index invalid.");
            if (Path.GetFileName(entry.Image) != entry.Image || Path.GetExtension(entry.Image) != ".png" || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(entry.Image), "N", out _)) throw new InvalidDataException("Recent image identity invalid.");
            entries.Add(entry);
        }
        return entries;
    }

    public BitmapSource Load(Guid id) { lock (_gate) return LoadCore(id); }
    private BitmapSource LoadCore(Guid id)
    {
        var entry = Read().FirstOrDefault(x => x.Id == id) ?? throw new FileNotFoundException("Recent capture missing.");
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.Combine(_folder, entry.Image));
        image.EndInit();
        image.Freeze();
        return image;
    }
    public byte[] Thumbnail(Guid id) { lock (_gate) return ThumbnailCore(id); }
    private byte[] ThumbnailCore(Guid id)
    {
        using var cmd = _database.Connection.CreateCommand(); cmd.CommandText = "SELECT id,entry,thumbnail FROM captures";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long row = reader.GetInt64(0);
            var entry = JsonSerializer.Deserialize<RecentCapture>(_database.Unseal((byte[])reader[1], "captures", row));
            if (entry?.Id == id) return _database.Unseal((byte[])reader[2], "thumbnails", row);
        }
        throw new FileNotFoundException();
    }

    public void Remove(Guid id) { lock (_gate) RemoveCore(id); }
    private void RemoveCore(Guid id)
    {
        var entry = Read().FirstOrDefault(x => x.Id == id);
        if (entry == null) return;
        using var transaction = _database.Connection.BeginTransaction();
        DeleteRow(id, transaction); transaction.Commit();
        File.Delete(Path.Combine(_folder, entry.Image));
    }

    public void Clear() { lock (_gate) ClearCore(); }
    private void ClearCore()
    {
        var entries = Read();
        using var cmd = _database.Connection.CreateCommand(); cmd.CommandText = "DELETE FROM captures"; cmd.ExecuteNonQuery();
        foreach (var entry in entries) File.Delete(Path.Combine(_folder, entry.Image));
    }
    public void Put(Guid draftId, BitmapSource flattened, DateTimeOffset time, string? savedFile)
    { lock (_gate) PutCore(draftId, flattened, time, savedFile); }
    private void PutCore(Guid draftId, BitmapSource flattened, DateTimeOffset time, string? savedFile)
    {
        var previous = Read();
        string name = Guid.NewGuid().ToString("N") + ".png";
        // SaveAs publishes a fully encoded file before its index row can become visible.
        CaptureFiles.SaveAs(flattened, Path.Combine(_folder, name), CaptureFileType.Png, true);
        var entry = new RecentCapture(draftId, name, time, flattened.PixelWidth, flattened.PixelHeight, savedFile ?? previous.FirstOrDefault(x => x.Id == draftId)?.SavedFile,
            savedFile != null ? CaptureSavedFile.Identity(savedFile) : previous.FirstOrDefault(x => x.Id == draftId)?.SavedIdentity);
        var thumbnail = CaptureFiles.Thumbnail(flattened);
        using var transaction = _database.Connection.BeginTransaction();
        foreach (var old in previous.Where(x => x.Id == draftId)) DeleteRow(old.Id, transaction);
        using var insert = _database.Connection.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO captures(entry,thumbnail) VALUES (X'',X''); SELECT last_insert_rowid()";
        long row = (long)insert.ExecuteScalar()!;
        using var update = _database.Connection.CreateCommand(); update.Transaction = transaction;
        update.CommandText = "UPDATE captures SET entry=$entry,thumbnail=$thumbnail WHERE id=$id";
        update.Parameters.AddWithValue("$id", row); update.Parameters.AddWithValue("$entry", _database.Seal(JsonSerializer.SerializeToUtf8Bytes(entry), "captures", row)); update.Parameters.AddWithValue("$thumbnail", _database.Seal(thumbnail, "thumbnails", row)); update.ExecuteNonQuery();
        var evicted = previous.Where(x => x.Id != draftId).Skip(49).ToArray();
        foreach (var old in evicted) DeleteRow(old.Id, transaction);
        transaction.Commit();
        foreach (var old in evicted.Concat(previous.Where(x => x.Id == draftId))) File.Delete(Path.Combine(_folder, old.Image));
    }

    private void DeleteRow(Guid id, Microsoft.Data.Sqlite.SqliteTransaction transaction)
    {
        using var cmd = _database.Connection.CreateCommand(); cmd.Transaction = transaction;
        cmd.CommandText = "SELECT id,entry FROM captures";
        var rows = new List<long>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                if (JsonSerializer.Deserialize<RecentCapture>(_database.Unseal((byte[])reader[1], "captures", reader.GetInt64(0)))?.Id == id) rows.Add(reader.GetInt64(0));
        foreach (long row in rows)
        {
            using var delete = _database.Connection.CreateCommand(); delete.Transaction = transaction; delete.CommandText = "DELETE FROM captures WHERE id=$id"; delete.Parameters.AddWithValue("$id", row); delete.ExecuteNonQuery();
        }
    }
    public void Dispose() { lock (_gate) _database.Dispose(); }
}


