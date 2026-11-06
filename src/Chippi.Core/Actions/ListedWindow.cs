// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

/// <summary>The shared Alt+Tab list. Identity must be captured and revalidated before any action.</summary>
public sealed record ListedWindow(nint Handle, uint ProcessId, string App, string Title, string Executable,
    bool Minimized, nint Monitor, bool CurrentDesktop = true, uint AppProcessId = 0);
