// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AgentUsage;

/// <summary>Settings › AI agent usage: which agents are read, and the alerts.</summary>
public sealed record AgentUsageSettings
{
    /// <summary>The warning levels offered, 50% to 95% in steps of 5.</summary>
    public static IReadOnlyList<int> Levels { get; } = [.. Enumerable.Range(10, 10).Select(i => i * 5)];

    public bool Claude { get; init; } = true;

    public bool Codex { get; init; } = true;

    /// <summary>At this share or over, a window turns Caution and the Near a limit alert shows as it passes it.</summary>
    public int WarningLevel { get; init; } = 80;

    public bool NearLimitAlerts { get; init; } = true;

    public bool ResetAlerts { get; init; } = true;

    public bool Reads(AgentKind agent) => agent == AgentKind.Claude ? Claude : Codex;

    /// <summary>A level that isn't one offered goes to the nearest that is.</summary>
    public AgentUsageSettings Normalized() =>
        Levels.Contains(WarningLevel) ? this : this with { WarningLevel = Levels.MinBy(l => Math.Abs(l - WarningLevel)) };
}
