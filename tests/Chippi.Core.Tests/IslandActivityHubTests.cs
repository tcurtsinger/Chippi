// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;

namespace Chippi.Core.Tests;

public sealed class IslandActivityHubTests
{
    private readonly IslandActivityHub _hub = new();

    [Theory]
    [InlineData("Brightness control failed")]
    [InlineData("Not launched")]
    public void Feedback_expires_without_a_publisher_timer(string title)
    {
        var time = new ModuleServicesHardeningTests.ManualTime();
        var hub = new IslandActivityHub(time, TimeSpan.FromSeconds(3));
        var publisher = hub.CreatePublisher("module");
        publisher.Publish(new IslandActivity("media", IslandActivityKind.Media, "Song", "Player"));
        publisher.Publish(new IslandActivity("feedback", IslandActivityKind.Status, title, "Module") { IsFeedback = true });
        var timer = Assert.Single(time.Timers);
        Assert.Equal(TimeSpan.FromSeconds(3), timer.DueTime);
        int changed = 0;
        hub.Changed += (_, _) => changed++;

        timer.Fire();

        Assert.Equal("Song", Assert.Single(hub.Activities).Title);
        Assert.Equal(1, changed);
        Assert.True(timer.Disposed);
    }

    [Fact]
    public void Republishing_feedback_restarts_its_time_and_an_old_callback_cannot_clear_it()
    {
        var time = new ModuleServicesHardeningTests.ManualTime();
        var hub = new IslandActivityHub(time);
        var publisher = hub.CreatePublisher("module");
        var line = new IslandActivity("feedback", IslandActivityKind.Status, "Failed", "Module") { IsFeedback = true };
        publisher.Publish(line);
        var old = Assert.Single(time.Timers);
        publisher.Publish(line); // even the same activity instance has a new expiry
        Assert.True(old.Disposed);
        Assert.Equal(TimeSpan.FromSeconds(6), time.Timers[^1].DueTime);

        old.Fire(); // already queued before disposal
        Assert.Single(hub.Activities);
        time.Timers[^1].Fire();
        Assert.Empty(hub.Activities);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Clearing_feedback_cancels_its_timer_and_a_new_lifetime_survives_the_old_callback(bool module)
    {
        var time = new ModuleServicesHardeningTests.ManualTime();
        var hub = new IslandActivityHub(time);
        var publisher = hub.CreatePublisher("module");
        var line = new IslandActivity("feedback", IslandActivityKind.Status, "Failed", "Module") { IsFeedback = true };
        publisher.Publish(line);
        var old = Assert.Single(time.Timers);
        if (module) hub.ClearModule("module"); else publisher.Clear(line.Key);
        Assert.True(old.Disposed);
        publisher.Publish(line);
        hub.CreatePublisher("other").Publish(line);

        old.Fire();
        Assert.Equal(2, hub.Activities.Count);
        time.Timers[^1].Fire();
        Assert.Equal("module", Assert.Single(hub.Activities).ModuleId);
    }

    [Fact]
    public void Persistent_activity_replacing_feedback_cancels_expiry_and_does_not_start_a_timer()
    {
        var time = new ModuleServicesHardeningTests.ManualTime();
        var hub = new IslandActivityHub(time);
        var publisher = hub.CreatePublisher("module");
        var line = new IslandActivity("status", IslandActivityKind.Status, "Keeping awake", "Until stopped");
        publisher.Publish(line);
        Assert.Empty(time.Timers);
        publisher.Publish(line with { IsFeedback = true });
        var timer = Assert.Single(time.Timers);
        publisher.Publish(line);

        timer.Fire();

        Assert.True(timer.Disposed);
        Assert.False(Assert.Single(hub.Activities).IsFeedback);
        Assert.Single(time.Timers);
    }

    [Fact]
    public void Resting_when_nothing_is_published() => Assert.Null(_hub.Current);

    [Fact]
    public void Priority_is_media_then_timer_then_download_or_stash_then_status_then_an_agent_working()
    {
        var p = _hub.CreatePublisher("debug");
        p.Publish(new IslandActivity("working", IslandActivityKind.Agent, "Working · 42%", "Claude Code · chippi"));
        Assert.Equal("working", _hub.Current!.Key);
        p.Publish(new IslandActivity("awake", IslandActivityKind.Status, "1 hr left", "Keep awake"));
        Assert.Equal("awake", _hub.Current!.Key);
        p.Publish(new IslandActivity("stash", IslandActivityKind.Stash, "invoice.pdf", "Downloads"));
        Assert.Equal("stash", _hub.Current!.Key);
        p.Publish(new IslandActivity("timer", IslandActivityKind.Timer, "Focus", "24:10"));
        Assert.Equal("timer", _hub.Current!.Key);
        p.Publish(new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify"));
        Assert.Equal("media", _hub.Current!.Key);

        Assert.Equal(["media", "timer", "stash", "awake", "working"], _hub.Activities.Select(a => a.Key));
        p.Clear("media");
        Assert.Equal("timer", _hub.Current!.Key);
    }

    [Fact]
    public void An_agent_waiting_on_you_ranks_under_media_and_over_the_rest()
    {
        var p = _hub.CreatePublisher("debug");
        p.Publish(new IslandActivity("timer", IslandActivityKind.Timer, "Focus", "24:10"));
        p.Publish(new IslandActivity("awake", IslandActivityKind.Status, "1 hr left", "Keep awake"));
        p.Publish(new IslandActivity("waiting", IslandActivityKind.Agent, "Waiting on you", "Claude Code · packrat") { NeedsAttention = true });
        Assert.Equal("waiting", _hub.Current!.Key);

        p.Publish(new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify"));
        Assert.Equal(["media", "waiting", "timer", "awake"], _hub.Activities.Select(a => a.Key));
    }

    [Fact]
    public void Status_is_shown_only_when_nothing_else_but_an_agent_working_is_live()
    {
        var p = _hub.CreatePublisher("keep-awake");
        p.Publish(new IslandActivity("awake", IslandActivityKind.Status, "Keeping awake", "Until 6:00 PM"));
        Assert.Equal("awake", _hub.Current!.Key);

        // Status loses to every other kind but a working agent, even one published before it.
        var debug = _hub.CreatePublisher("debug");
        debug.Publish(new IslandActivity("download", IslandActivityKind.Download, "setup.exe", "Downloads"));
        Assert.Equal("download", _hub.Current!.Key);
        debug.Publish(new IslandActivity("agent", IslandActivityKind.Agent, "Working", "Codex · smartwork"));
        Assert.Equal(["download", "awake", "agent"], _hub.Activities.Select(a => a.Key));

        debug.Clear("download");
        Assert.Equal("awake", _hub.Current!.Key);
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
