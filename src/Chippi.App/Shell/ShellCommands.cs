// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Tray;

namespace Chippi.App.Shell;

/// <summary>
/// The few actions one shell surface asks of another: the tray icon opens the island's pages, a
/// second launch shows them, the island's Settings button opens the settings window, the tray menu
/// quits. Each surface registers its handler when it is composed, so no window references another
/// window's type. Call on the UI thread.
/// </summary>
public sealed class ShellCommands
{
    private Action<bool>? _togglePages;
    private Action? _showPages;
    private Action<string>? _showPage;
    private Func<TrayAnchor?>? _trayAnchor;
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
    /// WS3 registers these: the island's pages, in the island while it shows, otherwise docked by the
    /// tray. Toggle's flag is true when the request came from the keyboard, so the pages take it.
    /// </summary>
    public void RegisterPages(Action<bool> toggle, Action show, Action<string> showPage)
    {
        _togglePages = toggle;
        _showPages = show;
        _showPage = showPage;
    }

    /// <summary>WS1 registers where its icon is, for the pages docked by the tray.</summary>
    internal void RegisterTray(Func<TrayAnchor?> anchor) => _trayAnchor = anchor;

    /// <summary>WS2 registers this. The page is a sidebar id such as "features", or null for the last page.</summary>
    public void RegisterSettings(Action<string?> open) => _openSettings = open;

    /// <summary>The tray icon's left click; <paramref name="keyboard"/> for Enter or Space on it.</summary>
    public void TogglePages(bool keyboard = false) => _togglePages?.Invoke(keyboard);

    public void ShowPages() => _showPages?.Invoke();

    /// <summary>The pages, open on a tile's page (a notice click). An unknown key opens them as <see cref="ShowPages"/> does.</summary>
    public void ShowPage(string key) => _showPage?.Invoke(key);

    /// <summary>The tray icon and its display, or null without a tray.</summary>
    internal TrayAnchor? TrayAnchor() => _trayAnchor?.Invoke();

    /// <summary>WS3 reports whether the island's tab is on screen.</summary>
    public void SetIslandShowing(bool showing)
    {
        if (showing == IslandShowing)
            return;
        IslandShowing = showing;
        IslandShowingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OpenSettings(string? page = null) => _openSettings?.Invoke(page);

    public void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}
