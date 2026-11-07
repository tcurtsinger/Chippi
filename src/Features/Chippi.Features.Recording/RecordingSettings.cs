// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;

namespace Chippi.Features.Recording;

/// <summary>What a recording is saved as.</summary>
public enum RecordingFormat { Mp4, Gif }

/// <summary>
/// Settings › Recording (screen-recording.md, Copy: Format, Frame rate, GIF limit, Cursor, Click rings, Save to,
/// Shortcuts). System sound and Mic aren't kept: both start off for every recording (README, task 487).
/// </summary>
public sealed record RecordingSettings
{
    /// <summary>What Record region, window and screen start with; Record GIF always starts with GIF.</summary>
    public RecordingFormat Format { get; init; } = RecordingFormat.Mp4;

    /// <summary>Frames per second of the MP4.</summary>
    public int FrameRate { get; init; } = 30;

    /// <summary>The pointer is in the video.</summary>
    public bool Cursor { get; init; } = true;

    /// <summary>Rings where the mouse clicks (Settings can show them; drawn by the overlay, not yet built).</summary>
    public bool ClickRings { get; init; }

    /// <summary>Where Save writes without asking; null is Videos\Chippi.</summary>
    public string? SaveTo { get; init; }

    /// <summary>The starters' and Stop's keys, by name; null until one is set (all unassigned).</summary>
    public Dictionary<string, KeyShortcut?>? Shortcuts { get; init; }

    public static readonly int[] FrameRates = [15, 30, 60];
}

/// <summary>The shortcut slots (screen-recording.md, Shortcuts). All unassigned.</summary>
public static class RecordingShortcuts
{
    public const string RecordRegion = "Record region";
    public const string RecordWindow = "Record window";
    public const string RecordScreen = "Record screen";
    public const string RecordGif = "Record GIF";
    public const string StopRecording = "Stop recording";

    public static readonly string[] All = [RecordRegion, RecordWindow, RecordScreen, RecordGif, StopRecording];
}
