// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Win32;
using Windows.Devices.Enumeration;

namespace Chippi.Features.QuickActions.Windows;

/// <summary>
/// Whether apps may use the camera, as Windows' Settings › Privacy › Camera switch: this user's choice, which Chippi
/// writes, unless the PC-wide one (admins only) denies it. Whether a camera exists is kept for a minute.
/// </summary>
internal sealed class CameraAccess
{
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    private readonly Refreshed<bool> _present = new(FindAsync, TimeSpan.FromMinutes(1));

    public Task<bool> PresentAsync() => _present.GetAsync();

    public static bool Allowed => !Denied(Registry.LocalMachine) && !Denied(Registry.CurrentUser);

    public static void Set(bool on)
    {
        if (on && Denied(Registry.LocalMachine))
            throw new InvalidOperationException("Camera failed: camera access is off for everyone on this PC");
        using var key = Registry.CurrentUser.CreateSubKey(Store, writable: true);
        key.SetValue("Value", on ? "Allow" : "Deny", RegistryValueKind.String);
    }

    private static bool Denied(RegistryKey root)
    {
        using var key = root.OpenSubKey(Store);
        return string.Equals(key?.GetValue("Value") as string, "Deny", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> FindAsync() => (await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)).Count > 0;
}
