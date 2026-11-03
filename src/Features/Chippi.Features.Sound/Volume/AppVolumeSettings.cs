// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.Sound.Volume;

/// <summary>Per-app output preferences, keyed by the app's Windows audio session identity.</summary>
public sealed record AppVolumeSettings
{
    public bool RememberOutputs { get; init; } = true;
    public Dictionary<string, string> Outputs { get; init; } = [];
}
