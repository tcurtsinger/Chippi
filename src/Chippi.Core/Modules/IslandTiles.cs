// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

/// <summary>Which round button beside the open island opens a tile directly (docs/phase-3/architecture.md §5).</summary>
public enum IslandOrbit
{
    None,
    Left,
    Right,
}

/// <summary>
/// One module's part of an island tile (docs/phase-3/architecture.md §5).
/// </summary>
/// <param name="Key">Identifies the tile, such as "sound.microphone". Modules that use the same key share the tile and its page.</param>
/// <param name="Name">The tile's name, such as "Microphone". A shared tile takes the name and icon of its first section.</param>
/// <param name="Icon">Lucide icon key, as in <see cref="FeatureDescriptor.Icon"/>.</param>
/// <param name="Order">Where the tile sits in the grid, lowest first. Now Playing is always first.</param>
/// <param name="SectionOrder">Where this module's section sits on a shared page, lowest first.</param>
/// <param name="CreateSection">This module's section of the page. Must return a WPF FrameworkElement.</param>
public sealed record IslandTileSpec(string Key, string Name, string Icon, int Order, int SectionOrder, Func<object> CreateSection)
{
    /// <summary>Default page width in DIPs, the grid's.</summary>
    public const double DefaultPageWidth = 328;

    /// <summary>The page's width in DIPs. On a shared page the widest section's width wins.</summary>
    public double PageWidth { get; init; } = DefaultPageWidth;

    /// <summary>A round button beside the open island that opens this tile.</summary>
    public IslandOrbit Orbit { get; init; } = IslandOrbit.None;

    /// <summary>On a shared tile, the status of the module with the highest priority that set one shows ("Muted" over "Live").</summary>
    public int StatusPriority { get; init; }
}

/// <summary>A module's handle on its island tile.</summary>
public interface IIslandTile
{
    /// <summary>The tile's status line, such as "Muted" or "1 hr left"; empty for none.</summary>
    void SetStatus(string status);
}

/// <summary>A running module's tile part, as the island host reads it.</summary>
public sealed class IslandTileContribution(string moduleId, IslandTileSpec spec) : IIslandTile
{
    private string _status = "";

    public string ModuleId { get; } = moduleId;

    public IslandTileSpec Spec { get; } = spec;

    public string Status => Volatile.Read(ref _status);

    /// <summary>Raised on the thread that set the status.</summary>
    public event EventHandler? StatusChanged;

    public void SetStatus(string status)
    {
        status ??= "";
        if (Interlocked.Exchange(ref _status, status) != status)
            StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Groups tile parts by key into tiles in grid order. A tile's parts are in section order, it takes
    /// its name and icon from its first part, and the widest part's page width.
    /// </summary>
    public static IReadOnlyList<IslandTileGroup> Group(IEnumerable<IslandTileContribution> parts) =>
        [.. parts
            .GroupBy(p => p.Spec.Key, StringComparer.Ordinal)
            .Select(g => new IslandTileGroup(g.Key, [.. g.OrderBy(p => p.Spec.SectionOrder)]))
            .OrderBy(t => t.Order)];
}

/// <summary>One tile in the grid: the parts that share its key, in section order.</summary>
public sealed record IslandTileGroup(string Key, IReadOnlyList<IslandTileContribution> Parts)
{
    public string Name => Parts[0].Spec.Name;

    public string Icon => Parts[0].Spec.Icon;

    public int Order => Parts.Min(p => p.Spec.Order);

    public double PageWidth => Parts.Max(p => p.Spec.PageWidth);

    public IslandOrbit Orbit => Parts.Select(p => p.Spec.Orbit).FirstOrDefault(o => o != IslandOrbit.None);

    /// <summary>The highest-priority non-empty status, or empty.</summary>
    public string Status =>
        Parts.Where(p => p.Status.Length > 0).OrderByDescending(p => p.Spec.StatusPriority).FirstOrDefault()?.Status ?? "";
}
