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
        int? pid = root.TryGetProperty("pid", out var p) && p.TryGetInt32(out int id) ? id : null;
        string sessionId = Text(root, "sessionId") ?? pid?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        string? cwd = Text(root, "cwd");
        string name = Text(root, "name") is { Length: > 0 } named ? named : LeafName(cwd) ?? "Session";
        string status = Text(root, "status") ?? "";
        bool working = status is "busy" or "running" or "working";
        long? since = Millis(root, "statusUpdatedAt") ?? Millis(root, "updatedAt");
        long? started = Text(root, "procStart") is { } ps && long.TryParse(ps, out long ft) ? ft : null;
        var at = since is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : new DateTimeOffset(written, TimeSpan.Zero);
        return new(new AgentSession(AgentKind.Claude, sessionId, name, working, at, pid, Text(root, "entrypoint")), pid, started);
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
    private readonly Dictionary<string, string?> _logs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Name, string? Source)> _meta = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _holders = new(StringComparer.OrdinalIgnoreCase);
    private (DateTime Written, Dictionary<string, string> Names) _index = (DateTime.MinValue, []);

    public string Locks => Path.Combine(codexHome, "thread-writer-locks");

    /// <summary>The logs of the threads open now, for the limits they record.</summary>
    public IReadOnlyList<string> OpenLogs { get; private set; } = [];

    public IReadOnlyList<AgentSession> Read(DateTimeOffset now, Func<int, bool> isOwn)
    {
        if (!Directory.Exists(Locks))
        {
            OpenLogs = [];
            return [];
        }
        var sessions = new List<AgentSession>();
        var open = new List<string>();
        foreach (string lockFile in Directory.EnumerateFiles(Locks, "*.lock"))
        {
            string thread = Path.GetFileNameWithoutExtension(lockFile);
            if (thread.StartsWith('.') || !processes.IsLocked(lockFile))
            {
                _holders.Remove(thread);
                continue;
            }
            if (!_logs.TryGetValue(thread, out string? log) || log is null || !File.Exists(log))
                _logs[thread] = log = logs.Find(thread);
            if (log is null)
                continue;
            open.Add(log);
            if (!_holders.TryGetValue(thread, out int? pid))
                _holders[thread] = pid = processes.HolderOf(lockFile);
            if (pid is { } own && isOwn(own))
                continue;
            var turn = logs.LastTurn(log);
            var (name, source) = Meta(thread, log);
            var session = new AgentSession(AgentKind.Codex, thread, name, turn?.Working ?? false,
                turn?.Since ?? new DateTimeOffset(File.GetLastWriteTimeUtc(log), TimeSpan.Zero), pid, source);
            if (!session.IsIdle(now))
                sessions.Add(session);
        }
        OpenLogs = open;
        return sessions;
    }

    // The thread's name from the index, else its folder's; and where it was started ("vscode", "cli"), from its first line.
    private (string Name, string? Source) Meta(string thread, string log)
    {
        string? indexed = Indexed(thread);
        if (_meta.TryGetValue(thread, out var known) && (indexed is null || known.Name == indexed))
            return known;
        string? cwd = null, source = null;
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            if (reader.ReadLine() is { } first)
            {
                using var doc = JsonDocument.Parse(first);
                if (doc.RootElement.TryGetProperty("payload", out var meta) && meta.ValueKind == JsonValueKind.Object)
                {
                    cwd = meta.TryGetProperty("cwd", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                    source = meta.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        var found = (indexed ?? ClaudeSessions.LeafName(cwd) ?? "Session", source);
        _meta[thread] = found;
        return found;
    }

    // ~/.codex/session_index.jsonl: {id, thread_name, updated_at} a line, the newest last; read again when it changes.
    private string? Indexed(string thread)
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
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                            && root.TryGetProperty("thread_name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } name)
                            names[id.GetString()!] = name;
                    }
                    catch (JsonException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _index = (written, names);
        }
        return _index.Names.GetValueOrDefault(thread);
    }
}
