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
    private static RulesSection Section(out DownloadsModule module)
    {
        module = new DownloadsModule();
        module.Settings.Rules.Add(new RuleSetting { Id = "r", Name = "Dated", Extensions = ["txt"], Action = RuleActionKind.Rename, Template = "{year}-{month}-{day} {filename}" });
        return new RulesSection(module);
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
            var editor = section.Editor(draft);
            var action = Named<SegmentedControl>(editor, "Action");

            action.SelectedIndex = (int)RuleActionKind.Move;
            action.SelectedIndex = (int)RuleActionKind.Rename;

            field = Named<TextBox>(editor, "New name").Text;
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
