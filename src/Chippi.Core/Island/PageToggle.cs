// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Island;

/// <summary>
/// A button outside the panel that opens one of its pages closes that page on a second press (the Mac-style menu bar's
/// items). The press itself closes the panel first, as any press off the panel does, so by the time the button hears of it
/// the page is gone and it would open it again. This remembers what a press just closed.
/// </summary>
public sealed class PageToggle
{
    /// <summary>How long after a press closed the panel that press can still reach its button.</summary>
    public const int Milliseconds = 500;

    private string? _page;
    private long _at;
    private bool _closed;

    /// <summary>A press off the panel is closing it on <paramref name="page"/> (null for the home).</summary>
    public void ClosedByPress(string? page, long now) => (_page, _at, _closed) = (page, now, true);

    /// <summary>
    /// A press found the panel already closed: whatever an earlier press closed isn't this press's doing. Without this, a
    /// press on the desktop that closed a page, then one on its button soon after, would count as the second press and open
    /// nothing.
    /// </summary>
    public void Forget() => _closed = false;

    /// <summary>
    /// Whether a press on the button for <paramref name="key"/> (null for the home) found its page open: open on it now, or
    /// closed by this same press. The press is forgotten either way.
    /// </summary>
    public bool WasOn(string? key, bool open, string? current, long now)
    {
        bool byPress = _closed && now - _at <= Milliseconds && _page == key;
        _closed = false;
        return (open && current == key) || byPress;
    }
}
