// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AgentUsage.Views;

/// <summary>What the island page and Settings both say about an agent (the brief's States).</summary>
internal static class AgentLines
{
    /// <summary>
    /// The windows a card lists: the reading's. Codex signed out lists none (its logs are from before); Claude Code signed
    /// out still lists the Claude app's own last check, as of when it was.
    /// </summary>
    public static IReadOnlyList<UsageWindow> Rows(AgentStatus agent) =>
        agent.Reading is not { } reading || agent.Check == CheckState.SignedOut && agent.Agent == AgentKind.Codex ? [] : reading.Windows;

    /// <summary>
    /// The card's note at the right: "As of 2:10 PM" (tertiary) for a stale reading; with no rows, "Signed out", "Too old to
    /// read limits" or "Check failed" in Caution.
    /// </summary>
    public static (string Text, bool Caution) State(AgentStatus agent, DateTimeOffset now)
    {
        if (Rows(agent).Count > 0)
            return agent.Fresh ? ("", false) : (AgentText.AsOf(agent.Reading!.At, now), false);
        return agent.Check switch
        {
            CheckState.SignedOut => ("Signed out", true),
            CheckState.TooOld => ("Too old to read limits", true),
            CheckState.Failed => ("Check failed", true),
            _ => ("", false),
        };
    }

    /// <summary>Settings' Agents row: "Signed in · Max", "Not found", "Signed out", "Too old to read limits", "Check failed".</summary>
    public static string Caption(AgentStatus agent) => agent.Check switch
    {
        CheckState.Ok => agent.Plan is { } plan ? $"Signed in · {plan}" : "Signed in",
        CheckState.NotFound => "Not found",
        CheckState.SignedOut => "Signed out",
        CheckState.TooOld => "Too old to read limits",
        CheckState.Failed => "Check failed",
        _ => agent.Found ? "" : "Not found",
    };
}
