// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AppSwitcher;

/// <summary>One window the switcher lists.</summary>
/// <param name="Handle">The window to activate.</param>
/// <param name="ProcessId">The process that owns <paramref name="Handle"/>: checked again before activating, so a reused handle is refused.</param>
/// <param name="App">The app's name ("Notepad"), for the row and for search.</param>
/// <param name="Path">The app's program file, for its icon; empty when unknown.</param>
/// <param name="Monitor">The display the window is on (an HMONITOR), for the current-display filter.</param>
public sealed record SwitcherWindow(nint Handle, uint ProcessId, string App, string Title, string Path, bool Minimized, nint Monitor);

/// <summary>
/// One opening of the switcher: the windows it shows, the search, the display filter and the selection. Memory only, and
/// pure: the module reads the windows, and the overlay draws this and calls <see cref="Type"/>, <see cref="Move"/> and
/// <see cref="Select"/>. A window that closes is removed with <see cref="Remove"/>, and nothing else is selected for it
/// to activate.
/// </summary>
public sealed class SwitcherSession
{
    private readonly IReadOnlyList<SwitcherWindow> _all;
    private List<SwitcherWindow> _shown = [];

    /// <param name="windows">Most recently active first.</param>
    /// <param name="original">The window in front before the switcher opened: Esc gives it the keyboard back.</param>
    /// <param name="monitor">The display the switcher was opened from, for <see cref="CurrentDisplayOnly"/>.</param>
    public SwitcherSession(IReadOnlyList<SwitcherWindow> windows, nint original, nint monitor, bool currentDisplayOnly)
    {
        _all = windows;
        Original = original;
        Monitor = monitor;
        CurrentDisplayOnly = currentDisplayOnly;
        Filter();
        // Like Alt+Tab: the window you came from is first, so the next one is selected.
        Selected = _shown.Count == 0 ? -1 : _shown.Count > 1 && _shown[0].Handle == original ? 1 : 0;
    }

    public nint Original { get; }

    public nint Monitor { get; }

    public bool CurrentDisplayOnly { get; private set; }

    public string Query { get; private set; } = "";

    /// <summary>The windows to draw, in order.</summary>
    public IReadOnlyList<SwitcherWindow> Shown => _shown;

    /// <summary>An index into <see cref="Shown"/>, or -1 when it's empty.</summary>
    public int Selected { get; private set; }

    public SwitcherWindow? Selection => Selected >= 0 && Selected < _shown.Count ? _shown[Selected] : null;

    /// <summary>Raised after anything the overlay draws changed.</summary>
    public event Action? Changed;

    /// <summary>The search text: every word must be in the window's title or its app's name. A new search selects the first match.</summary>
    public void Type(string query)
    {
        query ??= "";
        if (query == Query)
            return;
        Query = query;
        Filter();
        Selected = _shown.Count > 0 ? 0 : -1;
        Changed?.Invoke();
    }

    /// <summary>Steps the selection, wrapping at either end.</summary>
    public void Move(int by)
    {
        if (_shown.Count == 0)
            return;
        Selected = ((Selected + by) % _shown.Count + _shown.Count) % _shown.Count;
        Changed?.Invoke();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _shown.Count || index == Selected)
            return;
        Selected = index;
        Changed?.Invoke();
    }

    public void ShowCurrentDisplayOnly(bool on)
    {
        if (on == CurrentDisplayOnly)
            return;
        var keep = Selection?.Handle;
        CurrentDisplayOnly = on;
        Filter();
        int at = _shown.FindIndex(w => w.Handle == keep);
        Selected = at >= 0 ? at : _shown.Count > 0 ? 0 : -1;
        Changed?.Invoke();
    }

    /// <summary>
    /// Drops a window that closed (or whose handle now belongs to another process). The selection stays at the same
    /// place in the list, so it moves to a neighbour, which only a later Enter or click activates.
    /// </summary>
    public bool Remove(nint handle)
    {
        _removed.Add(handle);
        int at = _shown.FindIndex(w => w.Handle == handle);
        if (at < 0)
            return false;
        _shown.RemoveAt(at);
        if (_shown.Count == 0)
            Selected = -1;
        else if (Selected > at || Selected >= _shown.Count)
            Selected = Math.Max(0, Selected - 1);
        Changed?.Invoke();
        return true;
    }

    private readonly HashSet<nint> _removed = [];

    private void Filter()
    {
        string[] words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _shown = [.. _all.Where(w => !_removed.Contains(w.Handle)
            && (!CurrentDisplayOnly || w.Monitor == Monitor)
            && words.All(word => w.Title.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                || w.App.Contains(word, StringComparison.CurrentCultureIgnoreCase)))];
    }
}
