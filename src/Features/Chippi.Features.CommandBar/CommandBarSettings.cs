// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.Sound.Mute;
using System.Text.Json.Serialization;

namespace Chippi.Features.CommandBar;

/// <summary>Where the web row searches. Windows can't tell an app which engine the browser uses, so Chippi asks.</summary>
public enum WebEngine { Google, Bing, DuckDuckGo }

public static class WebEngines
{
    /// <summary>The engine's results for <paramref name="text"/> (the web row, and the Radial menu's Selected text).</summary>
    public static string SearchUrl(this WebEngine engine, string text) => engine switch
    {
        WebEngine.Bing => "https://www.bing.com/search?q=",
        WebEngine.DuckDuckGo => "https://duckduckgo.com/?q=",
        _ => "https://www.google.com/search?q=",
    } + Uri.EscapeDataString(text);
}

/// <summary>A hidden result: its key, and its name and what it is, as Settings › Command bar › Hidden lists it.</summary>
public sealed record HiddenResult(string Key, string Name, string Kind)
{
    /// <summary>The original category, also recovered for hidden entries saved before category recovery existed.</summary>
    [JsonIgnore]
    public ResultSwitch Switch => Kind.Split(" · ")[0] switch
    {
        "App" => ResultSwitch.Apps,
        "Window" => ResultSwitch.Windows,
        "File" => ResultSwitch.Files,
        "Windows Settings" => ResultSwitch.Settings,
        "Clipboard" => ResultSwitch.Clipboard,
        "Emoji" => ResultSwitch.Emoji,
        _ => ResultSwitch.Chippi,
    };
}

/// <summary>Settings › Command bar, and its shortcut in Settings › Shortcuts.</summary>
public sealed record CommandBarSettings
{
    /// <summary>Ctrl+Alt+Space (Travis's pick, docs/phase-5.5/command-bar.md).</summary>
    public static KeyShortcut DefaultShortcut { get; } = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x20);

    public WebEngine WebSearch { get; init; } = WebEngine.Google;

    /// <summary>The kinds switched off in Results; every kind is on by default.</summary>
    public List<ResultSwitch> Off { get; init; } = [];

    /// <summary>Hidden results, oldest first. Each stays hidden until Show or inline Undo.</summary>
    public List<HiddenResult> Hidden { get; init; } = [];

    /// <summary>The shortcut; null once removed.</summary>
    public KeyShortcut? Shortcut { get; init; } = DefaultShortcut;

    /// <summary>Recover structurally valid but incomplete settings without rewriting the saved file on load.</summary>
    internal CommandBarSettings Normalize() => this with
    {
        WebSearch = Enum.IsDefined(WebSearch) ? WebSearch : WebEngine.Google,
        Shortcut = Shortcut is { IsBindable: false } ? DefaultShortcut : Shortcut,
        Off = [.. (Off ?? []).Where(kind => Enum.IsDefined(kind)).Distinct()],
        Hidden = [.. (Hidden ?? []).Where(result => result is not null
            && !string.IsNullOrWhiteSpace(result.Key) && !string.IsNullOrWhiteSpace(result.Name)
            && !string.IsNullOrWhiteSpace(result.Kind)).DistinctBy(result => result.Key, StringComparer.Ordinal)],
    };
}
