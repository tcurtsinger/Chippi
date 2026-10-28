// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;
using Chippi.Features.Clipboard.CleanUrl;

namespace Chippi.Features.Clipboard.Tests;

public sealed class AutoCleanerTests : IDisposable
{
    private readonly FakeHub _hub = new();
    private readonly AutoCleaner _cleaner;

    public AutoCleanerTests() => _cleaner = new AutoCleaner(_hub);

    public void Dispose() => _cleaner.Dispose();

    private async Task CopyAsync(ClipContent content, ClipPrivacy privacy = ClipPrivacy.None)
    {
        _hub.Copy(content, privacy);
        await _cleaner.Rewriting;
    }

    [Fact]
    public async Task Off_by_default_and_listens_only_while_on()
    {
        Assert.Equal(0, _hub.Leases);
        await CopyAsync(ClipContent.FromText("https://youtu.be/abc?si=share&t=30"));
        Assert.Empty(_hub.Written);
        _cleaner.Apply(true, []);
        Assert.Equal(1, _hub.Leases);
        _cleaner.Apply(false, []);
        Assert.Equal(0, _hub.Leases);
    }

    [Fact]
    public async Task A_copied_link_with_trackers_becomes_its_clean_link()
    {
        _cleaner.Apply(true, []);
        await CopyAsync(ClipContent.FromText("https://youtu.be/abc?si=share&t=30"));
        Assert.Equal("https://youtu.be/abc?t=30", Assert.Single(_hub.Written).Text);
    }

    [Fact]
    public async Task Leaves_alone_a_private_copy_a_clean_link_text_files_and_pictures()
    {
        _cleaner.Apply(true, []);
        await CopyAsync(ClipContent.FromText("https://example.org/?utm_source=a"), ClipPrivacy.NoHistory);
        await CopyAsync(ClipContent.FromText("https://example.org/?item=7"));
        await CopyAsync(ClipContent.FromText("see https://example.org/?utm_source=a"));
        await CopyAsync(ClipContent.FromFiles([@"C:\Fake\a.txt"]));
        await CopyAsync(ClipContent.FromImage(new ClipImage(1, 1, [0, 0, 0, 255])));
        Assert.Empty(_hub.Written);
    }

    [Fact]
    public async Task A_site_to_skip_covers_its_subdomains_only()
    {
        _cleaner.Apply(true, ["Example.org"]);
        await CopyAsync(ClipContent.FromText("https://www.example.org/?utm_source=a"));
        await CopyAsync(ClipContent.FromText("https://example.org/?utm_source=a"));
        Assert.Empty(_hub.Written);
        await CopyAsync(ClipContent.FromText("https://notexample.org/?utm_source=a"));
        Assert.Equal("https://notexample.org/", Assert.Single(_hub.Written).Text);
    }

    [Theory]
    [InlineData("example.org", "example.org")]
    [InlineData(" WWW.Example.org. ", "www.example.org")]
    [InlineData("https://news.example.org/a?b=1", "news.example.org")]
    [InlineData("example", null)]
    [InlineData("exa mple.org", null)]
    [InlineData("", null)]
    public void Sites_are_hosts(string entry, string? host) => Assert.Equal(host, Sites.Normalize(entry));
}
