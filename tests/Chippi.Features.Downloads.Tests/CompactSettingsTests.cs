// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Storage;
using Chippi.Features.Downloads.Views.Settings;
using Chippi.Island.Tests;
using Chippi.Rules;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Tests;

public sealed class CompactSettingsTests
{
    [Fact]
    public void Unmatched_files_fold_and_Add_rule_opens_a_draft_without_approval() => WpfTestThread.Run(() =>
    {
        var at = DateTimeOffset.UtcNow;
        var plan = new PreviewPlan(Guid.NewGuid(), @"C:\Fake\Downloads", RunTrigger.Preview, at, at.AddMinutes(5),
            [.. Enumerable.Range(1, 19).Select(i => new PreviewItem(i, "file" + i, "file" + i, 1, at, OperationKind.NoMatch, null, null, null, [], null))]);
        bool add = false;
        var view = new PreviewView(new DownloadsModule(), () => Task.FromResult(plan), false, () => { }, () => add = true);
        typeof(PreviewView).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, plan);
        typeof(PreviewView).GetMethod("RenderPlan", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
        var fold = Find<ExpanderRow>(view).Single();
        Assert.Equal("19 files \u00b7 no rule matches", fold.Header);
        Assert.False(fold.IsExpanded);
        Assert.Equal(19, Find<TextBlock>((DependencyObject)fold.Content).Count(t => t.Text.StartsWith("file")));
        Assert.Contains(Find<TextBlock>(view), t => t.Text == "Nothing to organize");
        Assert.DoesNotContain(Find<Button>(view), b => Equals(b.Content, "Approve"));
        var action = Find<Button>(view).Single(b => AutomationProperties.GetName(b) == "Add rule");
        Assert.True(action.IsEnabled);
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(add);
    });

    [Fact]
    public void A_folder_header_organizes_that_folder_without_changing_its_mode() => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-folder-row-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var module = new DownloadsModule();
            module.Settings.Folders.Add(new Chippi.Features.Downloads.Model.WatchedFolder { Path = folder, Mode = Chippi.Features.Downloads.Model.FolderMode.Manual });
            string? requested = null;
            var section = new SettingsSection(module, _ => { }, path => requested = path);
            var row = Find<ExpanderRow>(section).Single();
            var organize = Find<Button>((DependencyObject)row.Value!).Single();
            organize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(folder, requested);
            Assert.Equal(Chippi.Features.Downloads.Model.FolderMode.Manual, module.Settings.Folders.Single().Mode);
            Assert.False(row.IsExpanded);
        }
        finally { Directory.Delete(folder); }
    });

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child)) yield return nested;
    }
}
