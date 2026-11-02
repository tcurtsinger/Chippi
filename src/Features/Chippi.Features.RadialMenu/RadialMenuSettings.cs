// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Sound.Mute;

namespace Chippi.Features.RadialMenu;

/// <summary>What a slice does (docs/phase-5.5/radial-menu.md, What a slice can do).</summary>
public enum SliceKind { App, File, Link, Keys, Chippi, Media, Window, Text, Command, Selection, SettingsPage, Submenu }

/// <summary>Where a Command slice's line runs (Travis, 2026-11-02: a Shell row, PowerShell first).</summary>
public enum CommandShell { PowerShell, Cmd }

public enum MediaAction { PlayPause, Next, Previous, Mute, VolumeUp, VolumeDown }

/// <summary>What a Window slice does to the window that was under the pointer when the wheel opened.</summary>
public enum WindowAction { SnapLeft, SnapRight, Maximize, Minimize, Close, KeepOnTop, OtherScreen }

/// <summary>What a Selected text slice does with the text selected in the app in front.</summary>
public enum SelectionAction { Search, OpenLink, CopyPlain }

/// <summary>One slice of the wheel.</summary>
public sealed record Slice
{
    public SliceKind Kind { get; init; }

    /// <summary>
    /// What it acts on: an app's Start menu parsing name or a program's path, a file's or folder's path, a web address,
    /// keys ("Ctrl+Shift+T"), a Chippi command's key, a media, window or selected-text action's name, the text, the
    /// command line, or a Windows Settings page's ms-settings: address. Empty for a submenu.
    /// </summary>
    public string Value { get; init; } = "";

    /// <summary>What the hub shows while it's pointed at; filled in from what it does, and changeable.</summary>
    public string Name { get; init; } = "";

    /// <summary>A glyph picked with Change (a <see cref="UI.Controls.LucideIcons"/> name); null for its kind's, or its app's or file's own icon.</summary>
    public string? Glyph { get; init; }

    /// <summary>A Command slice's shell.</summary>
    public CommandShell Shell { get; init; }

    /// <summary>A submenu's slices, clockwise after its Back: up to 7.</summary>
    public List<Slice> Slices { get; init; } = [];
}

/// <summary>Settings › Radial menu: its triggers and its wheel. None of the triggers is on until the owner turns one on.</summary>
public sealed record RadialMenuSettings
{
    /// <summary>Slices on the first wheel; a submenu's wheel holds one fewer beside its Back.</summary>
    public const int MaxSlices = 8;

    /// <summary>Wheels deep, the first counted: a submenu's submenu holds no submenu.</summary>
    public const int MaxDepth = 3;

    public bool HoldMiddle { get; init; }

    public bool HoldBack { get; init; }

    public bool HoldForward { get; init; }

    /// <summary>The keyboard shortcut; null until recorded.</summary>
    public MicShortcut? Shortcut { get; init; }

    public List<Slice> Slices { get; init; } = [];

    /// <summary>A mouse button opens it.</summary>
    public bool HoldsAButton => HoldMiddle || HoldBack || HoldForward;

    /// <summary>
    /// The same settings with no more slices than a wheel holds and no submenu deeper than three wheels, as a hand-edited
    /// settings.json might have.
    /// </summary>
    public RadialMenuSettings Checked()
    {
        if (Shortcut is { IsValid: false })
            return (this with { Shortcut = null }).Checked();
        return this with { Slices = Trim(Slices, depth: 1) };

        static List<Slice> Trim(List<Slice>? slices, int depth) =>
        [
            .. (slices ?? []).Where(s => s is not null && Enum.IsDefined(s.Kind) && (s.Kind != SliceKind.Submenu || depth < MaxDepth))
                .Take(depth == 1 ? MaxSlices : MaxSlices - 1)
                .Select(s => s with { Value = s.Value ?? "", Name = s.Name ?? "", Slices = s.Kind == SliceKind.Submenu ? Trim(s.Slices, depth + 1) : [] }),
        ];
    }
}
