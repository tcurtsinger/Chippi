// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Features.AgentUsage.Sessions;

namespace Chippi.Features.AgentUsage.Tests;

public sealed class UsageScheduleTests
{
    [Fact]
    public void Every_minute_while_a_page_shows_every_5_otherwise()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), UsageSchedule.Delay(seen: true, 0, CheckState.Ok));
        Assert.Equal(TimeSpan.FromMinutes(5), UsageSchedule.Delay(seen: false, 0, CheckState.Ok));
    }

    [Theory]
    [InlineData(true, 1, 2)]
    [InlineData(true, 2, 4)]
    [InlineData(true, 3, 8)]
    [InlineData(true, 4, 10)]
    [InlineData(false, 1, 10)]
    [InlineData(false, 5, 10)]
    public void A_failure_doubles_the_wait_up_to_10_minutes(bool seen, int failures, double minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), UsageSchedule.Delay(seen, failures, CheckState.Failed));

    [Theory]
    [InlineData(CheckState.NotFound)]
    [InlineData(CheckState.SignedOut)]
    [InlineData(CheckState.TooOld)]
    public void What_only_you_can_fix_waits_up_to_30_minutes(CheckState state)
    {
        Assert.Equal(TimeSpan.FromMinutes(10), UsageSchedule.Delay(seen: false, 1, state));
        Assert.Equal(TimeSpan.FromMinutes(20), UsageSchedule.Delay(seen: false, 2, state));
        Assert.Equal(TimeSpan.FromMinutes(30), UsageSchedule.Delay(seen: false, 3, state));
        Assert.Equal(TimeSpan.FromMinutes(30), UsageSchedule.Delay(seen: true, 9, state));
    }

    [Fact]
    public void The_first_check_waits_a_moment_after_the_start()
    {
        var start = Harness.Start;
        Assert.Equal(start + UsageSchedule.First, UsageSchedule.Due(start, null, seen: true, 0, CheckState.None));
        Assert.Equal(start.AddMinutes(6), UsageSchedule.Due(start, start.AddMinutes(1), seen: false, 0, CheckState.Ok));
    }
}

public sealed class UsageAlertsTests
{
    private static readonly DateTimeOffset Now = Harness.Start;

    private static UsageReading Reading(double used, DateTimeOffset at, DateTimeOffset? resets = null) =>
        new("Max", [new UsageWindow("5-hour", used, resets ?? Now.AddHours(2))], at);

    [Fact]
    public void The_first_reading_only_sets_where_a_window_stands()
    {
        var alerts = new UsageAlerts();
        Assert.Empty(alerts.Read(AgentKind.Claude, Reading(90, Now), 80, Now));
    }

    [Fact]
    public void Passing_the_level_is_near_and_coming_back_under_it_is_a_reset()
    {
        var alerts = new UsageAlerts();
        alerts.Read(AgentKind.Claude, Reading(70, Now), 80, Now);

        var near = Assert.Single(alerts.Read(AgentKind.Claude, Reading(86, Now.AddMinutes(5)), 80, Now.AddMinutes(5)));
        Assert.True(near.Near);
        Assert.Equal("80% used", near.Title(80));
        Assert.Equal("Claude Code · 5-hour", near.Source);
        Assert.Empty(alerts.Read(AgentKind.Claude, Reading(90, Now.AddMinutes(10)), 80, Now.AddMinutes(10)));

        var reset = Assert.Single(alerts.Read(AgentKind.Claude, Reading(3, Now.AddMinutes(15)), 80, Now.AddMinutes(15)));
        Assert.False(reset.Near);
        Assert.Equal("Reset", reset.Title(80));
    }

    [Fact]
    public void A_window_over_the_level_resets_when_its_time_comes_once()
    {
        var alerts = new UsageAlerts();
        var resets = Now.AddMinutes(30);
        alerts.Read(AgentKind.Codex, Reading(70, Now, resets), 80, Now);
        alerts.Read(AgentKind.Codex, Reading(85, Now.AddMinutes(1), resets), 80, Now.AddMinutes(1));

        Assert.Empty(alerts.Tick(Now.AddMinutes(29)));
        var reset = Assert.Single(alerts.Tick(Now.AddMinutes(30)));
        Assert.Equal("Codex · 5-hour", reset.Source);
        Assert.Empty(alerts.Tick(Now.AddMinutes(31)));
        // The next reading, still the old share before the window's next check, says nothing more.
        Assert.Empty(alerts.Read(AgentKind.Codex, Reading(85, Now.AddMinutes(32), resets), 80, Now.AddMinutes(32)));
    }

    [Fact]
    public void An_older_reading_and_a_new_level_say_nothing()
    {
        var alerts = new UsageAlerts();
        alerts.Read(AgentKind.Claude, Reading(70, Now), 80, Now);
        Assert.Empty(alerts.Read(AgentKind.Claude, Reading(95, Now.AddMinutes(-5)), 80, Now));
        Assert.Empty(alerts.Read(AgentKind.Claude, Reading(70, Now.AddMinutes(1)), 60, Now.AddMinutes(1)));
        Assert.Single(alerts.Read(AgentKind.Claude, Reading(50, Now.AddMinutes(2)), 60, Now.AddMinutes(2)));
    }
}

public sealed class AgentBarTests
{
    private static readonly DateTimeOffset Now = Harness.Start;

    private static AgentSession Session(AgentKind agent, string name, bool working, int minutesAgo) =>
        new(agent, name, name, working, Now.AddMinutes(-minutesAgo), 1, null);

    private static AgentStatus Claude(params (string Label, double Used)[] windows) =>
        new(AgentKind.Claude, true, CheckState.Ok, new UsageReading("Max", [.. windows.Select(w => new UsageWindow(w.Label, w.Used, Now.AddHours(1)))], Now), true, "Max");

    [Fact]
    public void Working_shows_the_agents_letters_closest_limit_and_session()
    {
        var working = AgentBar.Working([Session(AgentKind.Claude, "chippi", true, 12)], [Claude(("5-hour", 42), ("Weekly", 23))], Now)!;

        Assert.Equal(IslandActivityKind.Agent, working.Kind);
        Assert.Equal("CC", working.Monogram);
        Assert.Equal("Working · 42%", working.Title);
        Assert.Equal("Claude Code · chippi", working.Source);
        Assert.False(working.NeedsAttention);
        Assert.Null(working.TileKey); // it doesn't mark the home's button
        Assert.Equal(AgentUsageModule.TileKey, working.OpensTile);
    }

    [Fact]
    public void Several_sessions_are_counted()
    {
        var sessions = new[] { Session(AgentKind.Codex, "a", true, 3), Session(AgentKind.Codex, "b", true, 9) };
        Assert.Equal("Codex · 2 sessions", AgentBar.Working(sessions, [], Now)!.Source);
        Assert.Equal("Working", AgentBar.Working(sessions, [], Now)!.Title);
        var mixed = new[] { Session(AgentKind.Claude, "a", false, 3), Session(AgentKind.Codex, "b", false, 9) };
        var waiting = AgentBar.Waiting(mixed)!;
        Assert.Equal("2 sessions", waiting.Source);
        Assert.Equal("CC", waiting.Monogram); // the newest leads
    }

    [Fact]
    public void Waiting_on_you_is_Caution_and_ranks_above_all_but_a_song()
    {
        var hub = new IslandActivityHub();
        var others = hub.CreatePublisher("others");
        var agents = hub.CreatePublisher(AgentUsageModule.Id);
        others.Publish(new IslandActivity("awake", IslandActivityKind.Status, "1 hr left", "Keep awake"));
        others.Publish(new IslandActivity("download", IslandActivityKind.Download, "setup.exe", "Downloads"));
        agents.Publish(AgentBar.Working([Session(AgentKind.Claude, "chippi", true, 1)], [], Now)!);
        Assert.Equal(["download", "awake", AgentBar.WorkingKey], hub.Activities.Select(a => a.Key));

        var waiting = AgentBar.Waiting([Session(AgentKind.Claude, "packrat", false, 1)])!;
        Assert.True(waiting.NeedsAttention);
        Assert.Equal("Waiting on you", waiting.Title);
        agents.Publish(waiting);
        Assert.Equal(AgentBar.WaitingKey, hub.Current!.Key);

        others.Publish(new IslandActivity("song", IslandActivityKind.Media, "Weekend Mix", "Spotify"));
        Assert.Equal("song", hub.Current!.Key);
    }

    [Fact]
    public void No_sessions_no_activity()
    {
        Assert.Null(AgentBar.Working([], [], Now));
        Assert.Null(AgentBar.Waiting([Session(AgentKind.Claude, "chippi", true, 1)]));
    }

    [Fact]
    public void The_home_shows_the_closest_limit_and_turns_Caution_at_the_level()
    {
        var codex = new AgentStatus(AgentKind.Codex, true, CheckState.Ok, new UsageReading("Pro", [new UsageWindow("Weekly", 9, null)], Now), true, "Pro");
        Assert.Equal(("Claude 42%", false), AgentBar.Home([Claude(("5-hour", 42)), codex], 80, Now));
        Assert.Equal(("Claude 86%", true), AgentBar.Home([Claude(("5-hour", 86)), codex], 80, Now));
        Assert.Equal(("Codex 9%", false), AgentBar.Home([codex], 80, Now));
        Assert.Equal(("None", false), AgentBar.Home([], 80, Now));
    }

    [Fact]
    public void A_window_past_its_reset_counts_as_nothing_until_the_next_reading()
    {
        var status = new AgentStatus(AgentKind.Claude, true, CheckState.Ok,
            new UsageReading("Max", [new UsageWindow("5-hour", 95, Now.AddMinutes(-1)), new UsageWindow("Weekly", 30, Now.AddDays(3))], Now.AddHours(-1)), true, "Max");
        Assert.Equal(("Claude 30%", false), AgentBar.Home([status], 80, Now));
    }

    [Fact]
    public void An_alert_is_a_feedback_line_that_opens_Agents()
    {
        var near = AgentBar.Alert(new UsageAlert(AgentKind.Claude, "5-hour", Near: true), 80);
        Assert.True(near.IsFeedback);
        Assert.True(near.NeedsAttention);
        Assert.Equal(("80% used", "Claude Code · 5-hour"), (near.Title, near.Source));
        Assert.Equal(AgentUsageModule.TileKey, near.OpensTile);
        Assert.False(AgentBar.Alert(new UsageAlert(AgentKind.Claude, "5-hour", Near: false), 80).NeedsAttention);
    }
}

public sealed class AgentTextTests
{
    // In local time, as the page shows it: Sunday, Nov 1 at 4:52 PM.
    private static DateTimeOffset Local(int day, int hour, int minute) =>
        new(new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Local));

    private static readonly DateTimeOffset Today = Local(1, 16, 52);

    [Fact]
    public void Resets_today_by_time_this_week_by_day_later_by_date()
    {
        string t(DateTimeOffset at) => at.ToLocalTime().ToString("t");
        Assert.Equal($"Resets {t(Local(1, 18, 10))}", AgentText.Resets(new UsageWindow("5-hour", 42, Local(1, 18, 10)), Today));
        Assert.Equal($"Resets {Local(5, 9, 0):ddd} {t(Local(5, 9, 0))}", AgentText.Resets(new UsageWindow("Weekly", 23, Local(5, 9, 0)), Today));
        Assert.Equal($"Resets {Local(20, 9, 0):MMM d}", AgentText.Resets(new UsageWindow("Monthly", 23, Local(20, 9, 0)), Today));
        Assert.Equal($"Resets today, {t(Local(1, 18, 10))}", AgentText.ResetsLong(new UsageWindow("5-hour", 42, Local(1, 18, 10)), Today));
        Assert.Equal("", AgentText.Resets(new UsageWindow("5-hour", 42, null), Today));
    }

    [Fact]
    public void A_window_past_its_reset_reads_Reset_not_0()
    {
        var window = new UsageWindow("5-hour", 86, Local(1, 16, 0));
        Assert.Equal("Reset", AgentText.Value(window, Today));
        Assert.Equal("", AgentText.Resets(window, Today));
        Assert.Equal("86%", AgentText.Value(window, Local(1, 15, 0)));
    }

    [Fact]
    public void As_of_today_by_time_this_week_by_day_before_by_date()
    {
        Assert.Equal($"As of {Local(1, 14, 10):t}", AgentText.AsOf(Local(1, 14, 10), Today));
        // In local time on its own day: daylight saving time ended that morning in much of the US.
        var friday = new DateTimeOffset(new DateTime(2026, 10, 30, 9, 14, 0, DateTimeKind.Local));
        Assert.Equal($"As of {friday:ddd} {friday:t}", AgentText.AsOf(friday, Today));
        var before = new DateTimeOffset(new DateTime(2026, 10, 23, 9, 14, 0, DateTimeKind.Local));
        Assert.Equal($"As of {before:MMM d}", AgentText.AsOf(before, Today));
    }

    [Fact]
    public void Tooltips_say_it_in_full()
    {
        Assert.Equal($"5-hour limit: resets today at {Local(1, 18, 10):t} · 42% used", AgentText.Tip(new UsageWindow("5-hour", 42, Local(1, 18, 10)), Today));
        Assert.Equal("Weekly limit · 23% used", AgentText.Tip(new UsageWindow("Weekly", 23, null), Today));
    }

    [Theory]
    [InlineData(300, "5-hour")]
    [InlineData(10080, "Weekly")]
    [InlineData(1440, "Daily")]
    [InlineData(4320, "3-day")]
    public void Window_names_from_their_length(int minutes, string name) => Assert.Equal(name, AgentText.WindowName(minutes));

    [Theory]
    [InlineData("max", "Max")]
    [InlineData("pro", "Pro")]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void Plans_are_capitalized(string? plan, string? shown) => Assert.Equal(shown, AgentText.Plan(plan));

    [Fact]
    public void Minutes_read_as_the_page_says_them()
    {
        Assert.Equal("1 min", AgentText.Minutes(TimeSpan.FromSeconds(20)));
        Assert.Equal("12 min", AgentText.Minutes(TimeSpan.FromMinutes(12.7)));
        Assert.Equal("2 hr", AgentText.Minutes(TimeSpan.FromMinutes(120)));
        Assert.Equal("1 hr 5 min", AgentText.Minutes(TimeSpan.FromMinutes(65)));
    }
}
