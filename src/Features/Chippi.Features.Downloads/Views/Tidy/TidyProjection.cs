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
public sealed record TidyRow(string Name, string Detail, string Folder, string Source,
    bool IsHeld, bool IsWaiting, bool IsFailed, TidyRowAction Action, long? OperationId = null, string Age = "")
{
    public string ActionLabel => Action switch
    {
        TidyRowAction.Review => "Review",
        TidyRowAction.Retry or TidyRowAction.RetryUndo => "Retry",
        TidyRowAction.Undo => "Undo",
        _ => "",
    };
    public string AccessibleAction => $"{ActionLabel} {Name}";
    public bool HasAction => Action != TidyRowAction.None;
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
    public static string FolderName(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    public static string ModeLine(WatchedFolder folder, int ready) => folder.Mode switch
    {
        FolderMode.Manual => $"Manual · {ready} to review",
        FolderMode.Silent => $"Silent · moves after {Math.Max(0, folder.GraceSeconds)}s",
        _ => "Paused · not watching",
    };

    public static IReadOnlyList<TidyRow> Rows(IReadOnlyList<PendingFile> pending, IReadOnlyList<OperationRecord> history, DateTimeOffset now)
    {
        var rows = pending.Where(p => p.Status is PendingStatus.Held or PendingStatus.Waiting).Select(p => new TidyRow(
            p.Name, $"{(p.Status == PendingStatus.Held ? "Held" : "Waiting")} · {p.Reason ?? "Not ready"}",
            p.Folder, p.Path, p.Status == PendingStatus.Held, p.Status == PendingStatus.Waiting, false,
            p.Status == PendingStatus.Held ? TidyRowAction.Review : TidyRowAction.None));
        var recent = history.Where(o => o.State is OperationState.Completed or OperationState.Failed or OperationState.Undone)
            .Where(o => o.Kind != HistoryOperationKind.Restore || o.State == OperationState.Failed)
            .Where(o => !pending.Any(p => string.Equals(p.Path, o.SourcePath, StringComparison.OrdinalIgnoreCase)))
            .Select(o => new TidyRow(
                Path.GetFileName(o.State == OperationState.Failed ? o.SourcePath : o.DestinationPath),
                o.State == OperationState.Failed ? $"Failed · {o.Error ?? "Try again"}" :
                    o.State == OperationState.Undone ? "Restored" : Path.GetDirectoryName(o.DestinationPath) ?? "",
                Path.GetDirectoryName(o.SourcePath) ?? "", o.SourcePath, false, false, o.State == OperationState.Failed,
                o.State == OperationState.Failed ? o.Kind == HistoryOperationKind.Restore ? TidyRowAction.RetryUndo : TidyRowAction.Retry :
                    o.State == OperationState.Completed && o.Kind is HistoryOperationKind.Move or HistoryOperationKind.Rename ? TidyRowAction.Undo : TidyRowAction.None,
                o.Kind == HistoryOperationKind.Restore ? o.UndoOf : o.Id, Age(now - (o.CompletedAt ?? o.PlannedAt))));
        return [.. rows.Concat(recent).Take(12)];
    }

    private static string Age(TimeSpan age) => age.TotalMinutes < 1 ? "now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m" :
        age.TotalDays < 1 ? $"{(int)age.TotalHours}h" : $"{(int)age.TotalDays}d";
}
