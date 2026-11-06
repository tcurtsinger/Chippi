// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

/// <summary>Saved outer window bounds and work area in physical desktop pixels. No live identity is persisted.</summary>
public sealed record SavedWindowPlacement(int Left, int Top, int Width, int Height,
    int WorkLeft, int WorkTop, int WorkWidth, int WorkHeight, bool Maximized, bool Minimized)
{
    public bool IsValid => Width > 0 && Height > 0 && WorkWidth > 0 && WorkHeight > 0
        && (long)Left + Width <= int.MaxValue && (long)Top + Height <= int.MaxValue;
}

/// <summary>An executable launch, never a shell command. Arguments are entered deliberately.</summary>
public sealed record ExecutableLaunch(string Path, string Arguments = "", string WorkingFolder = "")
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Path) && Arguments is not null && WorkingFolder is not null
        && System.IO.Path.IsPathFullyQualified(Path)
        && string.Equals(System.IO.Path.GetExtension(Path), ".exe", StringComparison.OrdinalIgnoreCase)
        && (WorkingFolder.Length == 0 || System.IO.Path.IsPathFullyQualified(WorkingFolder))
        && !Path.Contains('\0') && !Arguments.Contains('\0') && !WorkingFolder.Contains('\0');
}
