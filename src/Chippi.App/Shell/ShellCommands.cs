// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.App.Shell;

/// <summary>
/// The few actions one shell surface asks of another: a second launch shows the island, the tray
/// icon and the island's Settings button open the settings window, the tray menu quits. Each surface
/// registers its handler when it is composed, so no window references another window's type. Call
/// on the UI thread.
/// </summary>
public sealed class ShellCommands
{
    private Func<bool, bool>? _showPages;
    private Func<string, bool>? _showPage;
    private Action<string?>? _openSettings;
    private Func<IDisposable> _holdIslandOpen = () => new Chippi.Core.Modules.IslandOpenHold().Hold();
    public void RegisterIslandHold(Func<IDisposable> hold) => _holdIslandOpen = hold;
    public IDisposable HoldIslandOpen() => _holdIslandOpen();

    /// <summary>Raised by <see cref="Quit"/>; the composition root stops modules and shuts down.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Raised when <see cref="IslandShowing"/> changes.</summary>
    public event EventHandler? IslandShowingChanged;

    /// <summary>Whether the island's tab is on screen: on, and not hidden for a fullscreen app. The tray icon can hide only then.</summary>
    public bool IslandShowing { get; private set; }

    /// <summary>
    /// WS3 registers these: open the island (taking the keyboard when the flag is true), or open it on
    /// a tile's page. Each returns false when the island can't show (off, or hidden for a fullscreen app).
    /// </summary>
    public void RegisterPages(Func<bool, bool> show, Func<string, bool> showPage)
    {
        _showPages = show;
        _showPage = showPage;
    }

    /// <summary>WS2 registers this. The page is a sidebar id such as "features", or null for the last page.</summary>
    public void RegisterSettings(Action<string?> open) => _openSettings = open;

    /// <summary>A second launch: the island, with the keyboard; Settings when the island can't show.</summary>
    public void ShowPages()
    {
        if (_showPages?.Invoke(true) != true)
            OpenSettings();
    }

    /// <summary>The island on a tile's page (a notice click); Settings when the island can't show. An unknown key opens the island as <see cref="ShowPages"/> does.</summary>
    public void ShowPage(string key)
    {
        if (_showPage?.Invoke(key) != true)
            OpenSettings();
    }

    /// <summary>WS3 reports whether the island's tab is on screen.</summary>
    public void SetIslandShowing(bool showing)
    {
        if (showing == IslandShowing)
            return;
        IslandShowing = showing;
        IslandShowingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Settings on <paramref name="page"/>, or on the last page used. The tray icon's click opens it this way (DESIGN.md §5, revised 2026-10-28).</summary>
    public void OpenSettings(string? page = null) => _openSettings?.Invoke(page);

    public void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}
