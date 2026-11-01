// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Win32;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>Facts about the PC that don't change while it runs, or that Windows keeps for free.</summary>
internal static class Machine
{
    private static readonly Lazy<string?> Processor = new(() =>
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() is { Length: > 0 } name
                ? string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    });

    /// <summary>The processor's name as Windows gives it ("AMD Ryzen 9 7950X 16-Core Processor"); null when it doesn't.</summary>
    public static string? ProcessorName => Processor.Value;

    /// <summary>How long since Windows started, from its tick count.</summary>
    public static TimeSpan UpTime => TimeSpan.FromMilliseconds(Environment.TickCount64);
}
