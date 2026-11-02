// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.CommandBar.Search;
using Chippi.Features.Sound.Mute;

namespace Chippi.Features.CommandBar;

/// <summary>Where the web row searches. Windows can't tell an app which engine the browser uses, so Chippi asks.</summary>
public enum WebEngine { Google, Bing, DuckDuckGo }

/// <summary>A hidden result: its key, and its name and what it is, as Settings › Command bar › Hidden lists it.</summary>
public sealed record HiddenResult(string Key, string Name, string Kind);

/// <summary>Settings › Command bar, and its shortcut in Settings › Shortcuts.</summary>
public sealed record CommandBarSettings
{
    /// <summary>Ctrl+Alt+Space (Travis's pick, docs/phase-5.5/command-bar.md).</summary>
    public static MicShortcut DefaultShortcut { get; } = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x20);

    public WebEngine WebSearch { get; init; } = WebEngine.Google;

    /// <summary>The kinds switched off in Results; every kind is on by default.</summary>
    public List<ResultSwitch> Off { get; init; } = [];

    /// <summary>Hidden results, oldest first. Each stays hidden until Show.</summary>
    public List<HiddenResult> Hidden { get; init; } = [];

    /// <summary>The shortcut; null once removed.</summary>
    public MicShortcut? Shortcut { get; init; } = DefaultShortcut;
}
