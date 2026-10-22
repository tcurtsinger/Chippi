// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Model;
using Chippi.Rules;

namespace Chippi.Features.Downloads.Views.Settings;

/// <summary>
/// The rules editor's model, kept free of WPF so it can be tested. <see cref="Drafts"/> are what is
/// on screen; each rule's committed version is its last one with every field valid, and only that
/// is saved and tested. A draft with a problem never broadens, drops or seeds a copy of a rule.
/// </summary>
internal sealed class RuleDrafts
{
    private readonly Dictionary<string, RuleSetting> _committed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _errors = new(StringComparer.Ordinal);

    public RuleDrafts(IEnumerable<RuleSetting> saved)
    {
        Drafts = [.. saved.OrderBy(r => r.Order).Select(r => r.Clone())];
        foreach (var rule in Drafts)
            _committed[rule.Id] = rule.Clone(); // as saved, even a hand-edited invalid one: opening the page never drops it
    }

    /// <summary>The rules on screen, in list order.</summary>
    public List<RuleSetting> Drafts { get; }

    public bool IsCommitted(string id) => _committed.ContainsKey(id);

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

    /// <summary>Every draft with no field problem that passes validation becomes its rule's committed version.</summary>
    public void Commit()
    {
        var valid = Valid(Drafts).Where(r => !_errors.TryGetValue(r.Id, out var fields) || fields.Count == 0).ToHashSet();
        foreach (var draft in Drafts)
        {
            if (valid.Contains(draft))
                _committed[draft.Id] = draft.Clone();
            else if (_committed.TryGetValue(draft.Id, out var saved))
                saved.Enabled = draft.Enabled; // on/off applies even while another field is being fixed
        }
    }

    /// <summary>The committed rules in the on-screen order: what is saved and what runs. A new rule that has never been valid is not in it.</summary>
    public List<RuleSetting> Effective() =>
        [.. Drafts.Where(r => _committed.ContainsKey(r.Id)).Select((r, i) =>
        {
            var rule = _committed[r.Id].Clone();
            rule.Order = i;
            return rule;
        })];

    /// <summary>
    /// Inserts a copy of a rule's committed version after it, never of a draft with a problem: a
    /// copy has no field problems of its own, so copying the draft would commit its unfinished,
    /// broader conditions (Karl's review). Null when the rule has never been valid.
    /// </summary>
    public RuleSetting? Duplicate(RuleSetting draft)
    {
        Commit();
        if (!_committed.TryGetValue(draft.Id, out var saved))
            return null;
        var copy = saved.Clone();
        copy.Id = NewId();
        copy.Name = $"{saved.Name} copy";
        Drafts.Insert(Drafts.IndexOf(draft) + 1, copy);
        return copy;
    }

    /// <summary>Only Delete removes a committed rule.</summary>
    public void Remove(RuleSetting draft)
    {
        Drafts.Remove(draft);
        _committed.Remove(draft.Id);
        _errors.Remove(draft.Id);
    }

    /// <summary>
    /// Before editors are rebuilt from drafts: a draft with a problem goes back to its committed
    /// version, so a rebuilt editor never hides a problem the draft still has.
    /// </summary>
    public void RevertProblems()
    {
        foreach (var (id, fields) in _errors.Where(e => e.Value.Count > 0).ToList())
        {
            int index = Drafts.FindIndex(r => r.Id == id);
            if (index >= 0 && _committed.TryGetValue(id, out var saved))
                Drafts[index] = saved.Clone();
            fields.Clear();
        }
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Rules that pass validation, in list order (stored Order values never invalidate one).</summary>
    public static IEnumerable<RuleSetting> Valid(IReadOnlyList<RuleSetting> rules)
    {
        var all = rules.Select((r, i) => r.ToRule() with { Order = i }).ToList();
        var bad = RuleValidator.Validate(all).Select(e => e.RuleId).ToHashSet(StringComparer.Ordinal);
        return rules.Where(r => !bad.Contains(r.Id) && (r.Action == RuleActionKind.Hold || RuleChecks.Template(r.Template ?? "", r.Action == RuleActionKind.Rename) is null));
    }
}
