// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.AgentUsage;
using Chippi.Features.AgentUsage.Readers;
using Chippi.Features.AgentUsage.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Island.Tests.IslandPanelTests;

namespace Chippi.Island.Tests;

/// <summary>
/// The Agents page (AI agent usage) off screen, from fixture files and scripted answers, never the owner's: the
/// mockup's states (agents-normal, -near, -per-model, -stale, -none) when CHIPPI_ISLAND_SHOTS names a folder, and the
/// page's open timed when CHIPPI_AGENTS_TIMINGS names a report.
/// </summary>
public sealed class AgentsPageShots
{
    private static readonly MonitorArea Area = new(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);

    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        WpfTestThread.Run(() =>
        {
            foreach (var state in new[] { "normal", "near", "per-model", "stale", "none" })
            {
                using var fixture = new Fixture(state);
                Shoot(fixture, Path.Combine(folder, $"agents-{state}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
            }
        });
    }

    [Fact]
    public void Time()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_AGENTS_TIMINGS") is not { Length: > 0 } report)
            return;
        var lines = new List<string>();
        WpfTestThread.Run(() =>
        {
            using var fixture = new Fixture("normal", sessions: 8);
            var panel = new IslandPanelWindow();
            var frames = new List<TimeSpan>();
            EventHandler record = (_, e) =>
            {
                var at = ((RenderingEventArgs)e).RenderingTime;
                if (frames.Count == 0 || frames[^1] != at)
                    frames.Add(at);
            };
            CompositionTarget.Rendering += record;
            try
            {
                panel.Initialize();
                panel.SetMedia(new FakeMedia());
                panel.SetTiles(fixture.Tiles);
                panel.SetLastPage(null);
                panel.Open(IslandGeometry.Start(Area.Work, 1), Area);
                Pump(TimeSpan.FromMilliseconds(600));
                lines.Add("open\tShowPage ms\tfirst frame after ms\tlongest gap in the 180 ms slide ms");
                for (int i = 0; i < 9; i++)
                {
                    int before = frames.Count;
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    panel.ShowPage(AgentUsageModule.TileKey, remember: false);
                    double call = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var asked = frames[^1];
                    Pump(TimeSpan.FromMilliseconds(400));
                    var after = frames.Skip(before).Where(t => t <= asked + TimeSpan.FromMilliseconds(260)).Prepend(asked).ToList();
                    double first = after.Count > 1 ? (after[1] - asked).TotalMilliseconds : double.NaN;
                    double longest = after.Zip(after.Skip(1), (a, b) => (b - a).TotalMilliseconds).DefaultIfEmpty(0).Max();
                    lines.Add($"{(i == 0 ? "first" : "warm")}\t{call:0.0}\t{first:0.0}\t{longest:0.0}");
                    panel.ShowPage(null, remember: false);
                    Pump(TimeSpan.FromMilliseconds(400));
                }
                var deltas = frames.Zip(frames.Skip(1), (a, b) => b - a).OrderBy(d => d).ToList();
                lines.Insert(0, $"frame {deltas[deltas.Count / 2].TotalMilliseconds:0.00} ms, {frames.Count} frames; {fixture.Module.Sessions.Count} sessions, 2 agents");
            }
            finally
            {
                CompositionTarget.Rendering -= record;
                panel.Close();
            }
        });
        File.WriteAllLines(report, lines);
    }

    private static void Shoot(Fixture fixture, string path)
    {
        var panel = new IslandPanelWindow();
        try
        {
            panel.Initialize();
            panel.SetMedia(new FakeMedia());
            panel.SetTiles(fixture.Tiles);
            panel.SetLastPage(null);
            panel.Open(IslandGeometry.Start(Area.Work, 1), Area);
            Pump(TimeSpan.FromMilliseconds(400));
            panel.ShowPage(AgentUsageModule.TileKey);
            Pump(TimeSpan.FromMilliseconds(400));
            IslandPanelShots.Save(panel, path);
        }
        finally
        {
            panel.ShowPage(null, remember: false);
            panel.Close();
        }
    }

    /// <summary>The module on scripted agents and fixture files, its checks run, at 4:52 PM on Sunday, Nov 1, 2026.</summary>
    internal sealed class Fixture : IDisposable
    {
        private static readonly DateTimeOffset At = new(new DateTime(2026, 11, 1, 16, 52, 0, DateTimeKind.Local));
        private readonly string _home = Path.Combine(Path.GetTempPath(), "chippi-agents-shots-" + Guid.NewGuid().ToString("N"));
        private readonly Clock _clock = new() { Now = At };
        private readonly Ui _ui = new();

        public Fixture(string state, int sessions = 4)
        {
            var paths = new AgentPaths(Path.Combine(_home, ".claude"), Path.Combine(_home, ".codex"), Path.Combine(_home, "Claude", "plan-usage-history.json"));
            var programs = new Programs { Codex = state != "none" };
            var processes = new Processes();
            if (state != "none")
            {
                programs.Claude = state == "stale" ? null : state switch
                {
                    "near" => Usage(("five_hour", 86, "18:10"), ("seven_day", 23, "Thu 9:00")),
                    "per-model" => Usage(("five_hour", 42, "18:10"), ("seven_day", 23, "Thu 9:00"), ("seven_day_opus", 61, "Thu 9:00"), ("seven_day_sonnet", 8, "Thu 9:00")),
                    _ => Usage(("five_hour", 42, "18:10"), ("seven_day", 23, "Thu 9:00")),
                };
                programs.CodexSignedOut = state == "stale";
                if (state == "stale")
                    Write("Claude/plan-usage-history.json", $$$"""{"version":2,"samples":[{"t":{{{new DateTimeOffset(new DateTime(2026, 11, 1, 14, 10, 0, DateTimeKind.Local)).ToUnixTimeMilliseconds()}}},"org":"o","u":{"fh":42,"sd":23}}]}""");
                var named = new (string Name, AgentKind Agent, bool Working, int Minutes, string App)[]
                {
                    ("chippi", AgentKind.Claude, true, 12, "Claude"), ("packrat", AgentKind.Claude, false, 1, "Terminal"),
                    ("smartwork", AgentKind.Codex, true, 3, "Codex"), ("glidemind", AgentKind.Claude, false, 8, "VS Code"),
                    ("hopper", AgentKind.Claude, true, 20, "Terminal"), ("serenity", AgentKind.Codex, false, 15, "VS Code"),
                    ("notes", AgentKind.Claude, false, 25, "Claude"), ("archer", AgentKind.Codex, true, 30, "Codex"),
                };
                foreach (var (name, agent, working, minutes, app) in named.Take(sessions).Where(s => state != "stale" || s.Agent == AgentKind.Claude))
                {
                    int pid = 1000 + Array.FindIndex(named, n => n.Name == name);
                    processes.Apps[pid] = new SessionApp(pid, app);
                    var since = At.AddMinutes(-minutes);
                    if (agent == AgentKind.Claude)
                        Write($".claude/sessions/{pid}.json", $$"""{"pid":{{pid}},"sessionId":"s{{pid}}","cwd":"X:\\code\\{{name}}","status":"{{(working ? "busy" : "idle")}}","statusUpdatedAt":{{since.ToUnixTimeMilliseconds()}}}""");
                    else
                    {
                        string thread = $"01a1996d-{pid:0000}-7000-8000-000000000000";
                        Write($".codex/thread-writer-locks/{thread}.lock", "");
                        processes.Locked.Add($"{thread}.lock");
                        processes.Holders[$"{thread}.lock"] = pid;
                        string turn = $$$"""{"timestamp":"{{{since.UtcDateTime:O}}}","type":"event_msg","payload":{"type":"{{{(working ? "task_started" : "task_complete")}}}"}}""";
                        Write($".codex/sessions/2026/11/01/rollout-2026-11-01T10-00-00-{thread}.jsonl",
                            $$$"""{"timestamp":"2026-11-01T16:00:00Z","type":"session_meta","payload":{"cwd":"X:\\code\\{{{name}}}"}}""" + "\n" + turn + "\n");
                    }
                }
            }
            Module = new AgentUsageModule(programs, processes, paths, _clock, () => false, loops: false);
            Module.StartAsync(new ModuleContext(AgentUsageModule.Id, new Settings(), NullLogger.Instance, new Island(), _ui) { DataDirectory = _home }, default).GetAwaiter().GetResult();
            _clock.Now = At + UsageSchedule.First;
            Module.StepAsync(default).GetAwaiter().GetResult();
        }

        public AgentUsageModule Module { get; }

        public IReadOnlyList<IslandTileGroup> Tiles => IslandTileContribution.Group([_ui.Tile!]);

        private void Write(string relative, string text)
        {
            string file = Path.Combine(_home, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        // Claude Code's usage: each window's share and its reset, local time today ("18:10") or this week ("Thu 9:00").
        private static string Usage(params (string Key, double Used, string Reset)[] windows)
        {
            string Reset(string at)
            {
                var day = at.StartsWith("Thu", StringComparison.Ordinal) ? new DateTime(2026, 11, 5) : new DateTime(2026, 11, 1);
                var time = TimeSpan.Parse(at.Split(' ')[^1], System.Globalization.CultureInfo.InvariantCulture);
                return new DateTimeOffset(DateTime.SpecifyKind(day + time, DateTimeKind.Local)).ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            }
            string limits = string.Join(',', windows.Select(w => $$"""
                "{{w.Key}}":{"utilization":{{w.Used}},"resets_at":"{{Reset(w.Reset)}}"}
                """));
            return "{\"type\":\"control_response\",\"response\":{\"subtype\":\"success\",\"request_id\":\"chippi-usage\",\"response\":{\"subscription_type\":\"max\",\"rate_limits_available\":true,\"rate_limits\":{" + limits + "}}}}";
        }

        public void Dispose()
        {
            Module.StopAsync(default).GetAwaiter().GetResult();
            try { Directory.Delete(_home, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Programs : IAgentPrograms
    {
        public string? Claude;
        public bool Codex = true;
        public bool CodexSignedOut;

        public bool IsInstalled(AgentKind agent) => agent == AgentKind.Codex ? Codex : Claude is not null;

        public bool IsOwn(int processId) => false;

        public IAgentConversation? Start(AgentKind agent, IReadOnlyList<string> arguments, string workingDirectory) => agent == AgentKind.Claude
            ? Claude is { } usage ? new Conversation(line => line.Contains("\"initialize\"")
                ? """{"type":"control_response","response":{"subtype":"success","request_id":"chippi-initialize","response":{"account":{"tokenSource":"claude.ai"}}}}"""
                : usage) : null
            : !Codex ? null : new Conversation(line =>
                line.Contains("\"initialize\"") ? """{"id":1,"result":{}}"""
                : line.Contains("account/read") ? CodexSignedOut ? """{"id":2,"result":{"account":null}}""" : """{"id":2,"result":{"account":{"type":"chatgpt","planType":"pro"}}}"""
                : line.Contains("rateLimits") ? """{"id":3,"result":{"rateLimits":{"primary":{"usedPercent":9,"windowDurationMins":10080,"resetsAt":1793801690},"secondary":null,"planType":"pro"}}}"""
                : null);
    }

    private sealed class Conversation(Func<string, string?> answer) : IAgentConversation
    {
        private readonly Queue<string> _out = new();

        public Task SendAsync(string line, CancellationToken ct)
        {
            if (answer(line) is { } reply)
                _out.Enqueue(reply);
            return Task.CompletedTask;
        }

        public Task<string?> ReadLineAsync(CancellationToken ct) => Task.FromResult(_out.TryDequeue(out var line) ? line : null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Processes : IProcesses
    {
        public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Holders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, SessionApp> Apps { get; } = [];
        public bool IsRunning(int processId, long? startedFileTime) => Apps.ContainsKey(processId);
        public bool IsLocked(string path) => Locked.Contains(Path.GetFileName(path));
        public int? HolderOf(string path) => Holders.TryGetValue(Path.GetFileName(path), out int pid) ? pid : null;
        public SessionApp? AppOf(int processId) => Apps.GetValueOrDefault(processId);
        public bool BringForward(nint window) => false;
    }

    private sealed class Ui : IModuleUi
    {
        public IslandTileContribution? Tile;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => Tile = new IslandTileContribution(AgentUsageModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> createView) { }
    }

    private sealed class Settings : IModuleSettings
    {
        private object? _value;
        public T Get<T>() where T : class, new() => _value as T ?? new();
        public void Save<T>(T value) where T : class => _value = value;
    }

    private sealed class Island : IIslandPublisher
    {
        public void Publish(IslandActivity activity) { }
        public void Clear(string key) { }
        public void ReportSuccess(string? message = null) { }
    }
}
#endif
