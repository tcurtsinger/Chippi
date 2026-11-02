// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;

namespace Chippi.Features.AgentUsage.Tests;

/// <summary>
/// What the loop's 5-second turn costs with no check due, on real-size fixtures: 12 Claude Code sessions, 8 open Codex
/// threads with 1 MB logs and 40 older logs. Writes a report to the file CHIPPI_AGENTS_SCAN_TIMINGS names, and runs only then.
/// </summary>
public sealed class ScanTimings
{
    [Fact]
    public void Measure()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_AGENTS_SCAN_TIMINGS") is not { Length: > 0 } report)
            return;
        using var h = new Harness();
        var now = Harness.Start;
        for (int i = 0; i < 12; i++)
        {
            h.Processes.Running[100 + i] = null;
            h.Write($".claude/sessions/{100 + i}.json",
                $$"""{"pid":{{100 + i}},"sessionId":"s{{i}}","cwd":"X:\\code\\p{{i}}","status":"{{(i % 3 == 0 ? "idle" : "busy")}}","statusUpdatedAt":{{now.AddMinutes(-i).ToUnixTimeMilliseconds()}}}""");
        }
        string filler = string.Concat(Enumerable.Repeat($$$"""{"timestamp":"2026-11-01T22:00:00Z","type":"response_item","payload":{"type":"message","text":"{{{new string('x', 2000)}}}"}}""" + "\n", 500));
        for (int i = 0; i < 48; i++)
        {
            string thread = $"01a1996d-{i:0000}-7000-8000-000000000000";
            string day = i < 8 ? "01" : $"{(i % 28) + 1:00}";
            string limits = """{"timestamp":"2026-11-01T22:40:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":9,"window_minutes":10080,"resets_at":1793801690},"plan_type":"pro"}}}""";
            h.Write($".codex/sessions/2026/{(i < 8 ? "11" : "10")}/{day}/rollout-x-{thread}.jsonl",
                """{"timestamp":"2026-11-01T22:00:00Z","type":"session_meta","payload":{"cwd":"X:\\code\\c"}}""" + "\n" + filler + limits + "\n"
                + """{"timestamp":"2026-11-01T22:41:00Z","type":"event_msg","payload":{"type":"task_started"}}""" + "\n");
            if (i < 8)
            {
                h.Write($".codex/thread-writer-locks/{thread}.lock", "");
                h.Processes.Locked.Add($"{thread}.lock");
                h.Processes.Holders[$"{thread}.lock"] = 500 + i;
            }
        }
        h.Step(); // the first turn reads every log's end once
        var first = Stopwatch.StartNew();
        h.Step();
        first.Stop();
        const int Turns = 200;
        var warm = Stopwatch.StartNew();
        for (int i = 0; i < Turns; i++)
            h.Step();
        warm.Stop();
        File.WriteAllLines(report,
        [
            $"{h.Module.Sessions.Count} sessions ({h.Module.Sessions.Count(s => s.Agent == AgentKind.Codex)} Codex), Codex limits from logs: {h.Module.Agents.Single(a => a.Agent == AgentKind.Codex).Reading?.Windows.Single().Percent}%",
            $"second turn {first.Elapsed.TotalMilliseconds:0.00} ms; warm turn {warm.Elapsed.TotalMilliseconds / Turns:0.00} ms (mean of {Turns})",
            $"every 5 s: {warm.Elapsed.TotalMilliseconds / Turns / 5000 * 100:0.000}% of one core",
        ]);
    }
}
