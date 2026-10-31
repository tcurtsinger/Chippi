// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.Sound.Volume;

/// <summary>Show apps that aren't playing is gone (2026-10-31): the island shows playing apps, Settings every app.</summary>
public sealed record AppVolumeSettings
{
    public bool RememberOutputs { get; init; } = true;
    public Dictionary<string, string> Outputs { get; init; } = [];
}
