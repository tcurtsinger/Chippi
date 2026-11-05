// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.RadialMenu;

/// <summary>What a slice does: one of the shared actions' kinds (<see cref="ActionKind"/>), or a submenu, the Radial menu's own.</summary>
public enum SliceKind
{
    App = ActionKind.App, File = ActionKind.File, Link = ActionKind.Link, Keys = ActionKind.Keys, Chippi = ActionKind.Chippi,
    Media = ActionKind.Media, Window = ActionKind.Window, Text = ActionKind.Text, Command = ActionKind.Command,
    Selection = ActionKind.Selection, SettingsPage = ActionKind.SettingsPage, Submenu,
}

/// <summary>One slice of the wheel: a shared action (<see cref="ToAction"/>), or a submenu.</summary>
public sealed record Slice
{
    public SliceKind Kind { get; init; }

    /// <summary>What it acts on (<see cref="ChippiAction.Value"/>). Empty for a submenu.</summary>
    public string Value { get; init; } = "";

    /// <summary>What the hub shows while it's pointed at; filled in from what it does, and changeable.</summary>
    public string Name { get; init; } = "";

    /// <summary>A glyph picked with Change (a <see cref="UI.Controls.LucideIcons"/> name); null for its kind's, or its app's or file's own icon.</summary>
    public string? Glyph { get; init; }

    /// <summary>A Command slice's shell.</summary>
    public CommandShell Shell { get; init; }

    /// <summary>A submenu's slices, clockwise after its Back: up to 7.</summary>
    public List<Slice> Slices { get; init; } = [];

    /// <summary>The action it runs; a submenu runs none.</summary>
    public ChippiAction ToAction() => new() { Kind = (ActionKind)Kind, Value = Value, Name = Name, Glyph = Glyph, Shell = Shell };

    /// <summary>A slice that runs the action.</summary>
    public static Slice From(ChippiAction action) =>
        new() { Kind = (SliceKind)action.Kind, Value = action.Value, Name = action.Name, Glyph = action.Glyph, Shell = action.Shell };
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
    public KeyShortcut? Shortcut { get; init; }

    /// <summary>
    /// Settings › Radial menu › Open over fullscreen apps: on (the default, as before Phase 6), the wheel opens over a game
    /// too. Off, while a fullscreen app (or one on Pause in these apps) is in front, its buttons and shortcut reach the app.
    /// </summary>
    public bool OpenOverFullscreen { get; init; } = true;

    public List<Slice> Slices { get; init; } = [];

    /// <summary>A mouse button opens it.</summary>
    public bool HoldsAButton => HoldMiddle || HoldBack || HoldForward;

    /// <summary>
    /// The same settings with no more slices than a wheel holds and no submenu deeper than three wheels, as a hand-edited
    /// settings.json might have.
    /// </summary>
    public RadialMenuSettings Checked()
    {
        if (Shortcut is { IsBindable: false })
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
