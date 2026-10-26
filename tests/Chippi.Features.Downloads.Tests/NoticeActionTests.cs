// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO.Compression;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Notifications;
using Chippi.Core.SystemIntegration;
using Chippi.Features.Downloads.Import;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Chippi.Features.Downloads.Tests;

/// <summary>What a Windows notification's body and buttons open, per event (Marge's calls, integration-scope.md).</summary>
public sealed class NoticeActionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chippi-notice-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = @"Software\Chippi.NoticeTest-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_key, false);
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private static RuleSetting MovePdfs() => new() { Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Docs" };

    private static IEnumerable<(string Label, string Activation)> Buttons(Notice notice) => notice.Actions.Select(a => (a.Label, a.Activation));

    [Fact]
    public void Organized_opens_its_run_and_offers_Undo_and_History()
    {
        using var f = new Fixture();
        f.AddRule(MovePdfs());
        f.Write("report.pdf");
        f.Settle();

        long run = f.History.Query(new HistoryQuery()).Single().RunId;
        var notice = f.Notifier.Shown.Single(n => n.Kind == DownloadsNotices.Organized);
        Assert.Equal($"--downloads-run {run}", notice.Activation);
        Assert.Equal([("Undo", $"--downloads-undo {run}"), ("History", $"--downloads-run {run}")], Buttons(notice));
    }

    [Fact]
    public void Held_has_no_run_so_it_opens_the_review()
    {
        using var f = new Fixture();
        f.AddRule(new RuleSetting { Extensions = ["exe"], Action = RuleActionKind.Move, Template = "Apps" });
        f.Write("invoice.pdf.exe");
        f.Settle();

        var notice = f.Notifier.Shown.Single(n => n.Kind == DownloadsNotices.Held);
        Assert.Equal("--downloads-review", notice.Activation);
        Assert.Equal([("Review", "--downloads-review")], Buttons(notice));
        Assert.Empty(f.History.Query(new HistoryQuery())); // nothing moved, nothing recorded
    }

    [Fact]
    public void Failed_reads_not_moved_and_opens_its_run()
    {
        using var f = new Fixture();
        f.AddRule(MovePdfs());
        File.WriteAllText(f.In("Docs"), "a file where the folder should be"); // the move cannot create its folder
        f.Write("report.pdf");
        f.Settle();

        long run = f.History.Query(new HistoryQuery()).Single(o => o.State == OperationState.Failed).RunId;
        var notice = f.Notifier.Shown.Single(n => n.Kind == DownloadsNotices.Failed);
        Assert.Equal("1 file not moved", notice.Title);
        Assert.StartsWith("report.pdf · ", notice.Body, StringComparison.Ordinal);
        Assert.Equal($"--downloads-run {run}", notice.Activation);
        Assert.Equal([("History", $"--downloads-run {run}")], Buttons(notice));
    }

    [Fact]
    public void Ready_to_review_opens_the_review_and_creates_no_run()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        f.Write("new.pdf");
        f.Settle();

        var notice = f.Notifier.Shown.Single(n => n.Kind == DownloadsNotices.ReadyToReview);
        Assert.Equal("--downloads-review", notice.Activation);
        Assert.Equal([("Review", "--downloads-review")], Buttons(notice));
        Assert.Empty(f.History.Query(new HistoryQuery()));
    }

    [Fact]
    public async Task Archive_imported_opens_its_run_and_offers_Undo_and_History()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        string zip = Path.Combine(f.Root, "in.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("a.pdf").Open()))
            writer.Write("pdf");
        var extracted = ArchiveImporter.Extract(zip, f.Folder, Path.Combine(f.Root, "data", "imports"));
        var preview = await f.Engine.PreviewImportAsync(f.Folder, extracted);
        var result = await f.Engine.ApproveAsync(preview.Id, [.. preview.Items.Select(i => i.Id)]);

        var notice = f.Notifier.Shown.Single(n => n.Kind == DownloadsNotices.ArchiveImported);
        Assert.Equal($"--downloads-run {result.RunId}", notice.Activation);
        Assert.Equal([("Undo", $"--downloads-undo {result.RunId}"), ("History", $"--downloads-run {result.RunId}")], Buttons(notice));
    }

    [Fact]
    public void Two_folders_in_one_pass_get_one_notice_per_run()
    {
        using var f = new Fixture();
        string inbox = Path.Combine(f.Root, "Inbox");
        Directory.CreateDirectory(inbox);
        f.Settings.Folders.Add(new WatchedFolder { Path = inbox, Mode = FolderMode.Silent, GraceSeconds = 0, BaselineUtc = f.Settings.Folders[0].BaselineUtc });
        f.AddRule(MovePdfs());
        f.Write("a.pdf");
        File.WriteAllText(Path.Combine(inbox, "b.pdf"), "b");
        // The watchers' events arrive on their own threads; a slow machine (CI) can need more passes
        // before both folders have been seen.
        List<long> Runs() => f.History.Query(new HistoryQuery()).Select(o => o.RunId).Distinct().Order().ToList();
        f.Settle();
        for (int pass = 0; pass < 40 && Runs().Count < 2; pass++)
            f.Settle(passes: 1);

        var runs = Runs();
        var notices = f.Notifier.Shown.Where(n => n.Kind == DownloadsNotices.Organized).ToList();
        Assert.Equal(2, runs.Count);
        Assert.Equal(runs, notices.Select(n => long.Parse(n.Activation!.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture)).Order());
        Assert.All(notices, n =>
        {
            Assert.EndsWith(" organized", n.Title, StringComparison.Ordinal);
            Assert.DoesNotContain("2 files", n.Title, StringComparison.Ordinal);
            Assert.All(n.Actions, a => Assert.EndsWith(n.Activation!.Split(' ')[1], a.Activation, StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task An_import_with_nothing_undoable_offers_only_History()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(MovePdfs());
        string zip = Path.Combine(f.Root, "in.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("notes.txt").Open()))
            writer.Write("no rule wants this"); // lands straight in the folder: an Import row
        var extracted = ArchiveImporter.Extract(zip, f.Folder, Path.Combine(f.Root, "data", "imports"));
        var preview = await f.Engine.PreviewImportAsync(f.Folder, extracted);
        var result = await f.Engine.ApproveAsync(preview.Id, [.. preview.Items.Select(i => i.Id)]);

        Assert.Equal(HistoryOperationKind.Import, f.History.RunOperations(result.RunId).Single().Kind);
        var notice = f.Notifier.Shown.Single(n => n.Kind == DownloadsNotices.ArchiveImported);
        Assert.Equal([("History", $"--downloads-run {result.RunId}")], Buttons(notice));
    }

    [Fact]
    public void Every_button_label_is_one_bare_word()
    {
        string[] labels = [DownloadsNotices.Undo(1).Label, DownloadsNotices.History(1).Label, DownloadsNotices.Review.Label];

        Assert.Equal(["Undo", "History", "Review"], labels);
    }

    [Theory]
    [InlineData("--downloads-undo", "7", "module:downloads-organizer/history/7/undo")]
    [InlineData("--downloads-run", "7", "module:downloads-organizer/history/7")]
    [InlineData("--downloads-review", null, "module:downloads-organizer/review")]
    public async Task Notice_verbs_open_Settings_at_their_section(string verb, string? value, string expected)
    {
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.SetModuleSettings("downloads-organizer", new DownloadsSettings { Initialized = true, OrganizeInExplorer = false });
        string? opened = null;
        var services = new ModuleServices { DataRoot = Path.Combine(_root, "data"), OpenSettings = page => opened = page };
        var registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance, services);
        registry.Register(new DownloadsModule(new ExplorerRegistration("test", _key, @"C:\Tools\Chippi.exe")));
        await registry.StartAsync();
        try
        {
            if (!registry.Find("downloads-organizer")!.IsRunning)
                await registry.SetInstalledAsync("downloads-organizer", true);

            Assert.True(services.Activation.TryRoute(value is null ? [verb] : [verb, value]));
            Assert.Equal(expected, opened);
        }
        finally
        {
            await registry.StopAllAsync();
        }
    }
}
