// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Views.Settings;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Tests;

/// <summary>The rules editor itself, built on its own UI thread.</summary>
public sealed class RulesEditorTests
{
    private static RulesSection Section(out DownloadsModule module, Action<DownloadsSettings>? persist = null)
    {
        module = new DownloadsModule();
        module.Settings.Rules.Add(new RuleSetting { Id = "r", Name = "Dated", Extensions = ["txt"], Action = RuleActionKind.Rename, Template = "{year}-{month}-{day} {filename}" });
        return new RulesSection(module, persist ?? (_ => { }));
    }

    [Fact]
    public void Rename_then_Move_then_Rename_saves_a_rename_rule()
    {
        RuleSetting? draft = null, saved = null;
        string? field = null;
        OnUiThread(() =>
        {
            var section = Section(out _);
            draft = section.Drafts.Drafts[0];
            Click(section, "Edit Dated");
            var action = Named<SegmentedControl>(section, "Action");

            action.SelectedIndex = (int)RuleActionKind.Move;
            action.SelectedIndex = (int)RuleActionKind.Rename;

            field = Named<TextBox>(section, "New name").Text;
            section.Drafts.Save(draft.Id);
            saved = section.Drafts.Effective()[0];
        });

        Assert.Equal(RuleActionKind.Rename, draft!.Action);
        Assert.Equal(field, draft.Template);
        Assert.Equal(RuleActionKind.Rename, saved!.Action);
        Assert.Equal("{year}-{month}-{day} {filename}", saved.Template);
    }

    [Fact]
    public void Edits_wait_for_Save_and_Cancel_drops_them()
    {
        bool saveBefore = true, saveAfter = false;
        string? savedName = null, afterCancel = null, afterSave = null;
        OnUiThread(() =>
        {
            var section = Section(out _);
            Click(section, "Edit Dated");
            saveBefore = Named<Button>(section, "Save Dated").IsEnabled;

            Named<TextBox>(section, "Rule name").Text = "Stamped";
            saveAfter = Named<Button>(section, "Save Dated").IsEnabled;
            savedName = section.Drafts.Effective()[0].Name;
            Click(section, "Cancel changes to Dated");
            afterCancel = section.Drafts.Drafts[0].Name;

            Click(section, "Edit Dated");
            Named<TextBox>(section, "Rule name").Text = "Stamped";
            Click(section, "Save Dated");
            afterSave = section.Drafts.Effective()[0].Name;
        });

        Assert.False(saveBefore);
        Assert.True(saveAfter);
        Assert.Equal("Dated", savedName);
        Assert.Equal("Dated", afterCancel);
        Assert.Equal("Stamped", afterSave);
    }

    [Fact]
    public void A_collapsed_draft_is_kept_and_leaving_asks_first()
    {
        bool unsavedBadge = false, leftAtOnce = true, left = false;
        int savedRules = -1, draftsAfterDiscard = -1;
        OnUiThread(() =>
        {
            var section = Section(out _);
            Click(section, "Add rule");
            Click(section, "Close New rule");
            unsavedBadge = Descendants(section).OfType<Badge>().Any(b => b.Text == "Unsaved changes");
            savedRules = section.Drafts.Effective().Count;

            leftAtOnce = section.ConfirmLeave(() => left = true);
            Click(section, "Discard unsaved rules");
            draftsAfterDiscard = section.Drafts.Drafts.Count;
        });

        Assert.True(unsavedBadge);
        Assert.Equal(1, savedRules);
        Assert.False(leftAtOnce);
        Assert.True(left);
        Assert.Equal(1, draftsAfterDiscard);
    }

    [Fact]
    public void Saving_and_enabling_describe_existing_files_in_their_watched_folders() => OnUiThread(() =>
    {
        var module = new DownloadsModule();
        module.Settings.Folders.AddRange([
            new() { Path = @"C:\Watch\Incoming", Mode = FolderMode.Silent },
            new() { Path = @"C:\Watch\Receipts", Mode = FolderMode.Manual },
            new() { Path = @"C:\Watch\Archive", Mode = FolderMode.Paused },
        ]);
        module.Settings.Rules.Add(new RuleSetting { Id = "pdf", Name = "File PDFs", Enabled = false,
            Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Documents" });
        var section = new RulesSection(module, _ => { });
        const string enabling = "Enabling can move existing matches in Incoming. Existing matches in Receipts wait for review.";
        Assert.Contains(Descendants(section).OfType<TextBlock>(), t => t.Text == enabling && t.Visibility == Visibility.Visible);
        Assert.Equal(enabling, AutomationProperties.GetHelpText(Named<ToggleButton>(section, "File PDFs on")));
        Click(section, "Edit File PDFs");
        var toggle = Named<ToggleButton>(section, "File PDFs on");
        toggle.IsChecked = true;
        Named<SegmentedControl>(section, "Action").SelectedIndex = (int)RuleActionKind.Rename;
        var save = Named<Button>(section, "Save File PDFs");
        Assert.True(save.IsEnabled);
        Assert.Equal("Saving can rename existing matches in Incoming. Existing matches in Receipts wait for review.", AutomationProperties.GetHelpText(save));
        toggle.IsChecked = false;
        Assert.Empty(AutomationProperties.GetHelpText(save));
        Assert.Equal(enabling, AutomationProperties.GetHelpText(toggle)); // enabling uses the saved Move, not the draft Rename
    });

    [Theory]
    [InlineData(false, "Saving")]
    [InlineData(true, "Enabling")]
    public void Scope_shows_full_paths_only_for_ambiguous_folder_names(bool enabling, string action)
    {
        var rule = new RuleSetting { Action = RuleActionKind.Move };
        var scope = RuleChecks.ExistingScope(rule, [
            new() { Path = @"C:\Work\Incoming", Mode = FolderMode.Silent },
            new() { Path = @"D:\Personal\incoming", Mode = FolderMode.Manual },
            new() { Path = @"C:\Work\Receipts", Mode = FolderMode.Manual },
        ], enabling);
        Assert.Equal($@"{action} can move existing matches in C:\Work\Incoming. Existing matches in D:\Personal\incoming, Receipts wait for review.", scope);
    }

    [Theory]
    [InlineData("priority.pdf", "Priority → Express", true)]
    [InlineData("invoice.pdf", "File size needed · Large PDFs", false)]
    [InlineData("notes.txt", "Modified date needed · Old notes", false)]
    [InlineData("cover.png", "No rule matches", false)]
    [InlineData("notes.pdf.exe", "Held", false)]
    public void A_filename_test_only_claims_a_match_when_the_name_decides_it(string name, string outcome, bool definite) => OnUiThread(() =>
    {
        var module = new DownloadsModule();
        module.Settings.Folders.Add(new() { Path = @"C:\Watch\Incoming", Mode = FolderMode.Manual });
        module.Settings.Rules.AddRange([
            new() { Id = "off", Name = "Off", Enabled = false, Order = 0, MinBytes = 512, Action = RuleActionKind.Move, Template = "Unused" },
            new() { Id = "first", Name = "Priority", Order = 1, NameRegex = "^priority", Action = RuleActionKind.Move, Template = "Express" },
            new() { Id = "size", Name = "Large PDFs", Order = 2, Extensions = ["pdf"], MinBytes = 2048, Action = RuleActionKind.Move, Template = "Large" },
            new() { Id = "fallback", Name = "Other PDFs", Order = 3, Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Other" },
            new() { Id = "date", Name = "Old notes", Order = 4, Extensions = ["txt"], ModifiedBefore = new DateOnly(2020, 1, 1), Action = RuleActionKind.Move, Template = "Old" },
        ]);
        var section = new RulesSection(module);
        Named<TextBox>(section, "Test a file name").Text = name;
        Assert.Contains(Descendants(section).OfType<TextBlock>(), t => t.Text.StartsWith(outcome, StringComparison.Ordinal));
        Assert.Equal(definite, Descendants(section).OfType<Badge>().Any(b => b.Text == "Match"));
    });

    [Fact]
    public void A_failed_write_restores_the_switch_and_keeps_the_draft_for_retry() => OnUiThread(() =>
    {
        bool reject = true;
        DownloadsSettings? written = null;
        var section = Section(out _, settings =>
        {
            if (reject) throw new UnauthorizedAccessException();
            written = settings;
        });
        var toggle = Named<ToggleButton>(section, "Dated on");
        toggle.IsChecked = false;
        Assert.True(toggle.IsChecked);
        Assert.True(section.Drafts.Effective()[0].Enabled);
        Click(section, "Edit Dated");
        var name = Named<TextBox>(section, "Rule name");
        name.Text = "Edited";
        Click(section, "Duplicate Dated");
        Click(section, "Delete Dated");
        Assert.Single(section.Drafts.Drafts);
        Click(section, "Save Dated");
        Assert.Same(name, Named<TextBox>(section, "Rule name"));
        Assert.Equal("Dated", section.Drafts.Effective()[0].Name);
        Assert.True(section.Drafts.HasUnsaved);
        Assert.Null(written);
        Assert.Contains(Descendants(section).OfType<TextBlock>(), t => t.Text == "Save failed · access denied" && t.Visibility == Visibility.Visible);
        reject = false;
        Click(section, "Save Dated");
        Assert.Equal("Edited", written!.Rules[0].Name);
        Assert.False(section.Drafts.HasUnsaved);
    });

    private static void Click(DependencyObject root, string name) =>
        Named<ButtonBase>(root, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().First(e => AutomationProperties.GetName(e) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child))
                yield return item;
    }

    private static void OnUiThread(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The UI thread failed.", failure);
    }
}
