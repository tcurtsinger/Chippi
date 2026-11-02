// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text;
using System.Text.Json;

namespace Chippi.Features.AgentUsage.Readers;

/// <summary>
/// Codex's limits. Its own server (<c>codex app-server</c>, JSON-RPC over standard input and output) answers
/// <c>account/read</c> and <c>account/rateLimits/read</c>, and Codex signs itself in. Between checks, each turn in its
/// session logs records the same limits. Both give a primary and a secondary window, whichever the plan has, each a share
/// used, its length in minutes and its reset in Unix seconds (checked on 0.166.0, 2026-11-01).
/// </summary>
internal static class CodexLimits
{
    public static IReadOnlyList<string> Arguments { get; } = ["app-server"];

    private const int MethodNotFound = -32601;

    public static async Task<CheckResult> CheckAsync(IAgentConversation codex, string version, DateTimeOffset now, CancellationToken ct)
    {
        await codex.SendAsync("{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"chippi\",\"title\":\"Chippi\",\"version\":\"" + version + "\"}}}", ct).ConfigureAwait(false);
        using (var init = await ReplyAsync(codex, 1, ct).ConfigureAwait(false))
        {
            if (init is null)
                return new(CheckState.Failed, Error: "initialize: no answer");
            if (ErrorOf(init.RootElement) is { } e)
                return new(CheckState.Failed, Error: "initialize: " + e.Message);
        }
        await codex.SendAsync("""{"method":"initialized"}""", ct).ConfigureAwait(false);

        // Without its token refreshed: Chippi only reads.
        await codex.SendAsync("""{"method":"account/read","id":2,"params":{"refreshToken":false}}""", ct).ConfigureAwait(false);
        string? plan;
        using (var account = await ReplyAsync(codex, 2, ct).ConfigureAwait(false))
        {
            if (account is null)
                return new(CheckState.Failed, Error: "account/read: no answer");
            if (ErrorOf(account.RootElement) is { } e)
                return new(e.Code == MethodNotFound ? CheckState.TooOld : CheckState.Failed, Error: "account/read: " + e.Message);
            var result = account.RootElement.GetProperty("result");
            if (!result.TryGetProperty("account", out var who) || who.ValueKind != JsonValueKind.Object)
                return new(CheckState.SignedOut, Error: "no account");
            plan = who.TryGetProperty("planType", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        }

        await codex.SendAsync("""{"method":"account/rateLimits/read","id":3}""", ct).ConfigureAwait(false);
        using var limits = await ReplyAsync(codex, 3, ct).ConfigureAwait(false);
        if (limits is null)
            return new(CheckState.Failed, Error: "account/rateLimits/read: no answer");
        if (ErrorOf(limits.RootElement) is { } error)
            return new(error.Code == MethodNotFound ? CheckState.TooOld : CheckState.Failed, Error: "account/rateLimits/read: " + error.Message);
        if (!limits.RootElement.GetProperty("result").TryGetProperty("rateLimits", out var snapshot) || snapshot.ValueKind != JsonValueKind.Object)
            return new(CheckState.Ok, new UsageReading(AgentText.Plan(plan), [], now));
        var reading = Parse(snapshot, now);
        return new(CheckState.Ok, reading with { Plan = reading.Plan ?? AgentText.Plan(plan) });
    }

    /// <summary>
    /// A limits snapshot, the server's (<c>usedPercent</c>, <c>windowDurationMins</c>, <c>resetsAt</c>, <c>planType</c>) or a
    /// log's (<c>used_percent</c>, <c>window_minutes</c>, <c>resets_at</c>, <c>plan_type</c>). Shorter windows first.
    /// </summary>
    internal static UsageReading Parse(JsonElement snapshot, DateTimeOffset at)
    {
        var windows = new List<(double Minutes, UsageWindow Window)>();
        foreach (string slot in new[] { "primary", "secondary" })
        {
            if (!snapshot.TryGetProperty(slot, out var w) || w.ValueKind != JsonValueKind.Object)
                continue;
            if (ClaudeLimits.Number(w, "usedPercent", "used_percent") is not { } used)
                continue;
            double minutes = ClaudeLimits.Number(w, "windowDurationMins", "window_minutes") ?? 0;
            string label = minutes > 0 ? AgentText.WindowName((int)minutes) : slot == "primary" ? "5-hour" : "Weekly";
            windows.Add((minutes, new UsageWindow(label, used, ClaudeLimits.Time(w, "resetsAt", "resets_at"))));
        }
        string? plan = snapshot.TryGetProperty("planType", out var p) || snapshot.TryGetProperty("plan_type", out p)
            ? p.ValueKind == JsonValueKind.String ? p.GetString() : null : null;
        return new UsageReading(AgentText.Plan(plan), [.. windows.OrderBy(w => w.Minutes).Select(w => w.Window)], at);
    }

    private sealed record RpcError(int Code, string Message);

    private static RpcError? ErrorOf(JsonElement root) =>
        root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
            ? new(e.TryGetProperty("code", out var c) && c.TryGetInt32(out int code) ? code : 0, e.TryGetProperty("message", out var m) ? m.ToString() : "error")
            : null;

    /// <summary>The reply to request <paramref name="id"/>, skipping the server's notifications; null if it closes first.</summary>
    private static async Task<JsonDocument?> ReplyAsync(IAgentConversation codex, int id, CancellationToken ct)
    {
        while (await codex.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var rid) && rid.ValueKind == JsonValueKind.Number
                && rid.TryGetInt32(out int got) && got == id && (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _)))
                return doc;
            doc.Dispose();
        }
        return null;
    }
}

/// <summary>
/// Codex's session logs, <c>~/.codex/sessions/yyyy/MM/dd/rollout-…-&lt;thread&gt;.jsonl</c>: one JSON event a line. The
/// limits are on each turn's <c>token_count</c> event; a turn runs from <c>task_started</c> to <c>task_complete</c> (or
/// <c>turn_aborted</c>). Only the end of a log is read, and only again once it has grown.
/// </summary>
internal sealed class CodexLogs(string codexHome)
{
    /// <summary>How much of a log's end is read for its last limits or turn.</summary>
    internal const int Tail = 256 * 1024;

    private readonly Dictionary<string, (long Length, DateTime Written, UsageReading? Limits, Turn? Turn)> _read = new(StringComparer.OrdinalIgnoreCase);

    public string Sessions => Path.Combine(codexHome, "sessions");

    /// <summary>A turn: working from <paramref name="Since"/> until it ends, then waiting from its end.</summary>
    public sealed record Turn(bool Working, DateTimeOffset Since);

    /// <summary>The newest limits any of the newest logs recorded (the last three days' folders, and <paramref name="also"/>).</summary>
    public UsageReading? Limits(IEnumerable<string> also)
    {
        UsageReading? newest = null;
        foreach (string log in Recent().Concat(also).Distinct(StringComparer.OrdinalIgnoreCase))
            if (Read(log).Limits is { } limits && (newest is null || limits.At > newest.At))
                newest = limits;
        return newest;
    }

    /// <summary>The last turn in <paramref name="log"/>, or null when its end shows none.</summary>
    public Turn? LastTurn(string log) => Read(log).Turn;

    /// <summary>The log of thread <paramref name="threadId"/>: in its day's folder (a v7 id holds its start time), or searched for.</summary>
    public string? Find(string threadId)
    {
        string pattern = $"rollout-*-{threadId}.jsonl";
        if (!Directory.Exists(Sessions))
            return null;
        if (Started(threadId) is { } started)
        {
            var day = started.ToLocalTime().Date;
            foreach (var d in new[] { day, day.AddDays(-1), day.AddDays(1) })
            {
                var c = System.Globalization.CultureInfo.InvariantCulture;
                string folder = Path.Combine(Sessions, d.ToString("yyyy", c), d.ToString("MM", c), d.ToString("dd", c));
                if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, pattern).FirstOrDefault() is { } found)
                    return found;
            }
        }
        return Directory.EnumerateFiles(Sessions, pattern, SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>The time a UUID v7 thread id was made, from its first 48 bits.</summary>
    internal static DateTimeOffset? Started(string threadId)
    {
        string hex = threadId.Replace("-", "", StringComparison.Ordinal);
        if (hex.Length < 13 || hex[12] != '7' || !long.TryParse(hex[..12], System.Globalization.NumberStyles.HexNumber, null, out long ms))
            return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    /// <summary>The logs in the newest three days' folders.</summary>
    private IEnumerable<string> Recent()
    {
        if (!Directory.Exists(Sessions))
            return [];
        var days = new List<string>();
        foreach (string year in Directory.EnumerateDirectories(Sessions).OrderDescending())
        {
            foreach (string month in Directory.EnumerateDirectories(year).OrderDescending())
            {
                days.AddRange(Directory.EnumerateDirectories(month).OrderDescending().Take(3 - days.Count));
                if (days.Count >= 3) break;
            }
            if (days.Count >= 3) break;
        }
        return days.SelectMany(d => Directory.EnumerateFiles(d, "rollout-*.jsonl"));
    }

    private (long Length, DateTime Written, UsageReading? Limits, Turn? Turn) Read(string log)
    {
        var info = new FileInfo(log);
        if (!info.Exists)
            return default;
        if (_read.TryGetValue(log, out var known) && known.Length == info.Length && known.Written == info.LastWriteTimeUtc)
            return known;
        UsageReading? limits = null;
        Turn? turn = null;
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - Tail);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new List<string>();
            if (start > 0) reader.ReadLine(); // a line cut by the tail's start
            while (reader.ReadLine() is { } line)
                lines.Add(line);
            for (int i = lines.Count - 1; i >= 0 && (limits is null || turn is null); i--)
                Event(lines[i], ref limits, ref turn);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var read = (info.Length, info.LastWriteTimeUtc, limits, turn);
        _read[log] = read;
        return read;
    }

    // From the end: the first token_count with limits, and the first turn event.
    private static void Event(string line, ref UsageReading? limits, ref Turn? turn)
    {
        bool wantsLimits = limits is null && line.Contains("\"rate_limits\"", StringComparison.Ordinal);
        bool wantsTurn = turn is null && (line.Contains("\"task_started\"", StringComparison.Ordinal)
            || line.Contains("\"task_complete\"", StringComparison.Ordinal) || line.Contains("\"turn_aborted\"", StringComparison.Ordinal));
        if (!wantsLimits && !wantsTurn)
            return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                return;
            var at = root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ts.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed : DateTimeOffset.MinValue;
            switch (type.GetString())
            {
                case "token_count" when wantsLimits && payload.TryGetProperty("rate_limits", out var rl) && rl.ValueKind == JsonValueKind.Object:
                    var reading = CodexLimits.Parse(rl, at);
                    if (reading.Windows.Count > 0)
                        limits = reading;
                    break;
                case "task_started" when wantsTurn:
                    turn = new Turn(true, at);
                    break;
                case "task_complete" or "turn_aborted" when wantsTurn:
                    turn = new Turn(false, at);
                    break;
            }
        }
        catch (JsonException) { } // a line still being written
    }
}
