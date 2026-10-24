// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;

namespace Chippi.Features.Downloads.Tests;

/// <summary>A rule that is saved or switched on sorts the files already there.</summary>
public sealed class RuleSweepTests
{
    private static RuleSetting MovePdfs(string to = "Docs") => new() { Id = "pdfs", Name = "PDFs", Extensions = ["pdf"], Action = RuleActionKind.Move, Template = to };

    /// <summary>Files that were there before the folder was watched, old enough to be past the stability wait.</summary>
    private static void Existing(Fixture f, params string[] names)
    {
        foreach (var name in names)
            f.Write(name);
        f.Time.Advance(TimeSpan.FromMinutes(1));
        f.Engine.ProcessDueNow();
    }

    /// <summary>What Save (or the switch going On) does: settings applied, then the rule sorts.</summary>
    private static void Save(Fixture f, string id)
    {
        f.Engine.Apply(f.Settings);
        f.Engine.SortExisting(id);
        f.Engine.ProcessDueNow();
    }

    [Fact]
    public void Saving_a_new_rule_sorts_the_files_already_there_as_one_run()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf", "b.pdf");
        f.Settings.Rules.Add(MovePdfs());

        Save(f, "pdfs");

        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
        Assert.True(File.Exists(f.In("Docs", "b.pdf")));
        var ops = f.History.Query(new HistoryQuery());
        Assert.Equal(2, ops.Count);
        Assert.Single(ops.Select(o => o.RunId).Distinct());
        Assert.Contains(f.Notifier.Shown, n => n.Kind == DownloadsNotices.Organized);
        Assert.Empty(f.Engine.Pending);
    }

    [Fact]
    public void Saving_a_changed_rule_uses_its_new_destination()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        f.AddRule(MovePdfs("Doc"));
        Existing(f, "a.pdf");

        f.Settings.Rules[0].Template = "Docs";
        Save(f, "pdfs");

        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
        Assert.False(Directory.Exists(f.In("Doc")));
    }

    [Fact]
    public void Settings_applied_without_a_Save_sort_nothing()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.AddRule(MovePdfs()); // reorder, Delete and Duplicate apply settings this way

        f.Settle();

        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.Empty(f.History.Query(new HistoryQuery()));
    }

    [Fact]
    public void Only_the_files_this_rule_wins_are_sorted()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        f.AddRule(MovePdfs());
        Existing(f, "a.pdf", "notes.txt", "photo.png");

        f.Settings.Rules.Add(new RuleSetting { Id = "other", Name = "Other", Order = 1, Extensions = ["pdf", "txt"], Action = RuleActionKind.Move, Template = "Other" });
        Save(f, "other");

        Assert.True(File.Exists(f.In("a.pdf"))); // the earlier rule wins it, and it wasn't saved
        Assert.True(File.Exists(f.In("Other", "notes.txt")));
        Assert.True(File.Exists(f.In("photo.png")));
    }

    [Fact]
    public void Manual_lists_them_for_review_and_moves_nothing()
    {
        using var f = new Fixture(FolderMode.Manual, existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.Settings.Rules.Add(MovePdfs());

        Save(f, "pdfs");
        f.Settle(); // past the rescanned file's own readiness checks

        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.Equal(PendingStatus.Ready, Assert.Single(f.Engine.Pending).Status);
        Assert.Empty(f.History.Query(new HistoryQuery()));
    }

    [Fact]
    public void Switching_a_rule_on_sorts_and_an_off_rule_never_does()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        var rule = MovePdfs();
        rule.Enabled = false;
        f.Settings.Rules.Add(rule);

        Save(f, "pdfs");
        Assert.True(File.Exists(f.In("a.pdf")));

        rule.Enabled = true;
        Save(f, "pdfs");
        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
    }

    [Fact]
    public void A_typo_never_moves_files()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.Settings.Rules.Add(new RuleSetting { Id = "bad", Name = "Bad", NameRegex = "(", Action = RuleActionKind.Move, Template = "Docs" });

        Save(f, "bad");
        f.Settle();

        Assert.True(File.Exists(f.In("a.pdf")));
        Assert.Empty(f.History.Query(new HistoryQuery()));
    }

    [Fact]
    public void A_paused_folder_is_left_alone()
    {
        using var f = new Fixture(FolderMode.Paused, existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.Settings.Rules.Add(MovePdfs());

        Save(f, "pdfs");

        Assert.True(File.Exists(f.In("a.pdf")));
    }

    [Fact]
    public void Starting_the_app_sorts_nothing()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        f.Settings.Rules.Add(MovePdfs());
        Existing(f, "a.pdf");

        f.Restart();
        f.Settle();

        Assert.True(File.Exists(f.In("a.pdf")));
    }

    [Fact]
    public async Task An_undone_file_is_not_sorted_again_when_the_rule_is_saved()
    {
        using var f = new Fixture(existingFilesAreOld: true);
        Existing(f, "a.pdf");
        f.Settings.Rules.Add(MovePdfs());
        Save(f, "pdfs");
        long run = Assert.Single(f.History.Query(new HistoryQuery())).RunId;
        Assert.All(await f.Engine.UndoRunAsync(run), u => Assert.True(u.Succeeded));

        f.Settings.Rules[0].Template = "Papers";
        Save(f, "pdfs");

        Assert.True(File.Exists(f.In("a.pdf")));
    }
}
