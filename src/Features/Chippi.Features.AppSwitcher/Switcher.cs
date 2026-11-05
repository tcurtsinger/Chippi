// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AppSwitcher;

/// <summary>The windows and the foreground. Tests pass a fake; the app passes <see cref="Windows.WindowsSwitcherPc"/>.</summary>
public interface ISwitcherPc
{
    /// <summary>The windows Alt+Tab would list, Chippi's own left out, front first.</summary>
    IReadOnlyList<SwitcherWindow> Windows();

    nint Foreground();

    /// <summary>The window's display, or 0 when it has none.</summary>
    nint MonitorOf(nint hwnd);

    nint PointerMonitor();

    /// <summary>Whether the window is still open and still the process's (a closed window's handle can be reused).</summary>
    bool IsSame(nint hwnd, uint processId);

    /// <summary>Gives the overlay the foreground, so keys reach it; false when Windows refused.</summary>
    bool TakeForeground(nint overlay);

    /// <summary>Restores the window when it's minimized and brings it to the front; false when Windows refused.</summary>
    bool Activate(SwitcherWindow window);

    /// <summary>Gives the foreground back to the window that had it, when it's still open.</summary>
    void GiveBack(nint original);
}

/// <summary>Windows coming to the front and closing, on the UI thread. Tests raise them by hand.</summary>
public interface IWindowEvents : IDisposable
{
    event Action<nint>? Foreground;

    /// <summary>A top-level window closed. Only listened to while the overlay is open.</summary>
    event Action<nint>? Destroyed;
}

/// <summary>
/// The overlay the switcher draws in (Lumen's screen). It shows the session without taking the foreground, and calls
/// the switcher back: <see cref="Switcher.Commit"/> on Enter or a click, <see cref="Switcher.Cancel"/> on Esc,
/// <see cref="Switcher.Dismiss"/> when it loses the foreground. It draws the session again on <see cref="SwitcherSession.Changed"/>.
/// </summary>
public interface ISwitcherView
{
    /// <summary>Shows the session on the display; returns the overlay's window, for the foreground.</summary>
    nint Show(SwitcherSession session, nint monitor);

    /// <summary>Windows refused the switch: the selected row says "Switch failed", and the overlay stays.</summary>
    void ShowFailed(SwitcherWindow window);

    void Hide();
}

public enum SwitchOutcome
{
    /// <summary>Nothing selected, or the switcher wasn't open.</summary>
    None,
    Switched,

    /// <summary>The window closed: it left the list, and nothing else was activated.</summary>
    Gone,

    /// <summary>Windows refused: the overlay stays, saying so.</summary>
    Failed,
}

/// <summary>
/// Opens, commits and cancels the App switcher. UI thread only. Opening reads the windows once; a commit checks the
/// window is still the same one before activating it, and only it.
/// </summary>
public sealed class Switcher(ISwitcherPc pc, RecentWindows recent, Func<Switcher, ISwitcherView> view)
{
    private ISwitcherView? _view;

    /// <summary>The open session, or null.</summary>
    public SwitcherSession? Session { get; private set; }

    public bool IsOpen => Session is not null;

    /// <summary>
    /// Opens the overlay. <paramref name="original"/> is the window in front before Chippi was used: for a shortcut the
    /// foreground, for the island and Search the window in front before they opened. Opening again while it's open
    /// steps the selection, like Alt+Tab. False when Windows refused the overlay the foreground: it closes again, so no
    /// key meant for it reaches another app.
    /// </summary>
    public bool Open(nint original, bool currentDisplayOnly)
    {
        if (Session is { } open)
        {
            open.Move(1);
            return true;
        }
        nint monitor = original != 0 ? pc.MonitorOf(original) : 0;
        if (monitor == 0)
            monitor = pc.PointerMonitor();
        var session = new SwitcherSession(recent.Order(pc.Windows()), original, monitor, currentDisplayOnly);
        _view ??= view(this);
        Session = session;
        nint overlay = _view.Show(session, monitor);
        if (!pc.TakeForeground(overlay))
        {
            Close();
            return false;
        }
        return true;
    }

    /// <summary>Activates the selected window and closes, or says why not.</summary>
    public SwitchOutcome Commit()
    {
        if (Session?.Selection is not { } window)
            return SwitchOutcome.None;
        if (!pc.IsSame(window.Handle, window.ProcessId))
        {
            Session.Remove(window.Handle);
            return SwitchOutcome.Gone;
        }
        // The overlay still holds the foreground here, which is what lets Windows hand it on.
        if (!pc.Activate(window))
        {
            _view?.ShowFailed(window);
            return SwitchOutcome.Failed;
        }
        Close();
        return SwitchOutcome.Switched;
    }

    /// <summary>Esc: the window that was in front gets the keyboard back, then the overlay closes.</summary>
    public void Cancel()
    {
        if (Session is not { } session)
            return;
        pc.GiveBack(session.Original);
        Close();
    }

    /// <summary>Closes without moving the foreground: the overlay lost it (a click elsewhere), the PC locked, or the shared pause began.</summary>
    public void Dismiss() => Close();

    /// <summary>A window closed while the overlay is open.</summary>
    public void Closed(nint hwnd) => Session?.Remove(hwnd);

    /// <summary>The overlay closed, however it did.</summary>
    public event Action? Closing;

    // Once: handing the foreground on deactivates the overlay, which dismisses it from inside Commit or Cancel.
    private void Close()
    {
        if (Session is null)
            return;
        Session = null;
        _view?.Hide();
        Closing?.Invoke();
    }
}
