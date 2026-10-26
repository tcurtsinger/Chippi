// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Config;

namespace Chippi.App.Island;

/// <summary>
/// What Settings › Island &amp; pet says about the island's content: which page it opens on, the
/// tiles' order and which are left off the grid, and what the round buttons hold.
/// </summary>
/// <param name="Left">The left (top edge) or upper (side edge) feature button: a tile key, "" for none, null for the feature's own.</param>
/// <param name="Right">Likewise for the right or lower button.</param>
internal sealed record IslandLayout(IslandOpensOn OpensOn, IReadOnlyList<string> Order, IReadOnlySet<string> Hidden, string? Left, string? Right, bool Live)
{
    public static IslandLayout Default { get; } = new(IslandOpensOn.LastPage, [], new HashSet<string>(), null, null, true);

    public static IslandLayout From(AppSettings settings) => new(
        settings.IslandOpensOn,
        settings.IslandTileOrder ?? [],
        new HashSet<string>(settings.IslandHiddenTiles ?? [], StringComparer.Ordinal),
        settings.IslandLeftButton,
        settings.IslandRightButton,
        settings.IslandLiveButton);

    /// <summary><paramref name="keys"/> (in their own order) put in the chosen order; keys it doesn't list follow in theirs.</summary>
    public IReadOnlyList<string> Sorted(IEnumerable<string> keys)
    {
        var all = keys.ToList();
        var order = Order.ToList();
        return [.. all.OrderBy(k => order.IndexOf(k) is >= 0 and var at ? at : order.Count + all.IndexOf(k))];
    }

    /// <summary>The grid's tiles: <see cref="Sorted"/>, without the hidden ones.</summary>
    public IReadOnlyList<string> Shown(IEnumerable<string> keys) => [.. Sorted(keys).Where(k => !Hidden.Contains(k))];

    /// <summary>
    /// A feature button's tile: the chosen key while it exists, none for "", or otherwise the tile
    /// whose feature asked for that side.
    /// </summary>
    public static string? Button(string? choice, Func<string, bool> exists, string? featureDefault) => choice switch
    {
        null => featureDefault,
        "" => null,
        _ => exists(choice) ? choice : null,
    };
}
