// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>Win+P's Extend and PC screen only.</summary>
internal static class DisplayTopology
{
    /// <summary>How many screens are connected, and whether the desktop is extended across them.</summary>
    public static (int Screens, bool Extended) Read()
    {
        var (active, topology) = Query(QUERY_DISPLAY_CONFIG_FLAGS.QDC_DATABASE_CURRENT);
        int screens = active >= 2 ? active : Query(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ALL_PATHS).Targets;
        return (screens, topology == DISPLAYCONFIG_TOPOLOGY_ID.DISPLAYCONFIG_TOPOLOGY_EXTEND);
    }

    public static void Set(bool extend)
    {
        var flags = SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY
            | (extend ? SET_DISPLAY_CONFIG_FLAGS.SDC_TOPOLOGY_EXTEND : SET_DISPLAY_CONFIG_FLAGS.SDC_TOPOLOGY_INTERNAL);
        int result = PInvoke.SetDisplayConfig([], [], flags);
        if (result != 0)
            throw new Win32Exception(result);
    }

    /// <summary>The distinct screens with a monitor attached in these paths, and the topology (only the current database has one).</summary>
    private static unsafe (int Targets, DISPLAYCONFIG_TOPOLOGY_ID Topology) Query(QUERY_DISPLAY_CONFIG_FLAGS flags)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var error = PInvoke.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (error != WIN32_ERROR.ERROR_SUCCESS)
                throw new Win32Exception((int)error);
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            DISPLAYCONFIG_TOPOLOGY_ID topology = 0;
            fixed (DISPLAYCONFIG_PATH_INFO* p = paths)
            fixed (DISPLAYCONFIG_MODE_INFO* m = modes)
            {
                error = PInvoke.QueryDisplayConfig(flags, &pathCount, p, &modeCount, m,
                    flags == QUERY_DISPLAY_CONFIG_FLAGS.QDC_DATABASE_CURRENT ? &topology : null);
            }
            if (error == WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                continue; // a screen came or went between the two calls
            if (error != WIN32_ERROR.ERROR_SUCCESS)
                throw new Win32Exception((int)error);

            var targets = new HashSet<(uint, int, uint)>();
            foreach (var path in paths.AsSpan(0, (int)pathCount))
            {
                if (path.targetInfo.targetAvailable)
                    targets.Add((path.targetInfo.adapterId.LowPart, path.targetInfo.adapterId.HighPart, path.targetInfo.id));
            }
            return (targets.Count, topology);
        }
        throw new Win32Exception((int)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER);
    }
}
