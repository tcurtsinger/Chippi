// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

/// <summary>What a command's row is, which decides its verb and whether it can be hidden.</summary>
public enum ChippiCommandKind
{
    /// <summary>One of the things Chippi does (Keep awake, Dark mode, Lock): Run.</summary>
    Run,

    /// <summary>A value typed in ("keep awake 2 hr" → Keep awake for 2 hr): Run, first in the list, never hidden.</summary>
    Value,

    /// <summary>One of the island's pages, or Settings: Open.</summary>
    Page,

    /// <summary>A clipboard copy: Paste, into the app that was in front.</summary>
    Paste,

    /// <summary>A text snippet: Insert, typed into the app that was in front.</summary>
    Insert,
}

/// <summary>
/// One thing Chippi does, as the Command bar lists it (docs/phase-5.5/command-bar.md) and the Radial menu will. Every
/// running feature adds its own through <see cref="IModuleUi.AddCommands"/>; the registry adds the island's pages and
/// Settings.
/// </summary>
/// <param name="Key">Identifies it across runs, for the uses that rank it and for hiding it ("keep-awake/toggle").</param>
/// <param name="Name">What the row says ("Keep awake", "Volume to 40%").</param>
/// <param name="Icon">Lucide icon key, as in <see cref="FeatureDescriptor.Icon"/>.</param>
/// <param name="Run">Does it. Called on the UI thread; a failure throws.</param>
public sealed record ChippiCommand(string Key, string Name, string Icon, Func<Task> Run)
{
    /// <summary>More words it's found by ("mic microphone").</summary>
    public string Words { get; init; } = "";

    /// <summary>The row's second line: what it is or where it lives.</summary>
    public string Detail { get; init; } = "Chippi";

    public ChippiCommandKind Kind { get; init; } = ChippiCommandKind.Run;

    /// <summary>A small level bar beside the name, 0 to 1 ("Volume to 40%"); null for none.</summary>
    public double? Level { get; init; }

    /// <summary>
    /// Whether it's on, for a command that turns something on and off (Key debounce, Always on in games): its Run flips it.
    /// Null for one that only runs. Read with the list, so it follows the state.
    /// </summary>
    public bool? IsOn { get; init; }

    /// <summary>The Actions tile it shows on (docs/phase-6/architecture.md §3); null for none.</summary>
    public ActionsTile? Tile { get; init; }
}

/// <summary>
/// A tile another feature adds to Actions. Commands with the same <see cref="Key"/> share one tile: a menu lists them as
/// rows and steps into the page, as Power does; otherwise the tile runs its one command, or switches it when it has
/// <see cref="ChippiCommand.IsOn"/>. A tile takes the label and icon of its first command's.
/// </summary>
/// <param name="Key">Its position's name in the saved gallery ("input"); never changes.</param>
/// <param name="Label">The tile's one word ("Input").</param>
/// <param name="Icon">Lucide icon key, as in <see cref="FeatureDescriptor.Icon"/>.</param>
public sealed record ActionsTile(string Key, string Label, string Icon)
{
    /// <summary>The tile steps into a list of its commands; false for a tile of one command.</summary>
    public bool Menu { get; init; }

    /// <summary>The Phase 6 switches: Debounce's, Close protection, Focus follows mouse, Always on in games.</summary>
    public static ActionsTile Input { get; } = new("input", "Input", "Mouse") { Menu = true };
}
