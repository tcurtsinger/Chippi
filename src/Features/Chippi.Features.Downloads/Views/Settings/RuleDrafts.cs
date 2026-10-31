// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Model;
using Chippi.Rules;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// The rules editor's model, kept free of WPF so it can be tested. <see cref="Drafts"/> are what is
/// on screen; each rule's saved version changes only when Save is pressed on a valid draft, so an
/// unfinished draft never broadens, drops or seeds a copy of a rule. The switch, reorder, Delete and
/// Duplicate act on the saved rules straight away.
/// </summary>
internal sealed class RuleDrafts
{
    private readonly Dictionary<string, RuleSetting> _saved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _errors = new(StringComparer.Ordinal);

    public RuleDrafts(IEnumerable<RuleSetting> saved)
    {
        Drafts = [.. saved.OrderBy(r => r.Order).Select(r => r.Clone())];
        foreach (var rule in Drafts)
            _saved[rule.Id] = rule.Clone(); // as saved, even a hand-edited invalid one: opening the page never drops it
    }

    /// <summary>The rules on screen, in list order.</summary>
    public List<RuleSetting> Drafts { get; }

    public bool IsSaved(string id) => _saved.ContainsKey(id);

    /// <summary>Records whether <paramref name="field"/> of a rule shows a problem right now.</summary>
    public void SetError(string id, string field, bool problem)
    {
        if (!_errors.TryGetValue(id, out var fields))
            _errors[id] = fields = [];
        if (problem)
            fields.Add(field);
        else
            fields.Remove(field);
    }

    /// <summary>
    /// The draft differs from its saved version (its switch aside), was never saved, or has a field
    /// showing a problem (whose text the draft can't hold, like "abc" for a size).
    /// </summary>
    public bool IsChanged(string id)
    {
        var draft = Drafts.Find(r => r.Id == id);
        return draft is not null && (!_saved.TryGetValue(id, out var saved) || !SameRule(draft, saved) || HasProblem(id));
    }

    private bool HasProblem(string id) => _errors.TryGetValue(id, out var fields) && fields.Count > 0;

    public bool HasUnsaved => Drafts.Any(r => IsChanged(r.Id));

    /// <summary>
    /// Save is offered: the draft changed, no field shows a problem, it has a condition and it passes
    /// validation. A rule with no condition would win every file, so it never saves.
    /// </summary>
    public bool CanSave(string id) =>
        IsChanged(id) && !HasProblem(id) && Drafts.Find(r => r.Id == id) is { } draft && HasCondition(draft) && Valid(Drafts).Any(r => r.Id == id);

    public static bool HasCondition(RuleSetting rule) =>
        rule.Extensions is not null || rule.NameRegex is { Length: > 0 } || rule.MinBytes is not null || rule.MaxBytes is not null
        || rule.ModifiedAfter is not null || rule.ModifiedBefore is not null;

    /// <summary>
    /// What "Test a file name" runs: the saved rules in list order, with the rule being edited as it
    /// stands in its place, a new one included, once it has a condition and no field shows a problem.
    /// </summary>
    public List<RuleSetting> Testing(string? open)
    {
        var rules = new List<RuleSetting>();
        foreach (var draft in Drafts)
        {
            bool editing = draft.Id == open && !HasProblem(draft.Id) && HasCondition(draft);
            if (editing)
                rules.Add(draft.Clone());
            else if (_saved.TryGetValue(draft.Id, out var saved))
                rules.Add(saved.Clone());
            else
                continue;
            rules[^1].Order = rules.Count - 1;
        }
        return rules;
    }

    /// <summary>
    /// An earlier rule that's on and tests only extensions (or nothing at all) and wins some of this draft's
    /// extensions first, with the ones it takes: "Rule 1 (PDFs to Documents) matches .pdf first". Null when
    /// none does or the draft tests no extension (other conditions can't be compared).
    /// </summary>
    public string? Shadow(RuleSetting draft)
    {
        int index = Drafts.IndexOf(draft);
        if (index <= 0 || draft.Extensions is not { Count: > 0 } mine)
            return null;
        for (int i = 0; i < index; i++)
        {
            if (!_saved.TryGetValue(Drafts[i].Id, out var earlier) || !earlier.Enabled
                || earlier.NameRegex is { Length: > 0 } || earlier.MinBytes is not null || earlier.MaxBytes is not null
                || earlier.ModifiedAfter is not null || earlier.ModifiedBefore is not null)
                continue;
            bool every = earlier.Extensions is null || earlier.Extensions.Contains("*");
            var taken = every ? mine : mine.Where(e => earlier.Extensions!.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();
            if (taken.Count == 0)
                continue;
            string what = every ? "every file" : string.Join(" ", taken.Select(e => e.Length == 0 ? "no extension" : "." + e));
            return $"Rule {i + 1} ({earlier.Name}) matches {what} first";
        }
        return null;
    }

    /// <summary>Makes the draft its rule's saved version. False when it can't be saved.</summary>
    public bool Save(string id)
    {
        if (!CanSave(id))
            return false;
        _saved[id] = Drafts.Find(r => r.Id == id)!.Clone();
        return true;
    }

    /// <summary>Puts the draft back to its saved version; a rule that was never saved goes away.</summary>
    public void Cancel(string id)
    {
        int index = Drafts.FindIndex(r => r.Id == id);
        if (index < 0)
            return;
        if (_saved.TryGetValue(id, out var saved))
            Drafts[index] = saved.Clone();
        else
            Drafts.RemoveAt(index);
        _errors.Remove(id);
    }

    public void CancelAll()
    {
        foreach (var draft in Drafts.ToList())
            Cancel(draft.Id);
    }

    /// <summary>The switch applies to the saved rule at once, whatever the draft holds.</summary>
    public void SetEnabled(RuleSetting draft, bool enabled)
    {
        draft.Enabled = enabled;
        if (_saved.TryGetValue(draft.Id, out var saved))
            saved.Enabled = enabled;
    }

    /// <summary>The saved rules in the on-screen order: what is saved and what runs. A rule never saved is not in it.</summary>
    public List<RuleSetting> Effective() =>
        [.. Drafts.Where(r => _saved.ContainsKey(r.Id)).Select((r, i) =>
        {
            var rule = _saved[r.Id].Clone();
            rule.Order = i;
            return rule;
        })];

    /// <summary>
    /// Inserts a saved copy of a rule's saved version after it, never of its draft: copying an
    /// unsaved draft would save what the person hasn't (Karl's review). Null when the rule was never saved.
    /// </summary>
    public RuleSetting? Duplicate(RuleSetting draft)
    {
        if (!_saved.TryGetValue(draft.Id, out var saved))
            return null;
        var copy = saved.Clone();
        copy.Id = NewId();
        copy.Name = $"{saved.Name} copy";
        Drafts.Insert(Drafts.IndexOf(draft) + 1, copy);
        _saved[copy.Id] = copy.Clone();
        return copy;
    }

    /// <summary>Delete: the rule goes, saved or not.</summary>
    public void Remove(RuleSetting draft)
    {
        Drafts.Remove(draft);
        _saved.Remove(draft.Id);
        _errors.Remove(draft.Id);
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Rules that pass validation, in list order (stored Order values never invalidate one).</summary>
    public static IEnumerable<RuleSetting> Valid(IReadOnlyList<RuleSetting> rules)
    {
        var all = rules.Select((r, i) => r.ToRule() with { Order = i }).ToList();
        var bad = RuleValidator.Validate(all).Select(e => e.RuleId).ToHashSet(StringComparer.Ordinal);
        return rules.Where(r => !bad.Contains(r.Id) && (r.Action == RuleActionKind.Hold || RuleChecks.Template(r.Template ?? "", r.Action == RuleActionKind.Rename) is null));
    }

    private static bool SameRule(RuleSetting a, RuleSetting b) =>
        a.Name == b.Name
        && (a.Extensions ?? []).SequenceEqual(b.Extensions ?? []) && (a.Extensions is null) == (b.Extensions is null)
        && a.NameRegex == b.NameRegex && a.MinBytes == b.MinBytes && a.MaxBytes == b.MaxBytes
        && a.ModifiedAfter == b.ModifiedAfter && a.ModifiedBefore == b.ModifiedBefore
        && a.Action == b.Action && a.Template == b.Template && a.HoldReason == b.HoldReason;
}
