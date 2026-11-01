// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>One app in a detail page's top apps: every process of one program file, added up.</summary>
internal sealed record AppUse(string App, string Name, string? Path, double Amount);

/// <summary>Groups processes by program file and ranks the apps by what a detail page measures.</summary>
internal static class TopApps
{
    public const int Count = 3;

    /// <summary>The apps using the most CPU, in percent of every logical processor.</summary>
    public static IReadOnlyList<AppUse> Cpu(IReadOnlyList<ProcessReading> processes) =>
        Rank(processes, p => p.CpuPercent, total => Math.Min(total, 100));

    /// <summary>The apps using the most memory, in bytes of active private working set.</summary>
    public static IReadOnlyList<AppUse> Memory(IReadOnlyList<ProcessReading> processes) =>
        Rank(processes, p => p.PrivateBytes, total => total);

    /// <summary>The apps keeping a GPU busiest: each app's processes added up, in percent.</summary>
    public static IReadOnlyList<AppUse> Gpu(IReadOnlyList<ProcessReading> processes, IReadOnlyDictionary<int, double>? loads) =>
        loads is null ? [] : Rank(processes, p => loads.TryGetValue(p.Id, out double load) ? load : null, total => Math.Min(total, 100));

    // An app too small for its figure to show (under 0.05%) is left out.
    private static List<AppUse> Rank(IReadOnlyList<ProcessReading> processes, Func<ProcessReading, double?> amount, Func<double, double> cap) =>
        [.. processes
            .Select(p => (Process: p, Amount: amount(p)))
            .Where(p => p.Amount is > 0)
            .GroupBy(p => p.Process.App, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AppUse(g.Key, g.First().Process.Name, g.Select(p => p.Process.Path).FirstOrDefault(p => p is not null), cap(g.Sum(p => p.Amount!.Value))))
            .Where(a => a.Amount >= 0.05)
            .OrderByDescending(a => a.Amount)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(Count)];
}
