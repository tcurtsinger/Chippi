// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Sound.Mute;

namespace Chippi.Features.QuickActions;

/// <summary>Quick actions' settings: what the page shows, the kept apps and the shortcuts.</summary>
public sealed record QuickActionsSettings
{
    /// <summary>The actions on the page, by key; null until the first Done, so the page first opens on the picker.</summary>
    public List<string>? Shown { get; init; }

    /// <summary>Apps "Quit all" never closes, by program path.</summary>
    public List<string> Kept { get; init; } = [];

    /// <summary>Each action's shortcut, by key; none by default.</summary>
    public Dictionary<string, MicShortcut> Shortcuts { get; init; } = [];

    public bool Shows(QuickActionId id) => Shown?.Contains(QuickAction.Of(id).Key) == true;

    public bool Keeps(string path) => Kept.Contains(path, StringComparer.OrdinalIgnoreCase);

    public MicShortcut? ShortcutOf(QuickActionId id) => Shortcuts.GetValueOrDefault(QuickAction.Of(id).Key);
}
