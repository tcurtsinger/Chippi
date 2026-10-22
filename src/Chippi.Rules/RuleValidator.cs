// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.RegularExpressions;

namespace Chippi.Rules;

public static class RuleValidator
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);
    internal const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static IReadOnlyList<ValidationError> Validate(IReadOnlyList<Rule> rules)
    {
        var errors = new List<ValidationError>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        foreach (var rule in rules)
        {
            void Error(string field, string message) => errors.Add(new(rule.Id, field, message));
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id)) Error(nameof(Rule.Id), "A unique ID is required.");
            if (string.IsNullOrWhiteSpace(rule.Name)) Error(nameof(Rule.Name), "A name is required.");
            if (!orders.Add(rule.Order)) Error(nameof(Rule.Order), "Rule orders must be unique.");
            if (rule.Conditions is not { } c) { Error(nameof(Rule.Conditions), "Conditions are required."); continue; }
            if (c.Extensions is { } extensions && (extensions.Count == 0 || extensions.Any(e => e is null || e != "*" && e.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-'))))
                Error(nameof(Conditions.Extensions), "Use extensions without dots, an empty extension, or *.");
            if (c.MinBytes < 0 || c.MaxBytes < 0 || c.MinBytes > c.MaxBytes) Error(nameof(Conditions.MinBytes), "Invalid size bounds.");
            if (c.ModifiedAfter > c.ModifiedBefore) Error(nameof(Conditions.ModifiedAfter), "Invalid date bounds.");
            if (c.NameRegex is { Length: > 0 } regex)
            {
                try { _ = new Regex(regex, Options, RegexTimeout); }
                catch (ArgumentException) { Error(nameof(Conditions.NameRegex), "Invalid expression."); }
            }
            try
            {
                switch (rule.Action)
                {
                    case MoveTo move: Templates.Validate(move.DestinationTemplate, false); break;
                    case RenameTo rename: Templates.Validate(rename.NameTemplate, true); break;
                    case Hold hold when !string.IsNullOrWhiteSpace(hold.Reason): break;
                    default: Error(nameof(Rule.Action), "A supported action is required."); break;
                }
            }
            catch (ArgumentException) { Error(nameof(Rule.Action), "Invalid path or template."); }
        }
        return errors;
    }
}
