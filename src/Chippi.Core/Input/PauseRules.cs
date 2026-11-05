// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Input;

/// <summary>
/// Settings › General › Fullscreen apps (docs/phase-6/input-layer.md, Travis, tasks 198, 211 and 227). With
/// <paramref name="AlwaysOn"/> off, every Phase 6 input feature pauses while the window in front is a fullscreen app, or
/// belongs to an app on <paramref name="Apps"/> (games that run in a window). Apps are matched by their exe's file name, so a
/// game that moves with its launcher's library still matches.
/// </summary>
public sealed record PauseRules(bool AlwaysOn, IReadOnlyList<string> Apps)
{
    public static PauseRules Default { get; } = new(false, []);

    /// <summary>The window in front counts as fullscreen: it is, or its app is on the list.</summary>
    public bool CountsAsFullscreen(bool fullscreen, string? appPath) => fullscreen || IsListed(appPath);

    /// <summary>Phase 6 input features stand down.</summary>
    public bool Pauses(bool fullscreen, string? appPath) => !AlwaysOn && CountsAsFullscreen(fullscreen, appPath);

    public bool IsListed(string? appPath)
    {
        if (string.IsNullOrEmpty(appPath) || Apps.Count == 0)
            return false;
        string name = Path.GetFileName(appPath);
        foreach (var app in Apps)
            if (string.Equals(Path.GetFileName(app), name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
