// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;

namespace Chippi.Core.Tests;

public sealed class IslandActivityHubTests
{
    private readonly IslandActivityHub _hub = new();

    [Fact]
    public void Resting_when_nothing_is_published() => Assert.Null(_hub.Current);

    [Fact]
    public void Priority_is_media_then_timer_then_download_or_stash_then_agent()
    {
        var p = _hub.CreatePublisher("debug");
        p.Publish(new IslandActivity("agent", IslandActivityKind.Agent, "Claude", "62% of 5h"));
        Assert.Equal("agent", _hub.Current!.Key);
        p.Publish(new IslandActivity("stash", IslandActivityKind.Stash, "invoice.pdf", "Downloads"));
        Assert.Equal("stash", _hub.Current!.Key);
        p.Publish(new IslandActivity("timer", IslandActivityKind.Timer, "Focus", "24:10"));
        Assert.Equal("timer", _hub.Current!.Key);
        p.Publish(new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify"));
        Assert.Equal("media", _hub.Current!.Key);

        Assert.Equal(["media", "timer", "stash", "agent"], _hub.Activities.Select(a => a.Key));
        p.Clear("media");
        Assert.Equal("timer", _hub.Current!.Key);
    }

    [Fact]
    public void Download_and_stash_share_a_priority_and_the_newest_wins()
    {
        var p = _hub.CreatePublisher("debug");
        p.Publish(new IslandActivity("stash", IslandActivityKind.Stash, "a", "b"));
        p.Publish(new IslandActivity("download", IslandActivityKind.Download, "c", "d"));
        Assert.Equal("download", _hub.Current!.Key);

        // An update keeps the activity's age, so it does not jump ahead.
        p.Publish(new IslandActivity("stash", IslandActivityKind.Stash, "a2", "b") { Progress = 0.5 });
        Assert.Equal("download", _hub.Current!.Key);
        Assert.Equal(0.5, _hub.Activities.Single(a => a.Key == "stash").Progress);
    }

    [Fact]
    public void Keys_are_scoped_per_module_and_ClearModule_removes_only_that_module()
    {
        var a = _hub.CreatePublisher("a");
        var b = _hub.CreatePublisher("b");
        a.Publish(new IslandActivity("x", IslandActivityKind.Timer, "A", "a"));
        b.Publish(new IslandActivity("x", IslandActivityKind.Timer, "B", "b"));
        Assert.Equal(2, _hub.Activities.Count);

        _hub.ClearModule("b");

        Assert.Equal("a", _hub.Current!.ModuleId);
        Assert.Single(_hub.Activities);
    }

    [Fact]
    public void Changed_and_Succeeded_fire()
    {
        int changed = 0;
        ModuleSucceededEventArgs? success = null;
        _hub.Changed += (_, _) => changed++;
        _hub.Succeeded += (_, e) => success = e;
        var p = _hub.CreatePublisher("keep-awake");

        p.Publish(new IslandActivity("on", IslandActivityKind.Timer, "Awake", "Keep awake"));
        p.Clear("on");
        p.Clear("on"); // no-op, no event
        p.ReportSuccess("Timer finished");

        Assert.Equal(2, changed);
        Assert.Equal("keep-awake", success!.ModuleId);
        Assert.Equal("Timer finished", success.Message);
    }
}
