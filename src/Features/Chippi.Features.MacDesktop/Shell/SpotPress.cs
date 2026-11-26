// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>
/// The display whose menu bar was just pressed (docs/mac-style/brief.md, The spot): a page that press opens goes to the
/// spot on that display. It's taken once and it goes stale, so a press that opened nothing (Sound, the clock, a press
/// that closed the panel) never sends a later page, asked for another way, to its display.
/// </summary>
public sealed class SpotPress
{
    /// <summary>How long after a press its page can still open: a command that opens one answers within this.</summary>
    public const int Milliseconds = 1000;

    private nint _monitor;
    private long _at;

    public void Set(nint monitor, long now) => (_monitor, _at) = (monitor, now);

    /// <summary>The pressed display, or 0 when nothing was pressed within <see cref="Milliseconds"/>; forgotten either way.</summary>
    public nint Take(long now)
    {
        nint monitor = _monitor != 0 && now - _at <= Milliseconds ? _monitor : 0;
        _monitor = 0;
        return monitor;
    }
}
