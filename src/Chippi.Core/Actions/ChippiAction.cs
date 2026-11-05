// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

/// <summary>
/// What an action does (docs/phase-5.5/radial-menu.md, What a slice can do). Any door fires one: a Radial slice, and
/// in Phase 6 a key shortcut, a Caps Lock combo or a mouse button (docs/phase-6/architecture.md section 3).
/// </summary>
public enum ActionKind { App, File, Link, Keys, Chippi, Media, Window, Text, Command, Selection, SettingsPage }

/// <summary>Where a Command action's line runs (Travis, 2026-11-02: a Shell row, PowerShell first).</summary>
public enum CommandShell { PowerShell, Cmd }

public enum MediaAction { PlayPause, Next, Previous, Mute, VolumeUp, VolumeDown }

/// <summary>What a Window action does to its target window (<see cref="WindowTarget"/>).</summary>
public enum WindowAction { SnapLeft, SnapRight, Maximize, Minimize, Close, KeepOnTop, OtherScreen, Center, Restore, LeftThird, MiddleThird, RightThird, LeftTwoThirds, RightTwoThirds, TopLeft, TopRight, BottomLeft, BottomRight, Zone }

/// <summary>What a Selected text action does with the text selected in the app in front.</summary>
public enum SelectionAction { Search, OpenLink, CopyPlain }

/// <summary>
/// The window a Window action acts on, by the door that fires it: the one under the pointer (the Radial menu, mouse
/// buttons), the one in front (keys, Caps Lock combos), or the one that was in front before the island opened (Actions
/// tiles, Search).
/// </summary>
public enum WindowTarget { UnderPointer, InFront, BeforeIsland }

/// <summary>One action, as a Radial slice or a shortcut holds it.</summary>
public sealed record ChippiAction
{
    public ActionKind Kind { get; init; }

    /// <summary>
    /// What it acts on: an app's Start menu parsing name or a program's path, a file's or folder's path, a web address,
    /// keys ("Ctrl+Shift+T"), a Chippi command's key, a media, window or selected-text action's name, the text, the
    /// command line, or a Windows Settings page's ms-settings: address.
    /// </summary>
    public string Value { get; init; } = "";

    /// <summary>What it's called where it shows; filled in from what it does, and changeable.</summary>
    public string Name { get; init; } = "";

    /// <summary>A glyph picked with Change (a Lucide icon's name); null for its kind's, or its app's or file's own icon.</summary>
    public string? Glyph { get; init; }

    /// <summary>A Command action's shell.</summary>
    public string? LayoutId { get; init; }
    public string? ZoneId { get; init; }

    public CommandShell Shell { get; init; }
}

/// <summary>Why an action didn't run, as the bar says it: "Snap left failed" over <see cref="Exception.Message"/> ("No window").</summary>
public sealed class ActionFailure(string reason) : Exception(reason);
