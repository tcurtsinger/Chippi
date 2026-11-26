// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>
/// Which displays' bars stay hidden for a fullscreen app. A display's bar hides when a fullscreen window comes to the
/// front on it, and stays hidden for as long as that window still covers the display, wherever the keyboard goes next:
/// a film on one display isn't drawn over because of a click on the other. It comes back when the window stops covering
/// the display (closed, minimized, out of fullscreen) or another app's window comes to the front on that display. Pure,
/// so it's tested without windows.
/// </summary>
internal sealed class FullscreenHolds
{
    private readonly Dictionary<nint, nint> _held = []; // display → the fullscreen window on it

    /// <summary>The displays whose bars are hidden now.</summary>
    /// <param name="front">The window in front.</param>
    /// <param name="frontFullscreen">It counts as fullscreen on its display.</param>
    /// <param name="frontDisplay">Its display.</param>
    /// <param name="frontOrdinary">It's another app's window: not the desktop, the taskbar, Start or one of Chippi's own.</param>
    /// <param name="covers">Whether a window still covers a display as a fullscreen app does.</param>
    public IReadOnlyCollection<nint> Update(nint front, bool frontFullscreen, nint frontDisplay, bool frontOrdinary, Func<nint, nint, bool> covers)
    {
        if (front != 0 && frontFullscreen && frontDisplay != 0)
            _held[frontDisplay] = front;
        foreach (var (display, window) in _held.ToArray())
        {
            if (window == front && frontFullscreen)
                continue;
            if (!covers(window, display) || (frontOrdinary && frontDisplay == display))
                _held.Remove(display);
        }
        return _held.Keys;
    }

    public void Clear() => _held.Clear();
}

/// <summary>
/// The app in front, held still while one of the bar's menus is open (the menu belongs to the app it opened for), and
/// caught up with when the last menu closes: a change that came meanwhile (the menu's own row minimized or closed the
/// window) isn't lost.
/// </summary>
internal sealed class FrontGate
{
    private int _open;
    private bool _missed;

    public bool MenuOpen => _open > 0;

    public void Opened() => _open++;

    /// <summary>The app in front changed. True when the bar follows it now; false while a menu holds it still.</summary>
    public bool Changed()
    {
        if (_open > 0)
        {
            _missed = true;
            return false;
        }
        _missed = false;
        return true;
    }

    /// <summary>A menu closed. True when it was the last one open and a change came while any was: the bar reads the app in front again.</summary>
    public bool Closed()
    {
        _open = Math.Max(0, _open - 1);
        if (_open > 0 || !_missed)
            return false;
        _missed = false;
        return true;
    }
}
