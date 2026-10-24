// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;

namespace Chippi.Features.Downloads.Tests;

/// <summary>A rule that is added, changed or turned on sorts the files already there once it settles.</summary>
public sealed class RuleSweepTests
{
    private static RuleSetting MovePdfs(string to = "Docs") => new() { Id = "pdfs", Extensions = ["pdf"], Action = RuleActionKind.Move, Template = to };

    /// <summary>Files that were there before the folder was watched, old enough to be past the stability wait.</summary>
    private static void Existing(Fixture f, params string[] names)
    {
        foreach (var name in names)
            f.Write(name);
        f.Time.Advance(TimeSpan.FromMinutes(1));
        f.Engine.ProcessDueNow();
    }

    private static void Wait(Fixture f, double seconds)
    {
        f.Time.Advance(TimeSpan.FromSeconds(seconds));
        f.Engine.ProcessDueNow();
    }

    [Fact]
    public void A_new_rule_waits_for_quiet_then_sorts_the_files_as_one_run()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf", "b.pdf");
        f.AddRule(MovePdfs());

        Wait(f, 2);
        Assert.True(File.Exists(f.In("a.pdf")));

        Wait(f, 1.5);
        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
        Assert.True(File.Exists(f.In("Docs", "b.pdf")));
        var ops = f.History.Query(new HistoryQuery());
        Assert.Equal(2, ops.Count);
        Assert.Single(ops.Select(o => o.RunId).Distinct());
        Assert.Contains(f.Notifier.Shown, n => n.Kind == DownloadsNotices.Organized);
        Assert.Empty(f.Engine.Pending);
    }

    [Fact]
    public void Closing_the_editor_settles_the_rule_at_once()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.AddRule(MovePdfs());

        f.Engine.SettleRule("pdfs");
        f.Engine.ProcessDueNow();

        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
    }

    [Fact]
    public void A_rule_open_in_the_editor_is_not_used_until_it_closes()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.Engine.EditingRule("pdfs");
        f.AddRule(MovePdfs("Documents")); // the editor's default destination

        Wait(f, 10);
        Assert.True(File.Exists(f.In("a.pdf")));

        f.Settings.Rules[0].Template = "Docs"; // the folder the person picked
        f.Engine.Apply(f.Settings);
        f.Engine.SettleRule("pdfs");
        f.Engine.ProcessDueNow();

        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
        Assert.False(Directory.Exists(f.In("Documents")));
    }

    [Fact]
    public void Every_edit_restarts_the_quiet_period_and_the_last_destination_wins()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.AddRule(MovePdfs("Doc"));

        Wait(f, 2);
        f.Settings.Rules[0].Template = "Docs";
        f.Engine.Apply(f.Settings);
        Wait(f, 2);
        Assert.True(File.Exists(f.In("a.pdf")));

        Wait(f, 1.5);
        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
        Assert.False(Directory.Exists(f.In("Doc")));
    }

    [Fact]
    public void Only_the_files_this_rule_wins_are_sorted()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        f.AddRule(MovePdfs());
        f.Engine.SettleRule("pdfs");
        f.Engine.ProcessDueNow();
        Existing(f, "a.pdf", "notes.txt", "photo.png");

        f.AddRule(new RuleSetting { Id = "other", Extensions = ["pdf", "txt"], Action = RuleActionKind.Move, Template = "Other" });
        Wait(f, 3.5);

        Assert.True(File.Exists(f.In("a.pdf"))); // the earlier rule wins it, and it didn't change
        Assert.True(File.Exists(f.In("Other", "notes.txt")));
        Assert.True(File.Exists(f.In("photo.png")));
    }

    [Fact]
    public void Manual_lists_them_for_review_and_moves_nothing()
    {
        using var f = new Fixture(FolderMode.Manual, existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.AddRule(MovePdfs());

        f.Settle(); // past the quiet period and the rescanned file's own readiness checks

        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.Equal(PendingStatus.Ready, Assert.Single(f.Engine.Pending).Status);
        Assert.Empty(f.History.Query(new HistoryQuery()));
    }

    [Fact]
    public void Turning_a_rule_on_counts_as_a_change_and_off_cancels_it()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        var rule = MovePdfs();
        rule.Enabled = false;
        f.AddRule(rule);
        Wait(f, 3.5);
        Assert.True(File.Exists(f.In("a.pdf")));

        rule.Enabled = true;
        f.Engine.Apply(f.Settings);
        Wait(f, 2);
        rule.Enabled = false;
        f.Engine.Apply(f.Settings);
        Wait(f, 3.5);
        Assert.True(File.Exists(f.In("a.pdf")));

        rule.Enabled = true;
        f.Engine.Apply(f.Settings);
        Wait(f, 3.5);
        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
    }

    [Fact]
    public void A_typo_never_moves_files()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.AddRule(new RuleSetting { Id = "bad", NameRegex = "(", Action = RuleActionKind.Move, Template = "Docs" });

        f.Engine.SettleRule("bad");
        Wait(f, 3.5);

        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.Empty(f.History.Query(new HistoryQuery()));
    }

    [Fact]
    public void A_paused_folder_is_left_alone()
    {
        using var f = new Fixture(FolderMode.Paused, existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.AddRule(MovePdfs());

        Wait(f, 3.5);

        Assert.True(File.Exists(f.In("a.pdf")));
    }

    [Fact]
    public void Starting_the_app_sorts_nothing()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        f.Settings.Rules.Add(MovePdfs());
        Existing(f, "a.pdf");

        f.Restart();
        Wait(f, 3.5);

        Assert.True(File.Exists(f.In("a.pdf")));
    }

    [Fact]
    public async Task An_undone_file_is_not_sorted_again_when_the_rule_changes()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.AddRule(MovePdfs());
        Wait(f, 3.5);
        long run = Assert.Single(f.History.Query(new HistoryQuery())).RunId;
        Assert.All(await f.Engine.UndoRunAsync(run), u => Assert.True(u.Succeeded));

        f.Settings.Rules[0].Template = "Papers";
        f.Engine.Apply(f.Settings);
        Wait(f, 3.5);

        Assert.True(File.Exists(f.In("a.pdf")));
    }
}
