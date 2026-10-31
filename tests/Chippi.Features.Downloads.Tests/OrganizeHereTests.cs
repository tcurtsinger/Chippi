// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.SystemIntegration;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Chippi.Features.Downloads.Views.Settings;
using Chippi.Features.Downloads.Views.Tidy;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Chippi.Features.Downloads.Tests;

/// <summary>
/// Critique 2026-10-31: the island page showed every folder's rows under the one picked, hid the files
/// waiting for review, and could only open Preview; new rules started as a copy of rule 1's .pdf.
/// </summary>
public sealed class OrganizeHereTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chippi-organize-here-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = @"Software\Chippi.Explorer.OrganizeHere-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_key, false);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Island_rows_follow_the_picked_folder_and_count_its_files_to_review()
    {
        const string downloads = @"C:\Users\me\Downloads", desktop = @"C:\Users\me\Desktop";
        PendingFile File(string folder, string name, PendingStatus status) => new(folder + @"\", Path.Combine(folder, name), name, status, status == PendingStatus.Held ? "Check before opening" : null);
        PendingFile[] pending =
        [
            File(downloads, "a.pdf", PendingStatus.Ready), File(downloads, "b.zip", PendingStatus.Ready),
            File(downloads, "c.torrent", PendingStatus.Held), File(desktop, "d.torrent", PendingStatus.Held),
        ];

        var rows = TidyProjection.Rows(downloads, pending, [], DateTimeOffset.UtcNow);
        Assert.Equal(["2 files to review", "c.torrent"], rows.Select(r => r.Name));
        Assert.True(rows[0].IsSummary);
        Assert.Equal(TidyRowAction.Review, rows[0].Action);
        Assert.Equal("Check before opening", rows[1].Detail); // the triangle and Review say it's held

        Assert.Equal(["d.torrent"], TidyProjection.Rows(desktop, pending, [], DateTimeOffset.UtcNow).Select(r => r.Name));
    }

    [Fact]
    public void Organize_now_on_the_island_moves_the_clean_files_and_leaves_the_rest_for_a_look() => WpfTestThread.Run(() =>
    {
        var watched = Path.Combine(_root, "Downloads");
        Directory.CreateDirectory(watched);
        foreach (var name in new[] { "a.pdf", "b.pdf", "setup.pdf.exe", "c.torrent", "notes.txt" })
            System.IO.File.WriteAllText(Path.Combine(watched, name), name);
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.SetModuleSettings("downloads-organizer", new DownloadsSettings
        {
            Initialized = true,
            OrganizeInExplorer = false,
            Folders = [new WatchedFolder { Path = watched, Mode = FolderMode.Manual, BaselineUtc = DateTimeOffset.UtcNow.AddHours(1) }],
            Rules =
            [
                new RuleSetting { Id = "pdf", Name = "PDFs", Order = 0, Extensions = ["pdf", "exe"], Action = RuleActionKind.Move, Template = "Docs" },
                new RuleSetting { Id = "torrent", Name = "Torrents", Order = 1, Extensions = ["torrent"], Action = RuleActionKind.Hold, HoldReason = "Check before opening" },
            ],
        });
        var registration = new ExplorerRegistration("test", _key, @"C:\Tools\Chippi.exe");
        var module = new DownloadsModule(registration);
        var registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance, new ModuleServices { DataRoot = Path.Combine(_root, "data") });
        registry.Register(module);
        var requests = new List<DownloadsRequest>();
        Window? window = null;
        try
        {
            registry.StartAsync().GetAwaiter().GetResult();
            var view = new TidyView(module, requests.Add);
            window = new Window { Content = view, Width = 496, Height = 304, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show();
            var organize = Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Organize now");

            organize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => System.IO.File.Exists(Path.Combine(watched, "Docs", "b.pdf")) && organize.IsEnabled);

            Assert.True(System.IO.File.Exists(Path.Combine(watched, "Docs", "a.pdf")));
            foreach (var stays in new[] { "setup.pdf.exe", "c.torrent", "notes.txt" })
                Assert.True(System.IO.File.Exists(Path.Combine(watched, stays)), stays);
            Assert.Empty(requests); // nothing opened Settings
            var first = Assert.IsType<TidyRow>(((ItemsControl)view.FindName("Rows")).Items[0]);
            Assert.Equal("2 moved", first.Name);
            Assert.Equal("2 need a look", first.Detail);
            Assert.Equal(TidyRowAction.UndoRun, first.Action);

            // Only files that need a look are left: Organize now opens Preview for them.
            organize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => requests.Count > 0 && organize.IsEnabled);
            Assert.Equal(new DownloadsRequest(DownloadsRequestKind.Organize, watched), Assert.Single(requests));
        }
        finally
        {
            window?.Close();
            registry.StopAllAsync().GetAwaiter().GetResult();
            registration.SetEnabled(false);
        }
    });

    [Fact]
    public void A_new_rule_needs_a_condition_and_says_which_earlier_rule_wins_its_files()
    {
        var drafts = new RuleDrafts(
        [
            new RuleSetting { Id = "pdf", Name = "PDFs to Documents", Order = 0, Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Documents" },
            new RuleSetting { Id = "regex", Name = "Invoices", Order = 1, NameRegex = "invoice", Action = RuleActionKind.Move, Template = "Invoices" },
        ]);
        var draft = new RuleSetting { Id = "new", Name = "New rule", Action = RuleActionKind.Move, Template = @"C:\Docs" };
        drafts.Drafts.Add(draft);

        Assert.False(drafts.CanSave("new")); // no condition would win every file
        draft.Extensions = ["pdf", "docx"];
        Assert.True(drafts.CanSave("new"));
        Assert.Equal("Rule 1 (PDFs to Documents) matches .pdf first", drafts.Shadow(draft));
        draft.Extensions = ["docx"];
        Assert.Null(drafts.Shadow(draft)); // the regex rule can't be compared, so it says nothing

        // The test field runs the rule being edited, in its place, before it's saved.
        Assert.Equal(["pdf", "regex", "new"], drafts.Testing("new").Select(r => r.Id));
        Assert.Equal(["pdf", "regex"], drafts.Testing(null).Select(r => r.Id));
    }

    [Fact]
    public void A_held_rows_Review_opens_Preview_at_that_file()
    {
        var request = new DownloadsRequest(DownloadsRequestKind.Organize, @"C:\Users\me\Downloads", File: "setup.pdf.exe");
        Assert.Equal(@"module:downloads-organizer/preview/C:\Users\me\Downloads*setup.pdf.exe", DownloadsModule.SettingsLink(request));
    }

    /// <summary>Polish 2026-10-31: the island page shows one folder, so its History › opens that folder's runs.</summary>
    [Fact]
    public void The_islands_History_opens_the_picked_folders_runs()
    {
        Assert.Equal(@"module:downloads-organizer/history/in/C:\Users\me\Desktop", DownloadsModule.SettingsLink(new DownloadsRequest(DownloadsRequestKind.Run, @"C:\Users\me\Desktop")));
        Assert.Equal("module:downloads-organizer/history", DownloadsModule.SettingsLink(new DownloadsRequest(DownloadsRequestKind.Run)));
    }

    private static void Until(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < until, "Timed out");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(20);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
