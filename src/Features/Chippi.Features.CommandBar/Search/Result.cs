// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.CommandBar.Search;

/// <summary>What a result is. The order of the matches' kinds after the uses and the names that start with the text.</summary>
public enum ResultKind { Value, Answer, Chippi, App, Window, Settings, File, Clipboard, Emoji, Web }

/// <summary>Each kind's switch in Settings › Command bar › Results, in that list's order, all on by default.</summary>
public enum ResultSwitch { Apps, Windows, Files, Settings, Chippi, Clipboard, Math, Emoji, Web }

/// <summary>One row of the Command bar (docs/phase-5.5/command-bar.md, The page).</summary>
/// <param name="Key">Identifies it across runs, for the uses that rank it and for hiding it ("app:Chrome").</param>
/// <param name="Name">The row's name, in 12.</param>
/// <param name="Detail">The second line, in 11 secondary: what it is or where it lives.</param>
/// <param name="Run">What Enter does. Called on the UI thread; a failure throws.</param>
public sealed record Result(string Key, ResultKind Kind, string Name, string Detail, Func<Task> Run)
{
    /// <summary>More words it's found by.</summary>
    public string Words { get; init; } = "";

    /// <summary>The lead's Lucide glyph; null for an app's icon or an emoji.</summary>
    public string? Icon { get; init; }

    /// <summary>The emoji itself, as the lead.</summary>
    public string? Emoji { get; init; }

    /// <summary>The app whose icon is the lead: its Start menu parsing name.</summary>
    public string? App { get; init; }

    /// <summary>The open window whose app icon is the lead.</summary>
    public OpenWindow? Window { get; init; }

    /// <summary>The full file or folder path, retained for tooltips and assistive technology.</summary>
    public string? FullPath { get; init; }

    /// <summary>A small level bar beside the name, 0 to 1 ("Volume to 40%").</summary>
    public double? Level { get; init; }

    /// <summary>One of Chippi's pages or Settings: Open, not Run.</summary>
    public bool Opens { get; init; }

    /// <summary>A text snippet, typed into the app that was in front: Insert, not Run.</summary>
    public bool Inserts { get; init; }

    /// <summary>What the selected row says at its right.</summary>
    public string Verb => Kind switch
    {
        ResultKind.Value => "Run",
        ResultKind.Chippi => Opens ? "Open" : Inserts ? "Insert" : "Run",
        ResultKind.Window => "Switch",
        ResultKind.Clipboard => "Paste",
        ResultKind.Answer or ResultKind.Emoji => "Copy",
        ResultKind.Web => "Search",
        _ => "Open",
    };

    /// <summary>Values, answers and the web row can't be hidden.</summary>
    public bool CanHide => Kind is not (ResultKind.Value or ResultKind.Answer or ResultKind.Web);

    /// <summary>What it is, under its name in Settings › Command bar › Hidden ("App", "File · Downloads").</summary>
    public string HiddenAs => Kind switch
    {
        ResultKind.App => "App",
        ResultKind.Window => $"Window · {Detail}",
        ResultKind.File => $"File · {Detail}",
        ResultKind.Settings => "Windows Settings",
        ResultKind.Clipboard => "Clipboard",
        ResultKind.Emoji => "Emoji",
        _ => "Chippi",
    };

    /// <summary>The switch that turns this kind off.</summary>
    public ResultSwitch Switch => Kind switch
    {
        ResultKind.App => ResultSwitch.Apps,
        ResultKind.Window => ResultSwitch.Windows,
        ResultKind.File => ResultSwitch.Files,
        ResultKind.Settings => ResultSwitch.Settings,
        ResultKind.Clipboard => ResultSwitch.Clipboard,
        ResultKind.Answer => ResultSwitch.Math,
        ResultKind.Emoji => ResultSwitch.Emoji,
        ResultKind.Web => ResultSwitch.Web,
        _ => ResultSwitch.Chippi,
    };
}
