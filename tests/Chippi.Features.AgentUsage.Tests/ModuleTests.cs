// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.AgentUsage.Sessions;
using Chippi.Features.AgentUsage.Views;

namespace Chippi.Features.AgentUsage.Tests;

public sealed class ModuleTests
{
    private static readonly TimeSpan First = UsageSchedule.First;

    private static string History(double fiveHour, double weekly, DateTimeOffset at) =>
        $$$"""{"version":2,"samples":[{"t":{{{at.ToUnixTimeMilliseconds()}}},"org":"o","u":{"fh":{{{fiveHour}}},"sd":{{{weekly}}}}}]}""";

    private static string ClaudeSession(int pid, string status, DateTimeOffset since) =>
        $$"""{"pid":{{pid}},"sessionId":"s{{pid}}","cwd":"X:\\code\\chippi","status":"{{status}}","statusUpdatedAt":{{since.ToUnixTimeMilliseconds()}},"entrypoint":"claude-desktop"}""";

    [Fact]
    public void The_home_button_is_Agents_with_the_gauge()
    {
        using var h = new Harness();
        Assert.Equal("agent-usage", h.Module.Descriptor.Id);
        Assert.Equal("AI agent usage", h.Module.Descriptor.Name);
        Assert.Equal("Agents", h.Tile.Spec.Name);
        Assert.Equal("Gauge", h.Tile.Spec.Icon);
        Assert.Equal("None", h.Tile.Status);
        Assert.NotNull(h.Ui.SettingsPage);
    }

    [Fact]
    public void Checks_wait_a_moment_then_run_every_5_minutes_or_every_minute_while_a_page_shows()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Codex] = () => Answers.Codex());
        int Checks() => h.Programs.Started.Count(s => s.Agent == AgentKind.Codex);

        h.Step();
        Assert.Equal(0, Checks());
        h.Step(First);
        Assert.Equal(1, Checks());
        Assert.Equal(["app-server"], h.Programs.Started[0].Arguments);
        h.Step(TimeSpan.FromMinutes(1));
        Assert.Equal(1, Checks());
        h.Step(TimeSpan.FromMinutes(4));
        Assert.Equal(2, Checks());

        h.Module.Showing(true);
        h.Step(TimeSpan.FromMinutes(1));
        Assert.Equal(3, Checks());
        h.Module.Showing(false);
        h.Step(TimeSpan.FromMinutes(1));
        Assert.Equal(3, Checks());
    }

    [Fact]
    public void Nothing_is_read_or_checked_while_nobody_can_see_the_screen()
    {
        bool unseen = true;
        using var h = new Harness(p => p.Scripts[AgentKind.Codex] = () => Answers.Codex(), () => unseen);
        h.Write(".claude/sessions/100.json", ClaudeSession(100, "busy", Harness.Start));
        h.Processes.Running[100] = null;

        h.Step(TimeSpan.FromMinutes(20));
        Assert.Empty(h.Programs.Started);
        Assert.Empty(h.Module.Sessions);

        unseen = false;
        h.Step();
        Assert.Single(h.Programs.Started);
        Assert.Single(h.Module.Sessions);
    }

    [Fact]
    public void A_failing_check_waits_longer_before_the_next()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Codex] = () => new ScriptedConversation(_ => []));
        h.Step(First);
        Assert.Equal(CheckState.Failed, h.Module.All[AgentKind.Codex].Check);
        h.Step(TimeSpan.FromMinutes(5));
        Assert.Single(h.Programs.Started);
        h.Step(TimeSpan.FromMinutes(5));
        Assert.Equal(2, h.Programs.Started.Count);
    }

    [Fact]
    public void A_check_that_throws_anything_fails_and_still_waits_before_the_next()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Codex] = () => throw new FormatException("odd"));
        h.Step(First);
        Assert.Equal(CheckState.Failed, h.Module.All[AgentKind.Codex].Check);
        h.Step(TimeSpan.FromSeconds(5));
        h.Step(TimeSpan.FromMinutes(5));
        Assert.Single(h.Programs.Started);
    }

    [Fact]
    public async Task A_check_that_hangs_times_out_and_fails()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Codex] = () => new ScriptedConversation(_ => []) { Hangs = true });
        h.Time.Advance(First);
        var step = h.Module.StepAsync(default);
        Assert.False(step.IsCompleted);
        h.Time.Advance(AgentUsageModule.CheckTimeout);
        await step.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(CheckState.Failed, h.Module.All[AgentKind.Codex].Check);
    }

    [Fact]
    public void Claude_signed_out_shows_the_Claude_apps_last_check_as_of_its_time()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Claude] = Answers.SignedOutClaude);
        var then = Harness.Start.AddDays(-2);
        h.Write("Claude/plan-usage-history.json", History(0, 23, then));

        h.Step();
        var before = h.Module.Agents[0];
        Assert.Equal(AgentKind.Claude, before.Agent);
        Assert.Equal(23, before.Reading!.Windows[1].Used); // there before the first check
        h.Step(First);

        var claude = h.Module.Agents[0];
        Assert.Equal(CheckState.SignedOut, claude.Check);
        Assert.True(claude.Found);
        Assert.False(claude.Fresh);
        Assert.Equal(then, claude.Reading!.At);
        Assert.Equal(AgentText.AsOf(then, h.Module.Now), AgentLines.State(claude, h.Module.Now).Text);
        Assert.Equal("Signed out", AgentLines.Caption(claude));
        Assert.Equal("Claude 23%", h.Tile.Status);
        Assert.Equal(TimeSpan.FromMinutes(10), UsageSchedule.Delay(false, 1, claude.Check));
    }

    [Fact]
    public void Codex_signed_out_lists_no_rows()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Codex] = () => Answers.Codex(account: """{"id":2,"result":{"account":null}}"""));
        h.Step(First);
        var codex = h.Module.Agents.Single(a => a.Agent == AgentKind.Codex);
        Assert.Empty(AgentLines.Rows(codex));
        Assert.Equal(("Signed out", true), AgentLines.State(codex, h.Module.Now));
    }

    private static string CodexLog(double used, DateTimeOffset at) =>
        """{"timestamp":"2026-11-01T22:00:00Z","type":"session_meta","payload":{"cwd":"X:\\code\\c"}}""" + "\n"
        + $$$$"""{"timestamp":"{{{{at.UtcDateTime:O}}}}","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":{{{{used}}}},"window_minutes":10080,"resets_at":1793801690},"plan_type":"pro"}}}""" + "\n";

    [Fact]
    public void An_agent_not_installed_shows_nothing_from_the_logs_it_left()
    {
        using var h = new Harness();
        h.Write(".codex/sessions/2026/11/01/rollout-x-01a1996d-5825-71d1-8fa7-2fccf5ed27b1.jsonl", CodexLog(90, Harness.Start));
        h.Step(First);
        var codex = h.Module.All[AgentKind.Codex];
        Assert.False(codex.Found);
        Assert.Null(codex.Reading);
        Assert.Equal("None", h.Tile.Status);
    }

    [Fact]
    public void Codex_signed_out_shows_no_limits_anywhere()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Codex] = () => Answers.Codex(account: """{"id":2,"result":{"account":null}}"""));
        h.Write(".codex/sessions/2026/11/01/rollout-x-01a1996d-5825-71d1-8fa7-2fccf5ed27b1.jsonl", CodexLog(90, Harness.Start));
        h.Step(First);
        var codex = h.Module.Agents.Single(a => a.Agent == AgentKind.Codex);
        Assert.True(codex.Found);
        Assert.Null(codex.Reading);
        Assert.Null(codex.Plan);
        Assert.Equal(("None", false), (h.Tile.Status, h.Tile.NeedsAttention));
    }

    [Fact]
    public void The_Claude_apps_old_file_never_makes_the_first_live_reading_a_reset()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Claude] = () => Answers.SignedInClaude(10, 23));
        h.Write("Claude/plan-usage-history.json", History(95, 23, Harness.Start.AddDays(-3)));
        h.Step();
        h.Step(First);
        Assert.Equal("Claude 23%", h.Tile.Status);
        Assert.Null(h.Activity(AgentBar.AlertKey));
    }

    [Fact]
    public async Task An_alert_waiting_its_turn_never_shows_after_the_stop()
    {
        double used = 70;
        using var h = new Harness(p => p.Scripts[AgentKind.Claude] = () => Answers.SignedInClaude(used, used));
        h.Step(First);
        used = 90; // both windows pass the level: the 5-hour shows, the weekly waits
        h.Step(TimeSpan.FromMinutes(5));
        Assert.Equal("Claude Code · 5-hour", h.Activity(AgentBar.AlertKey)!.Source);

        await h.Module.StopAsync(default);
        h.Time.Advance(AgentUsageModule.AlertTime * 2);
        Assert.Equal("Claude Code · 5-hour", h.Activity(AgentBar.AlertKey)!.Source); // the registry clears it after the stop
    }

    [Fact]
    public void An_agent_with_no_program_and_no_files_has_no_card()
    {
        using var h = new Harness();
        h.Step(First);
        Assert.All(h.Module.Agents, a => Assert.False(a.Found));
        Assert.Equal("Not found", AgentLines.Caption(h.Module.All[AgentKind.Claude]));
    }

    [Fact]
    public void Signed_in_limits_are_fresh_and_the_home_turns_Caution_at_the_level()
    {
        double fiveHour = 42;
        using var h = new Harness(p => p.Scripts[AgentKind.Claude] = () => Answers.SignedInClaude(fiveHour, 23));
        h.Step(First);
        var claude = h.Module.Agents[0];
        Assert.True(claude.Fresh);
        Assert.Equal("Max", claude.Plan);
        Assert.Equal("Signed in · Max", AgentLines.Caption(claude));
        Assert.Equal("Claude 42%", h.Tile.Status);
        Assert.False(h.Tile.NeedsAttention);

        fiveHour = 86;
        h.Step(TimeSpan.FromMinutes(5));
        Assert.Equal("Claude 86%", h.Tile.Status);
        Assert.True(h.Tile.NeedsAttention);
    }

    [Fact]
    public void Passing_the_level_shows_an_alert_for_6_seconds_then_a_reset_the_same_way()
    {
        double fiveHour = 70;
        using var h = new Harness(p => p.Scripts[AgentKind.Claude] = () => Answers.SignedInClaude(fiveHour, 23));
        h.Step(First);
        Assert.Null(h.Activity(AgentBar.AlertKey));

        fiveHour = 86;
        h.Step(TimeSpan.FromMinutes(5));
        var alert = h.Activity(AgentBar.AlertKey)!;
        Assert.True(alert.IsFeedback);
        Assert.Equal(("80% used", "Claude Code · 5-hour"), (alert.Title, alert.Source));
        h.Time.Advance(AgentUsageModule.AlertTime);
        Assert.Null(h.Activity(AgentBar.AlertKey));

        fiveHour = 2;
        h.Step(TimeSpan.FromMinutes(5));
        Assert.Equal("Reset", h.Activity(AgentBar.AlertKey)!.Title);
    }

    [Fact]
    public async Task Alerts_switched_off_never_show()
    {
        double fiveHour = 70;
        using var h = new Harness(p => p.Scripts[AgentKind.Claude] = () => Answers.SignedInClaude(fiveHour, 23));
        await h.Module.ApplyAsync(h.Module.Settings with { NearLimitAlerts = false });
        h.Step(First);
        fiveHour = 90;
        h.Step(TimeSpan.FromMinutes(5));
        Assert.Null(h.Activity(AgentBar.AlertKey));
    }

    [Fact]
    public void Sessions_reach_the_bar_working_below_the_rest_and_waiting_above()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Claude] = () => Answers.SignedInClaude(42, 23));
        h.Processes.Running[100] = null;
        h.Write(".claude/sessions/100.json", ClaudeSession(100, "busy", Harness.Start));
        h.Step(First);

        var working = h.Activity(AgentBar.WorkingKey)!;
        Assert.Equal(("Working · 42%", "Claude Code · chippi"), (working.Title, working.Source));
        Assert.Null(h.Activity(AgentBar.WaitingKey));

        h.Write(".claude/sessions/100.json", ClaudeSession(100, "waiting", h.Module.Now));
        h.Step(TimeSpan.FromSeconds(5));
        Assert.Null(h.Activity(AgentBar.WorkingKey));
        Assert.True(h.Activity(AgentBar.WaitingKey)!.NeedsAttention);
        Assert.False(h.Tile.NeedsAttention); // waiting doesn't mark the home's button

        h.Step(TimeSpan.FromMinutes(31));
        Assert.True(h.Activity(AgentBar.WaitingKey)!.NeedsAttention);
        h.Write(".claude/sessions/100.json", ClaudeSession(100, "idle", h.Module.Now));
        h.Step(TimeSpan.FromSeconds(5));
        Assert.Null(h.Activity(AgentBar.WaitingKey));
        Assert.Equal("Idle", Assert.Single(h.Module.Sessions).State(h.Module.Now));
        h.Step(TimeSpan.FromMinutes(31));
        Assert.Empty(h.Module.Sessions);
        Assert.Null(h.Activity(AgentBar.WaitingKey));
    }

    [Fact]
    public void Only_the_visible_Codex_chat_can_request_attention()
    {
        using var h = new Harness(p => p.Scripts[AgentKind.Codex] = () => Answers.Codex());
        string root = "01a1996d-0101-7111-8111-111111111111", child = "01a1996d-0102-7111-8111-111111111111";
        string Line(string type, object payload) => System.Text.Json.JsonSerializer.Serialize(new { timestamp = Harness.Start, type, payload }) + "\n";
        string Rollout(string id, object source, string lastEvent)
        {
            h.Write($".codex/thread-writer-locks/{id}.lock", "");
            h.Processes.Locked.Add($"{id}.lock");
            return h.Write($".codex/sessions/2026/11/01/rollout-x-{id}.jsonl",
                Line("session_meta", new { id, cwd = "project", source }) + Line("event_msg", new { type = lastEvent }));
        }

        Rollout(child, new { subagent = new { thread_spawn = new { parent_thread_id = root } } }, "task_started");
        var log = Rollout(root, "vscode", "task_complete");
        h.Step(First);
        Assert.Equal(root, Assert.Single(h.Module.Sessions).Id);
        Assert.Null(h.Activity(AgentBar.WorkingKey));
        Assert.Null(h.Activity(AgentBar.WaitingKey));

        File.AppendAllText(log, Line("response_item", new { type = "function_call", name = "request_user_input", call_id = "q", arguments = """{"questions":[{}]}""" }));
        h.Step();
        Assert.True(h.Activity(AgentBar.WaitingKey)!.NeedsAttention);
        File.AppendAllText(log, Line("response_item", new { type = "function_call_output", call_id = "q", output = "answered" }));
        h.Step();
        Assert.Null(h.Activity(AgentBar.WaitingKey));
    }

    [Fact]
    public async Task An_agent_switched_off_is_neither_checked_nor_listed()
    {
        using var h = new Harness(p =>
        {
            p.Scripts[AgentKind.Claude] = Answers.SignedOutClaude;
            p.Scripts[AgentKind.Codex] = () => Answers.Codex();
        });
        await h.Module.ApplyAsync(h.Module.Settings with { Codex = false });
        h.Step(First);
        Assert.DoesNotContain(h.Programs.Started, s => s.Agent == AgentKind.Codex);
        Assert.DoesNotContain(h.Module.Agents, a => a.Agent == AgentKind.Codex);
        Assert.False(h.Settings.Get<AgentUsageSettings>().Codex);
    }

    [Fact]
    public void Switch_to_brings_the_sessions_app_forward()
    {
        using var h = new Harness();
        h.Processes.Running[100] = null;
        h.Processes.Apps[100] = new SessionApp(77, "Claude");
        h.Write(".claude/sessions/100.json", ClaudeSession(100, "idle", Harness.Start));
        h.Step();

        var session = Assert.Single(h.Module.Sessions);
        Assert.Equal("Claude", h.Module.AppOf(session));
        Assert.True(h.Module.SwitchTo(session));
        Assert.Equal([77], h.Processes.Brought);

        h.Processes.Apps.Remove(100);
        Assert.False(h.Module.SwitchTo(session));
        Assert.Equal("Claude", h.Module.AppOf(session)); // from how it was started
    }

    [Fact]
    public void A_level_that_isnt_offered_goes_to_the_nearest()
    {
        Assert.Equal(80, new AgentUsageSettings { WarningLevel = 81 }.Normalized().WarningLevel);
        Assert.Equal(95, new AgentUsageSettings { WarningLevel = 140 }.Normalized().WarningLevel);
        Assert.Equal([50, 55, 60, 65, 70, 75, 80, 85, 90, 95], AgentUsageSettings.Levels);
    }
}
