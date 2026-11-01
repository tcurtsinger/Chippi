// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.UI.Pet;

/// <summary>Chippi's size in each place he's shown, named once (DESIGN.md §7, Sizes).</summary>
public static class PetSize
{
    /// <summary>In the 50 puck, and at the bar's end.</summary>
    public const double Puck = 40;

    /// <summary>The home's footer: the puck's Chippi shrinks to this as the puck becomes the panel (70%).</summary>
    public const double Footer = 28;

    /// <summary>The home's Now Playing card without art: his cover, filling its 52 square.</summary>
    public const double HomeCover = 44;

    /// <summary>Now Playing without art: his cover, filling its 156 square.</summary>
    public const double Cover = 120;

    /// <summary>Now Playing with nothing playing, asleep over the words.</summary>
    public const double NothingPlaying = 96;

    /// <summary>A feature page's top row (Keep awake's).</summary>
    public const double Page = 72;

    /// <summary>Settings › Island &amp; pet, beside the color swatches, at his puck size.</summary>
    public const double Preview = Puck;

    /// <summary>About, beside his name.</summary>
    public const double About = 32;

    /// <summary>The Settings titlebar: the small form.</summary>
    public const double Titlebar = 16;
}
