// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;
using Windows.Win32.Foundation;

namespace Chippi.App.Island;

/// <summary>A keyboard request lasts for one open panel; actual foreground ownership can change underneath it.</summary>
internal sealed class IslandKeyboardHandoff
{
    internal bool Requested { get; private set; }
    internal PasteTarget? PasteTarget { get; private set; }

    /// <returns>Whether the caller should initialize focus after the request.</returns>
    internal bool Take(HWND hwnd, Func<HWND, bool> isForeground, Func<PasteTarget?>? captureTarget, Action<HWND> activate)
    {
        bool foreground = isForeground(hwnd);
        if (Requested && foreground)
            return false;

        if (!Requested)
        {
            // Capture only once, before activation. A failed/reclaimed handoff must not replace
            // the original paste destination with another app (or with the island itself).
            if (!foreground)
                PasteTarget = captureTarget?.Invoke();
            Requested = true;
        }

        if (!foreground)
            activate(hwnd);
        return true;
    }

    internal void Reset()
    {
        Requested = false;
        PasteTarget = null;
    }
}
