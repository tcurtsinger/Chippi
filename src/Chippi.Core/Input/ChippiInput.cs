// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Input;

/// <summary>What marks the input Chippi sends itself.</summary>
public static class ChippiInput
{
    /// <summary>The extra info on every key and button Chippi sends ("CHIP"), so the input hub passes it on untouched.</summary>
    public const nint Marker = 0x43_48_49_50;
}
