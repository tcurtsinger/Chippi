// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>Which GPU and network adapter the page and the tile show.</summary>
internal static class Pick
{
    /// <summary>
    /// The chosen GPU while it's there, otherwise the one with the most dedicated memory among those
    /// that report load (a discrete card before an integrated one). With <paramref name="listed"/>,
    /// only GPUs that have reported load this session count (the card's list).
    /// </summary>
    public static GpuReading? Gpu(IReadOnlyList<GpuReading>? gpus, string? chosen, ISet<string>? listed)
    {
        var candidates = (gpus ?? []).Where(g => listed is null ? g.Load is not null : listed.Contains(g.Id)).ToList();
        return candidates.FirstOrDefault(g => string.Equals(g.Id, chosen, StringComparison.OrdinalIgnoreCase))
            ?? candidates.OrderByDescending(g => g.DedicatedTotalBytes ?? 0).FirstOrDefault();
    }

    /// <summary>
    /// The chosen adapter while it's there, otherwise the first connected one, otherwise the first
    /// (design notes #14: when the remembered one disappears, the card moves to another).
    /// </summary>
    public static NetworkReading? Adapter(IReadOnlyList<NetworkReading>? adapters, string? chosen)
    {
        var list = adapters ?? [];
        return list.FirstOrDefault(a => string.Equals(a.Id, chosen, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(a => a.Connected)
            ?? list.FirstOrDefault();
    }
}
