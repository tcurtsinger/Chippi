// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.System.Registry;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>The power plans, as Control Panel's Power Options lists them.</summary>
internal static class PowerPlans
{
    public static unsafe IReadOnlyList<PowerPlan> Read()
    {
        var active = Active();
        var plans = new List<PowerPlan>();
        for (uint index = 0; ; index++)
        {
            Guid id;
            uint size = (uint)sizeof(Guid);
            var error = PInvoke.PowerEnumerate(HKEY.Null, null, null, POWER_DATA_ACCESSOR.ACCESS_SCHEME, index, (byte*)&id, &size);
            if (error == WIN32_ERROR.ERROR_NO_MORE_ITEMS)
                return plans;
            if (error != WIN32_ERROR.ERROR_SUCCESS)
                throw new Win32Exception((int)error);
            plans.Add(new PowerPlan(id, Name(id), id == active));
        }
    }

    public static unsafe void SetActive(Guid id)
    {
        var error = PInvoke.PowerSetActiveScheme(HKEY.Null, &id);
        if (error != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)error);
    }

    private static unsafe Guid Active()
    {
        Guid* active;
        var error = PInvoke.PowerGetActiveScheme(HKEY.Null, &active);
        if (error != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)error);
        try
        {
            return *active;
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)active);
        }
    }

    private static unsafe string Name(Guid id)
    {
        uint size = 0;
        var error = PInvoke.PowerReadFriendlyName(HKEY.Null, &id, null, null, null, &size);
        if (error != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)error);
        var buffer = new byte[size];
        fixed (byte* text = buffer)
            error = PInvoke.PowerReadFriendlyName(HKEY.Null, &id, null, null, text, &size);
        if (error != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)error);
        return Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(size, (uint)buffer.Length)).TrimEnd('\0');
    }
}
