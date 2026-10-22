// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.RegularExpressions;

namespace Chippi.Rules;

public static class Planner
{
    public static PlannedOperation Plan(FileFacts file, IReadOnlyList<Rule> rules, PlanContext context)
    {
        var errors = RuleValidator.Validate(rules);
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors.Select(e => $"{e.RuleId}: {e.Field}: {e.Message}")), nameof(rules));
        ArgumentNullException.ThrowIfNull(context.TimeZone);
        ArgumentOutOfRangeException.ThrowIfNegative(file.Bytes);
        var warnings = new List<PlanWarning>();
        string source = file.FullPath, folder;
        PlannedOperation Held(PlanWarning warning, string? ruleId = null) => new(OperationKind.Hold, source, ruleId, null, [.. warnings, warning]);
        try
        {
            source = WindowsPaths.NormalizeAbsolute(file.FullPath);
            folder = WindowsPaths.NormalizeAbsolute(context.WatchedFolder);
            WindowsPaths.ValidateComponent(file.Name);
            if (!string.Equals(WindowsPaths.Leaf(source), file.Name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(WindowsPaths.Parent(source).TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("File must be an immediate child of the watched folder.");
        }
        catch (ArgumentException) { return Held(new(WarningKind.InvalidPath, "Invalid source path")); }
        if (FileSafety.Exclusion(file) is { } exclusion) return Held(exclusion);
        if (context.IgnorePatterns.Any(p => IgnoreFile.Matches(file.Name, p))) return Held(new(WarningKind.IgnoreMatch, "Ignored file"));
        if (FileSafety.IsMisleadingName(file.Name))
        {
            var warning = new PlanWarning(WarningKind.MisleadingName, "Misleading name");
            if (!context.Preview) return Held(warning);
            warnings.Add(warning);
        }

        var occupied = new HashSet<string>(context.OccupiedPaths.Select(WindowsPaths.NormalizeAbsolute), StringComparer.OrdinalIgnoreCase);
        occupied.Add(source);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.PlanningTime, context.TimeZone).DateTime);
        var modified = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(file.Modified, context.TimeZone).DateTime);
        foreach (var rule in rules.OrderBy(r => r.Order))
        {
            var c = rule.Conditions;
            string extension = Templates.Parts(file.Name).Extension;
            if (!rule.Enabled || c.Extensions is { } extensions && !extensions.Any(e => e == "*" || string.Equals(e, extension, StringComparison.OrdinalIgnoreCase))
                || c.MinBytes > file.Bytes || c.MaxBytes < file.Bytes || c.ModifiedAfter > modified || c.ModifiedBefore < modified)
                continue;
            try
            {
                if (c.NameRegex is { Length: > 0 } regex && !Regex.IsMatch(file.Name, regex, RuleValidator.Options, RuleValidator.RegexTimeout)) continue;
            }
            catch (RegexMatchTimeoutException)
            {
                warnings.Add(new(WarningKind.RegexTimeout, rule.Id));
                continue;
            }
            if (rule.Action is Hold hold) return new(OperationKind.Hold, source, rule.Id, null, warnings.ToArray()) { HoldReason = hold.Reason };
            try
            {
                bool rename = rule.Action is RenameTo;
                string template = rule.Action switch { MoveTo m => m.DestinationTemplate, RenameTo r => r.NameTemplate, _ => throw new InvalidOperationException() };
                string rendered = Templates.Render(template, file.Name, today);
                string destination;
                if (rename)
                {
                    WindowsPaths.ValidateComponent(rendered);
                    destination = WindowsPaths.Combine(folder, rendered);
                    if (string.Equals(destination, source, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Rename must change the name.");
                }
                else
                {
                    string targetFolder = WindowsPaths.IsAbsolute(rendered) ? WindowsPaths.NormalizeAbsolute(rendered) : WindowsPaths.Combine(folder, rendered);
                    if (string.Equals(targetFolder.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Destination must differ from source folder.");
                    destination = WindowsPaths.Combine(targetFolder, file.Name);
                }
                string leaf = WindowsPaths.Leaf(destination);
                var (stem, _) = Templates.Parts(leaf);
                string originalSuffix = leaf[stem.Length..];
                string parent = WindowsPaths.Parent(destination);
                string candidate = destination;
                for (int suffix = 2; occupied.Contains(candidate); suffix++)
                    candidate = WindowsPaths.Combine(parent, $"{stem} ({suffix}){originalSuffix}");
                return new(rename ? OperationKind.Rename : OperationKind.Move, source, rule.Id, candidate, warnings.ToArray());
            }
            catch (ArgumentException) { return Held(new(WarningKind.InvalidPath, "Invalid destination"), rule.Id); }
        }
        return new(OperationKind.NoMatch, source, null, null, warnings.ToArray());
    }

    /// <summary>Plans in input order and reserves destinations so two operations never propose the same path.</summary>
    public static IReadOnlyList<PlannedOperation> PlanBatch(IEnumerable<FileFacts> files, IReadOnlyList<Rule> rules, PlanContext context)
    {
        var input = files.ToArray();
        var occupied = new HashSet<string>(context.OccupiedPaths, StringComparer.OrdinalIgnoreCase);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in input)
        {
            string source;
            try { source = WindowsPaths.NormalizeAbsolute(file.FullPath); }
            catch (ArgumentException) { continue; } // Plan will report the invalid source as held.
            if (!sources.Add(source)) throw new ArgumentException("A batch cannot contain the same source twice.", nameof(files));
            occupied.Add(source);
        }
        var result = new List<PlannedOperation>();
        foreach (var file in input)
        {
            var plan = Plan(file, rules, context with { OccupiedPaths = occupied });
            result.Add(plan);
            if (plan.Destination is { } destination) occupied.Add(destination);
        }
        return result;
    }
}
