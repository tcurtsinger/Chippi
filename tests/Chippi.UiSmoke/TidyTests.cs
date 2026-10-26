// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Chippi.Features.Downloads.Storage;
using Xunit.Abstractions;

namespace Chippi.UiSmoke;

public sealed class TidyTests(ITestOutputHelper output)
{
    [DesktopFact]
    public void EmptyTidyOffersFolderActions()
    {
        using var app = new DesktopSession(output);
        var tidy = OpenTidy(app);
        DesktopSession.Find(tidy, "No folders yet");
        DesktopSession.ExpectAbsent(tidy, "New folders start in Manual");
        DesktopSession.Find(tidy, "Add Downloads", InvokePattern.Pattern);
        DesktopSession.Find(tidy, "Choose folder", InvokePattern.Pattern);
        DesktopSession.ExpectAbsent(tidy, "Organize now");
        Assert.Empty(app.DownloadsSettings.GetProperty("folders").EnumerateArray());
    }

    [DesktopFact]
    public void FolderPickerModesAndRulesUseTheSelectedFolder()
    {
        using var app = new DesktopSession(output, Seed);
        var tidy = OpenTidy(app);
        var picker = DesktopSession.Find(tidy, "Watched folder", ExpandCollapsePattern.Pattern);
        ((ExpandCollapsePattern)picker.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        AutomationElement? inbox = null;
        DesktopSession.Until(() =>
        {
            inbox = picker.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
                .FirstOrDefault(e => e.Current.Name.Contains("Inbox", StringComparison.Ordinal) && e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _));
            return inbox is not null;
        }, "Inbox folder option");
        ((SelectionItemPattern)inbox!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        ((ExpandCollapsePattern)picker.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
        var mode = DesktopSession.Find(tidy, "Folder mode");
        DesktopSession.Select(mode, "Paused");
        DesktopSession.Until(() => Mode(app, "Inbox") == "paused", "Inbox Paused persisted");
        Assert.Equal("manual", Mode(app, "Downloads"));
        DesktopSession.Select(mode, "Silent");
        DesktopSession.Until(() => Mode(app, "Inbox") == "silent", "Inbox Silent persisted");
        Assert.True(app.DownloadsSettings.GetProperty("folders")[1].GetProperty("baselineUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));
        // Rules are in Settings; the page's History link opens File organizer's Settings.
        DesktopSession.Invoke(DesktopSession.Find(tidy, "History", InvokePattern.Pattern));
        var settings = Settings(app);
        DesktopSession.Select(DesktopSession.Find(settings, "Section"), "Rules");
        DesktopSession.Find(settings, "Test a file name", ValuePattern.Pattern);
    }

    [DesktopFact]
    public void TidyPreviewApprovalAndUndoAffectOnlyTheSelectedFile()
    {
        using var app = new DesktopSession(output, (path, root) =>
        {
            Seed(path, root);
            File.WriteAllText(Path.Combine(root, "Downloads", "report.pdf"), "report");
            File.WriteAllText(Path.Combine(root, "Downloads", "invoice.pdf.exe"), "held");
        });
        var tidy = OpenTidy(app);
        DesktopSession.Invoke(DesktopSession.Find(tidy, "Organize now", InvokePattern.Pattern));
        var settings = Settings(app);
        var held = DesktopSession.Find(settings, "Include invoice.pdf.exe", TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, DesktopSession.Toggle(held).Current.ToggleState);
        Assert.Equal(ToggleState.On, DesktopSession.Toggle(DesktopSession.Find(settings, "Include report.pdf", TogglePattern.Pattern)).Current.ToggleState);
        DesktopSession.Invoke(DesktopSession.Find(settings, "Approve", InvokePattern.Pattern));
        var original = Path.Combine(app.FixtureRoot, "Downloads", "report.pdf");
        var moved = Path.Combine(app.FixtureRoot, "Downloads", "Sorted", "report.pdf");
        DesktopSession.Until(() => File.Exists(moved) && !File.Exists(original), "approved file moved");
        Assert.True(File.Exists(Path.Combine(app.FixtureRoot, "Downloads", "invoice.pdf.exe")));
        Close(settings);
        tidy = OpenTidy(app);
        DesktopSession.Invoke(DesktopSession.Find(tidy, "Undo report.pdf", InvokePattern.Pattern));
        DesktopSession.Until(() => File.Exists(original) && !File.Exists(moved), "Tidy verified undo restored file");
    }

    [DesktopFact]
    public void HeldReviewOpensAnUncheckedPreview()
    {
        using var app = new DesktopSession(output, (path, root) =>
        {
            Seed(path, root);
            File.WriteAllText(Path.Combine(root, "Downloads", "invoice.pdf.exe"), "held");
        });
        var tidy = OpenTidy(app);
        DesktopSession.Invoke(DesktopSession.Find(tidy, "Review invoice.pdf.exe", InvokePattern.Pattern));
        var settings = Settings(app);
        Assert.Equal(ToggleState.Off, DesktopSession.Toggle(DesktopSession.Find(settings, "Include invoice.pdf.exe", TogglePattern.Pattern)).Current.ToggleState);
    }

    [DesktopFact]
    public void FailedRetryOpensAFreshPreview()
    {
        using var app = new DesktopSession(output, (path, root) =>
        {
            Seed(path, root);
            var failed = Path.Combine(root, "Downloads", "budget.bin");
            File.WriteAllText(failed, "budget");
            using var history = History(root);
            long run = history.BeginRun(RunTrigger.Preview, Path.GetDirectoryName(failed)!);
            long op = history.Plan(run, HistoryOperationKind.Move, failed, Path.Combine(root, "Sorted", "budget.bin"), null, null, 6);
            history.MarkFailed(op, "Destination unavailable");
            history.EndRun(run);
        });
        var tidy = OpenTidy(app);
        DesktopSession.Invoke(DesktopSession.Find(tidy, "Retry budget.bin", InvokePattern.Pattern));
        var settings = Settings(app);
        Assert.False(DesktopSession.Find(settings, "Include budget.bin", TogglePattern.Pattern).Current.IsEnabled);
        Assert.True(File.Exists(Path.Combine(app.FixtureRoot, "Downloads", "budget.bin")));
    }

    [DesktopFact]
    public void RetryRemainsInvokableAfterHeldRowsRefresh()
    {
        using var app = new DesktopSession(output, (path, root) =>
        {
            Seed(path, root);
            File.WriteAllText(Path.Combine(root, "Downloads", "invoice.pdf.exe"), "held");
            var failed = Path.Combine(root, "Downloads", "budget.bin");
            File.WriteAllText(failed, "budget");
            using var history = History(root);
            long run = history.BeginRun(RunTrigger.Preview, Path.GetDirectoryName(failed)!);
            long op = history.Plan(run, HistoryOperationKind.Move, failed, Path.Combine(root, "Sorted", "budget.bin"), null, null, 6);
            history.MarkFailed(op, "Destination unavailable");
            history.EndRun(run);
        });
        var tidy = OpenTidy(app);
        DesktopSession.Invoke(DesktopSession.Find(tidy, "Review invoice.pdf.exe", InvokePattern.Pattern));
        Close(Settings(app));
        tidy = OpenTidy(app);
        Assert.DoesNotContain(tidy.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>(), e => e.Current.Name.Contains('{'));
        DesktopSession.Invoke(DesktopSession.Find(tidy, "Retry budget.bin", InvokePattern.Pattern));
        DesktopSession.Find(Settings(app), "Include budget.bin", TogglePattern.Pattern);
    }

    [DesktopFact]
    public void ClearingHistoryStaysClearedAfterTidyChangesMode()
    {
        using var app = new DesktopSession(output, (path, root) =>
        {
            Seed(path, root);
            var original = Path.Combine(root, "Downloads", "old.pdf");
            var moved = Path.Combine(root, "Downloads", "Sorted", "old.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
            File.WriteAllText(moved, "old");
            using var history = History(root);
            long run = history.BeginRun(RunTrigger.Preview, Path.GetDirectoryName(original)!);
            long op = history.Plan(run, HistoryOperationKind.Move, original, moved, null, null, 3);
            history.MarkCompleted(op, moved, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(moved))), File.GetLastWriteTimeUtc(moved));
            history.EndRun(run);
        });
        var tidy = OpenTidy(app);
        DesktopSession.Find(tidy, "Undo old.pdf", InvokePattern.Pattern);
        DesktopSession.Invoke(DesktopSession.Find(tidy, "History", InvokePattern.Pattern));
        var settings = Settings(app);
        DesktopSession.Select(DesktopSession.Find(settings, "Section"), "History");
        DesktopSession.Invoke(DesktopSession.Find(settings, "Clear history", InvokePattern.Pattern));
        DesktopSession.Invoke(DesktopSession.Find(settings, "Clear", InvokePattern.Pattern));
        DesktopSession.Until(() => app.DownloadsSettings.TryGetProperty("historyClearedUtc", out var at) && at.ValueKind == JsonValueKind.String, "history clear marker saved");
        var marker = app.DownloadsSettings.GetProperty("historyClearedUtc").GetString();
        Close(settings);
        tidy = OpenTidy(app);
        DesktopSession.ExpectAbsent(tidy, "Undo old.pdf");
        DesktopSession.Select(DesktopSession.Find(tidy, "Folder mode"), "Paused");
        DesktopSession.Until(() => Mode(app, "Downloads") == "paused", "mode saved after clearing history");
        Assert.Equal(marker, app.DownloadsSettings.GetProperty("historyClearedUtc").GetString());
        DesktopSession.ExpectAbsent(tidy, "Undo old.pdf");
        Assert.True(File.Exists(Path.Combine(app.FixtureRoot, "Downloads", "Sorted", "old.pdf")));
    }

    private static AutomationElement OpenTidy(DesktopSession app)
    {
        var pages = app.OpenPages();
        if (pages.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "File organizer")) is null
            || pages.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Folder mode")) is null)
        {
            if (pages.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "All features")) is { } grid)
                DesktopSession.Invoke(grid);
            DesktopSession.Invoke(DesktopTests.Tile(pages, "File organizer"));
        }
        return pages;
    }
    private static AutomationElement Settings(DesktopSession app)
    {
        AutomationElement? window = null;
        DesktopSession.Until(() => (window = app.WindowOrNull("Chippi Settings")) is not null, "module settings opened");
        return window!;
    }
    private static void Close(AutomationElement window) => ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
    private static string? Mode(DesktopSession app, string name) => app.DownloadsSettings.GetProperty("folders").EnumerateArray()
        .First(f => Path.GetFileName(f.GetProperty("path").GetString()) == name).GetProperty("mode").GetString();
    private static SqliteHistoryStore History(string root) => new(Path.Combine(Path.GetDirectoryName(root)!, "data", "downloads-organizer", "history.db"));
    private static void Seed(string path, string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Downloads"));
        Directory.CreateDirectory(Path.Combine(root, "Inbox"));
        var config = JsonNode.Parse(File.ReadAllText(path))!;
        config["modules"]!["downloads-organizer"] = JsonSerializer.SerializeToNode(new
        {
            initialized = true,
            organizeInExplorer = false,
            folders = new[] { "Downloads", "Inbox" }.Select(n => new { path = Path.Combine(root, n), mode = "manual", graceSeconds = 30, baselineUtc = DateTimeOffset.UtcNow.AddDays(-1) }),
            rules = new[] { new { id = "docs", name = "Documents", enabled = true, order = 0, extensions = new[] { "pdf", "exe" }, action = "move", template = "Sorted" } }
        });
        File.WriteAllText(path, config.ToJsonString());
    }
}
