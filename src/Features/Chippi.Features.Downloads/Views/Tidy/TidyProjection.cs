// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Chippi.UI.Controls;
using System.Windows.Media;

namespace Chippi.Features.Downloads.Views.Tidy;

public enum TidyRowAction { None, Review, Retry, Undo, RetryUndo }

/// <summary>A recent row's immutable display state; no file operation is decided by a visual toggle.</summary>
public sealed class TidyRow(string name, string detail, string folder, string source,
    bool isHeld, bool isWaiting, bool isFailed, TidyRowAction action, long? operationId = null)
{
    // UI Automation peers track item identity, so refreshed rows must not compare equal to detached containers.
    public string Name { get; } = name;
    public string Detail { get; } = detail;
    public string Folder { get; } = folder;
    public string Source { get; } = source;
    public bool IsHeld { get; } = isHeld;
    public bool IsWaiting { get; } = isWaiting;
    public bool IsFailed { get; } = isFailed;
    public TidyRowAction Action { get; } = action;
    public long? OperationId { get; } = operationId;

    public override string ToString() => $"{Name}. {Detail}";

    public string ActionLabel => Action switch
    {
        TidyRowAction.Review => "Review",
        TidyRowAction.Retry or TidyRowAction.RetryUndo => "Retry",
        TidyRowAction.Undo => "Undo",
        _ => "",
    };
    public string AccessibleAction => $"{ActionLabel} {Name}";
    public bool HasAction => Action != TidyRowAction.None;

    /// <summary>Held rows get a Review button; other actions are icon buttons.</summary>
    public bool IsReview => Action == TidyRowAction.Review;
    public bool HasIconAction => HasAction && !IsReview;

    /// <summary>A caution triangle (held), a clock (waiting), the info glyph (failed) or a file.</summary>
    public Geometry Lead => IsHeld ? LucideIcons.TriangleAlert : IsWaiting ? LucideIcons.Clock : IsFailed ? LucideIcons.Info : LucideIcons.File;
    public Geometry ActionIcon => Action switch
    {
        TidyRowAction.Review => LucideIcons.Eye,
        TidyRowAction.Retry or TidyRowAction.RetryUndo => RetryIcon,
        _ => LucideIcons.Undo,
    };
    private static Geometry RetryIcon { get; } = MakeRetry();
    private static Geometry MakeRetry()
    {
        var icon = Geometry.Parse("M21,3 V9 H15 M21,9 a9,9 0 1 0 0,6");
        icon.Freeze();
        return icon;
    }
}

internal static class TidyProjection
{
    /// <summary>The Recent card holds this many rows (DESIGN.md §5 Tidy panel).</summary>
    public const int RecentRows = 4;

    public static string FolderName(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    public static string ModeLine(WatchedFolder folder, int ready) => folder.Mode switch
    {
        FolderMode.Manual => $"Manual · {ready} to review",
        FolderMode.Silent => $"Silent · moves after {Math.Max(0, folder.GraceSeconds)}s",
        _ => "Paused · not watching",
    };

    /// <summary>Held rows first, then waiting, then the latest moves: "Documents · 23m", "Restored to Downloads".</summary>
    public static IReadOnlyList<TidyRow> Rows(IReadOnlyList<PendingFile> pending, IReadOnlyList<OperationRecord> history, DateTimeOffset now)
    {
        var rows = pending.Where(p => p.Status is PendingStatus.Held or PendingStatus.Waiting)
            .OrderBy(p => p.Status == PendingStatus.Held ? 0 : 1)
            .Select(p => new TidyRow(
                p.Name, $"{(p.Status == PendingStatus.Held ? "Held" : "Waiting")} · {p.Reason ?? "Not ready"}",
                p.Folder, p.Path, p.Status == PendingStatus.Held, p.Status == PendingStatus.Waiting, false,
                p.Status == PendingStatus.Held ? TidyRowAction.Review : TidyRowAction.None));
        var recent = history.Where(o => o.State is OperationState.Completed or OperationState.Failed or OperationState.Undone)
            .Where(o => o.Kind != HistoryOperationKind.Restore || o.State == OperationState.Failed)
            .Where(o => !pending.Any(p => string.Equals(p.Path, o.SourcePath, StringComparison.OrdinalIgnoreCase)))
            .Select(o => new TidyRow(
                Path.GetFileName(o.State == OperationState.Failed ? o.SourcePath : o.DestinationPath),
                o.State == OperationState.Failed ? o.Error is { } error ? $"Failed · {error}" : "Failed" :
                    o.State == OperationState.Undone ? $"Restored to {FolderName(Path.GetDirectoryName(o.SourcePath) ?? "")}" :
                    $"{FolderName(Path.GetDirectoryName(o.DestinationPath) ?? "")} · {Age(now - (o.CompletedAt ?? o.PlannedAt))}",
                Path.GetDirectoryName(o.SourcePath) ?? "", o.SourcePath, false, false, o.State == OperationState.Failed,
                o.State == OperationState.Failed ? o.Kind == HistoryOperationKind.Restore ? TidyRowAction.RetryUndo : TidyRowAction.Retry :
                    o.State == OperationState.Completed && o.Kind is HistoryOperationKind.Move or HistoryOperationKind.Rename ? TidyRowAction.Undo : TidyRowAction.None,
                o.Kind == HistoryOperationKind.Restore ? o.UndoOf : o.Id));
        return [.. rows.Concat(recent).Take(RecentRows)];
    }

    private static string Age(TimeSpan age) => age.TotalMinutes < 1 ? "now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m" :
        age.TotalDays < 1 ? $"{(int)age.TotalHours}h" : $"{(int)age.TotalDays}d";
}
