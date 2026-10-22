// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Storage;
using Chippi.Rules;

namespace Chippi.Features.Downloads.Engine;

/// <summary>Where a file in a watched folder stands, for the Tidy tab.</summary>
public enum PendingStatus
{
    /// <summary>Ready to organize; waits for Organize now (Manual, or already there when the folder was added).</summary>
    Ready,

    /// <summary>A rule or a safety check held it (misleading name, hold rule). Never moved silently.</summary>
    Held,

    /// <summary>Not ready yet after a long wait (still empty, still in use).</summary>
    Waiting,
}

public sealed record PendingFile(string Folder, string Path, string Name, PendingStatus Status, string? Reason);

/// <summary>One planned operation in a preview. <see cref="Selected"/> is the starting checkbox state.</summary>
public sealed record PreviewItem(
    int Id,
    string Source,
    string Name,
    long Bytes,
    DateTimeOffset ModifiedUtc,
    OperationKind Kind,
    string? Destination,
    string? RuleId,
    string? RuleName,
    IReadOnlyList<PlanWarning> Warnings,
    string? HoldReason)
{
    /// <summary>Moves and renames start selected; held items and misleading names start unselected (brief §7).</summary>
    public bool Selected => Kind is OperationKind.Move or OperationKind.Rename
        && !Warnings.Any(w => w.Kind == WarningKind.MisleadingName);
}

/// <summary>A preview to approve once, within its lifetime. Destinations are fixed: approval never re-plans.</summary>
public sealed record PreviewPlan(Guid Id, string Folder, RunTrigger Trigger, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, IReadOnlyList<PreviewItem> Items)
{
    /// <summary>Archive import: the archive's file name.</summary>
    public string? Archive { get; init; }

    /// <summary>Archive import: where the files were extracted. Deleted once the preview is used or discarded.</summary>
    public string? Staging { get; init; }
}

public sealed record ItemResult(int ItemId, string Source, string? Destination, bool Succeeded, string? Error);

public sealed record ApplyResult(long RunId, IReadOnlyList<ItemResult> Items)
{
    public int Succeeded => Items.Count(i => i.Succeeded);

    public int Failed => Items.Count(i => !i.Succeeded);
}

public sealed record UndoResult(bool Succeeded, long? OperationId, string? Error);
