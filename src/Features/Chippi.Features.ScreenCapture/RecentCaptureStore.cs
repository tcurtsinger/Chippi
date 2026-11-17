// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Clipboard.Storage;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.ScreenCapture;

public sealed record RecentCapture(Guid Id, string Image, DateTimeOffset Time, int Width, int Height, string? SavedFile, CaptureFileIdentity? SavedIdentity = null);

/// <summary>Single-worker store. Index and thumbnails are sealed; retained PNGs are ordinary local files.</summary>
public sealed class RecentCaptureStore : IDisposable
{
    private readonly object _gate = new();
    private readonly string _folder;
    private readonly SealedDatabase _database;
    private bool _keyLost;
    private int _skipped;

    public RecentCaptureStore(string folder) : this(folder, DpapiKeyProtector.Instance) { }

    internal RecentCaptureStore(string folder, IKeyProtector protector)
    {
        _folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "recent.db");
        try { _database = SealedDatabase.Open(path, protector); }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            // The key can't be unwrapped (another Windows account or a reset profile). Set the old index aside, start empty
            // and leave every retained image where it is, so the module still starts.
            Log.Warn("Screen capture: the recent captures key could not be read; starting an empty index", ex);
            SetAside(path);
            _keyLost = true;
            _database = SealedDatabase.Open(path, protector);
        }
        using var cmd = _database.Connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS captures(id INTEGER PRIMARY KEY, entry BLOB NOT NULL, thumbnail BLOB NOT NULL)";
        cmd.ExecuteNonQuery();
        // Authenticate every entry before attempting recovery. Unreadable entries or an index set aside leave files untouched.
        var live = Read().Select(x => x.Image).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_keyLost || _skipped > 0) return;
        // Images from before an earlier set-aside belong to that old index and stay; later orphans (a Put that failed) are swept.
        DateTime cutoff = NewestSetAside();
        foreach (string file in Directory.EnumerateFiles(_folder, "*.png"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) && !live.Contains(Path.GetFileName(file)) && File.GetLastWriteTimeUtc(file) > cutoff) File.Delete(file);
    }

    private DateTime NewestSetAside()
    {
        const string prefix = "recent.unreadable-";
        DateTime newest = DateTime.MinValue;
        foreach (string file in Directory.EnumerateFiles(_folder, prefix + "*"))
        {
            string name = Path.GetFileName(file);
            if (name.Length < prefix.Length + 17) return DateTime.MaxValue;
            if (DateTime.TryParseExact(name.Substring(prefix.Length, 17), "yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var stamp)) { if (stamp > newest) newest = stamp; }
            else return DateTime.MaxValue;
        }
        return newest;
    }
    private void SetAside(string path)
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        foreach (string suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(path + suffix)) File.Move(path + suffix, Path.Combine(_folder, $"recent.unreadable-{stamp}.db{suffix}"));
    }

    /// <summary>Older captures couldn't be read: the index was set aside (its key changed) or entries were skipped. Their images stay on disk.</summary>
    public bool HasUnreadable { get { lock (_gate) return _keyLost || _skipped > 0; } }

    private static bool Unreadable(Exception ex) => ex is System.Security.Cryptography.CryptographicException or InvalidDataException or JsonException or ArgumentException;

    public IReadOnlyList<RecentCapture> Read() { lock (_gate) return ReadCore(); }
    private IReadOnlyList<RecentCapture> ReadCore()
    {
        using var cmd = _database.Connection.CreateCommand(); cmd.CommandText = "SELECT id,entry FROM captures ORDER BY id DESC";
        using var reader = cmd.ExecuteReader();
        var entries = new List<RecentCapture>();
        int skipped = 0;
        while (reader.Read())
        {
            long row = reader.GetInt64(0);
            try { entries.Add(Entry((byte[])reader[1], row)); }
            catch (Exception ex) when (Unreadable(ex))
            {
                skipped++;
                Log.Warn($"Screen capture: skipping unreadable recent entry {row}", ex);
            }
        }
        _skipped = skipped;
        return entries;
    }

    private RecentCapture Entry(byte[] sealedEntry, long row)
    {
        var entry = JsonSerializer.Deserialize<RecentCapture>(_database.Unseal(sealedEntry, "captures", row)) ?? throw new InvalidDataException("Recent index invalid.");
        if (entry.Image == null || Path.GetFileName(entry.Image) != entry.Image || Path.GetExtension(entry.Image) != ".png" || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(entry.Image), "N", out _)) throw new InvalidDataException("Recent image identity invalid.");
        return entry;
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
            try
            {
                if (Entry((byte[])reader[1], row).Id == id) return _database.Unseal((byte[])reader[2], "thumbnails", row);
            }
            catch (Exception ex) when (Unreadable(ex)) { }
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
        _skipped = 0; _keyLost = false; // Clearing also drops entries that couldn't be read, so the notice goes.
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
            {
                try { if (Entry((byte[])reader[1], reader.GetInt64(0)).Id == id) rows.Add(reader.GetInt64(0)); }
                catch (Exception ex) when (Unreadable(ex)) { } // Left alone; the entry is already skipped on every read.
            }
        foreach (long row in rows)
        {
            using var delete = _database.Connection.CreateCommand(); delete.Transaction = transaction; delete.CommandText = "DELETE FROM captures WHERE id=$id"; delete.Parameters.AddWithValue("$id", row); delete.ExecuteNonQuery();
        }
    }
    public void Dispose() { lock (_gate) _database.Dispose(); }
}


