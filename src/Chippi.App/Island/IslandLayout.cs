// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Config;

namespace Chippi.App.Island;

/// <summary>
/// What Settings › Island &amp; pet says about the island's content: which page it opens on, the
/// tiles' order and which are left off the grid.
/// </summary>
internal sealed record IslandLayout(IslandOpensOn OpensOn, IReadOnlyList<string> Order, IReadOnlySet<string> Hidden)
{
    public static IslandLayout Default { get; } = new(IslandOpensOn.LastPage, [], new HashSet<string>());

    public static IslandLayout From(AppSettings settings) => new(
        settings.IslandOpensOn,
        settings.IslandTileOrder ?? [],
        new HashSet<string>(settings.IslandHiddenTiles ?? [], StringComparer.Ordinal));

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
    /// The hidden tiles once <paramref name="key"/>'s Show switch is turned on or off (<paramref name="show"/>). The tiles that
    /// were past the first <paramref name="room"/> shown in <paramref name="order"/>, whose switches showed off, are saved hidden
    /// too, so a slot freed stays free for the next switch turned on instead of going to the next tile in order.
    /// </summary>
    public static List<string> Switched(IEnumerable<string> hidden, IReadOnlyList<string> order, string key, bool show, int room)
    {
        var was = hidden.ToList();
        var next = was.Concat(order.Where(k => !was.Contains(k)).Skip(room)).Where(k => k != key).ToList();
        if (!show)
            next.Add(key);
        return next;
    }
}
