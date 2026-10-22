// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Tray;

/// <summary>Per-monitor DPI helpers. Chippi targets Windows 11, so the guards only satisfy the analyzer.</summary>
internal static class MonitorDpi
{
    /// <summary>The monitor's effective DPI (96 = 100%).</summary>
    public static uint Of(HMONITOR monitor)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(8, 1)
            && PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpi, out _).Succeeded
            && dpi > 0)
            return dpi;
        return 96;
    }

    /// <summary>The small-icon edge in pixels at a DPI: 16 at 100%, 24 at 150%, 32 at 200%.</summary>
    public static int SmallIconSize(uint dpi)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            int size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi);
            if (size > 0)
                return size;
        }
        return (int)Math.Round(16 * dpi / 96.0);
    }
}
