// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;
using Chippi.UI.Displays;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Chippi.Features.Displays;

/// <summary>
/// The real monitors (docs/phase-7/displays.md, Reading): DDC/CI through <c>dxva2</c> for an external one. A laptop's own panel
/// goes through WMI (<see cref="WmiPanel"/>). Only VCP 0x10 and 0x12 are ever read or written.
/// </summary>
internal sealed class WindowsMonitorBus : IMonitorBus
{
    public IMonitor? Open(DisplayInfo display) => display.IsInternal ? WmiPanel.Open() : Ddc.Open(display);

    /// <summary>The display the pointer is on, or null when it can't be read.</summary>
    public static unsafe nint? UnderPointer()
    {
        if (!PInvoke.GetCursorPos(out var point)) return null;
        return (nint)PInvoke.MonitorFromPoint(point, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST).Value;
    }

    private sealed unsafe class Ddc : IMonitor
    {
        private HANDLE _handle;
        private PHYSICAL_MONITOR _monitor;

        private Ddc(PHYSICAL_MONITOR monitor)
        {
            _monitor = monitor;
            _handle = monitor.hPhysicalMonitor;
        }

        public static IMonitor? Open(DisplayInfo display)
        {
            var hmonitor = new HMONITOR((void*)display.Handle);
            if (!PInvoke.GetNumberOfPhysicalMonitorsFromHMONITOR(hmonitor, out uint count) || count == 0) return null;
            var monitors = new PHYSICAL_MONITOR[count];
            if (!PInvoke.GetPhysicalMonitorsFromHMONITOR(hmonitor, monitors)) return null;
            // Cloned displays share one HMONITOR; the first answers for it. The rest are released.
            if (count > 1) PInvoke.DestroyPhysicalMonitors(monitors.AsSpan(1));
            return new Ddc(monitors[0]);
        }

        public bool TryRead(Vcp code, out int current, out int max)
        {
            current = max = 0;
            if (!Allowed(code)) return false;
            uint c, m;
            if (PInvoke.GetVCPFeatureAndVCPFeatureReply(_handle, (byte)code, null, &c, &m) == 0) return false;
            current = (int)c;
            max = (int)m;
            return true;
        }

        public bool TryWrite(Vcp code, int raw) => Allowed(code) && PInvoke.SetVCPFeature(_handle, (byte)code, (uint)Math.Max(0, raw)) != 0;

        // The bus refuses any other code (no input switching, power, volume, color or factory reset).
        private static bool Allowed(Vcp code) => code is Vcp.Brightness or Vcp.Contrast;

        public void Dispose()
        {
            if (_handle.IsNull) return;
            _handle = default;
            try { PInvoke.DestroyPhysicalMonitors(new[] { _monitor }); }
            catch (Exception ex) { Log.Warn("Displays: closing a monitor failed", ex); }
        }
    }
}

/// <summary>A laptop's own panel, through WMI (<c>root\wmi</c>, <c>WmiMonitorBrightness</c>).</summary>
internal static class WmiPanel
{
    public static IMonitor? Open() => null;
}
