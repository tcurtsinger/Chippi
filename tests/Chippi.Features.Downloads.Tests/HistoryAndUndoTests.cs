// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;

namespace Chippi.Features.Downloads.Tests;

public sealed class HistoryAndUndoTests
{
    private static RuleSetting MovePdfs() => new() { Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Docs" };

    private static async Task<long> OrganizeOne(Fixture f, string name = "a.pdf", string content = "hello")
    {
        f.Write(name, content);
        var preview = await f.Engine.CreatePreviewAsync(f.Folder);
        var result = await f.Engine.ApproveAsync(preview.Id, [.. preview.Items.Select(i => i.Id)]);
        return f.History.RunOperations(result.RunId).Single().Id;
    }

    [Fact]
    public async Task Undo_puts_the_file_back_and_records_a_restore()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        long id = await OrganizeOne(f);

        var undo = await f.Engine.UndoAsync(id);

        Assert.True(undo.Succeeded);
        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.False(File.Exists(f.In("Docs", "a.pdf")));
        Assert.Equal(OperationState.Undone, f.History.Get(id)!.State);
        var restore = f.History.Get(undo.OperationId!.Value)!;
        Assert.Equal(HistoryOperationKind.Restore, restore.Kind);
        Assert.Equal(id, restore.UndoOf);
        Assert.False((await f.Engine.UndoAsync(id)).Succeeded);
    }

    [Fact]
    public async Task Undo_refuses_a_file_that_changed()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        long id = await OrganizeOne(f);
        File.AppendAllText(f.In("Docs", "a.pdf"), "edited");

        var undo = await f.Engine.UndoAsync(id);

        Assert.False(undo.Succeeded);
        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
        Assert.Equal(OperationState.Completed, f.History.Get(id)!.State);
    }

    [Fact]
    public async Task Undo_never_overwrites_the_original_name()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        long id = await OrganizeOne(f, content: "organized");
        f.Write("a.pdf", "a new one");

        var undo = await f.Engine.UndoAsync(id);

        Assert.False(undo.Succeeded);
        Assert.Equal("a new one", File.ReadAllText(f.In("a.pdf")));
        Assert.Equal("organized", File.ReadAllText(f.In("Docs", "a.pdf")));
    }

    [Fact]
    public async Task A_restored_file_is_not_organized_again_in_silent()
    {
        using var f = new Fixture();
        f.AddRule(MovePdfs());
        f.Write("a.pdf");
        f.Settle();
        long id = f.History.Query(new HistoryQuery()).Single().Id;

        Assert.True((await f.Engine.UndoAsync(id)).Succeeded);
        f.Settle();
        f.Restart();
        f.Settle();

        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.False(File.Exists(f.In("Docs", "a.pdf")));
    }

    [Fact]
    public async Task Undo_run_restores_every_file_of_the_run()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        f.Write("a.pdf");
        f.Write("b.pdf");
        var preview = await f.Engine.CreatePreviewAsync(f.Folder);
        var result = await f.Engine.ApproveAsync(preview.Id, [.. preview.Items.Select(i => i.Id)]);

        var undos = await f.Engine.UndoRunAsync(result.RunId);

        Assert.Equal(2, undos.Count(u => u.Succeeded));
        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.True(File.Exists(f.In("b.pdf")));
    }

    [Fact]
    public void Reconcile_settles_interrupted_operations_by_where_the_file_is()
    {
        using var f = new Fixture(FolderMode.Paused);
        long run = f.History.BeginRun(RunTrigger.Silent, f.Folder);

        // Interrupted after the move: the file is at the destination.
        Directory.CreateDirectory(f.In("Docs"));
        File.WriteAllText(f.In("Docs", "moved.pdf"), "x");
        long moved = f.History.Plan(run, HistoryOperationKind.Move, f.In("moved.pdf"), f.In("Docs", "moved.pdf"), null, null, 1);
        f.History.MarkStarted(moved);

        // Interrupted before it: the file is still at the source, with a half-written copy, next
        // to someone else's file that merely ends in the suffix.
        f.Write("stayed.pdf");
        long stayed = f.History.Plan(run, HistoryOperationKind.Move, f.In("stayed.pdf"), f.In("Docs", "stayed.pdf"), null, null, 5);
        f.History.MarkStarted(stayed);
        string ours = Executor.PartialPath(f.In("Docs", "stayed.pdf"), stayed);
        File.WriteAllText(ours, "he");
        f.History.MarkPartial(stayed, ours);
        string theirs = f.In("Docs", "stayed.pdf.chippi-partial");
        File.WriteAllText(theirs, "not Chippi's");

        f.Restart();

        Assert.Equal(OperationState.Completed, f.History.Get(moved)!.State);
        Assert.NotNull(f.History.Get(moved)!.Sha256);
        Assert.Equal(OperationState.Failed, f.History.Get(stayed)!.State);
        Assert.False(File.Exists(ours));
        Assert.Equal("not Chippi's", File.ReadAllText(theirs));
        Assert.Empty(f.History.Incomplete());
    }

    [Fact]
    public void Pruning_keeps_the_newest_output_per_destination()
    {
        using var f = new Fixture(FolderMode.Paused);
        long run = f.History.BeginRun(RunTrigger.Silent, f.Folder);
        long older = f.History.Plan(run, HistoryOperationKind.Rename, f.In("a.txt"), f.In("b.txt"), null, null, 1);
        f.History.MarkCompleted(older, f.In("b.txt"), "AA", f.Time.GetUtcNow());
        long newer = f.History.Plan(run, HistoryOperationKind.Rename, f.In("c.txt"), f.In("b.txt"), null, null, 1);
        f.History.MarkCompleted(newer, f.In("b.txt"), "BB", f.Time.GetUtcNow());
        long failed = f.History.Plan(run, HistoryOperationKind.Move, f.In("d.txt"), f.In("e.txt"), null, null, 1);
        f.History.MarkFailed(failed, "no");

        int removed = f.History.ClearBefore(f.Time.GetUtcNow().AddDays(1));

        Assert.Equal(2, removed);
        Assert.Null(f.History.Get(older));
        Assert.Null(f.History.Get(failed));
        Assert.NotNull(f.History.Get(newer));
        Assert.Equal(newer, f.History.LatestOutputAt(f.In("B.TXT"))!.Id);
    }

    [Fact]
    public void History_query_filters_by_text_and_state()
    {
        using var f = new Fixture(FolderMode.Paused);
        long run = f.History.BeginRun(RunTrigger.Preview, f.Folder);
        long a = f.History.Plan(run, HistoryOperationKind.Move, f.In("100%_done.pdf"), f.In("Docs", "100%_done.pdf"), "r", "Docs", 1);
        f.History.MarkCompleted(a, f.In("Docs", "100%_done.pdf"), "AA", f.Time.GetUtcNow());
        long b = f.History.Plan(run, HistoryOperationKind.Move, f.In("100x done.pdf"), f.In("Docs", "100x done.pdf"), "r", "Docs", 1);
        f.History.MarkFailed(b, "no");

        Assert.Equal(a, Assert.Single(f.History.Query(new HistoryQuery { Text = "100%_" })).Id);
        Assert.Equal(b, Assert.Single(f.History.Query(new HistoryQuery { State = OperationState.Failed })).Id);
        Assert.Equal(2, f.History.Query(new HistoryQuery { Folder = f.Folder.ToUpperInvariant() }).Count);
    }
}
