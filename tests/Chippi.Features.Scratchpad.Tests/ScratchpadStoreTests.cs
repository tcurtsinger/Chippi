// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chippi.Clipboard.Storage;
using Microsoft.Data.Sqlite;

namespace Chippi.Features.Scratchpad.Tests;

public sealed class ScratchpadStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "chippi-scratchpad-tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "notes.db");
    private readonly TestProtector _protector = new();

    // Test-only stand-in. No DPAPI or clipboard/foreground implementation is constructed.
    private sealed class TestProtector : IKeyProtector
    {
        public byte[] Protect(byte[] bytes) => bytes.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] bytes) => Protect(bytes);
    }

    [Fact]
    public void Starts_with_one_empty_note_and_requires_load_before_save()
    {
        using var store = new ScratchpadStore(DatabasePath, _protector);
        Assert.Throws<InvalidOperationException>(() => store.Save(ScratchpadDocument.Empty()));
        var document = store.Load();
        Assert.Equal("Note 1", document.Selected.Name);
        Assert.Empty(document.Selected.Text);
        Assert.Single(document.Notes);
    }

    [Fact]
    public void Bodies_names_selected_tab_and_order_survive_restart_without_plaintext_in_the_files()
    {
        const string secretName = "Fake private note title";
        const string secretText = "Invented note body \u4e2d\U0001F600\tvalue\r\nsecond line";
        using (var store = new ScratchpadStore(DatabasePath, _protector))
        {
            var document = store.Load().Edit(secretText).Rename(secretName).Add().Edit("Other note").Select(1);
            store.Save(document);
        }
        foreach (var path in Directory.GetFiles(_directory))
        {
            var file = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain(secretName, file);
            Assert.DoesNotContain("Invented note body", file);
        }
        using var reopened = new ScratchpadStore(DatabasePath, _protector);
        var loaded = reopened.Load();
        Assert.Equal(1, loaded.SelectedId);
        Assert.Equal(new[] { secretName, "Note 2" }, loaded.Notes.Select(n => n.Name));
        Assert.Equal(secretText, loaded.Selected.Text);
        Assert.Equal("Other note", loaded.Notes[1].Text);
    }

    [Fact]
    public void Corrupt_payload_cannot_be_replaced_by_autosaving_an_empty_document()
    {
        using (var store = new ScratchpadStore(DatabasePath, _protector))
            store.Save(store.Load().Edit("Keep existing notes"));
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE scratchpad SET payload=randomblob(50)";
            command.ExecuteNonQuery();
        }
        using var failed = new ScratchpadStore(DatabasePath, _protector);
        Assert.ThrowsAny<CryptographicException>(() => failed.Load());
        Assert.Throws<InvalidOperationException>(() => failed.Save(ScratchpadDocument.Empty()));
    }

    [Fact]
    public void A_later_failed_load_revokes_an_earlier_save_permission()
    {
        using var store = new ScratchpadStore(DatabasePath, _protector);
        store.Save(store.Load().Edit("Existing note"));
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE scratchpad SET payload=randomblob(50)";
        command.ExecuteNonQuery();
        Assert.ThrowsAny<CryptographicException>(() => store.Load());
        Assert.Throws<InvalidOperationException>(() => store.Save(ScratchpadDocument.Empty()));
    }

    [Fact]
    public void Valid_encryption_with_an_invalid_document_also_fails_closed()
    {
        using (var db = SealedDatabase.Open(DatabasePath, _protector))
        {
            using var command = db.Connection.CreateCommand();
            command.CommandText = "CREATE TABLE scratchpad(id INTEGER PRIMARY KEY,payload BLOB); INSERT INTO scratchpad VALUES(1,$body)";
            command.Parameters.AddWithValue("$body", db.Seal(JsonSerializer.SerializeToUtf8Bytes(
                new ScratchpadDocument(9, new[] { new ScratchpadNote(1, "Note 1", "Existing text") })), "scratchpad", 1));
            command.ExecuteNonQuery();
        }
        using var store = new ScratchpadStore(DatabasePath, _protector);
        Assert.Throws<InvalidDataException>(() => store.Load());
        Assert.Throws<InvalidOperationException>(() => store.Save(ScratchpadDocument.Empty()));
    }

    [Fact]
    public void Invalid_save_preserves_the_last_valid_document()
    {
        using var store = new ScratchpadStore(DatabasePath, _protector);
        var original = store.Load().Edit("Keep this text");
        store.Save(original);
        Assert.Throws<InvalidDataException>(() => store.Save(original with { SelectedId = 99 }));
        Assert.Equal("Keep this text", store.Load().Selected.Text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}

public sealed class ScratchpadDocumentTests
{
    [Fact]
    public void Add_select_rename_and_delete_preserve_other_notes_and_stop_at_twelve()
    {
        var document = ScratchpadDocument.Empty().Edit("First");
        for (var i = 1; i < 12; i++)
            document = document.Add().Edit($"Body {i + 1}");
        Assert.Equal(12, document.Notes.Count);
        Assert.Same(document, document.Add());
        document = document.Select(1).Rename("  Standup  ");
        Assert.Equal("Standup", document.Selected.Name);
        Assert.Equal("First", document.Selected.Text);
        Assert.Same(document, document.Rename("  "));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Rename(new string('x', 41)));
        document = document.DeleteSelected();
        Assert.Equal(11, document.Notes.Count);
        Assert.Equal(2, document.SelectedId);
        Assert.DoesNotContain(document.Notes, n => n.Id == 1);
        document.Validate();
    }

    [Fact]
    public void Deleting_the_only_note_leaves_an_empty_Note_1()
    {
        var document = ScratchpadDocument.Empty().Edit("Text").Rename("Standup").DeleteSelected();
        Assert.Single(document.Notes);
        Assert.Equal("Note 1", document.Selected.Name);
        Assert.Empty(document.Selected.Text);
    }

    [Fact]
    public void Document_operations_do_not_mutate_previous_snapshots()
    {
        var first = ScratchpadDocument.Empty();
        var second = first.Edit("Draft").Add();
        Assert.Single(first.Notes);
        Assert.Empty(first.Selected.Text);
        Assert.Equal(2, second.SelectedId);
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Select(999));
    }
}
