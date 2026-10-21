// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Win32;

namespace Chippi.Core.SystemIntegration;

/// <summary>"Run at startup" through the per-user Run key (HKCU, no elevation).</summary>
public sealed class StartupRegistration(string valueName = "Chippi", string runKeyPath = StartupRegistration.RunKeyPath)
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Passed by the Run entry so the app starts quietly in the tray.</summary>
    public const string StartupArgument = "--startup";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
        return key?.GetValue(valueName) is not null;
    }

    /// <param name="exePath">Defaults to the running executable.</param>
    public void SetEnabled(bool enabled, string? exePath = null)
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(runKeyPath);

        if (enabled)
        {
            exePath ??= Environment.ProcessPath;
            if (exePath is null)
                return; // nothing sensible to register
            key.SetValue(valueName, $"\"{exePath}\" {StartupArgument}");
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    /// <summary>The registered command line, or null.</summary>
    public string? GetCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
        return key?.GetValue(valueName) as string;
    }
}
