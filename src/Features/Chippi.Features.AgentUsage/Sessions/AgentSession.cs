// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AgentUsage.Sessions;

public enum AgentSessionState { Idle, Working, NeedsInput }

/// <summary>One open, user-facing session. Only an outstanding request for the owner needs attention.</summary>
public sealed record AgentSession(AgentKind Agent, string Id, string Name, AgentSessionState Status, DateTimeOffset Since,
    int? ProcessId, string? Entrypoint)
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);

    public bool Working => Status == AgentSessionState.Working;
    public bool NeedsInput => Status == AgentSessionState.NeedsInput;
    public bool IsIdle(DateTimeOffset now) => Status == AgentSessionState.Idle && now - Since > IdleLimit;

    public string State(DateTimeOffset now) => Status switch
    {
        AgentSessionState.Working => $"Working · {AgentText.Minutes(now - Since)}",
        AgentSessionState.NeedsInput => "Waiting on you",
        _ => "Idle",
    };
}

/// <summary>A session's place on screen: the app it runs in and its window, which "Switch to" brings forward.</summary>
public sealed record SessionApp(nint Window, string App);
