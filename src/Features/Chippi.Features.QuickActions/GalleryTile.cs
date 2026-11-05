// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions;

/// <summary>
/// A tile another feature added (docs/phase-6/architecture.md §3): the running commands that share its key, in the order
/// Chippi lists them. A menu lists them; otherwise the tile is its first command.
/// </summary>
public sealed record AddedTile(ActionsTile Tile, IReadOnlyList<ChippiCommand> Commands)
{
    public string Key => Tile.Key;

    /// <summary>A menu's status: how many of its switches are on ("2 on"), "Off" with none on; a switch's On or Off.</summary>
    public string Status
    {
        get
        {
            if (!Tile.Menu)
                return Commands[0].IsOn is { } on ? on ? "On" : "Off" : "";
            int switches = Commands.Count(c => c.IsOn is not null), lit = Commands.Count(c => c.IsOn == true);
            return switches == 0 ? "" : lit == 0 ? "Off" : $"{lit} on";
        }
    }

    /// <summary>Groups the commands that name a tile, in list order; a tile takes its first command's label and icon.</summary>
    public static IReadOnlyList<AddedTile> From(IEnumerable<ChippiCommand> commands) =>
        [.. commands.Where(c => c.Tile is not null && QuickAction.FromKey(c.Tile.Key) is null)
            .GroupBy(c => c.Tile!.Key, StringComparer.Ordinal)
            .Select(g => new AddedTile(g.First().Tile!, [.. g]))];
}

/// <summary>One tile of the gallery or the editor: a Quick action or a tile another feature added.</summary>
internal sealed record GalleryTile(string Key, string Label, string Name, Geometry Icon, QuickAction? Action, AddedTile? Added)
{
    public GalleryTile(QuickAction action) : this(action.Key, action.Label, action.Name, action.Icon, action, null) { }

    public GalleryTile(AddedTile added) : this(added.Key, added.Tile.Label, added.Tile.Menu ? added.Tile.Label : added.Commands[0].Name, LucideIcons.Get(added.Tile.Icon), null, added) { }
}
