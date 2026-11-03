// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Chippi.Features.Downloads.Views.Settings;
using Chippi.Features.Downloads.Views.Tidy;
using Chippi.Island.Tests;
using Chippi.Rules;
using Chippi.UI.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Downloads.Tests;

public sealed class OrganizerHistoryPreviewTests
{
    [Theory]
    [InlineData(HistoryOperationKind.Move, RunTrigger.Preview, TidyRowAction.Retry)]
    [InlineData(HistoryOperationKind.Rename, RunTrigger.Preview, TidyRowAction.Retry)]
    [InlineData(HistoryOperationKind.Move, RunTrigger.ArchiveImport, TidyRowAction.None)]
    [InlineData(HistoryOperationKind.Rename, RunTrigger.ArchiveImport, TidyRowAction.None)]
    public void Recent_failure_offers_retry_only_when_a_folder_preview_can_recover_the_file(
        HistoryOperationKind kind, RunTrigger trigger, TidyRowAction action)
    {
        using var fixture = new Fixture(FolderMode.Paused);
        long run = fixture.History.BeginRun(trigger, fixture.Folder);
        long operation = fixture.History.Plan(run, kind, fixture.In("source.pdf"), fixture.In("target.pdf"), null, null, 4);
        fixture.History.MarkFailed(operation, "Access denied");
        fixture.History.EndRun(run);

        var history = fixture.History.Query(new HistoryQuery { Folder = fixture.Folder });
        var row = Assert.Single(TidyProjection.Rows(fixture.Folder, [], history, fixture.Time.GetUtcNow()));
        Assert.True(row.IsFailed);
        Assert.Equal(action, row.Action);
    }

    [Theory]
    [InlineData("Verify the sender", "Held · verify the sender")]
    [InlineData("Check before opening", "Held")]
    public void A_held_only_preview_explains_the_hold_and_finishes_without_adding_a_rule(string reason, string label) => WpfTestThread.Run(() =>
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new PreviewPlan(Guid.NewGuid(), @"C:\Fixture", RunTrigger.Preview, now, now.AddMinutes(5),
            [new PreviewItem(1, @"C:\Fixture\file.torrent", "file.torrent", 27, now, OperationKind.Hold, null, "rule", "Hold torrents", [], reason)]);
        bool done = false;
        var view = new PreviewView(new DownloadsModule(), () => Task.FromResult(plan), true, () => done = true, () => throw new InvalidOperationException());
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.Contains(Find<TextBlock>(view), t => t.Text == "1 file held");
        Assert.Contains(Find<TextBlock>(view), t => t.Text == label);
        Assert.DoesNotContain(Find<Button>(view), b => AutomationProperties.GetName(b) is "Approve" or "Add rule");
        Click(view, "Done");
        Assert.True(done);
    });

    [Fact]
    public void Result_shows_effective_filenames_and_only_exposes_undo_for_restorable_files() => WpfTestThread.Run(() =>
    {
        var now = DateTimeOffset.UtcNow;
        var rename = new PreviewItem(1, @"C:\Fixture\old.txt", "old.txt", 10, now, OperationKind.Rename, @"C:\Fixture\new.txt", "rename", "Rename text", [], null);
        var import = new PreviewItem(2, @"C:\Staging\keep.pdf", "keep.pdf", 22, now, OperationKind.Move, @"C:\Fixture\keep.pdf", null, null, [], null)
        {
            ImportedAs = @"C:\Fixture\keep.pdf",
        };
        var move = import with { Id = 3, Name = "moved.pdf", Source = @"C:\Staging\moved.pdf", Destination = @"C:\Fixture\Docs\moved.pdf", ImportedAs = @"C:\Fixture\moved.pdf" };
        var failedImport = import with { Id = 4, Name = "locked.pdf", Source = @"C:\Staging\locked.pdf", Destination = @"C:\Fixture\locked.pdf", ImportedAs = @"C:\Fixture\locked.pdf" };
        var plan = new PreviewPlan(Guid.NewGuid(), @"C:\Fixture", RunTrigger.ArchiveImport, now, now.AddMinutes(5), [rename, import, move, failedImport]);
        var view = new PreviewView(new DownloadsModule(), () => Task.FromResult(plan), false, () => { });
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var result = new ApplyResult(1, [.. plan.Items.Select(i => new ItemResult(i.Id, i.Source, i.Destination, i != failedImport, i == failedImport ? "File in use" : null) { OperationId = i.Id })]);
        typeof(PreviewView).GetMethod("RenderResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [result]);
        var labels = Find<TextBlock>(view).Select(t => t.Text).ToList();
        Assert.Contains("new.txt", labels);
        Assert.Contains("From old.txt", labels);
        Assert.Contains("Import failed · file in use", labels);
        Assert.Contains(labels, t => t.Contains("1 moved") && t.Contains("1 renamed") && t.Contains("1 imported"));
        var actions = Find<IconButton>(view).Select(AutomationProperties.GetName).ToList();
        Assert.Contains("Undo new.txt", actions);
        Assert.Contains("Undo moved.pdf", actions);
        Assert.DoesNotContain("Undo keep.pdf", actions);
    });

    [Fact]
    public void Filtered_history_keeps_the_full_run_undo_count_and_the_global_clear_scope() => WpfTestThread.Run(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), $"chippi-history-polish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var config = new ConfigStore(Path.Combine(root, "settings.json"));
        config.SetModuleSettings("downloads-organizer", new DownloadsSettings
        {
            Initialized = true, OrganizeInExplorer = false, Folders = [new WatchedFolder { Path = root, Mode = FolderMode.Paused }],
        });
        var module = new DownloadsModule();
        var registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance, new ModuleServices { DataRoot = Path.Combine(root, "data") });
        registry.Register(module);
        try
        {
            registry.StartAsync().GetAwaiter().GetResult();
            var history = module.Engine!.History;
            long id = history.BeginRun(RunTrigger.Preview, root);
            foreach (var (kind, from, to) in new[] { (HistoryOperationKind.Move, "find.pdf", "Docs/find.pdf"), (HistoryOperationKind.Rename, "old.txt", "new.txt") })
            {
                var destination = Path.Combine(root, to);
                long row = history.Plan(id, kind, Path.Combine(root, from), destination, null, null, 1);
                history.MarkCompleted(row, destination, "hash", DateTimeOffset.UtcNow);
            }
            long failedImport = history.Plan(id, HistoryOperationKind.Import, Path.Combine(root, "failed.pdf"), Path.Combine(root, "failed.pdf"), null, null, 1);
            history.MarkFailed(failedImport, "File in use");
            history.EndRun(id);
            var view = new HistorySection(module, id);
            Assert.Contains(Find<TextBlock>(view), t => t.Text == "Import failed · file in use");
            var failedRow = TidyProjection.Rows(root, [], history.RunOperations(id), DateTimeOffset.UtcNow).Single(row => row.IsFailed);
            Assert.Equal("Import failed · File in use", failedRow.Detail);
            Assert.False(failedRow.HasAction);
            var search = Find<SearchBox>(view).Single();
            var folder = Find<ComboBox>(view).Single();
            search.Text = "find.pdf";
            folder.SelectedIndex = 1; // Selecting the watched folder applies both filters immediately.
            var captions = Find<TextBlock>(view).Select(t => new System.Windows.Documents.TextRange(t.ContentStart, t.ContentEnd).Text);
            Assert.Contains(captions, text => text.Contains("1 moved · 1 renamed"));
            Click(view, "Undo run");
            Assert.Contains(Find<TextBlock>(view), t => t.Text == "Undo 2 changes?");
            search.Text = "unmatched query";
            folder.SelectedIndex = 0;
            Assert.Contains(Find<TextBlock>(view), t => t.Text == "No matching runs");
            var clear = Find<Button>(view).Single(b => Equals(b.Content, "Clear all history"));
            Assert.Contains("undone", AutomationProperties.GetHelpText(clear));
            Assert.Equal(AutomationProperties.GetHelpText(clear), clear.ToolTip);
            // Physical cleanup may fail after the logical cutoff has already been saved.
            using var database = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(root, "data", "downloads-organizer", "history.db"), Pooling = false,
            }.ToString());
            database.Open();
            using var trigger = database.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER fail_prune BEFORE DELETE ON operations BEGIN SELECT RAISE(ABORT, 'cleanup unavailable'); END;";
            trigger.ExecuteNonQuery();
            typeof(HistorySection).GetMethod("Clear", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
            Assert.NotNull(module.Settings.HistoryClearedUtc);
            Assert.Empty(history.Query(new HistoryQuery { From = module.Settings.HistoryClearedUtc }));
            Assert.NotEmpty(history.RunOperations(id));
            Assert.Contains(Find<TextBlock>(view), t => t.Text.StartsWith("History cleared · storage cleanup failed", StringComparison.Ordinal));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally
        {
            registry.StopAllAsync().GetAwaiter().GetResult();
            Directory.Delete(root, recursive: true);
        }
    });

    private static void Click(DependencyObject root, string name) =>
        Find<Button>(root).Single(b => AutomationProperties.GetName(b) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Find<T>(child)) yield return item;
    }
}
