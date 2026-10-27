// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.InteropServices;
using PowerBattery = Windows.Devices.Power.Battery;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>Charge and state from GetSystemPowerStatus; the charge rate from Windows' aggregate battery report.</summary>
internal sealed class BatterySource : IBatterySource
{
    private const byte NoBattery = 128, UnknownStatus = 255, Charging = 8;
    private const byte AcOffline = 0, AcOnline = 1;
    private const byte UnknownPercent = 255;
    private const uint UnknownLifetime = uint.MaxValue;

    public BatteryReading? Read()
    {
        if (!GetSystemPowerStatus(out var status))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (status.BatteryFlag == UnknownStatus)
            throw new InvalidOperationException("Battery status unknown");
        if ((status.BatteryFlag & NoBattery) != 0)
            return null;
        var state = status.ACLineStatus == AcOnline
            ? (status.BatteryFlag & Charging) != 0 ? BatteryState.Charging : BatteryState.PluggedIn
            : BatteryState.OnBattery;
        int? percent = status.BatteryLifePercent == UnknownPercent ? null : status.BatteryLifePercent;
        TimeSpan? left = state == BatteryState.OnBattery && status.BatteryLifeTime != UnknownLifetime
            ? TimeSpan.FromSeconds(status.BatteryLifeTime)
            : null;
        return new BatteryReading(state, percent, Rate(), left);
    }

    private static double? Rate()
    {
        try
        {
            int? milliwatts = PowerBattery.AggregateBattery.GetReport().ChargeRateInMilliwatts;
            return milliwatts is { } mw and not 0 ? mw / 1000.0 : null;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
