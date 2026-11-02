// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.AgentUsage.Readers;
using Chippi.Features.AgentUsage.Sessions;
using Chippi.Features.AgentUsage.Views;
using Chippi.UI.Controls;
using Chippi.UI.Themes;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.AgentUsage;

/// <summary>Where the agents keep the files Chippi reads. Never their credentials: no token is ever opened.</summary>
internal sealed record AgentPaths(string ClaudeHome, string CodexHome, string ClaudeHistory)
{
    public static AgentPaths ForUser()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? claude = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        string? codex = Environment.GetEnvironmentVariable("CODEX_HOME");
        return new(
            string.IsNullOrWhiteSpace(claude) ? Path.Combine(profile, ".claude") : claude,
            string.IsNullOrWhiteSpace(codex) ? Path.Combine(profile, ".codex") : codex,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "plan-usage-history.json"));
    }
}

/// <summary>
/// AI agent usage (Phase 5.5, docs/phase-5.5/ai-agent-usage.md): how much of Claude Code's and Codex's limits you've used,
/// when they reset, and which sessions are working or waiting on you. A loop scans the sessions every 5 seconds and checks
/// each agent's limits when <see cref="UsageSchedule"/> says, one program at a time; it rests while nobody can see the
/// screen. Chippi sends nothing anywhere itself: the agents' own programs sign themselves in.
/// </summary>
public sealed class AgentUsageModule : IFeatureModule
{
    public const string Id = "agent-usage";

    /// <summary>The island tile's key: the home's "Agents".</summary>
    public const string TileKey = "agent-usage";

    internal static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan AlertTime = TimeSpan.FromSeconds(6);
    private static readonly AgentKind[] Kinds = [AgentKind.Claude, AgentKind.Codex];

    private readonly IAgentPrograms _programs;
    private readonly IProcesses _processes;
    private readonly AgentPaths _paths;
    private readonly TimeProvider _time;
    private readonly Func<bool> _unseen;
    private readonly bool _loops;
    private readonly Lock _gate = new();
    private readonly Lock _alertGate = new(); // the alert queue and its timer, which fires on a timer thread
    private readonly SemaphoreSlim _wake = new(0);
    private readonly Dictionary<AgentKind, Track> _tracks = [];
    private readonly Dictionary<int, SessionApp?> _apps = [];
    private readonly Queue<UsageAlert> _alertQueue = new();
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private AgentsIslandView? _view;
    private CancellationTokenSource? _stop;
    private Task? _loop;
    private ITimer? _alertTimer;
    private bool _alertShowing;
    private UsageAlerts _alerts = new();
    private ClaudeSessions? _claudeSessions;
    private CodexSessions? _codexSessions;
    private CodexLogs? _codexLogs;
    private AgentUsageSettings _applied = new();
    private DateTimeOffset _started;
    private int _showing;
    private string _signature = "";
    private string _home = "";
    private bool _homeCaution;
    private IslandActivity? _working, _waiting;
    private ProgramCost? _lastCost;

    // One agent's checks: when and how the last went, how many failed in a row, and the readings from each source.
    private sealed class Track
    {
        public DateTimeOffset? Last;
        public int Failures;
        public CheckState State = CheckState.None;
        public bool Installed;
        public UsageReading? Live; // the agent's own answer
        public UsageReading? Other; // Codex's logs, or the Claude app's history file
        public string? Plan;
        public DateTimeOffset? Alerted; // the newest reading the alerts have seen
    }

    public AgentUsageModule() : this(null, new WindowsProcesses(), AgentPaths.ForUser(), TimeProvider.System, () => Motion.IsUnseen, loops: true) { }

    internal AgentUsageModule(IAgentPrograms? programs, IProcesses processes, AgentPaths paths, TimeProvider time, Func<bool> unseen, bool loops)
    {
        _programs = programs ?? new WindowsAgentPrograms((agent, cost) => _lastCost = cost);
        _processes = processes;
        _paths = paths;
        _time = time;
        _unseen = unseen;
        _loops = loops;
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "AI agent usage", "Your coding agents' usage and reset times in the island", FeatureGroup.System)
    {
        Icon = nameof(LucideIcons.Gauge),
        Presets = PresetMembership.PowerUser,
    };

    public AgentUsageSettings Settings { get; private set; } = new();

    /// <summary>The agents read, in order: Claude Code, then Codex; one switched off in Settings isn't here.</summary>
    public IReadOnlyList<AgentStatus> Agents { get; private set; } = [];

    /// <summary>Every running session of the agents read, the newest first.</summary>
    public IReadOnlyList<AgentSession> Sessions { get; private set; } = [];

    /// <summary>How each agent's last check went, switched off or not: Settings' Agents rows.</summary>
    public IReadOnlyDictionary<AgentKind, AgentStatus> All { get; private set; } = new Dictionary<AgentKind, AgentStatus>();

    /// <summary>Raised on the loop's thread when the agents, the sessions or the settings changed.</summary>
    public event Action? Changed;

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        Settings = _applied = context.Settings.Get<AgentUsageSettings>().Normalized();
        _started = _time.GetUtcNow();
        _alerts = new UsageAlerts();
        _tracks.Clear();
        foreach (var kind in Kinds)
            _tracks[kind] = new Track { Installed = _programs.IsInstalled(kind) };
        _codexLogs = new CodexLogs(_paths.CodexHome);
        _claudeSessions = new ClaudeSessions(_paths.ClaudeHome, _processes);
        _codexSessions = new CodexSessions(_paths.CodexHome, _codexLogs, _processes);
        _alertTimer = _time.CreateTimer(_ => NextAlert(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _tile = context.Ui.AddIslandTile(new IslandTileSpec(TileKey, "Agents", nameof(LucideIcons.Gauge), Order: 85, SectionOrder: 0,
            () => _view ??= new AgentsIslandView(this)));
        _tile.SetStatus(_home = "None");
        context.Ui.SetSettingsPage(() => new AgentUsageSettingsPage(this));
        Motion.Changed += OnMotionChanged;
        if (_loops)
        {
            _stop = new CancellationTokenSource();
            var stop = _stop.Token;
            _loop = Task.Run(() => RunAsync(stop), CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        Motion.Changed -= OnMotionChanged;
        if (_stop is not null)
        {
            await _stop.CancelAsync();
            if (_loop is not null)
            {
                try { await _loop; }
                catch (OperationCanceledException) { }
            }
            _stop.Dispose();
            _stop = null;
            _loop = null;
        }
        lock (_alertGate)
        {
            // Under the alerts' lock, so a firing timer neither publishes after the stop nor changes a disposed timer.
            _alertTimer?.Dispose();
            _alertTimer = null;
            _alertQueue.Clear();
            _alertShowing = false;
        }
        _context = null;
        _tile = null;
        _view = null;
        _working = _waiting = null;
        Agents = [];
        Sessions = [];
        _signature = "";
    }

    /// <summary>Saves new settings; the loop applies them at once.</summary>
    public Task ApplyAsync(AgentUsageSettings settings)
    {
        settings = settings.Normalized();
        _context?.Settings.Save(settings);
        Settings = settings;
        _wake.Release();
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>The page or its Settings page shows (or stops showing): checks run every minute while one does.</summary>
    public void Showing(bool shown)
    {
        bool first;
        lock (_gate)
        {
            _showing = Math.Max(0, _showing + (shown ? 1 : -1));
            first = shown && _showing == 1;
        }
        if (first)
            _wake.Release();
    }

    internal bool Seen
    {
        get { lock (_gate) return _showing > 0; }
    }

    /// <summary>"Claude", "Terminal", "VS Code": the app a session runs in, as last found.</summary>
    public string AppOf(AgentSession session)
    {
        lock (_gate)
            if (session.ProcessId is { } pid && _apps.TryGetValue(pid, out var app) && app is not null)
                return app.App;
        return session.Entrypoint switch
        {
            "claude-desktop" => "Claude",
            "vscode" or "claude-vscode" => "VS Code",
            "cli" or "exec" => "Terminal",
            _ => session.Agent == AgentKind.Codex ? "Codex" : "Terminal",
        };
    }

    /// <summary>Brings the window a session runs in forward: the terminal or the app. False when none was found.</summary>
    public bool SwitchTo(AgentSession session)
    {
        if (session.ProcessId is not { } pid || _processes.AppOf(pid) is not { Window: not 0 } app)
        {
            _context?.Logger.LogInformation("Agent usage: no window for {Agent} session {Name}", session.Agent, session.Name);
            return false;
        }
        lock (_gate)
            _apps[pid] = app;
        return _processes.BringForward(app.Window);
    }

    /// <summary>Closes the island, after a session's window came forward from it.</summary>
    public void CloseIsland() => _context?.CloseIsland();

    /// <summary>Opens this feature's Settings page.</summary>
    public void OpenSettings() => _context?.OpenSettings(null);

    internal DateTimeOffset Now => _time.GetUtcNow();

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        if (!_unseen())
            _wake.Release(); // seen again: what's due runs now
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await StepAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { _context?.Logger.LogError(ex, "Agent usage: a step failed"); }
            try { await _wake.WaitAsync(Tick, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One turn of the loop: the sessions, then any check that's due, then what the island and the pages show.</summary>
    internal async Task StepAsync(CancellationToken ct)
    {
        if (_unseen() || _context is null)
            return;
        var settings = Settings;
        if (settings != _applied)
        {
            foreach (var kind in Kinds.Where(k => !settings.Reads(k)))
                _alerts.Forget(kind);
            _applied = settings;
        }
        var claude = _tracks[AgentKind.Claude];
        if (claude.Last is null && claude.Other is null)
            claude.Other = Safely(() => ClaudeHistory.Read(_paths.ClaudeHistory), "the Claude app's usage file"); // something to show before the first check
        var now = _time.GetUtcNow();
        ScanSessions(settings, now);
        Publish(settings, now);
        foreach (var kind in Kinds)
        {
            var track = _tracks[kind];
            if (settings.Reads(kind) && now >= UsageSchedule.Due(_started, track.Last, Seen, track.Failures, track.State))
            {
                await CheckAsync(kind, track, ct).ConfigureAwait(false);
                now = _time.GetUtcNow();
                Publish(Settings, now);
            }
        }
    }

    private void ScanSessions(AgentUsageSettings settings, DateTimeOffset now)
    {
        var sessions = new List<AgentSession>();
        if (settings.Claude)
            sessions.AddRange(Safely(() => _claudeSessions!.Read(now, _programs.IsOwn), "Claude Code's sessions"));
        if (settings.Codex)
        {
            sessions.AddRange(Safely(() => _codexSessions!.Read(now, _programs.IsOwn), "Codex's sessions"));
            _tracks[AgentKind.Codex].Other = Safely(() => _codexLogs!.Limits(_codexSessions!.OpenLogs), "Codex's logs");
        }
        var pids = sessions.Select(s => s.ProcessId).OfType<int>().ToHashSet();
        List<int> unknown;
        lock (_gate)
        {
            foreach (int gone in _apps.Keys.Where(pid => !pids.Contains(pid)).ToList())
                _apps.Remove(gone);
            unknown = [.. pids.Where(pid => !_apps.ContainsKey(pid))];
        }
        // Found outside the lock: a window's title can take a message to the UI thread, which takes the lock too.
        var found = unknown.ToDictionary(pid => pid, pid => Safely(() => _processes.AppOf(pid), "a session's app"));
        lock (_gate)
        {
            foreach (var (pid, app) in found)
                _apps[pid] = app;
        }
        Sessions = [.. sessions.OrderByDescending(s => s.Since)];
    }

    private T? Safely<T>(Func<T> read, string what)
    {
        try { return read(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _context?.Logger.LogWarning(ex, "Agent usage: reading {What} failed", what);
            return default;
        }
    }

    private IEnumerable<AgentSession> Safely(Func<IReadOnlyList<AgentSession>> read, string what) => Safely<IReadOnlyList<AgentSession>>(read, what) ?? [];

    private async Task CheckAsync(AgentKind kind, Track track, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        _lastCost = null;
        track.Last = now; // first, so nothing a check does can make it due again at once
        var result = await RunCheckAsync(kind, now, ct).ConfigureAwait(false);
        track.State = result.State;
        track.Failures = result.State == CheckState.Ok ? 0 : track.Failures + 1;
        track.Installed = result.State != CheckState.NotFound;
        if (result.Reading is { } reading)
        {
            track.Live = reading;
            track.Plan = reading.Plan ?? track.Plan;
        }
        else if (kind == AgentKind.Claude)
            track.Other = Safely(() => ClaudeHistory.Read(_paths.ClaudeHistory), "the Claude app's usage file"); // the Claude app's own last check
        var due = UsageSchedule.Due(_started, track.Last, Seen, track.Failures, track.State);
        _context?.Logger.LogInformation("Agent usage: {Agent} check {State}{Error}{Cost}; next in {Next:0.#} min",
            kind, result.State, result.Error is { } error ? $" ({error})" : "", _lastCost is { } cost ? $" in {cost}" : "", (due - now).TotalMinutes);
    }

    private async Task<CheckResult> RunCheckAsync(AgentKind kind, DateTimeOffset now, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(CheckTimeout, _time);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            var arguments = kind == AgentKind.Claude ? ClaudeLimits.Arguments : CodexLimits.Arguments;
            await using var program = _programs.Start(kind, arguments, _context?.DataDirectory ?? Path.GetTempPath());
            if (program is null)
                return CheckResult.Missing;
            var result = kind == AgentKind.Claude
                ? await ClaudeLimits.CheckAsync(program, now, both.Token).ConfigureAwait(false)
                : await CodexLimits.CheckAsync(program, Version, now, both.Token).ConfigureAwait(false);
            return timeout.IsCancellationRequested && result.State == CheckState.Failed ? result with { Error = "timed out" } : result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(CheckState.Failed, Error: "timed out");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new(CheckState.Failed, Error: $"{ex.GetType().Name}: {ex.Message}"); // the next check may work: it backs off
        }
    }

    private static string Version => typeof(AgentUsageModule).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>
    /// What each agent shows now: the newest reading from any source, and whether it's fresh. An agent is found when its
    /// program is here, a session of it is open, or (Claude Code) the Claude app's own file is; one that isn't shows
    /// nothing. Codex signed out shows no limits, as its logs are from before. So the card, the home, the bar and the
    /// alerts all agree.
    /// </summary>
    private AgentStatus StatusOf(AgentKind kind, DateTimeOffset now)
    {
        var track = _tracks[kind];
        bool found = track.Installed || Sessions.Any(s => s.Agent == kind) || kind == AgentKind.Claude && File.Exists(_paths.ClaudeHistory);
        bool signedOut = track.State == CheckState.SignedOut;
        var reading = !found || kind == AgentKind.Codex && signedOut ? null
            : new[] { track.Live, track.Other }.Where(r => r is not null).MaxBy(r => r!.At);
        bool fresh = reading is not null && (now - reading.At < AgentStatus.StaleAfter || (track.State == CheckState.Ok && ReferenceEquals(reading, track.Live)));
        return new AgentStatus(kind, found, track.State, reading, fresh, reading?.Plan ?? (signedOut ? null : track.Plan));
    }

    // The home's button, the bar's activities, the alerts and the pages, each only when it changed.
    private void Publish(AgentUsageSettings settings, DateTimeOffset now)
    {
        var all = Kinds.ToDictionary(k => k, k => StatusOf(k, now));
        var agents = Kinds.Where(settings.Reads).Select(k => all[k]).ToList();
        All = all;
        Agents = agents;

        var (home, caution) = AgentBar.Home(agents, settings.WarningLevel, now);
        if (home != _home || caution != _homeCaution)
        {
            _home = home;
            _homeCaution = caution;
            _tile?.SetStatus(home);
            _tile?.SetAttention(caution);
        }

        var sessions = Sessions;
        Show(ref _waiting, AgentBar.Waiting(sessions), AgentBar.WaitingKey);
        Show(ref _working, AgentBar.Working(sessions, agents, now), AgentBar.WorkingKey);

        var alerts = new List<UsageAlert>(_alerts.Tick(now));
        foreach (var agent in agents)
        {
            var track = _tracks[agent.Agent];
            // Only a fresh reading: a days-old one (the Claude app's file) would make the first live one look like a reset.
            if (agent.Fresh && agent.Reading is { } reading && reading.At != track.Alerted)
            {
                track.Alerted = reading.At;
                alerts.AddRange(_alerts.Read(agent.Agent, reading, settings.WarningLevel, now));
            }
        }
        foreach (var alert in alerts.Where(a => a.Near ? settings.NearLimitAlerts : settings.ResetAlerts))
            Queue(alert);

        string signature = string.Join('|', agents.Select(a => $"{a.Agent}:{a.Found}:{a.Check}:{a.Fresh}:{a.Plan}:{a.Reading?.At.ToUnixTimeSeconds()}:"
                + string.Join(',', a.Reading?.Windows.Select(w => $"{w.Label}={w.Used}@{w.ResetsAt?.ToUnixTimeSeconds()}:{w.HasReset(now)}") ?? [])))
            + "#" + string.Join('|', sessions.Select(s => $"{s.Agent}:{s.Id}:{s.Name}:{s.Working}:{s.Since.ToUnixTimeSeconds()}:{AppOf(s)}"))
            + "#" + settings;
        if (signature != _signature)
        {
            _signature = signature;
            Changed?.Invoke();
        }
    }

    private void Show(ref IslandActivity? shown, IslandActivity? next, string key)
    {
        if (next == shown)
            return;
        shown = next;
        if (next is null)
            _context?.Island.Clear(key);
        else
            _context?.Island.Publish(next);
    }

    // Alerts take turns on the bar, 6 seconds each. The hub only posts its change to the UI thread, so publishing under the
    // lock can't wait on it.
    private void Queue(UsageAlert alert)
    {
        lock (_alertGate)
        {
            _alertQueue.Enqueue(alert);
            if (!_alertShowing)
                NextAlert();
        }
    }

    private void NextAlert()
    {
        lock (_alertGate)
        {
            if (_alertTimer is null || _context is not { } context)
                return; // stopped
            _alertShowing = _alertQueue.TryDequeue(out var next);
            if (next is null)
            {
                context.Island.Clear(AgentBar.AlertKey);
                return;
            }
            context.Island.Publish(AgentBar.Alert(next, Settings.WarningLevel));
            _alertTimer.Change(AlertTime, Timeout.InfiniteTimeSpan);
        }
    }
}
