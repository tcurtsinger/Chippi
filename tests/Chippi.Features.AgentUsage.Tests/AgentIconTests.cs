// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Island.Tests;

namespace Chippi.Features.AgentUsage.Tests;

public sealed class AgentIconTests
{
    private static BitmapSource Bitmap(byte red)
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, red, 255 }, 4);
        image.Freeze();
        return image;
    }

    [Fact]
    public void Provider_lookup_uses_the_desktop_app_identity_not_its_display_name()
    {
        Assert.Equal(["com.squirrel.AnthropicClaude.claude"], AgentIconSource.Identities(AgentKind.Claude));
        Assert.Equal(["OpenAI.Codex_"], AgentIconSource.Identities(AgentKind.Codex));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentIconSource.Identities((AgentKind)99));
    }

    [Fact]
    public async Task All_provider_surfaces_share_one_nonblocking_windows_lookup()
    {
        var ready = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        var icons = new AgentIconSource(ids => { Assert.Equal(["OpenAI.Codex_"], ids); reads++; return ready.Task; });
        Image first = null!, second = null!;
        Task firstBind = null!, secondBind = null!;
        WpfTestThread.Run(() =>
        {
            first = new Image(); second = new Image();
            firstBind = icons.SetSource(first, AgentKind.Codex);
            secondBind = icons.SetSource(second, AgentKind.Codex);
            Assert.False(firstBind.IsCompleted);
            Assert.False(secondBind.IsCompleted);
            Assert.Same(icons.Source(AgentKind.Codex), first.Source);
            Assert.Same(first.Source, second.Source);
            Assert.IsType<DrawingImage>(first.Source);
            Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(first));
            Assert.True(first.SnapsToDevicePixels);
        });
        var actual = Bitmap(180);
        ready.SetResult(actual);
        await Task.WhenAll(firstBind, secondBind);
        WpfTestThread.Run(() =>
        {
            Assert.Same(actual, first.Source);
            Assert.Same(actual, second.Source);
            Assert.Same(actual, icons.Source(AgentKind.Codex));
            var third = new Image();
            Assert.True(icons.SetSource(third, AgentKind.Codex).IsCompleted);
            Assert.Same(actual, third.Source);
            Assert.Equal(1, reads);
        });
    }

    [Fact]
    public async Task A_late_provider_icon_cannot_replace_a_reused_or_cleared_row()
    {
        var claude = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var codex = Bitmap(90);
        var icons = new AgentIconSource(ids => ids[0].StartsWith("OpenAI.", StringComparison.Ordinal)
            ? Task.FromResult<BitmapSource?>(codex) : claude.Task);
        Image reused = null!, cleared = null!;
        Task oldReused = null!, oldCleared = null!;
        WpfTestThread.Run(() =>
        {
            reused = new Image(); cleared = new Image();
            oldReused = icons.SetSource(reused, AgentKind.Claude);
            oldCleared = icons.SetSource(cleared, AgentKind.Claude);
            _ = icons.SetSource(reused, AgentKind.Codex);
            _ = icons.SetSource(cleared, null);
            Assert.Same(codex, reused.Source);
            Assert.Null(cleared.Source);
        });
        claude.SetResult(Bitmap(120));
        await Task.WhenAll(oldReused, oldCleared);
        WpfTestThread.Run(() =>
        {
            Assert.Same(codex, reused.Source);
            Assert.Null(cleared.Source);
        });
    }

    [Fact]
    public async Task Missing_or_failed_installed_app_keeps_neutral_initials()
    {
        foreach (bool fails in new[] { false, true })
        {
            var icons = new AgentIconSource(_ => fails
                ? Task.FromException<BitmapSource?>(new IOException("App unavailable"))
                : Task.FromResult<BitmapSource?>(null));
            Image image = null!;
            Task bind = null!;
            WpfTestThread.Run(() =>
            {
                image = new Image();
                bind = icons.SetSource(image, AgentKind.Claude);
            });
            await bind;
            WpfTestThread.Run(() =>
            {
                Assert.IsType<DrawingImage>(image.Source);
                Assert.True(image.Source.IsFrozen);
                Assert.Same(icons.Source(AgentKind.Claude), image.Source);
                Assert.NotSame(icons.Source(AgentKind.Codex), image.Source);
            });
        }
    }
}
