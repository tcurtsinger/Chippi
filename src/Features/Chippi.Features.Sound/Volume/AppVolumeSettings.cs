// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.Sound.Volume;

public sealed record AppVolumeSettings
{
    public bool ShowQuietApps { get; init; } = true;
    public bool RememberOutputs { get; init; } = true;
    public Dictionary<string, string> Outputs { get; init; } = [];
}