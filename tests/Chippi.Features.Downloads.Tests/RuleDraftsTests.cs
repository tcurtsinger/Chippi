// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Views.Settings;

namespace Chippi.Features.Downloads.Tests;

/// <summary>The rules editor's model: a draft changes nothing until its Save (Karl's reviews of 06623d1 and fbf74ca, Travis's Save button).</summary>
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
    }

    [Fact]
    public void An_unchanged_rule_has_nothing_to_save()
    {
        var drafts = Drafts();

        Assert.False(drafts.HasUnsaved);
        Assert.False(drafts.CanSave("narrow"));
    }

    [Fact]
    public void A_draft_changes_nothing_until_it_is_saved()
    {
        var drafts = Drafts();
        drafts.Drafts[0].NameRegex = "^bill";

        Assert.Equal("^invoice", drafts.Effective()[0].NameRegex);
        Assert.True(drafts.IsChanged("narrow"));
        Assert.True(drafts.Save("narrow"));
        Assert.Equal("^bill", drafts.Effective()[0].NameRegex);
        Assert.False(drafts.HasUnsaved);
    }

    [Fact]
    public void A_field_with_a_problem_cant_be_saved()
    {
        var drafts = Drafts();
        ClearPattern(drafts);

        Assert.True(drafts.IsChanged("narrow"));
        Assert.False(drafts.Save("narrow"));
        Assert.Equal("^invoice", drafts.Effective().Single(r => r.Id == "narrow").NameRegex);
    }

    [Fact]
    public void An_invalid_destination_cant_be_saved()
    {
        var drafts = Drafts();
        drafts.Drafts[0].Template = "Bad?Folder";

        Assert.False(drafts.Save("narrow"));
        Assert.Equal("Invoices", drafts.Effective()[0].Template);
    }

    [Fact]
    public void Cancel_puts_a_saved_rule_back()
    {
        var drafts = Drafts();
        ClearPattern(drafts);

        drafts.Cancel("narrow");

        Assert.Equal("^invoice", drafts.Drafts[0].NameRegex);
        Assert.False(drafts.HasUnsaved);
    }

    [Fact]
    public void A_new_rule_is_not_saved_or_copied_until_its_Save_and_Cancel_removes_it()
    {
        var drafts = Drafts();
        var draft = new RuleSetting { Id = "new", Name = "New", Extensions = ["txt"], Action = RuleActionKind.Move, Template = "Text" };
        drafts.Drafts.Add(draft);

        Assert.DoesNotContain(drafts.Effective(), r => r.Id == "new");
        Assert.Null(drafts.Duplicate(draft));
        Assert.True(drafts.CanSave("new"));

        drafts.Cancel("new");
        Assert.DoesNotContain(drafts.Drafts, r => r.Id == "new");
    }

    [Fact]
    public void Duplicating_a_draft_copies_and_saves_the_saved_version()
    {
        var drafts = Drafts();
        ClearPattern(drafts);

        var copy = drafts.Duplicate(drafts.Drafts[0])!;

        Assert.Equal("^invoice", copy.NameRegex);
        Assert.Equal(["narrow", copy.Id, "fallback"], drafts.Effective().Select(r => r.Id));
        Assert.False(drafts.IsChanged(copy.Id));
    }

    [Fact]
    public void On_off_applies_at_once_while_the_draft_is_unsaved()
    {
        var drafts = Drafts();
        ClearPattern(drafts);
        drafts.SetEnabled(drafts.Drafts[0], false);

        var narrow = drafts.Effective().Single(r => r.Id == "narrow");
        Assert.False(narrow.Enabled);
        Assert.Equal("^invoice", narrow.NameRegex);
    }

    [Fact]
    public void Only_delete_removes_a_saved_rule()
    {
        var drafts = Drafts();
        drafts.Remove(drafts.Drafts[0]);

        Assert.Equal(["fallback"], drafts.Effective().Select(r => r.Id));
    }
}
