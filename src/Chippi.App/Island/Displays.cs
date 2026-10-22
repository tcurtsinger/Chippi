// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Chippi.App.Island;

/// <summary>A connected monitor, as the island setting sees it.</summary>
/// <param name="Id">Stable device path (<c>\\?\DISPLAY#…</c>); survives renumbering and rearranging.</param>
/// <param name="Name">What Settings shows: the monitor's own name, with its resolution and "main" where it applies.</param>
internal sealed record DisplayInfo(string Id, string Name, HMONITOR Handle, bool IsMain);

/// <summary>
/// Connected monitors and their stable ids. Windows display configuration gives each active
/// monitor its friendly name and a device path that does not change when <c>\\.\DISPLAYn</c>
/// numbers do; the GDI source name ties it to the live HMONITOR. Read it fresh when needed (after a
/// display change it is different); it is cheap.
/// </summary>
internal static class Displays
{
    public static IReadOnlyList<DisplayInfo> Connected()
    {
        var byGdiName = Monitors();
        var found = new List<(string Id, string Friendly, HMONITOR Handle, bool IsMain, bool Internal, string Size)>();
        foreach (var (gdiName, id, friendly, isInternal) in ActiveTargets())
        {
            if (!byGdiName.TryGetValue(gdiName, out var monitor) || found.Any(f => f.Id == id))
                continue;
            found.Add((id, friendly, monitor.Handle, monitor.IsMain, isInternal, monitor.Size));
        }

        // Names: the monitor's own, "Built-in display" for a laptop panel that has none, then
        // resolution so two identical monitors can be told apart, and "main" for the main one.
        return
        [
            .. found
                .OrderByDescending(f => f.IsMain)
                .Select((f, i) =>
                {
                    string name = f.Friendly.Length > 0 ? f.Friendly : f.Internal ? "Built-in display" : $"Display {i + 1}";
                    return new DisplayInfo(f.Id, $"{name} ({f.Size}{(f.IsMain ? ", main" : "")})", f.Handle, f.IsMain);
                }),
        ];
    }

    /// <summary>The live handle for a stored id, or null when that monitor is not connected.</summary>
    public static HMONITOR? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : Connected().FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))?.Handle;

    public static HMONITOR Main => PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);

    /// <summary>Every monitor by its GDI name (<c>\\.\DISPLAY1</c>), with whether it is the main one and its size in pixels.</summary>
    private static unsafe Dictionary<string, (HMONITOR Handle, bool IsMain, string Size)> Monitors()
    {
        var result = new Dictionary<string, (HMONITOR, bool, string)>(StringComparer.OrdinalIgnoreCase);
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW { monitorInfo = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFOEXW) } };
            if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            {
                var r = info.monitorInfo.rcMonitor;
                bool main = (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0;
                result[info.szDevice.ToString()] = (monitor, main, $"{r.right - r.left}×{r.bottom - r.top}");
            }
            return true;
        }, default);
        return result;
    }

    /// <summary>Each active display path: its GDI source name, the target's device path, friendly name, and whether it is built in.</summary>
    private static unsafe List<(string GdiName, string Id, string Friendly, bool Internal)> ActiveTargets()
    {
        var targets = new List<(string, string, string, bool)>();
        const QUERY_DISPLAY_CONFIG_FLAGS flags = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (PInvoke.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount) != WIN32_ERROR.NO_ERROR)
                return targets;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            WIN32_ERROR result;
            fixed (DISPLAYCONFIG_PATH_INFO* p = paths)
            fixed (DISPLAYCONFIG_MODE_INFO* m = modes)
                result = PInvoke.QueryDisplayConfig(flags, &pathCount, p, &modeCount, m, null);
            if (result == WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                continue; // a monitor came or went between the two calls
            if (result != WIN32_ERROR.NO_ERROR)
                return targets;

            foreach (var path in paths.AsSpan(0, (int)pathCount))
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                source.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
                source.header.adapterId = path.sourceInfo.adapterId;
                source.header.id = path.sourceInfo.id;
                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
                target.header.adapterId = path.targetInfo.adapterId;
                target.header.id = path.targetInfo.id;
                if (PInvoke.DisplayConfigGetDeviceInfo(&source.header) != 0 || PInvoke.DisplayConfigGetDeviceInfo(&target.header) != 0)
                    continue;

                string id = target.monitorDevicePath.ToString();
                if (id.Length == 0)
                    continue;
                bool isInternal = target.outputTechnology is DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                    or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                    or DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED;
                targets.Add((source.viewGdiDeviceName.ToString(), id, target.monitorFriendlyDeviceName.ToString(), isInternal));
            }
            return targets;
        }
        return targets;
    }
}
