// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Views.Settings;

namespace Chippi.Features.Downloads.Tests;

/// <summary>The rules editor's model: a draft with a problem never changes what is saved (Karl's reviews of 06623d1 and fbf74ca).</summary>
public sealed class RuleDraftsTests
{
    private static RuleDrafts Drafts() => new(
    [
        new RuleSetting { Id = "narrow", Name = "Narrow", Order = 0, Extensions = ["pdf"], NameRegex = "^invoice", Action = RuleActionKind.Move, Template = "Invoices" },
        new RuleSetting { Id = "fallback", Name = "Fallback", Order = 1, Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Other" },
    ]);

    /// <summary>What the editor does when Name matches stays on and its pattern is cleared.</summary>
    private static void ClearPattern(RuleDrafts drafts)
    {
        drafts.Drafts[0].NameRegex = null;
        drafts.SetError("narrow", "regex", problem: true);
        drafts.Commit();
    }

    [Fact]
    public void A_cleared_pattern_keeps_the_saved_rule_narrow()
    {
        var drafts = Drafts();
        ClearPattern(drafts);

        Assert.Equal("^invoice", drafts.Effective().Single(r => r.Id == "narrow").NameRegex);
    }

    [Fact]
    public void Duplicating_a_draft_with_a_problem_copies_the_saved_version()
    {
        var drafts = Drafts();
        ClearPattern(drafts);

        var copy = drafts.Duplicate(drafts.Drafts[0])!;
        drafts.Commit();

        Assert.Equal("^invoice", copy.NameRegex);
        Assert.Equal(["narrow", copy.Id, "fallback"], drafts.Effective().Select(r => r.Id));
        Assert.All(drafts.Effective().Where(r => r.Id != "fallback"), r => Assert.Equal("^invoice", r.NameRegex));
    }

    [Fact]
    public void An_invalid_destination_keeps_the_rule_in_place()
    {
        var drafts = Drafts();
        drafts.Drafts[0].Template = "Bad?Folder";
        drafts.Commit();

        var saved = drafts.Effective();
        Assert.Equal(["narrow", "fallback"], saved.Select(r => r.Id));
        Assert.Equal("Invoices", saved[0].Template);
    }

    [Fact]
    public void On_off_applies_while_another_field_has_a_problem()
    {
        var drafts = Drafts();
        ClearPattern(drafts);
        drafts.Drafts[0].Enabled = false;
        drafts.Commit();

        var narrow = drafts.Effective().Single(r => r.Id == "narrow");
        Assert.False(narrow.Enabled);
        Assert.Equal("^invoice", narrow.NameRegex);
    }

    [Fact]
    public void Rebuilding_editors_reverts_a_draft_with_a_problem()
    {
        var drafts = Drafts();
        ClearPattern(drafts);

        drafts.RevertProblems();
        drafts.Commit();

        Assert.Equal("^invoice", drafts.Drafts[0].NameRegex);
        Assert.Equal("^invoice", drafts.Effective()[0].NameRegex);
    }

    [Fact]
    public void A_new_rule_never_valid_is_not_saved_or_copied()
    {
        var drafts = Drafts();
        var draft = new RuleSetting { Id = "new", Name = "New", Action = RuleActionKind.Move, Template = "" };
        drafts.Drafts.Add(draft);
        drafts.Commit();

        Assert.DoesNotContain(drafts.Effective(), r => r.Id == "new");
        Assert.Null(drafts.Duplicate(draft));
    }

    [Fact]
    public void Only_delete_removes_a_saved_rule()
    {
        var drafts = Drafts();
        drafts.Remove(drafts.Drafts[0]);

        Assert.Equal(["fallback"], drafts.Effective().Select(r => r.Id));
    }
}
