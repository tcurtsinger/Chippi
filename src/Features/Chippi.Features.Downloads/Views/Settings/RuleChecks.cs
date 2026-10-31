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
