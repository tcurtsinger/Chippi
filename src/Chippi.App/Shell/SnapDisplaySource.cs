// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Island;
using Chippi.Features.WindowLayouts;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace Chippi.App.Shell;

/// <summary>Reuses the island's stable monitor paths and friendly names.</summary>
internal static class SnapDisplaySource
{
    public static unsafe IReadOnlyList<SnapDisplay> Connected()
    {
        var displays = new List<SnapDisplay>();
        foreach (var display in Displays.Connected())
        {
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            if (!PInvoke.GetMonitorInfo(display.Handle, ref info)) continue;
            var bounds = info.rcMonitor;
            var work = info.rcWork;
            displays.Add(new(display.Id, Bare(display.Name), (nint)display.Handle.Value, display.IsMain,
                new(bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top),
                new(work.left, work.top, work.right - work.left, work.bottom - work.top)));
        }
        return displays.OrderBy(d => d.Bounds.X).ThenBy(d => d.Bounds.Y).ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// The monitor's own name ("DELL U2723QE"), without the island picker's resolution and "main", which Settings › Snap
    /// shows on its own caption line.
    /// </summary>
    internal static string Bare(string name) => name.EndsWith(')') && name.LastIndexOf(" (", StringComparison.Ordinal) is > 0 and int at ? name[..at] : name;
}
