// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

/// <summary>Captured before a menu opens. Identity is rechecked before the window is changed.</summary>
public sealed record ActionWindow(nint Handle, uint ProcessId, uint ThreadId, long ProcessStarted,
    string ClassName, string Executable, string App, string Title, bool Elevated);

/// <summary>A named zone's fractions of a monitor work area. Gaps are physical pixels.</summary>
public sealed record WindowZone(double X, double Y, double Width, double Height, int WindowGap, int ScreenEdgeGap);
