// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.ColorPicker;

/// <summary>One of a color's written forms: "RGB" and "rgb(124, 92, 255)".</summary>
public sealed record ColorFormat(string Name, string Value);

/// <summary>A sampled color: its HEX ("#7C5CFF") and its other forms in the page's order (RGB, HSL, HSV, CMYK).</summary>
public sealed record PickedColor(string Hex, IReadOnlyList<ColorFormat> Formats);

/// <summary>
/// What the island's Color page shows and does (color-picker.md, Color page; Lumen's Capture results v3). The feature's
/// module implements it and writes every value; the page only shows them and never opens the clipboard itself.
/// </summary>
public interface IColorPageModel
{
    /// <summary>The color on the page, or null before the first pick.</summary>
    PickedColor? Color { get; }

    /// <summary>The format Pick color copied on the pick ("HEX"), or null when nothing was copied (Pick and review).</summary>
    string? CopiedAs { get; }

    /// <summary>The recent palette as HEX, newest first, up to 10.</summary>
    IReadOnlyList<string> Recent { get; }

    /// <summary>Raised on any thread when the color, the palette or <see cref="CopiedAs"/> changes.</summary>
    event Action? Changed;

    /// <summary>Copies a format's value through the clipboard hub; false when the write was refused ("Copy failed").</summary>
    bool Copy(ColorFormat format);

    /// <summary>A recent swatch was clicked: that color goes on the page, and nothing else happens.</summary>
    void Show(string hex);

    /// <summary>Clears the recent palette, after its confirm.</summary>
    void ClearRecent();

    /// <summary>Starts Pick and review again.</summary>
    void PickAgain();
}
