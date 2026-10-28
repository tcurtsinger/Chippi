// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Security.Cryptography;
using System.Text;
using Chippi.Clipboard.Storage;

namespace Chippi.Clipboard.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chippi-clipboard-tests", Guid.NewGuid().ToString("N"));

    private string File(string name = "store.db") => Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Stands in for DPAPI so tests can also play a key that won't unwrap.</summary>
    private sealed class XorProtector(byte with) : IKeyProtector
    {
        public byte[] Protect(byte[] key) => [.. key.Select(b => (byte)(b ^ with)), with];

        public byte[] Unprotect(byte[] wrapped) =>
            wrapped[^1] == with ? [.. wrapped[..^1].Select(b => (byte)(b ^ with))] : throw new CryptographicException("Not this user");
    }

    private static void Exec(SealedDatabase db, string sql, params (string, object)[] args)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static byte[] Body(SealedDatabase db, long id)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT body FROM note WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return (byte[])cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Content_is_sealed_at_rest_and_opens_after_reopening()
    {
        var protector = new XorProtector(0x5A);
        byte[] plain = Encoding.UTF8.GetBytes("Standup notes: island fit fix");
        using (var db = SealedDatabase.Open(File(), protector))
        {
            Exec(db, "CREATE TABLE note (id INTEGER PRIMARY KEY, body BLOB)");
            Exec(db, "INSERT INTO note VALUES (1, $b)", ("$b", db.Seal(plain, "note", 1)));
            Assert.Equal(plain.Length + SealedDatabase.Overhead, Body(db, 1).Length);
            db.Compact();
        }
        Assert.DoesNotContain("Standup", Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(File())));
        using (var db = SealedDatabase.Open(File(), protector))
            Assert.Equal(plain, db.Unseal(Body(db, 1), "note", 1));
    }

    [Fact]
    public void A_payload_moved_to_another_row_or_table_or_changed_wont_open()
    {
        using var db = SealedDatabase.Open(File(), new XorProtector(1));
        var sealedBytes = db.Seal("x"u8, "note", 7);
        Assert.ThrowsAny<CryptographicException>(() => db.Unseal(sealedBytes, "note", 8));
        Assert.ThrowsAny<CryptographicException>(() => db.Unseal(sealedBytes, "entry", 7));
        sealedBytes[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => db.Unseal(sealedBytes, "note", 7));
    }

    [Fact]
    public void A_key_that_wont_unwrap_fails_without_touching_the_file()
    {
        using (var db = SealedDatabase.Open(File(), new XorProtector(1)))
            Exec(db, "CREATE TABLE note (id INTEGER PRIMARY KEY)");
        long size = new FileInfo(File()).Length;
        Assert.ThrowsAny<CryptographicException>(() => SealedDatabase.Open(File(), new XorProtector(2)));
        using var again = SealedDatabase.Open(File(), new XorProtector(1));
        Assert.True(new FileInfo(File()).Length >= size);
    }

    [Fact]
    public void Hashes_are_keyed_and_stable_until_erase()
    {
        using var db = SealedDatabase.Open(File(), new XorProtector(3));
        using var other = SealedDatabase.Open(File("other.db"), new XorProtector(3));
        long hash = db.Hash("same copy"u8);
        Assert.Equal(hash, db.Hash("same copy"u8));
        Assert.NotEqual(hash, db.Hash("other copy"u8));
        Assert.NotEqual(hash, other.Hash("same copy"u8));
        db.Erase();
        Assert.NotEqual(hash, db.Hash("same copy"u8));
    }

    [Fact]
    public void Erase_starts_over_with_a_new_key_and_an_empty_file()
    {
        using var db = SealedDatabase.Open(File(), new XorProtector(4));
        Exec(db, "CREATE TABLE note (id INTEGER PRIMARY KEY, body BLOB)");
        var old = db.Seal("kept?"u8, "note", 1);
        Exec(db, "INSERT INTO note VALUES (1, $b)", ("$b", old));
        db.Erase();
        Assert.ThrowsAny<CryptographicException>(() => db.Unseal(old, "note", 1));
        Exec(db, "CREATE TABLE note (id INTEGER PRIMARY KEY, body BLOB)");
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM note";
        Assert.Equal(0L, cmd.ExecuteScalar());
    }

    [Fact]
    public void Dpapi_wraps_and_unwraps_for_the_current_user()
    {
        var key = RandomNumberGenerator.GetBytes(64);
        var wrapped = DpapiKeyProtector.Instance.Protect(key);
        Assert.NotEqual(key, wrapped);
        Assert.Equal(key, DpapiKeyProtector.Instance.Unprotect(wrapped));
        wrapped[^5] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => DpapiKeyProtector.Instance.Unprotect(wrapped));
    }
}
