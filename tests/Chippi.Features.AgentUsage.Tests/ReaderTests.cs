// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;
using Chippi.Features.AgentUsage.Readers;

namespace Chippi.Features.AgentUsage.Tests;

public sealed class ClaudeLimitsTests
{
    private static readonly DateTimeOffset Now = Harness.Start;

    [Fact]
    public void Starts_Claude_Code_with_no_tools_servers_hooks_or_settings()
    {
        var args = ClaudeLimits.Arguments;
        Assert.Equal("--print", args[0]);
        Assert.Equal("", args[args.ToList().IndexOf("--tools") + 1]);
        Assert.Equal("""{"mcpServers":{}}""", args[args.ToList().IndexOf("--mcp-config") + 1]);
        Assert.Contains("--strict-mcp-config", args);
        Assert.Contains("--no-session-persistence", args);
        Assert.Equal("", args[args.ToList().IndexOf("--setting-sources") + 1]);
        Assert.Equal("""{"disableAllHooks":true}""", args[args.ToList().IndexOf("--settings") + 1]);
    }

    [Fact]
    public async Task Asks_initialize_then_get_usage_without_its_behaviors()
    {
        var claude = Answers.SignedOutClaude();
        await ClaudeLimits.CheckAsync(claude, Now, default);

        Assert.Equal(2, claude.Sent.Count);
        using var init = JsonDocument.Parse(claude.Sent[0]);
        Assert.Equal("control_request", init.RootElement.GetProperty("type").GetString());
        Assert.Equal("initialize", init.RootElement.GetProperty("request").GetProperty("subtype").GetString());
        using var usage = JsonDocument.Parse(claude.Sent[1]);
        Assert.Equal("get_usage", usage.RootElement.GetProperty("request").GetProperty("subtype").GetString());
        Assert.True(usage.RootElement.GetProperty("request").GetProperty("skip_behaviors").GetBoolean());
    }

    [Fact]
    public async Task No_sign_in_is_signed_out_as_on_the_owners_PC()
    {
        var result = await ClaudeLimits.CheckAsync(Answers.SignedOutClaude(), Now, default);
        Assert.Equal(CheckState.SignedOut, result.State);
        Assert.Null(result.Reading);
    }

    [Fact]
    public async Task Signed_in_reads_each_window_with_its_reset_and_the_plan()
    {
        var result = await ClaudeLimits.CheckAsync(Answers.SignedInClaude(42, 23), Now, default);

        Assert.Equal(CheckState.Ok, result.State);
        var reading = result.Reading!;
        Assert.Equal("Max", reading.Plan);
        Assert.Equal(Now, reading.At);
        Assert.Equal(["5-hour", "Weekly"], reading.Windows.Select(w => w.Label));
        Assert.Equal([42.0, 23.0], reading.Windows.Select(w => w.Used));
        Assert.Equal(DateTimeOffset.Parse("2026-11-02T00:10:00Z"), reading.Windows[0].ResetsAt);
    }

    [Fact]
    public void Per_model_weekly_windows_come_after_the_weekly_one()
    {
        using var doc = JsonDocument.Parse("""
            {"subscription_type":"max","rate_limits_available":true,"rate_limits":{
              "seven_day_sonnet":{"utilization":8,"resets_at":"2026-11-05T15:00:00Z"},
              "seven_day_opus":{"utilization":61,"resets_at":"2026-11-05T15:00:00Z"},
              "seven_day":{"utilization":23,"resets_at":"2026-11-05T15:00:00Z"},
              "five_hour":{"utilization":42,"resets_at":"2026-11-02T00:10:00Z"}}}
            """);
        var result = ClaudeLimits.Parse(doc.RootElement, "claude.ai", Now);
        Assert.Equal(["5-hour", "Weekly", "Opus weekly", "Sonnet weekly"], result.Reading!.Windows.Select(w => w.Label));
    }

    [Fact]
    public void Spend_and_anything_else_that_isnt_a_limits_window_is_left_out()
    {
        using var doc = JsonDocument.Parse("""
            {"subscription_type":"team","rate_limits_available":true,"rate_limits":{
              "five_hour":{"utilization":0,"resets_at":null},
              "seven_day":{"utilization":65,"resets_at":"2026-11-03T14:00:00Z"},
              "spend":{"utilization":0}}}
            """);
        var reading = ClaudeLimits.Parse(doc.RootElement, "claude.ai", Now).Reading!;
        Assert.Equal("Team", reading.Plan);
        Assert.Equal(["5-hour", "Weekly"], reading.Windows.Select(w => w.Label));
        Assert.Null(reading.Windows[0].ResetsAt);
    }

    [Fact]
    public async Task A_request_it_doesnt_know_is_too_old()
    {
        var claude = new ScriptedConversation(line =>
            line.Contains("\"initialize\"") ? [Answers.ClaudeInitializeSignedIn]
            : ["""{"type":"control_response","response":{"subtype":"error","request_id":"chippi-usage","error":"Unknown control request subtype: get_usage"}}"""]);
        Assert.Equal(CheckState.TooOld, (await ClaudeLimits.CheckAsync(claude, Now, default)).State);
    }

    [Fact]
    public async Task Closing_without_an_answer_fails_and_other_lines_are_passed_by()
    {
        var claude = new ScriptedConversation(line => line.Contains("\"initialize\"")
            ? ["not json", """{"type":"system","subtype":"init"}""", Answers.ClaudeInitializeSignedIn]
            : []);
        var result = await ClaudeLimits.CheckAsync(claude, Now, default);
        Assert.Equal(CheckState.Failed, result.State);
        Assert.Contains("get_usage", result.Error);
    }
}

public sealed class ClaudeHistoryTests
{
    [Fact]
    public void Reads_the_newest_sample_with_no_reset_times()
    {
        using var folder = new TempFolder();
        string path = folder.Write("plan-usage-history.json", """
            {"version":2,"samples":[
              {"t":1791011387809,"org":"a","u":{"fh":0,"sd":45}},
              {"t":1793369696878,"org":"b","u":{"fh":12,"sd":23,"so":61}},
              {"t":1792000000000,"org":"b","u":{"fh":90,"sd":30}}]}
            """);

        var reading = ClaudeHistory.Read(path)!;

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793369696878), reading.At);
        Assert.Null(reading.Plan);
        Assert.Equal(["5-hour", "Weekly", "Opus weekly"], reading.Windows.Select(w => w.Label));
        Assert.Equal([12.0, 23.0, 61.0], reading.Windows.Select(w => w.Used));
        Assert.All(reading.Windows, w => Assert.Null(w.ResetsAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":2,\"samples\":[]}")]
    [InlineData("[1,2]")]
    public void A_file_with_no_sample_gives_nothing(string text)
    {
        using var folder = new TempFolder();
        Assert.Null(ClaudeHistory.Read(folder.Write("h.json", text)));
    }

    [Fact]
    public void No_file_gives_nothing() => Assert.Null(ClaudeHistory.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
}

public sealed class CodexLimitsTests
{
    private static readonly DateTimeOffset Now = Harness.Start;

    [Fact]
    public async Task Reads_the_owners_answer_past_a_notification()
    {
        var codex = Answers.Codex();
        var result = await CodexLimits.CheckAsync(codex, "0.1.0", Now, default);

        Assert.Equal(CheckState.Ok, result.State);
        var reading = result.Reading!;
        Assert.Equal("Pro", reading.Plan);
        var weekly = Assert.Single(reading.Windows);
        Assert.Equal("Weekly", weekly.Label);
        Assert.Equal(9, weekly.Used);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793801690), weekly.ResetsAt);
        Assert.Equal(["initialize", "initialized", "account/read", "account/rateLimits/read"],
            codex.Sent.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("method").GetString()));
        // Only reads: it never asks Codex to refresh its sign-in.
        Assert.False(JsonDocument.Parse(codex.Sent[2]).RootElement.GetProperty("params").GetProperty("refreshToken").GetBoolean());
    }

    [Fact]
    public async Task No_account_is_signed_out()
    {
        var codex = Answers.Codex(account: """{"id":2,"result":{"account":null,"requiresOpenaiAuth":true}}""");
        Assert.Equal(CheckState.SignedOut, (await CodexLimits.CheckAsync(codex, "0.1.0", Now, default)).State);
    }

    [Fact]
    public async Task A_method_it_doesnt_have_is_too_old()
    {
        var codex = Answers.Codex(limits: """{"id":3,"error":{"code":-32601,"message":"Method not found"}}""");
        Assert.Equal(CheckState.TooOld, (await CodexLimits.CheckAsync(codex, "0.1.0", Now, default)).State);
    }

    [Fact]
    public void Five_hour_and_weekly_windows_shortest_first()
    {
        using var doc = JsonDocument.Parse("""
            {"primary":{"used_percent":64.0,"window_minutes":10080,"resets_at":1793801690},
             "secondary":{"used_percent":12.5,"window_minutes":300,"resets_at":1793600000},"plan_type":"plus"}
            """);
        var reading = CodexLimits.Parse(doc.RootElement, Now);
        Assert.Equal("Plus", reading.Plan);
        Assert.Equal(["5-hour", "Weekly"], reading.Windows.Select(w => w.Label));
        Assert.Equal(13, reading.Windows[0].Percent);
    }
}

public sealed class CodexLogsTests
{
    private const string Thread = "01a1996d-5825-71d1-8fa7-2fccf5ed27b1";

    private static string Line(string at, string payload) => $$"""{"timestamp":"{{at}}","type":"event_msg","payload":{{payload}}}""";

    private static string Limits(double used) =>
        $$$"""{"type":"token_count","info":{},"rate_limits":{"limit_id":"codex","primary":{"used_percent":{{{used}}},"window_minutes":10080,"resets_at":1793801690},"secondary":null,"plan_type":"pro"}}""";

    internal static string Log(params string[] lines) =>
        string.Join('\n', new[] { """{"timestamp":"2026-11-01T23:24:02.962Z","type":"session_meta","payload":{"id":"x","cwd":"X:\\code\\glidenexus","source":"vscode"}}""" }.Concat(lines)) + "\n";

    [Fact]
    public void The_last_turns_limits_and_whether_it_still_works()
    {
        using var home = new TempFolder();
        string log = home.Write($"sessions/2026/11/01/rollout-2026-11-01T17-24-02-{Thread}.jsonl", Log(
            Line("2026-11-01T23:24:03.221Z", """{"type":"task_started","turn_id":"a"}"""),
            Line("2026-11-01T23:24:07.143Z", Limits(8)),
            Line("2026-11-01T23:25:00.512Z", Limits(9)),
            Line("2026-11-01T23:25:00.525Z", """{"type":"task_complete","turn_id":"a"}"""),
            Line("2026-11-01T23:30:35.080Z", """{"type":"task_started","turn_id":"b"}""")));
        var logs = new CodexLogs(home.Path);

        var limits = logs.Limits([])!;
        Assert.Equal(9, limits.Windows.Single().Used);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T23:25:00.512Z"), limits.At);
        Assert.Equal(new CodexLogs.Turn(true, DateTimeOffset.Parse("2026-11-01T23:30:35.080Z")), logs.LastTurn(log));

        File.AppendAllText(log, Line("2026-11-01T23:31:00.870Z", """{"type":"task_complete","turn_id":"b"}""") + "\n");
        Assert.Equal(new CodexLogs.Turn(false, DateTimeOffset.Parse("2026-11-01T23:31:00.870Z")), logs.LastTurn(log));
    }

    [Fact]
    public void An_aborted_turn_waits_on_you_and_a_half_written_line_is_passed_by()
    {
        using var home = new TempFolder();
        string log = home.Write($"sessions/2026/11/01/rollout-x-{Thread}.jsonl",
            Log(Line("2026-11-01T23:24:03Z", """{"type":"task_started"}"""), Line("2026-11-01T23:26:00Z", """{"type":"turn_aborted"}""")) + """{"timestamp":"2026-11-01T23:27:00Z","type":"event_msg","payload":{"type":"task_star""");
        Assert.False(new CodexLogs(home.Path).LastTurn(log)!.Working);
    }

    [Fact]
    public void Only_the_end_of_a_long_log_is_read()
    {
        using var home = new TempFolder();
        string filler = Line("2026-11-01T23:00:00Z", $$"""{"type":"agent_message","message":"{{new string('x', 4000)}}"}""");
        string log = home.Write($"sessions/2026/11/01/rollout-x-{Thread}.jsonl", Log(
            [Line("2026-11-01T23:00:00Z", Limits(70)), .. Enumerable.Repeat(filler, 100), Line("2026-11-01T23:30:00Z", """{"type":"task_complete"}""")]));
        Assert.True(new FileInfo(log).Length > CodexLogs.Tail);
        var logs = new CodexLogs(home.Path);
        Assert.Null(logs.Limits([])); // the limits line is past the tail
        Assert.False(logs.LastTurn(log)!.Working);
    }

    [Fact]
    public void Finds_a_threads_log_in_its_days_folder()
    {
        using var home = new TempFolder();
        string log = home.Write($"sessions/2026/11/01/rollout-2026-11-01T17-24-02-{Thread}.jsonl", Log());
        home.Write("sessions/2026/10/30/rollout-2026-10-30T10-00-00-01a18000-0000-7000-8000-000000000000.jsonl", Log());
        Assert.Equal(Path.GetFullPath(log), Path.GetFullPath(new CodexLogs(home.Path).Find(Thread)!));
        Assert.Null(new CodexLogs(home.Path).Find("01a19999-0000-7000-8000-000000000000"));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 23, 24, 2, TimeSpan.Zero), CodexLogs.Started(Thread)!.Value.AddMilliseconds(-CodexLogs.Started(Thread)!.Value.Millisecond));
    }
}
