// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AgentUsage;

/// <summary>An alert for the bar: a window passed the warning level, or one that had passed it reset.</summary>
public sealed record UsageAlert(AgentKind Agent, string Window, bool Near)
{
    /// <summary>"80% used" (the level it passed) or "Reset".</summary>
    public string Title(int level) => Near ? $"{level}% used" : "Reset";

    /// <summary>"Claude Code · 5-hour".</summary>
    public string Source => $"{Agent.Name()} · {Window}";
}

/// <summary>
/// The two alerts (the brief's Surfaces 4). Near a limit: a window's reading passes the warning level. A reset: a window
/// that had passed it reaches its reset time, or a newer reading shows it under the level again. The first reading of a
/// window after a start, or after the level changes, only sets where it stands: nothing passed it.
/// </summary>
internal sealed class UsageAlerts
{
    private readonly Dictionary<(AgentKind, string), Seen> _windows = [];
    private int _level = -1;

    private sealed record Seen(DateTimeOffset At, DateTimeOffset? ResetsAt, bool Over);

    /// <summary>What <paramref name="reading"/> passed, against what the last reading of each window said.</summary>
    public IReadOnlyList<UsageAlert> Read(AgentKind agent, UsageReading reading, int level, DateTimeOffset now)
    {
        if (level != _level)
        {
            _windows.Clear();
            _level = level;
        }
        var alerts = new List<UsageAlert>();
        foreach (var window in reading.Windows)
        {
            var key = (agent, window.Label);
            bool over = !window.HasReset(now) && window.Used >= level;
            if (_windows.TryGetValue(key, out var seen))
            {
                if (reading.At <= seen.At)
                    continue; // not newer than what was seen
                if (over && !seen.Over)
                    alerts.Add(new UsageAlert(agent, window.Label, Near: true));
                else if (!over && seen.Over)
                    alerts.Add(new UsageAlert(agent, window.Label, Near: false));
            }
            _windows[key] = new Seen(reading.At, window.ResetsAt, over);
        }
        return alerts;
    }

    /// <summary>The windows over the level whose reset time has come since, each once.</summary>
    public IReadOnlyList<UsageAlert> Tick(DateTimeOffset now)
    {
        var alerts = new List<UsageAlert>();
        foreach (var (key, seen) in _windows.ToList())
        {
            if (seen.Over && seen.ResetsAt is { } at && now >= at)
            {
                alerts.Add(new UsageAlert(key.Item1, key.Item2, Near: false));
                _windows[key] = seen with { Over = false };
            }
        }
        return alerts;
    }

    /// <summary>Forgets an agent switched off, so turning it on again starts afresh.</summary>
    public void Forget(AgentKind agent)
    {
        foreach (var key in _windows.Keys.Where(k => k.Item1 == agent).ToList())
            _windows.Remove(key);
    }
}
