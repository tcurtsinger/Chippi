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
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19)]
    public void Unmatched_files_fold_and_Add_rule_opens_a_draft_without_approval(int count) => WpfTestThread.Run(() =>
    {
        var at = DateTimeOffset.UtcNow;
        var plan = new PreviewPlan(Guid.NewGuid(), @"C:\Fake\Downloads", RunTrigger.Preview, at, at.AddMinutes(5),
            [.. Enumerable.Range(1, count).Select(i => new PreviewItem(i, "file" + i, "file" + i, 1, at, OperationKind.NoMatch, null, null, null, [], null))]);
        bool add = false;
        var view = new PreviewView(new DownloadsModule(), () => Task.FromResult(plan), false, () => { }, () => add = true);
        typeof(PreviewView).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, plan);
        typeof(PreviewView).GetMethod("RenderPlan", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
        if (count == 0)
            Assert.Empty(Find<ExpanderRow>(view));
        else
        {
            var fold = Find<ExpanderRow>(view).Single();
            Assert.Equal($"{count} {(count == 1 ? "file" : "files")} \u00b7 no rule matches", fold.Header);
            Assert.False(fold.IsExpanded);
            Assert.Equal(count, Find<TextBlock>((DependencyObject)fold.Content).Count(t => t.Text.StartsWith("file")));
        }
        Assert.Single(Find<TextBlock>(view), t => t.Text == "Nothing to organize"); // the footer only (c4)
        Assert.DoesNotContain(Find<Button>(view), b => Equals(b.Content, "Approve"));
        var action = Find<Button>(view).Single(b => AutomationProperties.GetName(b) == "Add rule");
        Assert.True(action.IsEnabled);
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(add);
    });

    /// <summary>
    /// Critique 2026-10-31: 118 flat rows put the held files around row 45 and Approve 6,000px down. Files
    /// that need a look come first, open; each destination is a group with its own box, closed when long.
    /// </summary>
    [Fact]
    public void Preview_groups_by_destination_with_files_that_need_a_look_first() => WpfTestThread.Run(() =>
    {
        var at = DateTimeOffset.UtcNow;
        const string folder = @"C:\Fake\Downloads";
        var items = new List<PreviewItem>();
        PreviewItem Move(string name, string to) => new(items.Count + 1, Path.Combine(folder, name), name, 1024, at, OperationKind.Move, Path.Combine(folder, to, name), "r", "Rule", [], null);
        for (int i = 0; i < 9; i++) items.Add(Move($"photo{i}.jpg", "Pictures"));
        for (int i = 0; i < 6; i++) items.Add(Move($"doc{i}.pdf", "Documents"));
        items.Add(new PreviewItem(items.Count + 1, Path.Combine(folder, "a.torrent"), "a.torrent", 10, at, OperationKind.Hold, null, "t", "Torrents", [], "Check before opening"));
        items.Add(new PreviewItem(items.Count + 1, Path.Combine(folder, "setup.pdf.exe"), "setup.pdf.exe", 10, at, OperationKind.Move, Path.Combine(folder, "Installers", "setup.pdf.exe"), "r", "Rule",
            [new PlanWarning(WarningKind.MisleadingName, "Looks like a .pdf · is an app")], null));
        var plan = new PreviewPlan(Guid.NewGuid(), folder, RunTrigger.Preview, at, at.AddMinutes(5), items);
        var view = new PreviewView(new DownloadsModule(), () => Task.FromResult(plan), false, () => { }) { ShowFirst = "photo3.jpg" };
        typeof(PreviewView).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, plan);
        typeof(PreviewView).GetMethod("RenderPlan", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);

        var boxes = Find<CheckBox>(view).Select(AutomationProperties.GetName).Where(n => n?.StartsWith("Include ", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(["Include Needs a look", "Include a.torrent", "Include setup.pdf.exe", "Include Documents"], boxes.Take(4));
        Assert.Contains("Include Pictures", boxes);
        Assert.Single(Find<TextBlock>(view), t => t.Text == "Held · looks like a .pdf · is an app");
        Assert.Single(Find<TextBlock>(view), t => t.Text == "Rule → Installers"); // checkable in Needs a look, so it says where it goes
        Assert.Single(Find<TextBlock>(view), t => t.Text == "Torrents"); // held: it goes nowhere
        Assert.Contains(Find<IconButton>(view), b => AutomationProperties.GetName(b) == "Show setup.pdf.exe in folder");

        // 15 files to approve: Documents starts closed; Pictures holds the file Review asked for, so it's open.
        var expanders = Find<IconButton>(view).Select(AutomationProperties.GetName).ToList();
        Assert.Contains("Show 6 files", expanders);
        Assert.Equal(2, expanders.Count(n => n == "Hide files")); // Needs a look and Pictures

        // A group's box selects its files; the misleading name starts out.
        Assert.Single(Find<TextBlock>(view), t => t.Text == "15 of 16 selected · 15 KB");
        var documents = Find<CheckBox>(view).Single(b => AutomationProperties.GetName(b) == "Include Documents");
        documents.IsChecked = false;
        Assert.Single(Find<TextBlock>(view), t => t.Text == "9 of 16 selected · 9 KB");
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
