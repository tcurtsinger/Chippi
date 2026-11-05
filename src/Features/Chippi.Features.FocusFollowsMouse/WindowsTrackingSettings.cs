// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.Features.FocusFollowsMouse;

// Only the feature module constructs this adapter. Never use it in host-side tests.
public sealed class WindowsTrackingSettings : ITrackingSettings
{
    public unsafe uint Read(TrackingSetting setting)
    {
        var action = setting switch
        {
            TrackingSetting.Enabled => SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETACTIVEWINDOWTRACKING,
            TrackingSetting.Raise => SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETACTIVEWNDTRKZORDER,
            TrackingSetting.Delay => SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETACTIVEWNDTRKTIMEOUT,
            _ => throw new ArgumentOutOfRangeException(nameof(setting))
        };
        uint value = 0;
        if (!PInvoke.SystemParametersInfo(action, 0, &value, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Focus change failed");
        return value;
    }

    public unsafe void Write(TrackingSetting setting, uint value)
    {
        var action = setting switch
        {
            TrackingSetting.Enabled => SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETACTIVEWINDOWTRACKING,
            TrackingSetting.Raise => SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETACTIVEWNDTRKZORDER,
            TrackingSetting.Delay => SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETACTIVEWNDTRKTIMEOUT,
            _ => throw new ArgumentOutOfRangeException(nameof(setting))
        };
        // These three SET actions take the value in pvParam, not a pointer to the value.
        // No profile update: ownership recovery covers a process crash without changing logon defaults.
        if (!PInvoke.SystemParametersInfo(action, 0, (void*)(nuint)value, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Focus change failed");
    }
}
