// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AgentUsage;

/// <summary>The two agents Chippi reads (Travis, 2026-11-01): Claude Code and Codex.</summary>
public enum AgentKind { Claude, Codex }

public static class AgentNames
{
    /// <summary>"Claude Code", "Codex": a card's name and the bar's second line.</summary>
    public static string Name(this AgentKind agent) => agent == AgentKind.Claude ? "Claude Code" : "Codex";

    /// <summary>"Claude", "Codex": the home button's status ("Claude 42%").</summary>
    public static string ShortName(this AgentKind agent) => agent == AgentKind.Claude ? "Claude" : "Codex";

    /// <summary>"CC", "Cx": the agent's letters on the accent tint. Chippi never redraws another company's logo.</summary>
    public static string Monogram(this AgentKind agent) => agent == AgentKind.Claude ? "CC" : "Cx";
}

/// <summary>One limit: its name ("5-hour", "Weekly", "Opus weekly"), the share used (0 to 100) and when it resets.</summary>
/// <param name="ResetsAt">Null where the source doesn't say (the Claude app's history file).</param>
public sealed record UsageWindow(string Label, double Used, DateTimeOffset? ResetsAt)
{
    /// <summary>Past its reset time: the window shows "Reset" until the next reading, never 0%.</summary>
    public bool HasReset(DateTimeOffset now) => ResetsAt is { } at && now >= at;

    /// <summary>The share as the page shows it, whole percents: "42%".</summary>
    public int Percent => (int)Math.Round(Math.Clamp(Used, 0, 100), MidpointRounding.AwayFromZero);
}

/// <summary>What one source said about an agent's limits, and when.</summary>
/// <param name="Plan">"Max", "Pro"; null where the source doesn't say.</param>
/// <param name="At">When the reading was taken (a log's turn, the Claude app's last check), not when Chippi read it.</param>
public sealed record UsageReading(string? Plan, IReadOnlyList<UsageWindow> Windows, DateTimeOffset At);

/// <summary>How the last check of an agent's limits went.</summary>
public enum CheckState
{
    /// <summary>Not checked yet.</summary>
    None,

    /// <summary>The agent answered with its limits.</summary>
    Ok,

    /// <summary>The agent's program isn't on this PC.</summary>
    NotFound,

    /// <summary>The agent answered, but isn't signed in.</summary>
    SignedOut,

    /// <summary>The agent's program doesn't know the request: it's older than the way Chippi asks.</summary>
    TooOld,

    /// <summary>Anything else: it timed out, exited or said something Chippi couldn't read. The next check may work.</summary>
    Failed,
}

public static class CheckStates
{
    /// <summary>What only you can fix (installing, signing in, updating): checks back off up to 30 minutes, not 10.</summary>
    public static bool OnlyYouCanFix(this CheckState state) => state is CheckState.NotFound or CheckState.SignedOut or CheckState.TooOld;
}

/// <summary>The result of one check.</summary>
/// <param name="Reading">The limits, with <see cref="CheckState.Ok"/>; null otherwise.</param>
/// <param name="Error">Why it failed, for the log; never shown.</param>
public sealed record CheckResult(CheckState State, UsageReading? Reading = null, string? Error = null)
{
    public static CheckResult Missing { get; } = new(CheckState.NotFound);
}

/// <summary>An agent as the page, the bar and Settings show it: how its last check went and the reading to show.</summary>
/// <param name="Found">Its program, its files or a reading is on this PC: it gets a card.</param>
/// <param name="Check">How the last check went.</param>
/// <param name="Reading">The newest reading from any source, or null.</param>
/// <param name="Fresh">The last check gave this reading, or it's under <see cref="AgentStatus.StaleAfter"/> old: no "As of …".</param>
/// <param name="Plan">The plan, from the last check that said ("Max"); kept when a later reading doesn't say.</param>
public sealed record AgentStatus(AgentKind Agent, bool Found, CheckState Check, UsageReading? Reading, bool Fresh, string? Plan)
{
    /// <summary>A reading older than this, that the last check didn't give, shows "As of …".</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public static AgentStatus Unknown(AgentKind agent) => new(agent, false, CheckState.None, null, false, null);

    /// <summary>The window closest to its limit, as of <paramref name="now"/>: one past its reset counts as 0.</summary>
    public UsageWindow? Closest(DateTimeOffset now) =>
        Reading?.Windows.Where(w => !w.HasReset(now)).OrderByDescending(w => w.Used).FirstOrDefault();
}
