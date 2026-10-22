// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Data.Sqlite;

namespace Chippi.Features.Downloads.Storage;

/// <summary>
/// <see cref="IHistoryStore"/> on SQLite (Travis's call). One connection, WAL journal, schema
/// version in <c>PRAGMA user_version</c>. Times are stored as UTC Unix milliseconds.
/// </summary>
public sealed class SqliteHistoryStore : IHistoryStore
{
    private const int SchemaVersion = 1;

    private readonly Lock _gate = new();
    private readonly SqliteConnection _connection;
    private readonly TimeProvider _time;

    public SqliteHistoryStore(string databasePath, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;");
        Migrate();
    }

    public long BeginRun(RunTrigger trigger, string folder) => Scalar(
        "INSERT INTO runs (trigger, folder, started_at) VALUES ($t, $f, $at); SELECT last_insert_rowid();",
        ("$t", (long)trigger), ("$f", folder), ("$at", Now()));

    public void EndRun(long runId) =>
        Execute("UPDATE runs SET ended_at = $at WHERE id = $id", ("$at", Now()), ("$id", runId));

    public long Plan(long runId, HistoryOperationKind kind, string source, string destination, string? ruleId, string? ruleName, long bytes, long? undoOf = null) => Scalar(
        """
        INSERT INTO operations (run_id, kind, source_path, dest_path, rule_id, rule_name, bytes, state, planned_at, undo_of)
        VALUES ($run, $kind, $src, $dest, $rid, $rname, $bytes, $state, $at, $undo);
        SELECT last_insert_rowid();
        """,
        ("$run", runId), ("$kind", (long)kind), ("$src", source), ("$dest", destination), ("$rid", ruleId), ("$rname", ruleName),
        ("$bytes", bytes), ("$state", (long)OperationState.Planned), ("$at", Now()), ("$undo", undoOf));

    public void MarkStarted(long operationId) => SetState(operationId, OperationState.Started);

    public void MarkCompleted(long operationId, string destination, string sha256, DateTimeOffset destinationModifiedUtc) => Execute(
        "UPDATE operations SET state = $s, dest_path = $dest, sha256 = $sha, dest_modified = $mod, completed_at = $at, error = NULL WHERE id = $id",
        ("$s", (long)OperationState.Completed), ("$dest", destination), ("$sha", sha256),
        ("$mod", destinationModifiedUtc.ToUnixTimeMilliseconds()), ("$at", Now()), ("$id", operationId));

    public void MarkFailed(long operationId, string error) => Execute(
        "UPDATE operations SET state = $s, error = $e, completed_at = $at WHERE id = $id",
        ("$s", (long)OperationState.Failed), ("$e", error), ("$at", Now()), ("$id", operationId));

    public void MarkPartial(long operationId, string? partialPath) =>
        Execute("UPDATE operations SET partial_path = $p WHERE id = $id", ("$p", partialPath), ("$id", operationId));

    public void MarkUndone(long operationId) => SetState(operationId, OperationState.Undone);

    public OperationRecord? Get(long operationId) =>
        Read("SELECT * FROM operations WHERE id = $id", ("$id", operationId)).FirstOrDefault();

    public IReadOnlyList<OperationRecord> Query(HistoryQuery query)
    {
        var where = new List<string>();
        var parameters = new List<(string, object?)>();
        if (query.Folder is { Length: > 0 } folder)
        {
            where.Add("run_id IN (SELECT id FROM runs WHERE folder = $folder COLLATE NOCASE)");
            parameters.Add(("$folder", folder));
        }
        if (query.Text is { Length: > 0 } text)
        {
            where.Add("(source_path LIKE $text ESCAPE '\\' OR dest_path LIKE $text ESCAPE '\\')");
            parameters.Add(("$text", "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%"));
        }
        if (query.From is { } from)
        {
            where.Add("planned_at >= $from");
            parameters.Add(("$from", from.ToUnixTimeMilliseconds()));
        }
        if (query.To is { } to)
        {
            where.Add("planned_at <= $to");
            parameters.Add(("$to", to.ToUnixTimeMilliseconds()));
        }
        if (query.State is { } state)
        {
            where.Add("state = $state");
            parameters.Add(("$state", (long)state));
        }
        parameters.Add(("$take", (long)query.Take));
        parameters.Add(("$skip", (long)query.Skip));
        string filter = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);
        return Read($"SELECT * FROM operations {filter} ORDER BY planned_at DESC, id DESC LIMIT $take OFFSET $skip", [.. parameters]);
    }

    public IReadOnlyList<OperationRecord> RunOperations(long runId) =>
        Read("SELECT * FROM operations WHERE run_id = $run ORDER BY id", ("$run", runId));

    public IReadOnlyList<OperationRecord> Incomplete() =>
        Read("SELECT * FROM operations WHERE state IN ($p, $s) ORDER BY id", ("$p", (long)OperationState.Planned), ("$s", (long)OperationState.Started));

    public OperationRecord? LatestOutputAt(string destination) => Read(
        "SELECT * FROM operations WHERE dest_path = $dest COLLATE NOCASE AND state = $s ORDER BY completed_at DESC, id DESC LIMIT 1",
        ("$dest", destination), ("$s", (long)OperationState.Completed)).FirstOrDefault();

    public int ClearBefore(DateTimeOffset cutoff)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            // The newest completed row per destination stays: the self-trigger guard still needs it,
            // or a file Chippi renamed long ago would be renamed again. Rows an undo points at stay too.
            using var operations = Command(
                """
                DELETE FROM operations
                WHERE planned_at < $cut AND state IN ($c, $f, $u)
                  AND id NOT IN (SELECT MAX(id) FROM operations WHERE state = $c GROUP BY dest_path COLLATE NOCASE)
                  AND id NOT IN (SELECT undo_of FROM operations WHERE undo_of IS NOT NULL)
                """,
                transaction, ("$cut", cutoff.ToUnixTimeMilliseconds()), ("$c", (long)OperationState.Completed),
                ("$f", (long)OperationState.Failed), ("$u", (long)OperationState.Undone));
            int removed = operations.ExecuteNonQuery();
            using var runs = Command("DELETE FROM runs WHERE id NOT IN (SELECT DISTINCT run_id FROM operations) AND started_at < $cut",
                transaction, ("$cut", cutoff.ToUnixTimeMilliseconds()));
            runs.ExecuteNonQuery();
            transaction.Commit();
            return removed;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
        }
    }

    private void Migrate()
    {
        long version = Scalar("PRAGMA user_version;");
        if (version >= SchemaVersion)
            return;
        Execute($"""
            CREATE TABLE IF NOT EXISTS runs (
                id          INTEGER PRIMARY KEY,
                trigger     INTEGER NOT NULL,
                folder      TEXT NOT NULL,
                started_at  INTEGER NOT NULL,
                ended_at    INTEGER
            );
            CREATE TABLE IF NOT EXISTS operations (
                id            INTEGER PRIMARY KEY,
                run_id        INTEGER NOT NULL REFERENCES runs(id),
                kind          INTEGER NOT NULL,
                source_path   TEXT NOT NULL,
                dest_path     TEXT NOT NULL,
                rule_id       TEXT,
                rule_name     TEXT,
                bytes         INTEGER NOT NULL,
                sha256        TEXT,
                state         INTEGER NOT NULL,
                error         TEXT,
                planned_at    INTEGER NOT NULL,
                completed_at  INTEGER,
                undo_of       INTEGER REFERENCES operations(id),
                dest_modified INTEGER,
                partial_path  TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_operations_dest ON operations(dest_path COLLATE NOCASE, state);
            CREATE INDEX IF NOT EXISTS ix_operations_state ON operations(state);
            CREATE INDEX IF NOT EXISTS ix_operations_run ON operations(run_id);
            PRAGMA user_version = {SchemaVersion};
            """);
    }

    private void SetState(long id, OperationState state) =>
        Execute("UPDATE operations SET state = $s WHERE id = $id", ("$s", (long)state), ("$id", id));

    private long Now() => _time.GetUtcNow().ToUnixTimeMilliseconds();

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command(sql, null, parameters);
            command.ExecuteNonQuery();
        }
    }

    private long Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command(sql, null, parameters);
            return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private List<OperationRecord> Read(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command(sql, null, parameters);
            using var reader = command.ExecuteReader();
            var result = new List<OperationRecord>();
            while (reader.Read())
                result.Add(ReadOperation(reader));
            return result;
        }
    }

    private SqliteCommand Command(string sql, SqliteTransaction? transaction, params (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static OperationRecord ReadOperation(SqliteDataReader r)
    {
        static DateTimeOffset? Time(SqliteDataReader r, string column) =>
            r.IsDBNull(r.GetOrdinal(column)) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(r.GetOrdinal(column)));
        static string? Text(SqliteDataReader r, string column) =>
            r.IsDBNull(r.GetOrdinal(column)) ? null : r.GetString(r.GetOrdinal(column));

        return new OperationRecord(
            r.GetInt64(r.GetOrdinal("id")),
            r.GetInt64(r.GetOrdinal("run_id")),
            (HistoryOperationKind)r.GetInt64(r.GetOrdinal("kind")),
            r.GetString(r.GetOrdinal("source_path")),
            r.GetString(r.GetOrdinal("dest_path")),
            Text(r, "rule_id"),
            Text(r, "rule_name"),
            r.GetInt64(r.GetOrdinal("bytes")),
            Text(r, "sha256"),
            (OperationState)r.GetInt64(r.GetOrdinal("state")),
            Text(r, "error"),
            Time(r, "planned_at")!.Value,
            Time(r, "completed_at"),
            r.IsDBNull(r.GetOrdinal("undo_of")) ? null : r.GetInt64(r.GetOrdinal("undo_of")),
            Time(r, "dest_modified"),
            Text(r, "partial_path"));
    }
}
