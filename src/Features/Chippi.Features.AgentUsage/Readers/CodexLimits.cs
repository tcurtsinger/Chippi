// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Buffers;
using System.Text;
using System.Text.Json;
using Chippi.Features.AgentUsage.Sessions;

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
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("account", out var who) || who.ValueKind != JsonValueKind.Object)
                return new(CheckState.SignedOut, Error: "no account");
            plan = who.TryGetProperty("planType", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        }

        await codex.SendAsync("""{"method":"account/rateLimits/read","id":3}""", ct).ConfigureAwait(false);
        using var limits = await ReplyAsync(codex, 3, ct).ConfigureAwait(false);
        if (limits is null)
            return new(CheckState.Failed, Error: "account/rateLimits/read: no answer");
        if (ErrorOf(limits.RootElement) is { } error)
            return new(error.Code == MethodNotFound ? CheckState.TooOld : CheckState.Failed, Error: "account/rateLimits/read: " + error.Message);
        if (limits.RootElement.GetProperty("result") is not { ValueKind: JsonValueKind.Object } answer
            || !answer.TryGetProperty("rateLimits", out var snapshot) || snapshot.ValueKind != JsonValueKind.Object)
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
            ? new(e.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int code) ? code : 0, e.TryGetProperty("message", out var m) ? m.ToString() : "error")
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
/// Codex's session logs, <c>~/.codex/sessions/yyyy/MM/dd/rollout-…-&lt;thread&gt;.jsonl</c>, contain one JSON event per line.
/// Limits and session state are replayed in order from a bounded tail on the first visit. Later scans continue at the
/// last whole JSONL record, retaining the state established by records already consumed.
/// </summary>
internal sealed partial class CodexLogs(string codexHome)
{
    /// <summary>How much of a log is read at a time.</summary>
    internal const int Chunk = 256 * 1024;

    /// <summary>How far back a log seen for the first time is read for its last turn and limits.</summary>
    internal const int Back = 16 * 1024 * 1024;

    private readonly Dictionary<string, Seen> _read = new(StringComparer.OrdinalIgnoreCase);

    // A log as last read: its size and time then, where its last whole line ended, and what it said.
    private sealed record Seen(long Length, DateTime Written, long Parsed, bool SkipRemainder, LogState State);

    public string Sessions => Path.Combine(codexHome, "sessions");

    /// <summary>The latest observable turn state, or a confirmed outstanding request for input.</summary>
    public sealed record Turn(AgentSessionState Status, DateTimeOffset Since);

    /// <summary>The newest limits any of the newest logs recorded (the last three days' folders, and <paramref name="also"/>).</summary>
    public UsageReading? Limits(IEnumerable<string> also, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        UsageReading? newest = null;
        var logs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string log in Recent(ct).Concat(also))
        {
            ct.ThrowIfCancellationRequested();
            if (logs.Add(log) && Read(log, ct)?.State.Limits is { } limits && (newest is null || limits.At > newest.At))
                newest = limits;
        }
        // What's neither recent nor open is let go.
        foreach (string gone in _read.Keys.Where(k => !logs.Contains(k)).ToList())
            _read.Remove(gone);
        return newest;
    }

    /// <summary>The last turn in <paramref name="log"/>, or null when its end shows none.</summary>
    public Turn? LastTurn(string log, CancellationToken ct = default) => Read(log, ct)?.State.Current;

    /// <summary>The log of thread <paramref name="threadId"/>: in its day's folder (a v7 id holds its start time), or searched for.</summary>
    public string? Find(string threadId, bool searchAll = true, DateTimeOffset? now = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string pattern = $"rollout-*-{threadId}.jsonl";
        if (!Directory.Exists(Sessions))
            return null;
        var day = (Started(threadId) ?? now ?? DateTimeOffset.Now).ToLocalTime().Date;
        foreach (int offset in new[] { 0, -1, 1 })
        {
            if ((offset == -1 && day == DateTime.MinValue.Date) || (offset == 1 && day == DateTime.MaxValue.Date))
                continue;
            var d = day.AddDays(offset);
            var c = System.Globalization.CultureInfo.InvariantCulture;
            string folder = Path.Combine(Sessions, d.ToString("yyyy", c), d.ToString("MM", c), d.ToString("dd", c));
            if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, pattern).FirstOrDefault() is { } found)
                return found;
        }
        if (searchAll)
        {
            foreach (string folder in Directory.EnumerateDirectories(Sessions, "*", SearchOption.AllDirectories).Prepend(Sessions))
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.EnumerateFiles(folder, pattern).FirstOrDefault() is { } found)
                    return found;
            }
        }
        return null;
    }

    /// <summary>The time a UUID v7 thread id was made, from its first 48 bits.</summary>
    internal static DateTimeOffset? Started(string threadId)
    {
        string hex = threadId.Replace("-", "", StringComparison.Ordinal);
        if (hex.Length < 13 || hex[12] != '7' || !long.TryParse(hex[..12], System.Globalization.NumberStyles.HexNumber, null, out long ms)
            || ms > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    /// <summary>The logs in the newest three days' folders.</summary>
    private IEnumerable<string> Recent(CancellationToken ct)
    {
        if (!Directory.Exists(Sessions))
            return [];
        var days = new List<string>();
        foreach (string year in Directory.EnumerateDirectories(Sessions).OrderDescending())
        {
            foreach (string month in Directory.EnumerateDirectories(year).OrderDescending())
            {
                ct.ThrowIfCancellationRequested();
                days.AddRange(Directory.EnumerateDirectories(month).OrderDescending().Take(3 - days.Count));
                if (days.Count >= 3) break;
            }
            if (days.Count >= 3) break;
        }
        return days.SelectMany(d => Directory.EnumerateFiles(d, "rollout-*.jsonl"));
    }

    private Seen? Read(string log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = new FileInfo(log);
        if (!info.Exists)
            return null;
        _read.TryGetValue(log, out var known);
        if (known is not null && known.Length == info.Length && known.Written == info.LastWriteTimeUtc)
            return known;

        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            bool append = known is not null && length > known.Length;
            long start = append ? known!.Parsed : Math.Max(0, length - Back);
            var state = append ? new LogState(known!.State) : new LogState();
            var (parsed, skipRemainder) = ReadLines(stream, start, length, state, append && known!.SkipRemainder, ct);
            var read = new Seen(length, info.LastWriteTimeUtc, parsed, skipRemainder, state);
            _read[log] = read;
            return read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return known;
        }
    }

    // Read whole JSONL records in order. Each block advances even when a single line spans many blocks.
    // The last unfinished record stays at its start so a later append can complete it.
    private static (long Parsed, bool SkipRemainder) ReadLines(FileStream stream, long start, long length, LogState state,
        bool cutHead, CancellationToken ct)
    {
        if (start == length)
            return (start, cutHead);
        if (start > 0 && !cutHead)
        {
            stream.Position = start - 1;
            cutHead = stream.ReadByte() != '\n';
        }

        int capacity = (int)Math.Min(Chunk, length - start);
        byte[] block = ArrayPool<byte>.Shared.Rent(capacity);
        long record = start;
        try
        {
            for (long cursor = start; cursor < length;)
            {
                ct.ThrowIfCancellationRequested();
                int count = (int)Math.Min(capacity, length - cursor);
                stream.Position = cursor;
                stream.ReadExactly(block.AsSpan(0, count));
                int next = 0;
                int end;
                while ((end = Array.IndexOf(block, (byte)'\n', next, count - next)) >= 0)
                {
                    long newline = cursor + end;
                    if (!cutHead && newline > record && newline - record <= Back)
                    {
                        if (record >= cursor)
                            state.InspectLine(Encoding.UTF8.GetString(block, (int)(record - cursor), (int)(newline - record)));
                        else
                        {
                            // Only a record crossing a block boundary needs a separate, bounded buffer.
                            stream.Position = record;
                            var line = new byte[newline - record];
                            stream.ReadExactly(line);
                            state.InspectLine(Encoding.UTF8.GetString(line));
                        }
                    }
                    cutHead = false;
                    record = newline + 1;
                    next = end + 1;
                }
                cursor += count;
            }
            // A sliced/oversized unfinished record cannot be parsed. Resume at EOF, discarding its remainder
            // up to the first newline on the next append instead of repeatedly reading a growing record.
            return cutHead || length - record > Back ? (length, true) : (record, false);
        }
        finally { ArrayPool<byte>.Shared.Return(block); }
    }
}
