// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Chippi.Rules;

namespace Chippi.Features.Downloads.Tests;

public sealed class PipelineTests
{
    private static RuleSetting MovePdfs() => new() { Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Docs" };

    [Fact]
    public void Silent_moves_a_new_file_once_it_is_ready()
    {
        using var f = new Fixture();
        f.AddRule(MovePdfs());
        f.Write("report.pdf");

        f.Settle();

        Assert.True(File.Exists(f.In("Docs", "report.pdf")));
        Assert.False(File.Exists(f.In("report.pdf")));
        var op = Assert.Single(f.History.Query(new HistoryQuery()));
        Assert.Equal(OperationState.Completed, op.State);
        Assert.NotNull(op.Sha256);
        Assert.Contains(f.Notifier.Shown, n => n.Kind == DownloadsNotices.Organized && n.Title == "report.pdf organized");
    }

    [Fact]
    public void Duplicate_orders_in_settings_do_not_disable_the_rules()
    {
        using var f = new Fixture();
        f.Settings.Rules.Add(new RuleSetting { Id = "a", Name = "A", Order = 0, Extensions = ["txt"], Action = RuleActionKind.Move, Template = "Text" });
        f.Settings.Rules.Add(new RuleSetting { Id = "b", Name = "B", Order = 0, Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Docs" });
        f.Engine.Apply(f.Settings);
        f.Write("report.pdf");

        f.Settle();

        Assert.True(File.Exists(f.In("Docs", "report.pdf")));
    }

    [Fact]
    public void Silent_rename_happens_once_and_not_again_on_rescan_or_restart()
    {
        // The self-trigger guard: a rename that stays in the watched folder must not feed itself.
        using var f = new Fixture();
        f.AddRule(new RuleSetting { Extensions = ["txt"], Action = RuleActionKind.Rename, Template = "done-{filename}" });
        f.Write("a.txt");

        f.Settle();
        Assert.True(File.Exists(f.In("done-a.txt")));

        f.Settle(); // the watcher's own events for done-a.txt
        f.Engine.Apply(f.Settings); // a full rescan
        f.Settle();
        f.Restart(); // a new process on the same history
        f.Settle();

        Assert.Equal(["done-a.txt"], Directory.GetFiles(f.Folder).Select(Path.GetFileName));
        Assert.Single(f.History.Query(new HistoryQuery()));
    }

    [Fact]
    public void A_changed_output_is_new_again()
    {
        using var f = new Fixture();
        f.AddRule(new RuleSetting { Extensions = ["txt"], Action = RuleActionKind.Rename, Template = "done-{filename}" });
        f.Write("a.txt");
        f.Settle();

        f.Write("done-a.txt", "hello more");
        f.Settle();

        Assert.True(File.Exists(f.In("done-done-a.txt")));
    }

    [Fact]
    public void An_empty_file_waits_until_it_has_content()
    {
        using var f = new Fixture();
        f.AddRule(MovePdfs());
        string path = f.Write("late.pdf", "");

        f.Settle();
        Assert.True(File.Exists(path));

        File.WriteAllText(path, "now");
        f.Settle(passes: 30); // through the backoff delays

        Assert.True(File.Exists(f.In("Docs", "late.pdf")));
    }

    [Fact]
    public void Files_already_there_are_never_moved_silently()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        f.Write("old.pdf");
        f.AddRule(MovePdfs());

        f.Settle();

        Assert.True(File.Exists(f.In("old.pdf")));
        var pending = Assert.Single(f.Engine.Pending);
        Assert.Equal(PendingStatus.Ready, pending.Status);
        Assert.DoesNotContain(f.Notifier.Shown, n => n.Kind == DownloadsNotices.ReadyToReview);
    }

    [Fact]
    public void Misleading_names_are_held_even_in_silent()
    {
        using var f = new Fixture();
        f.AddRule(new RuleSetting { Extensions = ["exe"], Action = RuleActionKind.Move, Template = "Apps" });
        f.Write("invoice.pdf.exe");

        f.Settle();

        Assert.True(File.Exists(f.In("invoice.pdf.exe")));
        Assert.Equal(PendingStatus.Held, Assert.Single(f.Engine.Pending).Status);
        Assert.Contains(f.Notifier.Shown, n => n.Kind == DownloadsNotices.Held && n.NeedsAttention);
    }

    [Fact]
    public void The_ignore_file_keeps_files_in_place()
    {
        using var f = new Fixture();
        f.AddRule(MovePdfs());
        File.WriteAllText(f.In(EngineOptions.IgnoreFileName), "keep*\n");
        f.Write("keep-me.pdf");

        f.Settle();

        Assert.True(File.Exists(f.In("keep-me.pdf")));
    }

    [Fact]
    public void Manual_lists_new_files_and_announces_them()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        f.Write("new.pdf");

        f.Settle();

        Assert.True(File.Exists(f.In("new.pdf")));
        Assert.Equal(PendingStatus.Ready, Assert.Single(f.Engine.Pending).Status);
        var notice = Assert.Single(f.Notifier.Shown, n => n.Kind == DownloadsNotices.ReadyToReview);
        Assert.Equal("1 file to review", notice.Title);
    }

    [Fact]
    public async Task Preview_then_approve_moves_only_selected_items_once()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        f.Write("a.pdf");
        f.Write("b.pdf");
        f.Write("notes.txt"); // no rule: listed, never selectable
        File.WriteAllText(f.In(EngineOptions.IgnoreFileName), "keep*\n");
        f.Write("keep.pdf"); // ignored: listed as held

        var preview = await f.Engine.CreatePreviewAsync(f.Folder);
        Assert.Equal(4, preview.Items.Count);
        Assert.Equal(["a.pdf", "b.pdf"], preview.Items.Where(i => i.Selected).Select(i => i.Name).Order());
        Assert.Equal(OperationKind.NoMatch, preview.Items.Single(i => i.Name == "notes.txt").Kind);
        var held = preview.Items.Single(i => i.Name == "keep.pdf");
        Assert.Equal(OperationKind.Hold, held.Kind);
        Assert.Contains(held.Warnings, w => w.Kind == WarningKind.IgnoreMatch);

        var a = preview.Items.Single(i => i.Name == "a.pdf");
        var result = await f.Engine.ApproveAsync(preview.Id, [a.Id]);

        Assert.Equal(1, result.Succeeded);
        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
        Assert.True(File.Exists(f.In("b.pdf")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Engine.ApproveAsync(preview.Id, [a.Id]));
    }

    [Fact]
    public async Task An_expired_preview_cannot_be_approved()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        f.Write("a.pdf");
        var preview = await f.Engine.CreatePreviewAsync(f.Folder);

        f.Time.Advance(TimeSpan.FromMinutes(31));

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Engine.ApproveAsync(preview.Id, [1]));
        Assert.True(File.Exists(f.In("a.pdf")));
    }

    [Fact]
    public async Task Approval_fails_an_item_whose_destination_was_taken_instead_of_renaming_it()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        f.Write("a.pdf");
        var preview = await f.Engine.CreatePreviewAsync(f.Folder);

        Directory.CreateDirectory(f.In("Docs"));
        File.WriteAllText(f.In("Docs", "a.pdf"), "someone else");
        var result = await f.Engine.ApproveAsync(preview.Id, [1]);

        var item = Assert.Single(result.Items);
        Assert.False(item.Succeeded);
        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.Equal("someone else", File.ReadAllText(f.In("Docs", "a.pdf")));
        Assert.False(File.Exists(f.In("Docs", "a (2).pdf")));
    }

    [Fact]
    public async Task Approval_fails_an_item_that_changed_after_the_preview()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        f.Write("a.pdf");
        var preview = await f.Engine.CreatePreviewAsync(f.Folder);

        File.AppendAllText(f.In("a.pdf"), " and more");
        var result = await f.Engine.ApproveAsync(preview.Id, [1]);

        Assert.False(Assert.Single(result.Items).Succeeded);
        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.Equal(OperationState.Failed, Assert.Single(f.History.Query(new HistoryQuery())).State);
    }

    [Fact]
    public async Task Preview_numbers_around_files_already_in_the_destination()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        Directory.CreateDirectory(f.In("Docs"));
        File.WriteAllText(f.In("Docs", "a.pdf"), "older");
        f.Write("a.pdf");

        var preview = await f.Engine.CreatePreviewAsync(f.Folder);

        Assert.Equal(f.In("Docs", "a (2).pdf"), Assert.Single(preview.Items).Destination, ignoreCase: true);
    }

    [Fact]
    public void Silent_takes_the_next_free_name_on_a_conflict()
    {
        using var f = new Fixture();
        f.AddRule(MovePdfs());
        Directory.CreateDirectory(f.In("Docs"));
        File.WriteAllText(f.In("Docs", "a.pdf"), "older");
        f.Write("a.pdf", "newer");

        f.Settle();

        Assert.Equal("older", File.ReadAllText(f.In("Docs", "a.pdf")));
        Assert.Equal("newer", File.ReadAllText(f.In("Docs", "a (2).pdf")));
    }

    [Fact]
    public void Forced_copy_verifies_and_leaves_no_partial_file()
    {
        using var f = new Fixture(options: new EngineOptions { ForceCopy = true });
        f.AddRule(MovePdfs());
        string source = f.Write("big.pdf", new string('x', 200_000));
        var modified = File.GetLastWriteTimeUtc(source);

        f.Settle();

        string moved = f.In("Docs", "big.pdf");
        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(source));
        Assert.False(File.Exists(moved + ".chippi-partial"));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(moved));
        Assert.Equal(OperationState.Completed, Assert.Single(f.History.Query(new HistoryQuery())).State);
    }
}
