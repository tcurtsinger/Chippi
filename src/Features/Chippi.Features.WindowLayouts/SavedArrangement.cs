// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;

namespace Chippi.Features.WindowLayouts;

public sealed record ArrangementRow
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string AppIdentity { get; init; } = "";
    public string Target { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string WorkingFolder { get; init; } = "";
    public string App { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string TitleHint { get; init; } = "";
    public string DisplayId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int Dpi { get; init; } = 96;
    public SavedWindowPlacement? Placement { get; init; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(AppIdentity)
        && !string.IsNullOrWhiteSpace(Target) && !string.IsNullOrWhiteSpace(DisplayId)
        && Placement?.IsValid == true && Dpi > 0 && Arguments is not null && WorkingFolder is not null
        && (System.IO.Path.IsPathFullyQualified(Target)
            ? new ExecutableLaunch(Target, Arguments, WorkingFolder).IsValid
            : Arguments.Length == 0 && WorkingFolder.Length == 0 && !Target.Contains('\0'));
}

public sealed record SavedArrangement
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public List<ArrangementRow> Rows { get; init; } = [];

    public bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name)
        && Name.Trim().Length <= 80 && Rows is { Count: > 0 }
        && Rows.All(r => r is not null && r.IsValid)
        && Rows.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() == Rows.Count;

    /// <summary>A replacement draft keeps deliberate launch edits only for unambiguous identities on both sides.</summary>
    public SavedArrangement Recapture(IEnumerable<ArrangementRow> captured)
    {
        var rows = captured.ToList();
        var previous = Rows.GroupBy(Key).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var counts = rows.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        return this with { Rows = rows.Select(row => counts[Key(row)] == 1 && previous.TryGetValue(Key(row), out var old)
            ? row with { Id = old.Id, Target = old.Target, Arguments = old.Arguments, WorkingFolder = old.WorkingFolder }
            : row).ToList() };
    }

    private static (string, string, string) Key(ArrangementRow row) =>
        (row.AppIdentity.ToUpperInvariant(), row.ClassName, row.TitleHint);
}
