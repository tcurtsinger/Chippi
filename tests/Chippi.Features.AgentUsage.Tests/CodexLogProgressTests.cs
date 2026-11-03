// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Features.AgentUsage.Readers;
using Chippi.Features.AgentUsage.Sessions;

namespace Chippi.Features.AgentUsage.Tests;

public sealed class CodexLogProgressTests
{
    private const string Started = """{"timestamp":"2026-11-03T15:00:00Z","type":"event_msg","payload":{"type":"task_started"}}""";
    private const string Limits = """{"timestamp":"2026-11-03T14:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":34}}}}""";

    [Fact]
    public async Task A_long_record_before_the_latest_turn_does_not_block_the_first_scan()
    {
        using var home = new TempFolder();
        string longRecord = "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"text\":\"" + new string('x', 369839) + "\"}}";
        string log = home.Write("rollout.jsonl", string.Join('\n', Limits, longRecord, Started, ""));
        var logs = new CodexLogs(home.Path);
        var scan = Task.Run(() => (logs.LastTurn(log), logs.Limits([log])));
        try
        {
            var (turn, usage) = await scan.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(AgentSessionState.Working, turn?.Status);
            Assert.Equal(34, usage!.Windows.Single().Used);
        }
        finally
        {
            // An old reader loops on the same byte range. Truncating this fixture makes that read finish too.
            if (!scan.IsCompleted)
            {
                using var file = new FileStream(log, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                file.SetLength(0);
                await scan.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Fact]
    public void An_appended_event_crossing_blocks_is_only_applied_after_its_newline()
    {
        using var home = new TempFolder();
        string log = home.Write("rollout.jsonl", Limits + "\n");
        var logs = new CodexLogs(home.Path);
        Assert.Null(logs.LastTurn(log));
        string record = Started.Replace("\"task_started\"",
            $"\"task_started\",\"extra\":\"{new string('x', CodexLogs.Chunk + 1024)}\"", StringComparison.Ordinal);
        File.AppendAllText(log, record + "\r");
        Assert.Null(logs.LastTurn(log));
        File.AppendAllText(log, "\n");
        Assert.Equal(AgentSessionState.Working, logs.LastTurn(log)?.Status);
        Assert.Equal(34, logs.Limits([log])!.Windows.Single().Used);
    }

    [Fact]
    public void An_oversized_unfinished_append_keeps_the_last_known_state_and_skips_to_the_next_event()
    {
        using var home = new TempFolder();
        string log = home.Write("rollout.jsonl", Limits + "\n" + Started + "\n");
        var logs = new CodexLogs(home.Path);
        Assert.NotNull(logs.Limits([log]));
        using (var file = new FileStream(log, FileMode.Open, FileAccess.Write, FileShare.Read))
            file.SetLength(CodexLogs.Back + CodexLogs.Chunk); // overlong record crossing the retained tail boundary
        Assert.Equal(AgentSessionState.Working, logs.LastTurn(log)?.Status);
        Assert.Equal(34, logs.Limits([log])!.Windows.Single().Used);
        File.AppendAllText(log, "more data\n");
        Assert.Equal(AgentSessionState.Working, logs.LastTurn(log)?.Status);
        File.AppendAllText(log, Started.Replace("task_started", "task_complete", StringComparison.Ordinal) + "\n");
        Assert.Equal(AgentSessionState.Idle, logs.LastTurn(log)?.Status);
        Assert.Equal(34, logs.Limits([log])!.Windows.Single().Used);
    }

    [Fact]
    public void Stopping_an_in_progress_scan_releases_it_before_the_next_log()
    {
        using var home = new TempFolder();
        using var stop = new CancellationTokenSource();
        var logs = new CodexLogs(home.Path);
        string log = home.Write("rollout.jsonl", Limits + "\n");
        IEnumerable<string> OpenLogs()
        {
            yield return log;
            stop.Cancel();
            yield return log;
            throw new InvalidOperationException("Scan continued after cancellation.");
        }
        Assert.Throws<OperationCanceledException>(() => logs.Limits(OpenLogs(), stop.Token));
        Assert.Throws<OperationCanceledException>(() => logs.LastTurn(log, stop.Token));
        Assert.Throws<OperationCanceledException>(() => logs.Find("thread", ct: stop.Token));
    }
}
