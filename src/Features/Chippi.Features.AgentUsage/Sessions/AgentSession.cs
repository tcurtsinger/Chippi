// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AgentUsage.Sessions;

/// <summary>
/// One running session of an agent. Working: a turn is being written. Waiting on you: the turn has ended and the agent
/// waits for your next message. A permission prompt in the middle of a turn can't be told apart from a long tool, so it
/// shows as Working.
/// </summary>
/// <param name="Id">The agent's own id for the session (Claude's session id, Codex's thread id).</param>
/// <param name="Name">The session's name, or its folder's ("chippi").</param>
/// <param name="Since">When it started working, or started waiting.</param>
/// <param name="ProcessId">The agent's process, whose window "Switch to" brings forward; null when it couldn't be told.</param>
/// <param name="Entrypoint">How it was started, where the agent says ("claude-desktop", "cli", "vscode"): the app's name when no window is found.</param>
public sealed record AgentSession(AgentKind Agent, string Id, string Name, bool Working, DateTimeOffset Since, int? ProcessId, string? Entrypoint)
{
    /// <summary>A session waiting on you this long leaves the list.</summary>
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);

    /// <summary>Gone from the list: waiting on you for over <see cref="IdleLimit"/>.</summary>
    public bool IsIdle(DateTimeOffset now) => !Working && now - Since > IdleLimit;

    /// <summary>"Working · 12 min" or "Waiting on you".</summary>
    public string State(DateTimeOffset now) => Working ? $"Working · {AgentText.Minutes(now - Since)}" : "Waiting on you";
}

/// <summary>A session's place on screen: the app it runs in and its window, which "Switch to" brings forward.</summary>
/// <param name="Window">The app's top-level window, or 0 when none was found.</param>
/// <param name="App">"Claude", "Terminal", "VS Code", "Codex".</param>
public sealed record SessionApp(nint Window, string App);
