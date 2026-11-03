// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;
using System.Text.RegularExpressions;
using Chippi.Features.AgentUsage.Readers;

namespace Chippi.Features.AgentUsage.Sessions;

/// <summary>
/// Claude Code's sessions: its own file per running session, <c>~/.claude/sessions/&lt;pid&gt;.json</c>, with the process,
/// the folder, the session's name and busy or idle. Only those files are opened: the folder's <c>*.key</c> files hold
/// tokens. A file whose process has gone, or is another process now, is no session.
/// </summary>
internal sealed partial class ClaudeSessions(string claudeHome, IProcesses processes)
{
    [GeneratedRegex(@"^\d+\.json$")]
    private static partial Regex SessionFile();

    public string SessionsFolder => Path.Combine(claudeHome, "sessions");

    public IReadOnlyList<AgentSession> Read(DateTimeOffset now, Func<int, bool> isOwn)
    {
        if (!Directory.Exists(SessionsFolder))
            return [];
        var sessions = new List<AgentSession>();
        foreach (string file in Directory.EnumerateFiles(SessionsFolder, "*.json"))
        {
            if (!SessionFile().IsMatch(Path.GetFileName(file)) || Parse(file) is not { } session)
                continue;
            if (session.ProcessId is not { } pid || isOwn(pid) || !processes.IsRunning(pid, session.Started))
                continue;
            if (!session.Session.IsIdle(now))
                sessions.Add(session.Session);
        }
        return sessions;
    }

    internal sealed record Parsed(AgentSession Session, int? ProcessId, long? Started);

    internal static Parsed? Parse(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            return Parse(doc.RootElement, File.GetLastWriteTimeUtc(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static Parsed? Parse(JsonElement root, DateTime written)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        int? pid = root.TryGetProperty("pid", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int id) ? id : null;
        string sessionId = Text(root, "sessionId") ?? pid?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        string? cwd = Text(root, "cwd");
        string name = Text(root, "name") is { Length: > 0 } named ? named : LeafName(cwd) ?? "Session";
        string status = Text(root, "status") ?? "";
        var state = status switch
        {
            "busy" or "running" or "working" => AgentSessionState.Working,
            "waiting" => AgentSessionState.NeedsInput,
            _ => AgentSessionState.Idle,
        };
        long? since = Millis(root, "statusUpdatedAt") ?? Millis(root, "updatedAt");
        long? started = Text(root, "procStart") is { } ps && long.TryParse(ps, out long ft) ? ft : null;
        var at = since is { } ms && ms <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : new DateTimeOffset(written, TimeSpan.Zero);
        return new(new AgentSession(AgentKind.Claude, sessionId, name, state, at, pid, Text(root, "entrypoint")), pid, started);
    }

    /// <summary>A folder's own name: "chippi" for X:\code\chippi.</summary>
    internal static string? LeafName(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } leaf ? leaf : path;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Millis(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long ms) && ms > 0 ? ms : null;
}

/// <summary>
/// Codex's sessions: a thread is open while a running Codex holds its writer lock,
/// <c>~/.codex/thread-writer-locks/&lt;thread&gt;.lock</c>, and its log's last turn says whether it works or waits. Its
/// name is the thread's (from Codex's session index) or its folder's.
/// </summary>
internal sealed class CodexSessions(string codexHome, CodexLogs logs, IProcesses processes)
{
    /// <summary>Retry a missing log's full-history search after this. Its expected day is checked every scan.</summary>
    internal static readonly TimeSpan LookAgain = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, string> _logs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _missing = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Origin(string? Folder, string? Entrypoint, bool IsSubagent);
    private readonly Dictionary<string, Origin> _meta = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _holders = new(StringComparer.OrdinalIgnoreCase);
    private (DateTime Written, Dictionary<string, string> Names) _index = (DateTime.MinValue, []);

    public string Locks => Path.Combine(codexHome, "thread-writer-locks");

    /// <summary>The logs of the threads open now, for the limits they record.</summary>
    public IReadOnlyList<string> OpenLogs { get; private set; } = [];

    public IReadOnlyList<AgentSession> Read(DateTimeOffset now, Func<int, bool> isOwn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(Locks))
        {
            OpenLogs = [];
            return [];
        }
        var sessions = new List<AgentSession>();
        var open = new List<string>();
        var threads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string lockFile in Directory.EnumerateFiles(Locks, "*.lock"))
        {
            ct.ThrowIfCancellationRequested();
            string thread = Path.GetFileNameWithoutExtension(lockFile);
            if (!Guid.TryParseExact(thread, "D", out _) || !processes.IsLocked(lockFile))
                continue;
            threads.Add(thread);
            if (!_logs.TryGetValue(thread, out string? log) || !File.Exists(log))
            {
                bool searchAll = !_missing.TryGetValue(thread, out var again) || now >= again;
                log = logs.Find(thread, searchAll, now, ct);
                if (log is null)
                {
                    if (searchAll)
                        _missing[thread] = now + LookAgain;
                    continue;
                }
                _logs[thread] = log;
                _meta.Remove(thread);
                _missing.Remove(thread);
            }
            open.Add(log);
            // A worker has its own writer lock and rollout. It belongs to its parent conversation.
            if (Meta(thread, log) is not { IsSubagent: false } origin)
                continue;
            if (!_holders.TryGetValue(thread, out int? pid))
                _holders[thread] = pid = processes.HolderOf(lockFile);
            if (pid is { } own && isOwn(own))
                continue;
            var turn = logs.LastTurn(log, ct);
            string name = Indexed(thread, ct) ?? origin.Folder ?? "Session";
            var session = new AgentSession(AgentKind.Codex, thread, name, turn?.Status ?? AgentSessionState.Idle,
                turn?.Since ?? new DateTimeOffset(File.GetLastWriteTimeUtc(log), TimeSpan.Zero), pid, origin.Entrypoint);
            if (!session.IsIdle(now))
                sessions.Add(session);
        }
        OpenLogs = open;
        // What's known of threads no longer open is let go.
        Prune(_logs, threads);
        Prune(_missing, threads);
        Prune(_meta, threads);
        Prune(_holders, threads);
        return sessions;
    }

    private static void Prune<T>(Dictionary<string, T> known, HashSet<string> open)
    {
        foreach (string thread in known.Keys.Where(t => !open.Contains(t)).ToList())
            known.Remove(thread);
    }

    private Origin? Meta(string thread, string log)
    {
        if (_meta.TryGetValue(thread, out var known))
            return known;
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            if (reader.ReadLine() is not { } first)
                return null;
            using var doc = JsonDocument.Parse(first);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != "session_meta"
                || !root.TryGetProperty("payload", out var meta) || meta.ValueKind != JsonValueKind.Object)
                return null;
            string? entrypoint = Text(meta, "source");
            bool subagent = Text(meta, "thread_source") == "subagent" || entrypoint == "subagent"
                || (meta.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object && source.TryGetProperty("subagent", out _));
            return _meta[thread] = new(ClaudeSessions.LeafName(Text(meta, "cwd")), entrypoint, subagent);
        }
        // The writer lock can precede the first complete line. Retry next scan.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // ~/.codex/session_index.jsonl: {id, thread_name, updated_at} a line, the newest last; read again when it changes.
    private string? Indexed(string thread, CancellationToken ct)
    {
        string path = Path.Combine(codexHome, "session_index.jsonl");
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (written != _index.Written)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                            && root.TryGetProperty("thread_name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } name)
                            names[id.GetString()!] = name;
                    }
                    catch (JsonException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return _index.Names.GetValueOrDefault(thread); }
            _index = (written, names);
        }
        return _index.Names.GetValueOrDefault(thread);
    }
}
