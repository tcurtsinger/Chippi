// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.CommandBar.Windows;

namespace Chippi.Features.CommandBar.Tests;

public sealed class SearchProviderTests
{
    [Fact]
    public void Apps_and_open_windows_keep_their_icon_identity()
    {
        using var pc = new FakePc();
        var window = new OpenWindow(42, "Chippi · Chrome", "Google Chrome") { IconPath = @"C:\Apps\Chrome\chrome.exe" };
        var rows = Search(pc).Find("chrome", [window], []);
        var app = Assert.Single(rows, r => r.Kind == ResultKind.App);
        var openWindow = Assert.Single(rows, r => r.Kind == ResultKind.Window);
        Assert.Equal("Chrome", app.App);
        Assert.Same(window, openWindow.Window);
        Assert.Equal(window.IconPath, openWindow.Window!.IconPath);
        Assert.Null(openWindow.Icon);
    }

    [Fact]
    public void Identically_named_files_show_the_shortest_distinct_parent_and_keep_full_paths()
    {
        using var pc = new FakePc();
        var files = new[]
        {
            new FoundFile(@"C:\Users\Travis\Work\Reports\Budget.xlsx", "Budget.xlsx", "Reports", false),
            new FoundFile(@"C:\Users\Travis\Personal\Reports\Budget.xlsx", "Budget.xlsx", "Reports", false),
            new FoundFile(@"D:\Reports\Budget.xlsx", "Budget.xlsx", "Reports", false),
        };
        var rows = Search(pc).Find("budget", [], files).Where(r => r.Kind == ResultKind.File).ToArray();
        Assert.Equal([@"Work\Reports", @"Personal\Reports", @"D:\Reports"], rows.Select(r => r.Detail));
        Assert.Equal(files.Select(f => f.Path), rows.Select(r => r.FullPath));
    }

    [Fact]
    public void File_disambiguation_covers_case_differences_and_shared_parent_chains()
    {
        using var pc = new FakePc();
        var files = new[]
        {
            new FoundFile(@"C:\Work\2026\Reports\Budget.xlsx", "Budget.xlsx", "Reports", false),
            new FoundFile(@"C:\Personal\2026\Reports\budget.xlsx", "budget.xlsx", "reports", false),
        };
        var rows = Search(pc).Find("budget", [], files).Where(r => r.Kind == ResultKind.File).ToArray();
        Assert.Contains(rows, r => r.Detail == @"Work\2026\Reports");
        Assert.Contains(rows, r => r.Detail == @"Personal\2026\Reports");
    }

    [Fact]
    public void Unique_files_keep_the_short_parent_label()
    {
        using var pc = new FakePc();
        var file = new FoundFile(@"C:\Users\Travis\Downloads\Budget.xlsx", "Budget.xlsx", "Downloads", false);
        var row = Assert.Single(Search(pc).Find("budget", [], [file]), r => r.Kind == ResultKind.File);
        Assert.Equal("Downloads", row.Detail);
        Assert.Equal(file.Path, row.FullPath);
    }

    [Fact]
    public async Task A_failed_file_search_can_recover_on_the_next_request_without_restarting()
    {
        int calls = 0;
        var expected = new FoundFile(@"C:\Budget.xlsx", "Budget.xlsx", @"C:\", false);
        using var pc = new WindowsPc((_, _, _) => ++calls == 1 ? throw new InvalidOperationException("Index unavailable") : [expected]);
        var failure = await Assert.ThrowsAsync<FileSearchUnavailableException>(() => pc.FindFilesAsync("budget", 8, default));
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal("Files unavailable", failure.Message);
        Assert.Equal([expected], await pc.FindFilesAsync("budget", 8, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task File_search_cancellation_is_not_reported_as_an_unavailable_index()
    {
        using var canceled = new CancellationTokenSource();
        using var pc = new WindowsPc((_, _, ct) =>
        {
            canceled.Cancel();
            ct.ThrowIfCancellationRequested();
            return [];
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pc.FindFilesAsync("budget", 8, canceled.Token));
    }

    [Fact]
    public async Task Empty_or_zero_limit_requests_do_not_touch_the_file_index()
    {
        using var pc = new WindowsPc((_, _, _) => throw new InvalidOperationException("Must not query"));
        Assert.Empty(await pc.FindFilesAsync("💜", 8, default));
        Assert.Empty(await pc.FindFilesAsync("budget", 0, default));
    }

    private static CommandSearch Search(IPc pc) => new(pc, () => [], _ => [], () => new CommandBarSettings(), new Uses(), _ => Task.CompletedTask);

    private sealed class FakePc : IPc
    {
        public IReadOnlyList<StartApp> Apps { get; } = [new("Google Chrome", "Chrome")];
        public event Action? AppsChanged { add { } remove { } }
        public void RefreshApps() { }
        public IReadOnlyList<OpenWindow> Windows() => [];
        public Task<IReadOnlyList<FoundFile>> FindFilesAsync(string text, int count, CancellationToken ct) => Task.FromResult<IReadOnlyList<FoundFile>>([]);
        public void Open(string target) => throw new InvalidOperationException("No OS actions in this fixture");
        public void Switch(nint window) => throw new InvalidOperationException("No OS actions in this fixture");
        public Task<ImageSource?> AppIconAsync(StartApp app, int pixels) => Task.FromResult<ImageSource?>(null);
        public void Dispose() { }
    }
}
