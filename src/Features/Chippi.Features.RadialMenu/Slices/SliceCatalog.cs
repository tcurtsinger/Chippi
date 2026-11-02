// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.RadialMenu.Slices;

/// <summary>The slice kinds and their actions as Settings › Radial menu names them, and each one's glyph.</summary>
public static class SliceCatalog
{
    /// <summary>The Kind list, in the brief's order.</summary>
    public static IReadOnlyList<(SliceKind Kind, string Name)> Kinds { get; } =
    [
        (SliceKind.App, "App"),
        (SliceKind.File, "File or folder"),
        (SliceKind.Link, "Link"),
        (SliceKind.Keys, "Key shortcut"),
        (SliceKind.Chippi, "Chippi"),
        (SliceKind.Media, "Media"),
        (SliceKind.Window, "Window"),
        (SliceKind.Text, "Text"),
        (SliceKind.Command, "Command"),
        (SliceKind.Selection, "Selected text"),
        (SliceKind.SettingsPage, "Settings page"),
        (SliceKind.Submenu, "Submenu"),
    ];

    public static IReadOnlyList<(MediaAction Action, string Name, Geometry Glyph)> Media { get; } =
    [
        (MediaAction.PlayPause, "Play/pause", LucideIcons.PlayPause),
        (MediaAction.Next, "Next", LucideIcons.SkipForward),
        (MediaAction.Previous, "Previous", LucideIcons.SkipBack),
        (MediaAction.Mute, "Mute", LucideIcons.VolumeMuted),
        (MediaAction.VolumeUp, "Volume up", LucideIcons.VolumeUp),
        (MediaAction.VolumeDown, "Volume down", LucideIcons.VolumeDown),
    ];

    public static IReadOnlyList<(WindowAction Action, string Name, Geometry Glyph)> Window { get; } =
    [
        (WindowAction.SnapLeft, "Snap left", LucideIcons.SnapLeft),
        (WindowAction.SnapRight, "Snap right", LucideIcons.SnapRight),
        (WindowAction.Maximize, "Maximize", LucideIcons.Maximize),
        (WindowAction.Minimize, "Minimize", LucideIcons.Minus),
        (WindowAction.Close, "Close", LucideIcons.Close),
        (WindowAction.KeepOnTop, "Keep on top", LucideIcons.Pin),
        (WindowAction.OtherScreen, "Move to other screen", LucideIcons.OtherScreen),
    ];

    public static IReadOnlyList<(SelectionAction Action, string Name, Geometry Glyph)> Selection { get; } =
    [
        (SelectionAction.Search, "Search the web", LucideIcons.Globe),
        (SelectionAction.OpenLink, "Open as link", LucideIcons.Link),
        (SelectionAction.CopyPlain, "Copy as plain text", LucideIcons.Clipboard),
    ];

    /// <summary>The glyphs Change offers: the island's, a row of 8 at a time.</summary>
    public static IReadOnlyList<string> Glyphs { get; } =
    [
        nameof(LucideIcons.Star), nameof(LucideIcons.Zap), nameof(LucideIcons.Heart), nameof(LucideIcons.Bookmark),
        nameof(LucideIcons.Home), nameof(LucideIcons.Mail), nameof(LucideIcons.Globe), nameof(LucideIcons.Link),
        nameof(LucideIcons.Folder), nameof(LucideIcons.File), nameof(LucideIcons.Files), nameof(LucideIcons.Download),
        nameof(LucideIcons.Image), nameof(LucideIcons.Camera), nameof(LucideIcons.Music), nameof(LucideIcons.Video),
        nameof(LucideIcons.Keyboard), nameof(LucideIcons.Type), nameof(LucideIcons.Terminal), nameof(LucideIcons.Code),
        nameof(LucideIcons.Scissors), nameof(LucideIcons.Clipboard), nameof(LucideIcons.Search), nameof(LucideIcons.Settings),
        nameof(LucideIcons.AppWindow), nameof(LucideIcons.LayoutGrid), nameof(LucideIcons.Monitor), nameof(LucideIcons.Screens),
        nameof(LucideIcons.Gamepad), nameof(LucideIcons.Headphones), nameof(LucideIcons.Mic), nameof(LucideIcons.Coffee),
        nameof(LucideIcons.Lock), nameof(LucideIcons.Moon), nameof(LucideIcons.Sun), nameof(LucideIcons.Bell),
        nameof(LucideIcons.Clock), nameof(LucideIcons.Wrench), nameof(LucideIcons.Shield), nameof(LucideIcons.Power),
    ];

    public static string NameOf(SliceKind kind) => Kinds.First(k => k.Kind == kind).Name;

    public static string NameOf(MediaAction action) => Media.First(m => m.Action == action).Name;

    public static string NameOf(WindowAction action) => Window.First(m => m.Action == action).Name;

    public static string NameOf(SelectionAction action) => Selection.First(m => m.Action == action).Name;

    /// <summary>
    /// A slice's glyph: the one picked with Change, else its action's or its kind's. Apps and files have their own icons
    /// (<see cref="SliceRunner"/> reads them); this is what shows until one is read, or when there's none.
    /// </summary>
    public static Geometry GlyphOf(Slice slice, Func<string, string?>? commandIcon = null)
    {
        if (slice.Glyph is { } picked)
            return LucideIcons.Get(picked);
        return slice.Kind switch
        {
            SliceKind.App => LucideIcons.AppWindow,
            SliceKind.File => Path.HasExtension(slice.Value) ? LucideIcons.File : LucideIcons.Folder,
            SliceKind.Link => LucideIcons.Globe,
            SliceKind.Keys => LucideIcons.Keyboard,
            SliceKind.Chippi => commandIcon?.Invoke(slice.Value) is { } icon ? LucideIcons.Get(icon) : LucideIcons.Zap,
            SliceKind.Media => Enum.TryParse<MediaAction>(slice.Value, out var media) ? Media.First(m => m.Action == media).Glyph : LucideIcons.PlayPause,
            SliceKind.Window => Enum.TryParse<WindowAction>(slice.Value, out var window) ? Window.First(m => m.Action == window).Glyph : LucideIcons.AppWindow,
            SliceKind.Selection => Enum.TryParse<SelectionAction>(slice.Value, out var selection) ? Selection.First(m => m.Action == selection).Glyph : LucideIcons.Globe,
            SliceKind.Text => LucideIcons.Type,
            SliceKind.Command => LucideIcons.Terminal,
            SliceKind.SettingsPage => LucideIcons.Settings,
            _ => LucideIcons.LayoutGrid,
        };
    }

    /// <summary>
    /// The name a slice gets from what it does: an app's or a page's name, a file's, the address's site, the keys, the
    /// action, the text's first line, the command line. <paramref name="known"/> names an app or a Chippi command.
    /// </summary>
    public static string NameFor(Slice slice, Func<string, string?>? known = null)
    {
        string value = slice.Value.Trim();
        return slice.Kind switch
        {
            SliceKind.App => known?.Invoke(value) ?? (Path.IsPathFullyQualified(value) ? Path.GetFileNameWithoutExtension(value) : value),
            SliceKind.File => Path.GetFileName(value.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : value,
            SliceKind.Link => Uri.TryCreate(Links.Normalize(value), UriKind.Absolute, out var uri) && uri.Host.Length > 0
                ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
                : value,
            SliceKind.Keys => KeyCombo.Parse(value)?.ToString() ?? value,
            SliceKind.Chippi => known?.Invoke(value) ?? "Chippi",
            SliceKind.Media => Enum.TryParse<MediaAction>(value, out var media) ? NameOf(media) : "Media",
            SliceKind.Window => Enum.TryParse<WindowAction>(value, out var window) ? window == WindowAction.OtherScreen ? "Other screen" : NameOf(window) : "Window",
            SliceKind.Selection => Enum.TryParse<SelectionAction>(value, out var selection) ? NameOf(selection) : "Selected text",
            SliceKind.Text => FirstLine(value, "Text"),
            SliceKind.Command => FirstLine(value, "Command"),
            SliceKind.SettingsPage => CommandBar.Search.SettingsPages.All.FirstOrDefault(p => p.Uri == value)?.Name ?? "Settings",
            _ => "Submenu",
        };

        static string FirstLine(string text, string fallback)
        {
            string line = text.Split('\n')[0].Trim();
            return line.Length == 0 ? fallback : line.Length > 24 ? line[..23].TrimEnd() + "…" : line;
        }
    }
}

/// <summary>Web addresses as a Link slice or a selection holds them.</summary>
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
