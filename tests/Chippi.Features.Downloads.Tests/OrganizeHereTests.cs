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
        Assert.Equal(["2 files", "c.torrent"], rows.Select(r => r.Name));
        Assert.True(rows[0].IsSummary);
        Assert.Equal(TidyRowAction.Review, rows[0].Action);
        Assert.Equal("Inspect", rows[1].ActionLabel);
        Assert.Equal("", rows[1].Detail);
        Assert.Equal("1 file", TidyProjection.Rows(downloads, pending.Take(1).ToList(), [], DateTimeOffset.UtcNow)[0].Name);

        Assert.Equal(["d.torrent"], TidyProjection.Rows(desktop, pending, [], DateTimeOffset.UtcNow).Select(r => r.Name));
    }

    [Fact]
    public void Organize_now_on_the_island_moves_the_clean_files_and_leaves_the_rest_for_a_look() => WpfTestThread.Run(() =>
    {
        var watched = Path.Combine(_root, "Downloads");
        Directory.CreateDirectory(watched);
        foreach (var name in new[] { "a.pdf", "b.pdf", "setup.pdf.exe", "c.torrent", "notes.txt", "receipt.txt" })
            System.IO.File.WriteAllText(Path.Combine(watched, name), name);
        WithWidget(new DownloadsSettings
        {
            Initialized = true,
            OrganizeInExplorer = false,
            Folders = [new WatchedFolder { Path = watched, Mode = FolderMode.Manual, BaselineUtc = DateTimeOffset.UtcNow.AddHours(1) }],
            Rules =
            [
                new RuleSetting { Id = "pdf", Name = "PDFs", Order = 0, Extensions = ["pdf", "exe"], Action = RuleActionKind.Move, Template = "Docs" },
                new RuleSetting { Id = "torrent", Name = "Torrents", Order = 1, Extensions = ["torrent"], Action = RuleActionKind.Hold, HoldReason = "Check before opening" },
                new RuleSetting { Id = "receipt", Name = "Receipt", Order = 2, NameRegex = @"^receipt\.txt$", Action = RuleActionKind.Rename, Template = "Filed receipt.txt" },
            ],
        }, (view, _, _, requests) =>
        {
            var organize = Descendants(view).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Organize");

            organize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => System.IO.File.Exists(Path.Combine(watched, "Docs", "b.pdf")) && organize.IsEnabled);

            Assert.True(System.IO.File.Exists(Path.Combine(watched, "Docs", "a.pdf")));
            foreach (var stays in new[] { "setup.pdf.exe", "c.torrent", "notes.txt" })
                Assert.True(System.IO.File.Exists(Path.Combine(watched, stays)), stays);
            Assert.Empty(requests); // nothing opened Settings
            var first = Assert.IsType<TidyRow>(((ItemsControl)view.FindName("Rows")).Items[0]);
            Assert.True(System.IO.File.Exists(Path.Combine(watched, "Filed receipt.txt")));
            Assert.Equal("2 moved · 1 renamed", first.Name);
            Assert.Equal("2 need a look", first.Detail);
            Assert.Equal(TidyRowAction.UndoRun, first.Action);
            Assert.True(first.HasTextAction);
            Assert.Equal("Undo", first.ActionLabel);
            Assert.StartsWith("Undo run", first.AccessibleAction);

            // Only files that need a look are left: Organize now opens Preview for them.
            organize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => requests.Count > 0 && organize.IsEnabled);
            Assert.Equal(new DownloadsRequest(DownloadsRequestKind.Organize, watched), Assert.Single(requests));
        });
    });

    [Fact]
    public void Batch_review_opens_the_selected_folder_and_Inspect_stays_on_one_file() => WpfTestThread.Run(() =>
    {
        var personal = Path.Combine(_root, "Personal", "Downloads");
        var work = Path.Combine(_root, "Work", "Downloads");
        const string held = "sample.torrent";
        foreach (string folder in new[] { personal, work })
        {
            Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(Path.Combine(folder, held), folder);
        }
        string ready = Path.Combine(work, "notes.txt");
        System.IO.File.WriteAllText(ready, "contents");
        WithWidget(new DownloadsSettings
        {
            Initialized = true, OrganizeInExplorer = false,
            Folders = [.. new[] { personal, work }.Select(path => new WatchedFolder { Path = path, Mode = FolderMode.Manual, GraceSeconds = 0 })],
            Rules =
            [
                new RuleSetting { Id = "notes", Name = "Notes", Extensions = ["txt"], Action = RuleActionKind.Move, Template = "Sorted" },
                new RuleSetting { Id = "held", Name = "Held", Extensions = ["torrent"], Action = RuleActionKind.Hold, HoldReason = "Verify the sender" },
            ],
        }, (view, tile, module, requests) =>
        {
            var picker = (ComboBox)view.FindName("Folders");
            picker.IsDropDownOpen = true;
            Assert.True(tile.HandleEscape!());
            Assert.False(picker.IsDropDownOpen);
            picker.SelectedIndex = 1;
            Button? Action(string label) => Descendants(view).OfType<Button>().SingleOrDefault(b => b.IsVisible && Equals(b.Content, label));
            void Press(string label) => Action(label)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var detail = (FrameworkElement)view.FindName("InspectPage");
            var showFile = (Button)view.FindName("ShowFile");
            var reveals = new List<string>();
            view.RevealFile = path => { reveals.Add(path); return false; }; // exercise failure without launching Explorer
            var focusTargets = new List<object>();
            view.PreviewGotKeyboardFocus += (_, e) =>
            {
                if (e.NewFocus is not null) focusTargets.Add(e.NewFocus);
                e.Handled = true; // observe the requested target without giving a test the owner's keyboard
            };

            Until(() => Action("Review") is not null && Action("Inspect") is not null);
            var review = Action("Review")!;
            var inspect = Action("Inspect")!;
            Assert.Equal("Review 1 file", AutomationProperties.GetName(review));
            Assert.Equal($"Inspect {held}", AutomationProperties.GetName(inspect));
            Assert.Equal("Held", AutomationProperties.GetItemStatus(inspect));
            var recent = (Border)view.FindName("Recent");
            double oldHeight = recent.ActualHeight;
            Window.GetWindow(view)!.Height += 8;
            Until(() => recent.ActualHeight > oldHeight);
            Assert.Same(review, Action("Review"));
            Assert.Same(inspect, Action("Inspect"));
            Press("Review");
            Assert.Equal(new DownloadsRequest(DownloadsRequestKind.Organize, work), Assert.Single(requests));
            requests.Clear();
            Assert.False(tile.HandleBack!());

            Press("Inspect");
            Assert.True(detail.IsVisible);
            Assert.Equal(held, ((TextBlock)view.FindName("InspectName")).Text);
            Assert.Equal("Verify the sender", ((TextBlock)view.FindName("InspectReason")).Text);
            Assert.Equal(work, ((TextBlock)view.FindName("InspectFolder")).ToolTip);
            Assert.True(showFile.IsVisible && showFile.IsEnabled);
            Assert.Empty(requests);
            Assert.Null(Action("Review"));
            Press("Show in folder");
            Assert.Equal(Path.Combine(work, held), Assert.Single(reveals));
            Assert.Equal("Show in folder failed", ((TextBlock)view.FindName("InspectError")).Text);

            focusTargets.Clear();
            Assert.True(tile.HandleBack());
            Until(() => Action("Inspect") is { } restored && focusTargets.Contains(restored));
            Assert.Equal(work, picker.ToolTip);
            Assert.False(detail.IsVisible);
            Assert.False(tile.HandleEscape!());

            Press("Inspect");
            // The same basename is still held in Personal; the stale Work detail must not switch to it.
            System.IO.File.Delete(Path.Combine(work, held));
            Until(() => !showFile.IsEnabled && !module.Engine!.Pending.Any(p => p.Path == Path.Combine(work, held)));
            Assert.True(detail.IsVisible);
            Assert.NotEqual("Held", ((TextBlock)view.FindName("InspectStatus")).Text);
            showFile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(reveals);
            focusTargets.Clear();
            Assert.True(tile.HandleEscape());
            Until(() => focusTargets.Contains(picker));
            Assert.False(detail.IsVisible);
            Assert.Equal(work, picker.ToolTip);
            Assert.True(System.IO.File.Exists(Path.Combine(personal, held)));
            Assert.True(System.IO.File.Exists(ready));
            Assert.Empty(module.Engine!.History.Query(new HistoryQuery()));
            Assert.Empty(requests);
        });
    });

    private void WithWidget(DownloadsSettings settings, Action<TidyView, IslandTileSpec, DownloadsModule, List<DownloadsRequest>> check)
    {
        var config = new ConfigStore(Path.Combine(_root, "settings.json"));
        config.SetModuleSettings("downloads-organizer", settings);
        var registration = new ExplorerRegistration("test", _key, @"C:\Tools\Chippi.exe");
        var module = new DownloadsModule(registration);
        var registry = new ModuleRegistry(config, new IslandActivityHub(), NullLoggerFactory.Instance,
            new ModuleServices { DataRoot = Path.Combine(_root, "data") });
        registry.Register(module);
        var requests = new List<DownloadsRequest>();
        module.Requested += requests.Add;
        Window? window = null;
        try
        {
            registry.StartAsync().GetAwaiter().GetResult();
            var spec = Assert.Single(registry.Find("downloads-organizer")!.IslandTiles).Spec;
            var view = Assert.IsType<TidyView>(spec.CreateSection());
            window = new Window { Content = view, Width = 496, Height = 304, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show();
            check(view, spec, module, requests);
        }
        finally
        {
            window?.Close();
            registry.StopAllAsync().GetAwaiter().GetResult();
            registration.SetEnabled(false);
        }
    }

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
    public void An_existing_file_specific_Settings_deep_link_still_opens_Preview_at_that_file()
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
