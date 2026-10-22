// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Rules;

public sealed record Rule(string Id, string Name, bool Enabled, int Order, Conditions Conditions, RuleAction Action);

/// <summary>All supplied conditions must match. Empty extension matches extensionless files; null means unrestricted.</summary>
public sealed record Conditions
{
    public IReadOnlyList<string>? Extensions { get; init; }
    public string? NameRegex { get; init; }
    public long? MinBytes { get; init; }
    public long? MaxBytes { get; init; }
    public DateOnly? ModifiedAfter { get; init; }
    public DateOnly? ModifiedBefore { get; init; }
}

public abstract record RuleAction;
public sealed record MoveTo(string DestinationTemplate) : RuleAction;
public sealed record RenameTo(string NameTemplate) : RuleAction;
public sealed record Hold(string Reason) : RuleAction;

/// <summary>Collected by the module. This library never reads a file or probes its attributes.</summary>
public sealed record FileFacts(string Name, string FullPath, long Bytes, DateTimeOffset Created, DateTimeOffset Modified, FileAttributes Attributes = FileAttributes.Normal);

/// <summary>Time zone and time are explicit. OccupiedPaths includes existing and already-planned files.</summary>
public sealed record PlanContext(string WatchedFolder, DateTimeOffset PlanningTime, TimeZoneInfo TimeZone, IReadOnlyCollection<string> OccupiedPaths)
{
    public IReadOnlyList<string> IgnorePatterns { get; init; } = [];

    /// <summary>Preview may propose a warned executable for explicit review; this never authorizes execution.</summary>
    public bool Preview { get; init; }
}

public enum OperationKind { Move, Rename, Hold, NoMatch }
public enum WarningKind { BuiltInExclusion, IgnoreMatch, EmptyFile, CloudPlaceholder, MisleadingName, InvalidPath, RegexTimeout }
public sealed record PlanWarning(WarningKind Kind, string Detail);
public sealed record PlannedOperation(OperationKind Kind, string Source, string? RuleId, string? Destination, IReadOnlyList<PlanWarning> Warnings)
{
    public string? HoldReason { get; init; }
}
public sealed record ValidationError(string RuleId, string Field, string Message);

/// <summary>A one-based physical line number and its unsupported pattern.</summary>
public sealed record IgnoreParseError(int Line, string Pattern, string Message);
public sealed record IgnoreParseResult(IReadOnlyList<string> Patterns, IReadOnlyList<IgnoreParseError> Errors);
