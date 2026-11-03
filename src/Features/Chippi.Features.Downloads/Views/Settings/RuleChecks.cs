// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.RegularExpressions;
using Chippi.Features.Downloads.Model;
using Chippi.Rules;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// Field-level messages for the rules editor, shown live in amber under the field. Chippi.Rules
/// validates the whole rule; these say which part is wrong in words a person can act on.
/// </summary>
internal static class RuleChecks
{
    public static readonly string[] MoveTokens = ["{year}", "{month}", "{day}", "{extension}", "{filename}"];
    public static readonly string[] RenameTokens = ["{year}", "{month}", "{day}", "{stem}", "{extension}", "{filename}"];

    /// <summary>The scope of Save or Enable. Folder modes are known without scanning for files.</summary>
    public static string ExistingScope(RuleSetting? rule, IReadOnlyList<WatchedFolder> folders, bool enabling)
    {
        if (rule is null || rule.Action == RuleActionKind.Hold || folders.Count == 0) return "";
        var ambiguous = OrganizerLabels.RepeatedFolderNames(folders.Select(f => Ui.FolderName(f.Path)));
        string Names(FolderMode mode)
        {
            var paths = folders.Where(f => f.Mode == mode).Select(f => f.Path).ToList();
            var names = paths.Take(2).Select(path => ambiguous.Contains(Ui.FolderName(path)) ? path : Ui.FolderName(path));
            return string.Join(", ", names) + (paths.Count > 2 ? $" and {paths.Count - 2} more" : "");
        }
        string auto = Names(FolderMode.Silent), manual = Names(FolderMode.Manual);
        if (auto.Length == 0 && manual.Length == 0) return "All watched folders are paused.";
        string when = enabling ? "Enabling" : "Saving";
        string verb = rule.Action == RuleActionKind.Move ? "move" : "rename";
        var effects = new List<string>();
        if (auto.Length > 0) effects.Add($"{when} can {verb} existing matches in {auto}.");
        if (manual.Length > 0) effects.Add($"Existing matches in {manual} wait for review.");
        return string.Join(" ", effects);
    }

    /// <summary>Ask the real planner which rule could win on the name alone, keeping its validation,
    /// safety checks and order. A size or date on that rule needs facts the field cannot supply.</summary>
    public static (string Text, string? Match, bool Warning) TestName(string name, string folder, IReadOnlyList<Rule> rules)
    {
        var nameRules = rules.Select(r => r with { Conditions = r.Conditions with
        {
            MinBytes = null, MaxBytes = null, ModifiedAfter = null, ModifiedBefore = null,
        }}).ToList();
        var now = DateTimeOffset.Now;
        PlannedOperation plan;
        try
        {
            // Model an ordinary non-empty file; only its name participates in the rule conditions.
            plan = Planner.Plan(new FileFacts(name, Path.Combine(folder, name), 1, now, now), nameRules, new PlanContext(folder, now, TimeZoneInfo.Local, []));
        }
        catch (ArgumentException)
        {
            return ("Not a valid file name", null, true);
        }
        var winner = rules.FirstOrDefault(r => r.Id == plan.RuleId);
        if (winner is not null)
        {
            var c = winner.Conditions;
            string? needed = (c.MinBytes is not null || c.MaxBytes is not null, c.ModifiedAfter is not null || c.ModifiedBefore is not null) switch
            {
                (true, true) => "File size and modified date",
                (true, false) => "File size",
                (false, true) => "Modified date",
                _ => null,
            };
            if (needed is not null) return ($"{needed} needed · {winner.Name}", null, false);
        }
        return plan.Kind switch
        {
            OperationKind.Move or OperationKind.Rename => ($"{winner!.Name} → {Ui.Relative(folder, plan.Destination!)}", plan.RuleId, false),
            OperationKind.Hold => ($"Held · {plan.HoldReason ?? plan.Warnings.LastOrDefault()?.Detail ?? "held"}", plan.RuleId, true),
            _ when winner is not null => ($"Already in place · {winner.Name}", plan.RuleId, false),
            _ => ($"No rule matches · stays in {Ui.FolderName(folder)}", null, false),
        };
    }

    public static string? Extensions(string text)
    {
        foreach (string part in SplitExtensions(text))
        {
            if (part != "*" && part.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-'))
                return $"Use letters and digits only: {part}";
        }
        return null;
    }

    public static List<string> SplitExtensions(string text) =>
        [.. text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(e => e.TrimStart('.').ToLowerInvariant())];

    public static string? Regex(string pattern)
    {
        if (pattern.Length == 0)
            return null;
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RuleValidator.RegexTimeout);
            return null;
        }
        catch (RegexParseException ex)
        {
            return ex.Error switch
            {
                RegexParseError.InsufficientClosingParentheses => "Missing )",
                RegexParseError.InsufficientOpeningParentheses => "Extra )",
                RegexParseError.UnterminatedBracket => "Missing ]",
                RegexParseError.QuantifierAfterNothing => "Nothing to repeat",
                _ => "Not a valid pattern",
            };
        }
    }

    /// <summary>A destination folder (rename: false) or new name (rename: true) template.</summary>
    public static string? Template(string template, bool rename)
    {
        if (string.IsNullOrWhiteSpace(template))
            return rename ? "A name is required" : "A folder is required";
        var tokens = rename ? RenameTokens : MoveTokens;
        foreach (Match match in System.Text.RegularExpressions.Regex.Matches(template, @"\{[^{}]*\}?"))
        {
            if (!tokens.Contains(match.Value))
                return match.Value.EndsWith('}') ? $"Unknown placeholder {match.Value}" : "Missing }";
        }
        if (template.Contains('}') && template.Count(c => c == '}') > template.Count(c => c == '{'))
            return "Missing {";
        string sample = tokens.Aggregate(template, (s, t) => s.Replace(t, "x", StringComparison.Ordinal));
        if (rename && sample.IndexOfAny(['\\', '/']) >= 0)
            return "A name can't contain \\ or /";
        bool unc = sample.StartsWith(@"\\", StringComparison.Ordinal);
        if (!rename && !unc && (sample.StartsWith('\\') || sample.StartsWith('/')))
            return "Start with a folder name or a drive";
        var parts = unc ? sample[2..].Split('\\')
            : WindowsPaths.IsAbsolute(sample) ? sample[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            : sample.Split(['\\', '/']);
        foreach (string part in parts)
        {
            if (part == "..")
                return "Can't go above the folder";
            if (part.Length == 0)
                return "Empty folder name";
            if (part.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) is >= 0 and var at)
                return $"Can't use {part[at]} in a name";
            try
            {
                WindowsPaths.ValidateComponent(part);
            }
            catch (ArgumentException ex) when (ex.Message.StartsWith("Reserved", StringComparison.Ordinal))
            {
                return $"{part.Split('.')[0]} is a reserved name";
            }
            catch (ArgumentException)
            {
                return part.EndsWith('.') || part.EndsWith(' ') ? "A name can't end with a dot or space" : "Not a valid name";
            }
        }
        try
        {
            Templates.Validate(template, rename);
            return null;
        }
        catch (ArgumentException)
        {
            return "Not a valid path";
        }
    }

    /// <summary>
    /// ".pdf · name matches "^inv" · up to 25 MB → Documents\Invoices\{year}\{month}", ".png .jpg → Pictures",
    /// ".torrent · hold: Check before opening". The pattern shows, so two pattern rules don't read alike.
    /// </summary>
    public static string Summary(RuleSetting rule)
    {
        var parts = new List<string>();
        if (rule.Extensions is { Count: > 0 } extensions)
            parts.Add(string.Join(" ", extensions.Select(e => e.Length == 0 ? "no extension" : e == "*" ? e : "." + e)));
        if (!string.IsNullOrEmpty(rule.NameRegex))
            parts.Add($"name matches \"{rule.NameRegex}\"");
        if (rule.MinBytes is { } min && rule.MaxBytes is { } max)
            parts.Add($"{Ui.Size(min)} to {Ui.Size(max)}");
        else if (rule.MaxBytes is { } upTo)
            parts.Add($"up to {Ui.Size(upTo)}");
        else if (rule.MinBytes is { } from)
            parts.Add($"from {Ui.Size(from)}");
        if (rule.ModifiedAfter is not null || rule.ModifiedBefore is not null)
            parts.Add("modified");
        string when = parts.Count == 0 ? "any file" : string.Join(" · ", parts);
        return rule.Action switch
        {
            RuleActionKind.Rename => $"{when} · rename to {rule.Template}",
            RuleActionKind.Hold => string.IsNullOrWhiteSpace(rule.HoldReason) ? $"{when} · hold" : $"{when} · hold: {rule.HoldReason}",
            _ => $"{when} → {DestinationName(rule.Template ?? "")}",
        };
    }

    /// <summary>
    /// A move destination as the summary shows it: a chosen folder by its own name, from the last
    /// named folder on when it ends in placeholders ("Documents\{year}"). Relative ones read as typed.
    /// </summary>
    public static string DestinationName(string template)
    {
        if (!WindowsPaths.IsAbsolute(template))
            return template;
        var parts = template.TrimEnd('\\', '/').Split('\\', '/');
        int from = parts.Length - 1;
        while (from > 0 && parts[from].Contains('{'))
            from--;
        return string.Join('\\', parts[from..]);
    }
}
