// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.AgentUsage.Readers;
using Chippi.Features.AgentUsage.Sessions;

namespace Chippi.Features.AgentUsage.Tests;

public sealed class ClaudeSessionsTests
{
    private static readonly DateTimeOffset Now = Harness.Start;

    private static string Session(int pid, string status, DateTimeOffset since, string? name = "Phase 5.5: build AI agent usage", string cwd = "X:\\\\code\\\\chippi", long proc = 134380504408940117) =>
        $$"""{"pid":{{pid}},"sessionId":"s-{{pid}}","cwd":"{{cwd}}","procStart":"{{proc}}","kind":"interactive","entrypoint":"claude-desktop",{{(name is null ? "" : $"\"name\":\"{name}\",")}}"status":"{{status}}","updatedAt":{{since.ToUnixTimeMilliseconds()}},"statusUpdatedAt":{{since.ToUnixTimeMilliseconds()}}}""";

    [Fact]
    public void Busy_works_idle_waits_and_each_names_its_session_or_folder()
    {
        using var home = new TempFolder();
        home.Write("sessions/100.json", Session(100, "busy", Now.AddMinutes(-12)));
        home.Write("sessions/200.json", Session(200, "idle", Now.AddMinutes(-3), name: null, cwd: "X:\\\\code\\\\packrat"));
        var processes = new FakeProcesses { Running = { [100] = 134380504408940117, [200] = 134380504408940117 } };

        var sessions = new ClaudeSessions(home.Path, processes).Read(Now, _ => false).OrderBy(s => s.ProcessId).ToList();

        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].Working);
        Assert.Equal("Phase 5.5: build AI agent usage", sessions[0].Name);
        Assert.Equal("Working · 12 min", sessions[0].State(Now));
        Assert.Equal("claude-desktop", sessions[0].Entrypoint);
        Assert.False(sessions[1].Working);
        Assert.Equal("packrat", sessions[1].Name);
        Assert.Equal("Waiting on you", sessions[1].State(Now));
    }

    [Fact]
    public void A_gone_process_a_reused_pid_and_a_check_of_Chippis_own_are_no_sessions()
    {
        using var home = new TempFolder();
        home.Write("sessions/100.json", Session(100, "busy", Now));
        home.Write("sessions/200.json", Session(200, "busy", Now));
        home.Write("sessions/300.json", Session(300, "busy", Now));
        var processes = new FakeProcesses { Running = { [200] = 999, [300] = null } };

        var sessions = new ClaudeSessions(home.Path, processes).Read(Now, pid => pid == 300);

        Assert.Empty(sessions); // 100 has gone, 200 is another process now, 300 is Chippi's own check
    }

    [Fact]
    public void Waiting_over_30_minutes_leaves_the_list_but_working_never_does()
    {
        using var home = new TempFolder();
        home.Write("sessions/100.json", Session(100, "idle", Now.AddMinutes(-31)));
        home.Write("sessions/200.json", Session(200, "busy", Now.AddMinutes(-90)));
        var processes = new FakeProcesses { Running = { [100] = null, [200] = null } };

        var session = Assert.Single(new ClaudeSessions(home.Path, processes).Read(Now, _ => false));
        Assert.Equal(200, session.ProcessId);
        Assert.Equal("Working · 1 hr 30 min", session.State(Now));
    }

    [Fact]
    public void Only_pid_json_files_are_opened_never_the_key_files()
    {
        using var home = new TempFolder();
        // A .key file holds a token; one shaped like a session would show if it were ever read.
        home.Write("sessions/100.ca2836daa8b3e9e965322e088dae079f99c1f6574355e0fc97c73fa6480b8063.key", Session(100, "busy", Now));
        home.Write("sessions/100.json.bak", Session(100, "busy", Now));
        home.Write("sessions/notes.json", Session(100, "busy", Now));
        var processes = new FakeProcesses { Running = { [100] = null } };

        Assert.Empty(new ClaudeSessions(home.Path, processes).Read(Now, _ => false));
    }

    [Fact]
    public void A_file_with_odd_values_leaves_the_others_listed()
    {
        using var home = new TempFolder();
        home.Write("sessions/100.json", Session(100, "busy", Now));
        home.Write("sessions/200.json", """{"pid":"200","status":"busy","statusUpdatedAt":"soon"}""");
        home.Write("sessions/300.json", """{"pid":300,"status":"busy","statusUpdatedAt":99999999999999999}""");
        home.Write("sessions/400.json", "[]");
        var processes = new FakeProcesses { Running = { [100] = 134380504408940117, [200] = null, [300] = null } };

        var sessions = new ClaudeSessions(home.Path, processes).Read(Now, _ => false);

        Assert.Equal([100, 300], sessions.Select(s => s.ProcessId!.Value).Order());
    }

    [Fact]
    public void No_sessions_folder_is_no_sessions() =>
        Assert.Empty(new ClaudeSessions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), new FakeProcesses()).Read(Now, _ => false));
}

public sealed class CodexSessionsTests
{
    private const string Open = "01a1996d-5825-71d1-8fa7-2fccf5ed27b1";
    private const string Closed = "01a198c8-6335-7a51-8f7c-cea695401cc9";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-01T23:40:00Z");

    private static string Turn(string at, string type) => $$$"""{"timestamp":"{{{at}}}","type":"event_msg","payload":{"type":"{{{type}}}"}}""";

    private static (TempFolder Home, FakeProcesses Processes) Fixture(string lastType, string lastAt)
    {
        var home = new TempFolder();
        home.Write($"thread-writer-locks/{Open}.lock", "");
        home.Write($"thread-writer-locks/{Closed}.lock", "");
        home.Write("thread-writer-locks/.coordination.lock", "");
        home.Write($"sessions/2026/11/01/rollout-2026-11-01T17-24-02-{Open}.jsonl", CodexLogsTests.Log(Turn("2026-11-01T23:24:03Z", "task_started"), Turn(lastAt, lastType)));
        home.Write($"sessions/2026/11/01/rollout-2026-11-01T14-23-51-{Closed}.jsonl", CodexLogsTests.Log(Turn("2026-11-01T23:30:00Z", "task_started")));
        var processes = new FakeProcesses { Locked = { $"{Open}.lock", ".coordination.lock" }, Holders = { [$"{Open}.lock"] = 4242 } };
        return (home, processes);
    }

    [Fact]
    public void A_thread_open_in_a_running_Codex_is_a_session_named_for_its_folder()
    {
        var (home, processes) = Fixture("task_started", "2026-11-01T23:30:35Z");
        using var _ = home;
        var codex = new CodexSessions(home.Path, new CodexLogs(home.Path), processes);

        var session = Assert.Single(codex.Read(Now, _ => false));

        Assert.Equal(AgentKind.Codex, session.Agent);
        Assert.Equal(Open, session.Id);
        Assert.Equal("glidenexus", session.Name);
        Assert.True(session.Working);
        Assert.Equal("Working · 9 min", session.State(Now));
        Assert.Equal(4242, session.ProcessId);
        Assert.Equal("vscode", session.Entrypoint);
        Assert.Single(codex.OpenLogs);
    }

    [Fact]
    public void A_finished_turn_waits_on_you_until_30_minutes_pass()
    {
        var (home, processes) = Fixture("task_complete", "2026-11-01T23:31:00Z");
        using var _ = home;
        var codex = new CodexSessions(home.Path, new CodexLogs(home.Path), processes);

        Assert.False(Assert.Single(codex.Read(Now, _ => false)).Working);
        Assert.Empty(codex.Read(Now.AddMinutes(25), _ => false));
    }

    [Fact]
    public void The_threads_own_name_wins_over_its_folder()
    {
        var (home, processes) = Fixture("task_started", "2026-11-01T23:30:35Z");
        using var _ = home;
        home.Write("session_index.jsonl", $$"""
            {"id":"{{Closed}}","thread_name":"Other","updated_at":"2026-11-01T20:23:46Z"}
            {"id":"{{Open}}","thread_name":"Archer capability","updated_at":"2026-11-01T23:30:46Z"}
            """);
        Assert.Equal("Archer capability", Assert.Single(new CodexSessions(home.Path, new CodexLogs(home.Path), processes).Read(Now, _ => false)).Name);
    }

    [Fact]
    public void A_thread_whose_log_isnt_there_yet_is_looked_for_again_after_5_minutes()
    {
        using var home = new TempFolder();
        home.Write($"thread-writer-locks/{Open}.lock", "");
        var processes = new FakeProcesses { Locked = { $"{Open}.lock" } };
        var codex = new CodexSessions(home.Path, new CodexLogs(home.Path), processes);

        Assert.Empty(codex.Read(Now, _ => false));
        home.Write($"sessions/2026/11/01/rollout-2026-11-01T17-24-02-{Open}.jsonl", CodexLogsTests.Log(Turn("2026-11-01T23:30:35Z", "task_started")));
        Assert.Empty(codex.Read(Now.AddMinutes(1), _ => false));
        Assert.Single(codex.Read(Now + CodexSessions.LookAgain, _ => false));
    }

    [Fact]
    public void Chippis_own_check_is_no_session()
    {
        var (home, processes) = Fixture("task_started", "2026-11-01T23:30:35Z");
        using var _ = home;
        Assert.Empty(new CodexSessions(home.Path, new CodexLogs(home.Path), processes).Read(Now, pid => pid == 4242));
    }
}
