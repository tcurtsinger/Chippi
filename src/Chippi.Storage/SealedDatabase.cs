// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Chippi.Clipboard.Storage;

/// <summary>
/// A feature's SQLite file with content sealed at rest (architecture §4): one random 256-bit key,
/// wrapped by <see cref="IKeyProtector"/> (DPAPI, current user) in the <c>chippi_meta</c> table; each
/// payload sealed with AES-256-GCM, bound to the row it belongs to so rows can't be swapped. A second
/// key makes keyed hashes for spotting duplicates without storing the content in plain.
/// The feature owns its own tables. Not thread-safe: use it from one worker, like SqliteHistoryStore.
/// Protects copies of the file taken elsewhere, not against other processes running as the same user.
/// </summary>
public sealed class SealedDatabase : IDisposable
{
    private const int NonceSize = 12, TagSize = 16;
    private readonly string _path;
    private readonly IKeyProtector _protector;
    private SqliteConnection _connection;
    private AesGcm _aes;
    private byte[] _hashKey;

    private SealedDatabase(string path, IKeyProtector protector)
    {
        _path = path;
        _protector = protector;
        (_connection, _aes, _hashKey) = Load();
    }

    /// <summary>Opens or creates the file. A key that can't be unwrapped (another user, a damaged row) throws <see cref="CryptographicException"/>; nothing is overwritten.</summary>
    public static SealedDatabase Open(string path, IKeyProtector protector)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return new SealedDatabase(path, protector);
    }

    /// <summary>The open connection for the feature's own tables. Replaced by <see cref="Erase"/>.</summary>
    public SqliteConnection Connection => _connection;

    /// <summary>Bytes added to each sealed payload.</summary>
    public const int Overhead = NonceSize + TagSize;

    /// <summary>Seals <paramref name="plain"/> for row <paramref name="rowId"/> of <paramref name="table"/>: nonce, tag, ciphertext.</summary>
    public byte[] Seal(ReadOnlySpan<byte> plain, string table, long rowId)
    {
        var output = new byte[Overhead + plain.Length];
        RandomNumberGenerator.Fill(output.AsSpan(0, NonceSize));
        _aes.Encrypt(output.AsSpan(0, NonceSize), plain, output.AsSpan(Overhead), output.AsSpan(NonceSize, TagSize), Bind(table, rowId));
        return output;
    }

    /// <summary>Opens a payload sealed by <see cref="Seal"/> for the same table and row. Tampering or a moved row throws <see cref="CryptographicException"/>.</summary>
    public byte[] Unseal(ReadOnlySpan<byte> sealedBytes, string table, long rowId)
    {
        if (sealedBytes.Length < Overhead)
            throw new CryptographicException("Sealed payload too short");
        var plain = new byte[sealedBytes.Length - Overhead];
        _aes.Decrypt(sealedBytes[..NonceSize], sealedBytes[Overhead..], sealedBytes.Slice(NonceSize, TagSize), plain, Bind(table, rowId));
        return plain;
    }

    /// <summary>A keyed hash (HMAC-SHA256, first 8 bytes) for duplicate detection. Stable for this file until <see cref="Erase"/>.</summary>
    public long Hash(ReadOnlySpan<byte> content) => BitConverter.ToInt64(HMACSHA256.HashData(_hashKey, content).AsSpan(0, 8));

    /// <summary>
    /// After deleting rows: checkpoints the write-ahead log and rewrites the file, so the live file no
    /// longer holds the freed pages. Copies made earlier (backups, File History) are untouched.
    /// </summary>
    public void Compact()
    {
        Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        Execute("VACUUM;");
    }

    /// <summary>
    /// Erase all: closes the file, deletes it with its log, and starts over empty with a new key. The
    /// feature recreates its tables. No claim that older copies of the file are unreadable.
    /// </summary>
    public void Erase()
    {
        Close();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_path + suffix);
        (_connection, _aes, _hashKey) = Load();
    }

    public void Dispose() => Close();

    private void Close()
    {
        _aes.Dispose();
        CryptographicOperations.ZeroMemory(_hashKey);
        _connection.Dispose();
        SqliteConnection.ClearPool(_connection);
    }

    private (SqliteConnection, AesGcm, byte[]) Load()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        try
        {
            Execute(connection, "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;"
                + "CREATE TABLE IF NOT EXISTS chippi_meta (name TEXT PRIMARY KEY, value BLOB NOT NULL);");
            byte[] keys = ReadKeys(connection) ?? WriteKeys(connection);
            try
            {
                return (connection, new AesGcm(keys.AsSpan(0, 32), TagSize), keys[32..]);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keys);
            }
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private byte[]? ReadKeys(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM chippi_meta WHERE name = 'keys'";
        return cmd.ExecuteScalar() is byte[] wrapped ? _protector.Unprotect(wrapped) : null;
    }

    private byte[] WriteKeys(SqliteConnection connection)
    {
        byte[] keys = RandomNumberGenerator.GetBytes(64);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO chippi_meta (name, value) VALUES ('keys', $v)";
        cmd.Parameters.AddWithValue("$v", _protector.Protect(keys));
        cmd.ExecuteNonQuery();
        return keys;
    }

    private static byte[] Bind(string table, long rowId) => System.Text.Encoding.UTF8.GetBytes($"{table}\0{rowId}");

    private void Execute(string sql) => Execute(_connection, sql);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
