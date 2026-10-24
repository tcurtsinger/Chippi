// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Views.Settings;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Tests;

/// <summary>The rules editor itself, built on its own UI thread.</summary>
public sealed class RulesEditorTests
{
    [Fact]
    public void Rename_then_Move_then_Rename_saves_a_rename_rule()
    {
        RuleSetting? draft = null, saved = null;
        string? field = null;
        OnUiThread(() =>
        {
            var module = new DownloadsModule();
            module.Settings.Rules.Add(new RuleSetting { Id = "r", Name = "Dated", Extensions = ["txt"], Action = RuleActionKind.Rename, Template = "{year}-{month}-{day} {filename}" });
            var section = new RulesSection(module);
            draft = section.Drafts.Drafts[0];
            var editor = section.Editor(draft, new TextBlock());
            var action = Named<SegmentedControl>(editor, "Action");

            action.SelectedIndex = (int)RuleActionKind.Move;
            action.SelectedIndex = (int)RuleActionKind.Rename;

            field = Named<TextBox>(editor, "New name").Text;
            section.Drafts.Commit();
            saved = section.Drafts.Effective()[0];
        });

        Assert.Equal(RuleActionKind.Rename, draft!.Action);
        Assert.Equal(field, draft.Template);
        Assert.Equal(RuleActionKind.Rename, saved!.Action);
        Assert.Equal("{year}-{month}-{day} {filename}", saved.Template);
    }

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
