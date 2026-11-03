// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Storage;
using Chippi.Rules;

namespace Chippi.Features.Downloads.Views;

internal static class OrganizerLabels
{
    public static string FailedAction(HistoryOperationKind kind) => kind switch
    {
        HistoryOperationKind.Import => "Import failed",
        HistoryOperationKind.Restore => "Undo failed",
        HistoryOperationKind.Rename => "Rename failed",
        _ => "Move failed",
    };

    public static HashSet<string> RepeatedFolderNames(IEnumerable<string> names) =>
        names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string HoldReason(string? reason)
    {
        string text = reason?.Trim() ?? "";
        return text.Equals("Check before opening", StringComparison.OrdinalIgnoreCase) || text.Equals("Held", StringComparison.OrdinalIgnoreCase) ? "" : text;
    }

    public static IEnumerable<string> CompletedCounts(IEnumerable<PreviewItem> completed) =>
        completed.GroupBy(i => i.IsDirectImport ? "imported" : i.Kind == OperationKind.Rename ? "renamed" : "moved")
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Count()} {g.Key}");
}
