// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Features.AgentUsage.Sessions;

namespace Chippi.Features.AgentUsage;

/// <summary>
/// What the bar and the home show for the agents (the brief's Surfaces 1, 3 and 4). While an agent works: its provider icon in
/// the lead, "Working · 42%" (its closest limit) over "Claude Code · chippi" ("2 sessions" when several), ranked below
/// every other activity. Waiting on you: the provider mark on an amber tint, "Waiting on you", ranked above all but a song. Neither
/// marks the home's button, which is Caution only at the warning level; a click on the bar opens Agents.
/// </summary>
internal static class AgentBar
{
    public const string WorkingKey = "working";
    public const string WaitingKey = "waiting";
    public const string AlertKey = "alert";

    /// <summary>The activity for the sessions waiting on you, or null when none is.</summary>
    public static IslandActivity? Waiting(IReadOnlyList<AgentSession> sessions) =>
        Of(sessions.Where(s => s.NeedsInput).ToList(), WaitingKey, _ => "Waiting on you", attention: true);

    /// <summary>The activity for the sessions working, or null when none is.</summary>
    public static IslandActivity? Working(IReadOnlyList<AgentSession> sessions, IReadOnlyList<AgentStatus> agents, DateTimeOffset now) =>
        Of(sessions.Where(s => s.Working).ToList(), WorkingKey, agent =>
            agents.FirstOrDefault(a => a.Agent == agent)?.Closest(now) is { } closest ? $"Working · {closest.Percent}%" : "Working",
            attention: false);

    /// <summary>An alert, on the bar for 6 seconds over a song as Plain paste's feedback is: the provider mark, Caution for a near limit.</summary>
    public static IslandActivity Alert(UsageAlert alert, int level) =>
        new(AlertKey, IslandActivityKind.Status, alert.Title(level), alert.Source)
        {
            ProviderId = alert.Agent.ToString(),
            IsFeedback = true,
            NeedsAttention = alert.Near,
            OpensTile = AgentUsageModule.TileKey,
        };

    /// <summary>The home button's status: the closest limit, "Claude 42%"; "None" with no limits to show.</summary>
    public static (string Status, bool Caution) Home(IReadOnlyList<AgentStatus> agents, int level, DateTimeOffset now)
    {
        var top = agents
            .Select(a => (a.Agent, Window: a.Closest(now)))
            .Where(t => t.Window is not null)
            .OrderByDescending(t => t.Window!.Used)
            .FirstOrDefault();
        return top.Window is { } window ? ($"{top.Agent.ShortName()} {window.Percent}%", window.Used >= level) : ("None", false);
    }

    // The newest session's agent leads; the second line names the one session, or counts them.
    private static IslandActivity? Of(IReadOnlyList<AgentSession> sessions, string key, Func<AgentKind, string> title, bool attention)
    {
        if (sessions.Count == 0)
            return null;
        var lead = sessions.OrderByDescending(s => s.Since).First();
        bool oneAgent = sessions.All(s => s.Agent == lead.Agent);
        string source = sessions.Count == 1 ? $"{lead.Agent.Name()} · {lead.Name}"
            : oneAgent ? $"{lead.Agent.Name()} · {sessions.Count} sessions"
            : $"{sessions.Count} sessions";
        return new IslandActivity(key, IslandActivityKind.Agent, title(lead.Agent), source)
        {
            ProviderId = lead.Agent.ToString(),
            NeedsAttention = attention,
            OpensTile = AgentUsageModule.TileKey,
        };
    }
}
