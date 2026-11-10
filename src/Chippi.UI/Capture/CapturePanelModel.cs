// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.UI.Capture;

/// <summary>
/// What the Capture panel (designs/capture-panel v1, <see cref="CapturePanelView"/>) reads and asks for. The Capture host
/// implements it over the installed capture features (screen-capture.md, Capture panel): Screen capture brings the modes,
/// Delay and Recent; Text and QR, Color picker and, in wave 2, Recording and Media tools each bring their tools.
/// The view reads it on the UI thread.
/// </summary>
public interface ICapturePanelModel
{
    /// <summary>Screen capture is installed: the modes, Delay and Recent show. Without it the panel shows only the tools.</summary>
    bool CanCapture { get; }
    bool IsRecording => false;
    bool HasRecent => CanCapture;

    /// <summary>Seconds the next mode waits: 0, or one of <see cref="Chippi.Capture.Countdown.Delays"/>. The host keeps it.</summary>
    int Delay { get; set; }

    /// <summary>
    /// The keys that start <paramref name="mode"/>, as the shortcut service names them ("Print Screen", "Ctrl+Shift+W"), for
    /// its tooltip. Null when none is set, and for Print Screen while Windows' own setting holds the key.
    /// </summary>
    string? Keys(CaptureMode mode);

    /// <summary>The installed features' tools in order (Text, QR code, Color; wave 2 adds Record, GIF, Convert). Six fit.</summary>
    IReadOnlyList<CapturePanelTool> Tools { get; }

    /// <summary>Finished captures, newest first, thumbnails already decoded (frozen). The panel shows the first four.</summary>
    IReadOnlyList<CapturePanelRecent> Recent { get; }

    /// <summary>
    /// A mode's card was pressed: start it after <see cref="Delay"/> from <see cref="CaptureDoor.Island"/>, so the island
    /// closes before the screen freezes.
    /// </summary>
    void Start(CaptureMode mode);

    /// <summary>A Recent thumbnail was pressed: open that capture on its result page.</summary>
    void OpenRecent(string id);

    /// <summary>Open folder: the newest saved capture's folder, otherwise Save to.</summary>
    void OpenFolder();

    /// <summary>Anything above changed (a feature came or went, a capture finished, a shortcut changed). Any thread.</summary>
    event Action? Changed;
}

/// <summary>A tool on the panel's second row, from the feature that offers it.</summary>
/// <param name="Id">Stable within the panel, such as "screen-text/extract".</param>
/// <param name="Name">The card's label: "Text", "QR code", "Color".</param>
/// <param name="Verb">What pressing it does, for its tooltip and screen readers: "Extract text", "Scan QR", "Pick color".</param>
/// <param name="Icon">The card's glyph, from <see cref="Chippi.UI.Controls.LucideIcons"/>.</param>
/// <param name="Start">Starts it from the island, on the UI thread.</param>
public sealed record CapturePanelTool(string Id, string Name, string Verb, Geometry Icon, Action Start)
{
    public bool Enabled { get; init; } = true;
    /// <summary>Its keys once set, for the tooltip; null for none.</summary>
    public string? Keys { get; init; }
}

/// <summary>A finished capture under Recent.</summary>
/// <param name="Id">The history entry, passed back to <see cref="ICapturePanelModel.OpenRecent"/>.</param>
/// <param name="Name">The mode and its size or window: "Region · 1280 × 720", "Window · Terminal".</param>
/// <param name="When">Its age: "2 min ago", "Yesterday".</param>
/// <param name="Thumbnail">The prepared thumbnail, frozen; null shows the card's face with the image glyph.</param>
public sealed record CapturePanelRecent(string Id, string Name, string When, ImageSource? Thumbnail);
