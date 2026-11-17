// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Shell;

namespace Chippi.Notifications.Tests;

/// <summary>How the shell fails while starting and how it stops (windowless).</summary>
public sealed class ShellLifetimeTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);

    [Fact]
    public void A_failure_before_the_tray_exists_ends_the_app_and_a_later_one_does_not()
    {
        Assert.True(ShellLifetime.ExitOnUnhandled(trayComposed: false));
        Assert.False(ShellLifetime.ExitOnUnhandled(trayComposed: true));
    }

    [Fact]
    public async Task A_stop_that_finishes_reports_true()
    {
        bool stopped = false;
        Assert.True(await ShellLifetime.StopWithinAsync(_ => { stopped = true; return Task.CompletedTask; }, TimeSpan.FromSeconds(5)));
        Assert.True(stopped);
    }

    [Fact]
    public async Task A_stop_that_never_completes_gives_up_after_the_limit()
    {
        var started = DateTime.UtcNow;
        Assert.False(await ShellLifetime.StopWithinAsync(_ => new TaskCompletionSource().Task, Short));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_stop_that_honors_its_token_reports_false()
    {
        Assert.False(await ShellLifetime.StopWithinAsync(async ct => await Task.Delay(Timeout.Infinite, ct), Short));
    }

    [Fact]
    public async Task A_stop_that_fails_is_not_thrown()
    {
        Assert.True(await ShellLifetime.StopWithinAsync(_ => Task.FromException(new InvalidOperationException("boom")), TimeSpan.FromSeconds(5)));
        Assert.True(await ShellLifetime.StopWithinAsync(_ => throw new InvalidOperationException("boom"), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_second_stop_still_runs_after_the_first_gave_up()
    {
        Assert.False(await ShellLifetime.StopWithinAsync(_ => new TaskCompletionSource().Task, Short));
        Assert.True(await ShellLifetime.StopWithinAsync(_ => Task.CompletedTask, TimeSpan.FromSeconds(5)));
    }
}
