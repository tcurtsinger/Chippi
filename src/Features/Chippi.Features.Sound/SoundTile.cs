// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;

namespace Chippi.Features.Sound;

/// <summary>
/// The island's Sound tile (DESIGN.md §5 Island): Audio routing, Mic mute shortcut and Mic processing
/// each add sections to its one page, Output beside Microphone.
/// </summary>
public static class SoundTile
{
    public const string Key = "sound";

    public const string Output = "Output";

    public const string Microphone = "Microphone";

    /// <summary>A section of the Sound page, in <paramref name="column"/>, at <paramref name="order"/> (lowest first).</summary>
    public static IslandTileSpec Section(int order, string column, Func<object> create) =>
        new(Key, "Sound", "AudioLines", Order: 10, SectionOrder: order, create) { Column = column };
}
