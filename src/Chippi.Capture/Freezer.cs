// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Capture;

/// <summary>
/// The freeze's read (capture-layer.md, The freeze, steps 5 and 6): every display once, then the window list at the same
/// moment, so the list matches the image. Any thread.
/// </summary>
/// <param name="windows">Top-level windows front first, Chippi's overlays and puck left out.</param>
/// <param name="displayId">DisplayCatalog's id for an HMONITOR, or "".</param>
public sealed class Freezer(IScreenReader reader, Func<IReadOnlyList<FrozenWindow>> windows, Func<nint, string> displayId, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The frozen screen, or null when a display couldn't be read ("Capture failed": the secure desktop).</summary>
    public Freeze? Take(IReadOnlyList<MonitorBounds> monitors)
    {
        if (monitors.Count == 0)
            return null;
        var now = _time.GetUtcNow();
        var screens = new List<CapturedScreen>(monitors.Count);
        foreach (var m in monitors)
        {
            if (reader.Read(m.Bounds) is not { } pixels)
                return null;
            screens.Add(new CapturedScreen(displayId(m.Monitor), m.Monitor, m.Bounds, m.Dpi, pixels));
        }
        return new Freeze(screens, windows(), now);
    }
}
