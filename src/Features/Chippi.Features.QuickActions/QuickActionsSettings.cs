// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using System.IO;
using Chippi.Features.Sound.Mute;

namespace Chippi.Features.QuickActions;

/// <summary>Quick actions' settings: what the page shows, the kept apps and the shortcuts.</summary>
public sealed record QuickActionsSettings
{
    /// <summary>The actions on the page, by key; null until the first Done, so the page first opens on the picker.</summary>
    public List<string>? Shown { get; init; }

    /// <summary>The gallery's twenty positions, including hidden actions and intentional empty slots. Null migrates the old packed gallery.</summary>
    public List<string?>? Layout { get; init; }

    /// <summary>Apps "Quit all" never closes, by program path; matched by the file's name, which an update's new folder doesn't change.</summary>
    public List<string> Kept { get; init; } = [];

    /// <summary>Each action's shortcut, by key; none by default.</summary>
    public Dictionary<string, KeyShortcut> Shortcuts { get; init; } = [];

    /// <summary>
    /// Tiles other features added (Input), by key, once they had a position: the first time its feature runs, a tile goes
    /// after the last tile, and from then on keeps its position while its feature is out, as Cleaning does.
    /// </summary>
    public List<string> Added { get; init; } = [];

    public bool Shows(QuickActionId id) => Shows(QuickAction.Of(id).Key);

    public bool Shows(string key) => Shown?.Contains(key) == true;

    public bool Keeps(string path) =>
        Kept.Any(kept => string.Equals(Path.GetFileName(kept), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));

    public KeyShortcut? ShortcutOf(QuickActionId id) => Shortcuts.GetValueOrDefault(QuickAction.Of(id).Key);
}
