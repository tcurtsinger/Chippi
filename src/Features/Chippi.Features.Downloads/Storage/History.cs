// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.Downloads.Storage;

/// <summary>What started a run of operations.</summary>
public enum RunTrigger
{
    Silent,
    Preview,
    Explorer,
    ArchiveImport,
    Undo,
}

public enum HistoryOperationKind
{
    Move,
    Rename,

    /// <summary>A verified undo moving a file back to where it came from.</summary>
    Restore,

    /// <summary>
    /// An archive's file put straight into the folder because no rule wanted it. There is nothing
    /// before it to go back to, so it is not undoable. An imported file a rule moved is a
    /// <see cref="Move"/> or <see cref="Rename"/> whose source is the folder it was imported into,
    /// so its undo puts it there.
    /// </summary>
    Import,
}

/// <summary>Planned, then Started before the file system changes, then Completed or Failed after. Undone marks a completed row that was restored.</summary>
public enum OperationState
{
    Planned,
    Started,
    Completed,
    Failed,
    Undone,
}

public sealed record RunRecord(long Id, RunTrigger Trigger, string Folder, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

public sealed record OperationRecord(
    long Id,
    long RunId,
    HistoryOperationKind Kind,
    string SourcePath,
    string DestinationPath,
    string? RuleId,
    string? RuleName,
    long Bytes,
    string? Sha256,
    OperationState State,
    string? Error,
    DateTimeOffset PlannedAt,
    DateTimeOffset? CompletedAt,
    long? UndoOf,
    DateTimeOffset? DestinationModifiedUtc,
    string? PartialPath = null);

/// <summary>History view filters. All optional; newest first.</summary>
public sealed record HistoryQuery
{
    public string? Folder { get; init; }

    /// <summary>Matches source or destination path, case-insensitive.</summary>
    public string? Text { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public OperationState? State { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 200;
}

/// <summary>
/// The organizer's history (architecture.md §3). The only seam to storage: <see cref="SqliteHistoryStore"/>
/// is the only class that knows SQLite, so a different store is one more class behind this interface.
/// Thread-safe.
/// </summary>
public interface IHistoryStore : IDisposable
{
    long BeginRun(RunTrigger trigger, string folder);

    void EndRun(long runId);

    long Plan(long runId, HistoryOperationKind kind, string source, string destination, string? ruleId, string? ruleName, long bytes, long? undoOf = null);

    void MarkStarted(long operationId);

    void MarkCompleted(long operationId, string destination, string sha256, DateTimeOffset destinationModifiedUtc);

    void MarkFailed(long operationId, string error);

    /// <summary>
    /// Records the copy-in-progress file this operation created (null once it is gone), so a
    /// restart deletes only a file it has proof of creating.
    /// </summary>
    void MarkPartial(long operationId, string? partialPath);

    void MarkUndone(long operationId);

    OperationRecord? Get(long operationId);

    IReadOnlyList<OperationRecord> Query(HistoryQuery query);

    IReadOnlyList<OperationRecord> RunOperations(long runId);

    /// <summary>Started but never finished: reconciled at startup.</summary>
    IReadOnlyList<OperationRecord> Incomplete();

    /// <summary>Rows in any state that still own a partial file waiting to be deleted.</summary>
    IReadOnlyList<OperationRecord> WithPartial();

    /// <summary>The newest completed move, rename or restore that produced <paramref name="destination"/>, if any. Backs the self-trigger guard.</summary>
    OperationRecord? LatestOutputAt(string destination);

    /// <summary>Removes finished rows planned before <paramref name="cutoff"/>. Never touches files.</summary>
    int ClearBefore(DateTimeOffset cutoff);
}
