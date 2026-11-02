// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.AgentUsage.Readers;
using Chippi.Features.AgentUsage.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.AgentUsage.Tests;

/// <summary>A clock the tests move by hand, with timers that fire as it passes them.</summary>
internal sealed class FakeTime(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset until;
        lock (_gate) until = _now + by;
        while (true)
        {
            FakeTimer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.Due is { } due && due <= until).MinBy(t => t.Due);
                if (next is null) { _now = until; return; }
                _now = next.Due!.Value;
                next.Due = next.Period > TimeSpan.Zero ? _now + next.Period : null;
            }
            next.Fire();
        }
    }

    private sealed class FakeTimer(FakeTime time, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due;
        public TimeSpan Period;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (time._gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : time._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                if (!time._timers.Contains(this)) time._timers.Add(this);
            }
            return true;
        }

        public void Dispose() { lock (time._gate) time._timers.Remove(this); }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

/// <summary>An agent's program that answers each line it's sent from a script; it closes when it has nothing more to say.</summary>
internal sealed class ScriptedConversation(Func<string, IEnumerable<string>> answer) : IAgentConversation
{
    private readonly ConcurrentQueue<string> _out = new();

    public List<string> Sent { get; } = [];

    public bool Disposed { get; private set; }

    /// <summary>Never answers or closes: a program that hangs until the check times out.</summary>
    public bool Hangs { get; init; }

    public Task SendAsync(string line, CancellationToken ct)
    {
        Sent.Add(line);
        foreach (string reply in answer(line))
            _out.Enqueue(reply);
        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        if (_out.TryDequeue(out string? line))
            return line;
        if (!Hangs)
            return null;
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        return null;
    }

    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}

/// <summary>The agents' programs, each answering from a script; a missing script is a program not on this PC.</summary>
internal sealed class FakePrograms : IAgentPrograms
{
    public Dictionary<AgentKind, Func<ScriptedConversation>> Scripts { get; } = [];

    public List<(AgentKind Agent, IReadOnlyList<string> Arguments)> Started { get; } = [];

    public HashSet<int> Own { get; } = [];

    public bool IsInstalled(AgentKind agent) => Scripts.ContainsKey(agent);

    public IAgentConversation? Start(AgentKind agent, IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (!Scripts.TryGetValue(agent, out var script))
            return null;
        Started.Add((agent, arguments));
        return script();
    }

    public bool IsOwn(int processId) => Own.Contains(processId);
}

/// <summary>Windows as the tests say: which processes run, which locks are held and by whom, and each process's app.</summary>
internal sealed class FakeProcesses : IProcesses
{
    public Dictionary<int, long?> Running { get; } = [];

    public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> Holders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<int, SessionApp> Apps { get; } = [];

    public List<nint> Brought { get; } = [];

    public bool IsRunning(int processId, long? startedFileTime) =>
        Running.TryGetValue(processId, out long? started) && (startedFileTime is null || started is null || started == startedFileTime);

    public bool IsLocked(string path) => Locked.Contains(Path.GetFileName(path));

    public int? HolderOf(string path) => Holders.TryGetValue(Path.GetFileName(path), out int pid) ? pid : null;

    public SessionApp? AppOf(int processId) => Apps.GetValueOrDefault(processId);

    public bool BringForward(nint window)
    {
        Brought.Add(window);
        return window != 0;
    }
}

/// <summary>A folder of fixture files that goes when the test ends: never the owner's real files.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chippi-agent-usage-" + Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    public string Write(string relative, string text)
    {
        string file = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A started module on fakes: its tile, its activities on a real hub, its settings, and a clock to move.</summary>
internal sealed class Harness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 11, 1, 16, 52, 0, TimeSpan.FromHours(-6));

    private readonly TempFolder _folder = new();

    public Harness(Action<FakePrograms>? programs = null, Func<bool>? unseen = null)
    {
        programs?.Invoke(Programs);
        Paths = new AgentPaths(System.IO.Path.Combine(_folder.Path, ".claude"), System.IO.Path.Combine(_folder.Path, ".codex"),
            System.IO.Path.Combine(_folder.Path, "Claude", "plan-usage-history.json"));
        Module = new AgentUsageModule(Programs, Processes, Paths, Time, unseen ?? (() => false), loops: false);
        var context = new ModuleContext(AgentUsageModule.Id, Settings, NullLogger.Instance, Hub.CreatePublisher(AgentUsageModule.Id), Ui)
        {
            DataDirectory = _folder.Path,
            CloseIsland = () => Closed++,
        };
        Module.StartAsync(context, default).GetAwaiter().GetResult();
    }

    public FakeTime Time { get; } = new(Start);

    public FakePrograms Programs { get; } = new();

    public FakeProcesses Processes { get; } = new();

    public IslandActivityHub Hub { get; } = new();

    public FakeSettings Settings { get; } = new();

    public FakeUi Ui { get; } = new();

    public AgentPaths Paths { get; }

    public AgentUsageModule Module { get; }

    public int Closed { get; private set; }

    public IslandTileContribution Tile => Ui.Tile!;

    public string Write(string relative, string text) => _folder.Write(relative, text);

    /// <summary>Moves the clock and runs one turn of the loop.</summary>
    public void Step(TimeSpan? after = null)
    {
        if (after is { } by)
            Time.Advance(by);
        Module.StepAsync(default).GetAwaiter().GetResult();
    }

    public IslandActivity? Activity(string key) => Hub.Activities.FirstOrDefault(a => a.Key == key);

    public void Dispose()
    {
        Module.StopAsync(default).GetAwaiter().GetResult();
        _folder.Dispose();
    }
}

internal sealed class FakeSettings : IModuleSettings
{
    private object? _value;
    public T Get<T>() where T : class, new() => _value as T ?? new();
    public void Save<T>(T value) where T : class => _value = value;
}

internal sealed class FakeUi : IModuleUi
{
    public IslandTileContribution? Tile;
    public Func<string?, object>? SettingsPage;

    public IIslandTile AddIslandTile(IslandTileSpec spec) => Tile = new IslandTileContribution(AgentUsageModule.Id, spec);

    public void SetSettingsPage(Func<string?, object> createView) => SettingsPage = createView;
}

/// <summary>What the agents said on the owner's PC on 2026-11-01, as fixtures (the account's email taken out).</summary>
internal static class Answers
{
    public const string ClaudeInitializeSignedOut =
        """{"type":"control_response","response":{"subtype":"success","request_id":"chippi-initialize","response":{"commands":[],"account":{"tokenSource":"none","apiProvider":"firstParty"},"pid":49232,"claude_code_version":"2.1.314","session_state":"idle"}}}""";

    public const string ClaudeUsageSignedOut =
        """{"type":"control_response","response":{"subtype":"success","request_id":"chippi-usage","response":{"session":{"total_cost_usd":0,"total_api_duration_ms":0,"total_duration_ms":326,"total_lines_added":0,"total_lines_removed":0,"model_usage":{}},"subscription_type":null,"rate_limits_available":false,"rate_limits":null,"behaviors":null}}}""";

    public const string ClaudeInitializeSignedIn =
        """{"type":"control_response","response":{"subtype":"success","request_id":"chippi-initialize","response":{"account":{"email":"someone@example.com","organization":"Example","subscriptionType":"Claude Team","apiProvider":"firstParty"}}}}""";

    /// <summary>The owner's signed-in answer (2026-11-01, 19:47), as it came: listed and keyed limits, spend, credits.</summary>
    public const string ClaudeUsageTeam =
        """{"type":"control_response","response":{"subtype":"success","request_id":"chippi-usage","response":{"session":{"total_cost_usd":0,"total_api_duration_ms":0,"total_duration_ms":1414,"total_lines_added":0,"total_lines_removed":0,"model_usage":{}},"subscription_type":"team","rate_limits_available":true,"rate_limits":{"five_hour":{"utilization":0,"resets_at":null,"limit_dollars":null,"used_dollars":null,"remaining_dollars":null,"locked_reason":null},"seven_day":{"utilization":65,"resets_at":"2026-11-03T14:00:00.354007+00:00","limit_dollars":null,"used_dollars":null,"remaining_dollars":null,"locked_reason":null},"seven_day_oauth_apps":null,"seven_day_opus":null,"seven_day_sonnet":null,"seven_day_cowork":null,"seven_day_omelette":null,"tangelo":null,"iguana_necktie":null,"omelette_promotional":null,"nimbus_quill":null,"cinder_cove":null,"copper_kite":null,"brass_thimble":null,"harbor_lantern":null,"wattle_ember":null,"promo_credits":null,"amber_ladder":null,"granite_teacup":null,"amber_cistern":null,"amber_cistern_offer":null,"juniper_tide":null,"cedar_ember":null,"starry_waterfall":null,"amber_gauge":null,"pace":null,"extra_usage":{"is_enabled":true,"monthly_limit":null,"used_credits":0,"utilization":null,"currency":"USD","decimal_places":2,"disabled_reason":null,"user_disabled":false,"spend_limit_reached":false,"credits_ever_enabled":true,"daily":null,"weekly":null},"limits":[{"kind":"session","group":"session","percent":0,"severity":"normal","resets_at":null,"scope":null,"is_active":false},{"kind":"weekly_all","group":"weekly","percent":65,"severity":"normal","resets_at":"2026-11-03T14:00:00.354007+00:00","scope":null,"is_active":true},{"kind":"weekly_scoped","group":"weekly","percent":0,"severity":"normal","resets_at":"2026-11-03T14:00:00+00:00","scope":{"model":{"id":null,"display_name":"Fable"},"surface":null},"is_active":false}],"spend":{"used":{"amount_minor":0,"currency":"USD","exponent":2},"limit":null,"percent":0,"severity":"normal","enabled":true,"disabled_reason":null,"cap":null,"balance":null,"auto_reload":null,"disclaimer":"Usage credits cover you when you hit your plan limits.","can_purchase_credits":false,"can_toggle":false},"member_dashboard_available":false,"seven_day_breakdown":null,"weekly_scoped_shares":[{"limit_index":2,"used_percent_of_weekly":0,"allowance_percent_of_weekly":50}],"model_scoped":[{"display_name":"Fable","utilization":0,"resets_at":"2026-11-03T14:00:00+00:00"}]},"behaviors":null}}}""";

    public const string CodexInitialize =
        """{"id":1,"result":{"userAgent":"chippi/0.166.0 (Windows 10.0.26300; x86_64) unknown (chippi; 0.1.0)","codexHome":"C:\\Users\\Someone\\.codex","platformFamily":"windows","platformOs":"windows"}}""";

    public const string CodexStatus =
        """{"method":"remoteControl/status/changed","params":{"status":"disabled","serverName":"PC","installationId":"4a4de7d6","environmentId":null},"emittedAtMs":1793577342515}""";

    public const string CodexAccount =
        """{"id":2,"result":{"account":{"type":"chatgpt","email":"someone@example.com","planType":"pro","workspaceName":null},"requiresOpenaiAuth":true}}""";

    public const string CodexLimits =
        """{"id":3,"result":{"ordinaryUsageAllowed":true,"rateLimits":{"limitId":"codex","limitName":null,"normalModelSlug":null,"primary":{"usedPercent":9,"windowDurationMins":10080,"resetsAt":1793801690},"secondary":null,"credits":{"hasCredits":true,"unlimited":false,"balance":"62498.9550000000"},"individualLimit":null,"spendControlReached":false,"planType":"pro","rateLimitReachedType":null}}}""";

    /// <summary>Claude Code answering with limits, in the shape its usage takes: a share used and a reset time per window.</summary>
    public static string ClaudeUsage(double fiveHour, double weekly, string fiveHourReset = "2026-11-02T00:10:00Z", string weeklyReset = "2026-11-05T15:00:00Z") =>
        "{\"type\":\"control_response\",\"response\":{\"subtype\":\"success\",\"request_id\":\"chippi-usage\",\"response\":{\"subscription_type\":\"max\",\"rate_limits_available\":true,"
        + $"\"rate_limits\":{{\"five_hour\":{{\"utilization\":{fiveHour},\"resets_at\":\"{fiveHourReset}\"}},\"seven_day\":{{\"utilization\":{weekly},\"resets_at\":\"{weeklyReset}\"}}}}}}}}}}";

    /// <summary>A Claude Code that answers each request as the owner's signed-out one did.</summary>
    public static ScriptedConversation SignedOutClaude() => new(line =>
        line.Contains("\"initialize\"") ? [ClaudeInitializeSignedOut] : line.Contains("get_usage") ? [ClaudeUsageSignedOut] : []);

    public static ScriptedConversation SignedInClaude(double fiveHour, double weekly) => new(line =>
        line.Contains("\"initialize\"") ? [ClaudeInitializeSignedIn] : line.Contains("get_usage") ? [ClaudeUsage(fiveHour, weekly)] : []);

    /// <summary>A Codex server that answers as the owner's did, with a notification in between.</summary>
    public static ScriptedConversation Codex(string limits = CodexLimits, string account = CodexAccount) => new(line =>
        line.Contains("\"initialize\"") ? [CodexInitialize]
        : line.Contains("account/read") ? [CodexStatus, account]
        : line.Contains("rateLimits") ? [limits]
        : []);
}
