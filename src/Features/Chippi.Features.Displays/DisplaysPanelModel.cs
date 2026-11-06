// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;

namespace Chippi.Features.Displays;

/// <summary>A slider on the Displays panel: VCP 0x10 or 0x12.</summary>
public enum DisplayControl
{
    Brightness,
    Contrast,
}

/// <summary>
/// What the island's Displays panel shows and changes (docs/phase-7/displays.md, Displays panel; Lumen's Displays v3).
/// The feature's module implements it next to <see cref="IDisplaysPageModel"/>; the panel never reads or writes a monitor
/// itself.
/// </summary>
public interface IDisplaysPanelModel
{
    /// <summary>The panel's displays: the first four by display order, those that don't answer included.</summary>
    IReadOnlyList<DisplayStatus> PanelDisplays { get; }

    /// <summary>Where a display sits on the desktop, in desktop pixels, so the panel draws them as Windows arranges them; null when unknown.</summary>
    Rect? Bounds(string displayId);

    /// <summary>The display under the pointer, which the panel opens on; null when it isn't one of <see cref="PanelDisplays"/>.</summary>
    string? UnderPointer();

    /// <summary>Raised on any thread when a display, its levels or the list change.</summary>
    event Action? Changed;

    /// <summary>The panel opened: reads every display again. The panel shows the last levels until the reads land.</summary>
    void Read();

    /// <summary>A display's slider moved. Called for every step of a drag; the latest value wins.</summary>
    void Set(string displayId, DisplayControl control, int percent);

    /// <summary>The All displays slider starts moving: each display's level now is its start (keep the difference).</summary>
    void BeginAll(DisplayControl control);

    /// <summary>Moves every display by what the All displays slider moved since <see cref="BeginAll"/>.</summary>
    void MoveAll(DisplayControl control, int value);

    /// <summary>The All displays slider stopped (release, or after a key or a click on the track).</summary>
    void EndAll();

    /// <summary>Reopens the display's handle and reads it again.</summary>
    void TryAgain(string displayId);
}
