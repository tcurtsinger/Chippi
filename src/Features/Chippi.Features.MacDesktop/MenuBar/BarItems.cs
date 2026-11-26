// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>
/// One thing the right side of the menu bar can hold. <paramref name="Feature"/> is the Chippi feature it needs running
/// (null for one that needs none); <paramref name="Windows"/> marks Windows' own things, listed apart when adding.
/// </summary>
public sealed record BarItemInfo(string Id, string Name, string? Feature = null, bool Windows = false);

/// <summary>
/// The right side of the menu bar (docs/mac-style/brief.md, Menu bar items): Chippi's own buttons, chosen and ordered in
/// Settings › Mac-style desktop. Each does one thing on a press; the clock comes after them and isn't in the list. What
/// shows, what can be added and how the list changes are pure and tested.
/// </summary>
public static class BarItems
{
    public const string Search = "search", Capture = "capture", Clipboard = "clipboard", Microphone = "microphone", Sound = "sound",
        HiddenIcons = "hidden-icons", Controls = "controls", Record = "record", CopyText = "copy-text", PickColor = "pick-color",
        Scratchpad = "scratchpad", Shelf = "shelf", KeepAwake = "keep-awake", Displays = "displays", System = "system",
        Agents = "agents", Actions = "actions", Network = "network", Battery = "battery", Language = "language";

    /// <summary>Every item, in the order Add item lists them: Chippi's, then Windows'.</summary>
    public static IReadOnlyList<BarItemInfo> All { get; } =
    [
        new(Search, "Search", "command-bar"),
        new(Capture, "Capture", "screen-capture"),
        new(Clipboard, "Clipboard", "clipboard-history"),
        new(Microphone, "Microphone", "mic-mute"),
        new(Sound, "Sound"),
        new(Controls, "Controls"),
        new(Record, "Record", "screen-recording"),
        new(CopyText, "Copy text", "screen-text"),
        new(PickColor, "Pick color", "color-picker"),
        new(Scratchpad, "Scratchpad", "scratchpad"),
        new(Shelf, "Shelf", "shelf"),
        new(KeepAwake, "Keep awake", "keep-awake"),
        new(Displays, "Displays", "displays"),
        new(System, "System", "system-monitor"),
        new(Agents, "Agents", "agent-usage"),
        new(Actions, "Actions", "quick-actions"),
        new(HiddenIcons, "Hidden icons", Windows: true),
        new(Network, "Network", Windows: true),
        new(Battery, "Battery", Windows: true),
        new(Language, "Language", Windows: true),
    ];

    /// <summary>
    /// The bar before the user changes it, left to right. Battery and Language are in it for the PCs that have a battery
    /// and a second input language; like every item, they show only where they're available.
    /// </summary>
    public static IReadOnlyList<string> Start { get; } = [Search, Capture, Clipboard, Microphone, Sound, Battery, Language, HiddenIcons, Controls];

    public static BarItemInfo? Find(string id) => All.FirstOrDefault(item => item.Id == id);

    /// <summary>Search's own command, and the one that stops a recording.</summary>
    public const string SearchCommand = "command-bar/open", StopRecordingCommand = "screen-recording/stop";

    /// <summary>
    /// What a press on <paramref name="id"/> does. The command and page keys are the features' own; Chippi.App's tests hold
    /// them against the features' constants, since this project doesn't reference them.
    /// </summary>
    public static BarPress Press(string id) => id switch
    {
        Search => new(BarPressKind.Search, "command-bar"),
        Capture => new(BarPressKind.Command, "screen-capture/region"),
        Clipboard => new(BarPressKind.Page, "clipboard"),
        Microphone => new(BarPressKind.Command, "mic-mute/toggle"),
        Sound => new(BarPressKind.Sound),
        HiddenIcons => new(BarPressKind.HiddenIcons),
        Controls => new(BarPressKind.Home),
        Record => new(BarPressKind.Record, "recording"),
        CopyText => new(BarPressKind.Command, "screen-text/copy"),
        PickColor => new(BarPressKind.Command, "color-picker/pick"),
        Scratchpad => new(BarPressKind.Page, "scratchpad"),
        Shelf => new(BarPressKind.Page, "shelf"),
        KeepAwake => new(BarPressKind.Command, "keep-awake/toggle"),
        Displays => new(BarPressKind.Page, "displays"),
        System => new(BarPressKind.Page, "system-monitor"),
        Agents => new(BarPressKind.Page, "agent-usage"),
        Actions => new(BarPressKind.Page, "quick-actions"),
        Network => new(BarPressKind.Open, "ms-settings:network"),
        Battery => new(BarPressKind.Open, "ms-settings:powersleep"),
        Language => new(BarPressKind.Keys, "Win+Space"),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Not a menu bar item"),
    };

    /// <summary>Whether an item can show on this PC now: its feature runs, the PC has a battery, a second input language.</summary>
    public static bool Available(string id, Func<string, bool> running, bool battery, bool languages) => Find(id) switch
    {
        null => false,
        { Id: Battery } => battery,
        { Id: Language } => languages,
        { Feature: { } feature } => running(feature),
        _ => true,
    };

    /// <summary>
    /// The items on the bar, left to right: the saved list, or the start set while nothing is saved, without anything
    /// unknown, repeated or unavailable.
    /// </summary>
    public static IReadOnlyList<string> Shown(IReadOnlyList<string>? saved, Func<string, bool> available) =>
        [.. (saved ?? Start).Distinct().Where(id => Find(id) is not null && available(id))];

    /// <summary>What Add item offers: every available item that isn't on the bar, in <see cref="All"/>'s order.</summary>
    public static IReadOnlyList<string> CanAdd(IReadOnlyList<string> shown, Func<string, bool> available) =>
        [.. All.Select(item => item.Id).Where(id => !shown.Contains(id) && available(id))];

    /// <summary>The list with <paramref name="id"/> at place <paramref name="to"/>; the same list when it isn't there or doesn't move.</summary>
    public static IReadOnlyList<string> Moved(IReadOnlyList<string> shown, string id, int to)
    {
        int from = shown.ToList().IndexOf(id);
        to = Math.Clamp(to, 0, Math.Max(0, shown.Count - 1));
        if (from < 0 || from == to)
            return shown;
        var list = shown.ToList();
        list.RemoveAt(from);
        list.Insert(to, id);
        return list;
    }

    /// <summary>The list with <paramref name="id"/> at its right end, next to the clock.</summary>
    public static IReadOnlyList<string> Added(IReadOnlyList<string> shown, string id) =>
        shown.Contains(id) || Find(id) is null ? shown : [.. shown, id];

    public static IReadOnlyList<string> Removed(IReadOnlyList<string> shown, string id) => [.. shown.Where(other => other != id)];

    /// <summary>
    /// What to save after an edit turned the bar's <paramref name="before"/> into <paramref name="after"/>: the edit, with
    /// the saved items that can't show on this PC now kept, each after the item it followed (the nearest one still on the
    /// bar). They come back with what they need: a battery, a second input language, their feature.
    /// </summary>
    public static IReadOnlyList<string> Kept(IReadOnlyList<string>? saved, IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var first = new List<string>();
        var following = new Dictionary<string, List<string>>();
        var held = first;
        foreach (string id in (saved ?? Start).Distinct())
        {
            if (after.Contains(id))
                held = following[id] = [];
            else if (!before.Contains(id) && Find(id) is not null)
                held.Add(id);
        }
        var kept = new List<string>(first);
        foreach (string id in after.Distinct())
        {
            kept.Add(id);
            if (following.TryGetValue(id, out var behind))
                kept.AddRange(behind);
        }
        return kept;
    }
}

/// <summary>What a press on an item does.</summary>
public enum BarPressKind
{
    /// <summary>Runs one of Chippi's commands by its key.</summary>
    Command,
    /// <summary>Opens an island page by its tile key; a second press closes it.</summary>
    Page,
    /// <summary>Opens Search with the keyboard in its field; a second press closes it. The target is its page.</summary>
    Search,
    /// <summary>Opens the notch's home; a second press closes it.</summary>
    Home,
    /// <summary>Opens Screen recording (the target); while recording, stops.</summary>
    Record,
    /// <summary>Opens Chippi Settings on Output.</summary>
    Sound,
    /// <summary>Opens Windows' own hidden-icons flyout.</summary>
    HiddenIcons,
    /// <summary>Presses keys Windows answers.</summary>
    Keys,
    /// <summary>Opens a page of Windows Settings.</summary>
    Open,
}

/// <summary>What a press does and on what: a command's key, a page's tile key, keys, an address.</summary>
public readonly record struct BarPress(BarPressKind Kind, string Target = "");

/// <summary>What the stateful items show: a muted mic, keep awake on, an agent at work, a recording's time.</summary>
public readonly record struct BarStates(bool MicMuted, bool Awake, bool AgentWorking, string? Recording);
