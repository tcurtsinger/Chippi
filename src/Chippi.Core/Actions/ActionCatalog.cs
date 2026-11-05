// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

/// <summary>
/// The action kinds and their actions as Settings names them, and each one's glyph as a Lucide icon's name
/// (<c>Chippi.UI.Controls.LucideIcons.Get</c> draws it).
/// </summary>
public static class ActionCatalog
{
    /// <summary>The Kind list, in the brief's order.</summary>
    public static IReadOnlyList<(ActionKind Kind, string Name)> Kinds { get; } =
    [
        (ActionKind.App, "App"),
        (ActionKind.File, "File or folder"),
        (ActionKind.Link, "Link"),
        (ActionKind.Keys, "Key shortcut"),
        (ActionKind.Chippi, "Chippi"),
        (ActionKind.Media, "Media"),
        (ActionKind.Window, "Window"),
        (ActionKind.Text, "Text"),
        (ActionKind.Command, "Command"),
        (ActionKind.Selection, "Selected text"),
        (ActionKind.SettingsPage, "Settings page"),
    ];

    public static IReadOnlyList<(MediaAction Action, string Name, string Glyph)> Media { get; } =
    [
        (MediaAction.PlayPause, "Play/pause", "PlayPause"),
        (MediaAction.Next, "Next", "SkipForward"),
        (MediaAction.Previous, "Previous", "SkipBack"),
        (MediaAction.Mute, "Mute", "VolumeMuted"),
        (MediaAction.VolumeUp, "Volume up", "VolumeUp"),
        (MediaAction.VolumeDown, "Volume down", "VolumeDown"),
    ];

    public static IReadOnlyList<(WindowAction Action, string Name, string Glyph)> Window { get; } =
    [
        (WindowAction.SnapLeft, "Snap left", "SnapLeft"),
        (WindowAction.SnapRight, "Snap right", "SnapRight"),
        (WindowAction.Maximize, "Maximize", "Maximize"),
        (WindowAction.Minimize, "Minimize", "Minus"),
        (WindowAction.Close, "Close", "Close"),
        (WindowAction.KeepOnTop, "Keep on top", "Pin"),
        (WindowAction.OtherScreen, "Move to other screen", "OtherScreen"),
        (WindowAction.Center, "Center", "AppWindow"),
        (WindowAction.Restore, "Restore", "AppWindow"),
        (WindowAction.LeftThird, "Left third", "SnapLeft"),
        (WindowAction.MiddleThird, "Middle third", "AppWindow"),
        (WindowAction.RightThird, "Right third", "SnapRight"),
        (WindowAction.LeftTwoThirds, "Left two-thirds", "SnapLeft"),
        (WindowAction.RightTwoThirds, "Right two-thirds", "SnapRight"),
        (WindowAction.TopLeft, "Top left", "LayoutGrid"),
        (WindowAction.TopRight, "Top right", "LayoutGrid"),
        (WindowAction.BottomLeft, "Bottom left", "LayoutGrid"),
        (WindowAction.BottomRight, "Bottom right", "LayoutGrid"),
    ];

    public static IReadOnlyList<(SelectionAction Action, string Name, string Glyph)> Selection { get; } =
    [
        (SelectionAction.Search, "Search the web", "Globe"),
        (SelectionAction.OpenLink, "Open as link", "Link"),
        (SelectionAction.CopyPlain, "Copy as plain text", "Clipboard"),
    ];

    /// <summary>The glyphs Change offers: the island's, a row of 8 at a time.</summary>
    public static IReadOnlyList<string> Glyphs { get; } =
    [
        "Star", "Zap", "Heart", "Bookmark", "Home", "Mail", "Globe", "Link",
        "Folder", "File", "Files", "Download", "Image", "Camera", "Music", "Video",
        "Keyboard", "Type", "Terminal", "Code", "Scissors", "Clipboard", "Search", "Settings",
        "AppWindow", "LayoutGrid", "Monitor", "Screens", "Gamepad", "Headphones", "Mic", "Coffee",
        "Lock", "Moon", "Sun", "Bell", "Clock", "Wrench", "Shield", "Power",
    ];

    public static string NameOf(ActionKind kind) => Kinds.First(k => k.Kind == kind).Name;

    public static string NameOf(MediaAction action) => Media.First(m => m.Action == action).Name;

    public static string NameOf(WindowAction action) => Window.First(m => m.Action == action).Name;

    public static string NameOf(SelectionAction action) => Selection.First(m => m.Action == action).Name;

    /// <summary>
    /// An action's glyph: the one picked with Change, else its action's or its kind's. Apps and files have their own icons;
    /// this is what shows until one is read, or when there's none. <paramref name="commandIcon"/> names a Chippi command's.
    /// </summary>
    public static string GlyphOf(ChippiAction action, Func<string, string?>? commandIcon = null)
    {
        if (action.Glyph is { } picked)
            return picked;
        return action.Kind switch
        {
            ActionKind.App => "AppWindow",
            ActionKind.File => Path.HasExtension(action.Value) ? "File" : "Folder",
            ActionKind.Link => "Globe",
            ActionKind.Keys => "Keyboard",
            ActionKind.Chippi => commandIcon?.Invoke(action.Value) ?? "Zap",
            ActionKind.Media => Enum.TryParse<MediaAction>(action.Value, out var media) ? Media.First(m => m.Action == media).Glyph : "PlayPause",
            ActionKind.Window => Enum.TryParse<WindowAction>(action.Value, out var window) ? Window.First(m => m.Action == window).Glyph : "AppWindow",
            ActionKind.Selection => Enum.TryParse<SelectionAction>(action.Value, out var selection) ? Selection.First(m => m.Action == selection).Glyph : "Globe",
            ActionKind.Text => "Type",
            ActionKind.Command => "Terminal",
            ActionKind.SettingsPage => "Settings",
            _ => "LayoutGrid",
        };
    }

    /// <summary>
    /// The name an action gets from what it does: an app's or a page's name, a file's, the address's site, the keys, the
    /// action, the text's first line, the command line. <paramref name="known"/> names an app, a Chippi command or a
    /// Settings page by its value.
    /// </summary>
    public static string NameFor(ChippiAction action, Func<string, string?>? known = null)
    {
        string value = action.Value.Trim();
        return action.Kind switch
        {
            ActionKind.App => known?.Invoke(value) ?? (Path.IsPathFullyQualified(value) ? Path.GetFileNameWithoutExtension(value) : value),
            ActionKind.File => Path.GetFileName(value.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : value,
            ActionKind.Link => Uri.TryCreate(Links.Normalize(value), UriKind.Absolute, out var uri) && uri.Host.Length > 0
                ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
                : value,
            ActionKind.Keys => KeyCombo.Parse(value)?.ToString() ?? value,
            ActionKind.Chippi => known?.Invoke(value) ?? "Chippi",
            ActionKind.Media => Enum.TryParse<MediaAction>(value, out var media) ? NameOf(media) : "Media",
            ActionKind.Window => Enum.TryParse<WindowAction>(value, out var window) ? window == WindowAction.OtherScreen ? "Other screen" : NameOf(window) : "Window",
            ActionKind.Selection => Enum.TryParse<SelectionAction>(value, out var selection) ? NameOf(selection) : "Selected text",
            ActionKind.Text => FirstLine(value, "Text"),
            ActionKind.Command => FirstLine(value, "Command"),
            ActionKind.SettingsPage => known?.Invoke(value) ?? "Settings",
            _ => "Action",
        };

        static string FirstLine(string text, string fallback)
        {
            string line = text.Split('\n')[0].Trim();
            return line.Length == 0 ? fallback : line.Length > 24 ? line[..23].TrimEnd() + "…" : line;
        }
    }
}

/// <summary>Web addresses as a Link action or a selection holds them.</summary>
public static class Links
{
    /// <summary>"github.com/x" gets https:// in front; an address with a scheme stays as it is.</summary>
    public static string Normalize(string text)
    {
        text = text.Trim();
        return text.Contains("://", StringComparison.Ordinal) || text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? text : "https://" + text;
    }

    /// <summary>
    /// The address a selection names, or null when it isn't one: no spaces, and a scheme or a dotted host ("github.com",
    /// "https://x.dev/a"). Only http, https and mailto open, so a selection can't start a program.
    /// </summary>
    public static string? FromSelection(string text)
    {
        text = text.Trim();
        if (text.Length is 0 or > 2048 || text.Any(char.IsWhiteSpace))
            return null;
        if (!Uri.TryCreate(Normalize(text), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto"))
            return null;
        return uri.Scheme == "mailto" || uri.Host.Contains('.', StringComparison.Ordinal) ? uri.AbsoluteUri : null;
    }
}
