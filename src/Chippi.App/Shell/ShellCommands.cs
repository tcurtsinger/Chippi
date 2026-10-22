// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.App.Shell;

/// <summary>
/// The few actions one shell surface asks of another: the flyout's Settings button opens the
/// settings window, a second launch shows the flyout, the tray menu quits. Each surface registers
/// its handler when it is composed, so no window references another window's type. Call on the
/// UI thread.
/// </summary>
public sealed class ShellCommands
{
    private Action? _toggleFlyout;
    private Action? _showFlyout;
    private Action<string?>? _openSettings;

    /// <summary>Raised by <see cref="Quit"/>; the composition root stops modules and shuts down.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>WS1 registers these when the tray and flyout exist.</summary>
    public void RegisterFlyout(Action toggle, Action show)
    {
        _toggleFlyout = toggle;
        _showFlyout = show;
    }

    /// <summary>WS2 registers this. The page is a sidebar id such as "features", or null for the last page.</summary>
    public void RegisterSettings(Action<string?> open) => _openSettings = open;

    public void ToggleFlyout() => _toggleFlyout?.Invoke();

    public void ShowFlyout() => _showFlyout?.Invoke();

    public void OpenSettings(string? page = null) => _openSettings?.Invoke(page);

    public void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}
