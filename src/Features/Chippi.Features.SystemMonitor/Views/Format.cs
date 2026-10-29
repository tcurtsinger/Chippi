// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// The page's numbers in DESIGN.md's units (design notes #19): binary sizes up to TiB, KiB/s then MiB/s
/// (or Kbps then Mbps), "58 °C" with a space, RPM with a thousands separator.
/// </summary>
internal static class Format
{
    private const double KiB = 1024, MiB = KiB * 1024, GiB = MiB * 1024, TiB = GiB * 1024;

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>"12%": whole percent, clamped to 0–100 (utility above 100 is a clock boost, not more than all of it).</summary>
    public static string Percent(double value) => Math.Clamp(Math.Round(value), 0, 100).ToString("0", Culture) + "%";

    /// <summary>"612 GiB", "45.1 GiB", "1.21 TiB".</summary>
    public static string Size(ulong bytes)
    {
        var (divisor, unit) = SizeUnit(bytes);
        return Number(bytes / divisor, unit == "TiB") + " " + unit;
    }

    /// <summary>"24.1 of 61.7 GiB": both in the total's unit.</summary>
    public static string Of(ulong used, ulong total)
    {
        var (divisor, unit) = SizeUnit(total);
        bool tib = unit == "TiB";
        return Number(used / divisor, tib) + " of " + Number(total / divisor, tib) + " " + unit;
    }

    /// <summary>"640 KiB/s", "1.2 MiB/s"; in bits, "310 Kbps", "9.6 Mbps".</summary>
    public static string Rate(double bytesPerSecond, NetworkUnit unit)
    {
        bytesPerSecond = Math.Max(bytesPerSecond, 0);
        if (unit == NetworkUnit.Bits)
        {
            double bits = bytesPerSecond * 8;
            return bits < 1_000_000
                ? (bits / 1000).ToString("0", Culture) + " Kbps"
                : Number(bits / 1_000_000, false) + " Mbps";
        }
        return bytesPerSecond < MiB
            ? (bytesPerSecond / KiB).ToString("0", Culture) + " KiB/s"
            : Number(bytesPerSecond / MiB, false) + " MiB/s";
    }

    /// <summary>"58 °C" or "136 °F".</summary>
    public static string Temperature(double celsius, TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit
            ? (celsius * 9 / 5 + 32).ToString("0", Culture) + " °F"
            : celsius.ToString("0", Culture) + " °C";

    /// <summary>"4.69 GHz".</summary>
    public static string Clock(double mhz) => (mhz / 1000).ToString("0.00", Culture) + " GHz";

    /// <summary>"1,120 RPM".</summary>
    public static string Rpm(uint rpm) => rpm.ToString("N0", Culture) + " RPM";

    /// <summary>"11.8 W", without a sign: the state says which way it flows.</summary>
    public static string Watts(double watts) => Math.Abs(watts).ToString("0.0", Culture) + " W";

    /// <summary>"2 hr 40 min left", "35 min left".</summary>
    public static string TimeLeft(TimeSpan left)
    {
        int minutes = (int)Math.Round(left.TotalMinutes);
        int hours = minutes / 60;
        minutes %= 60;
        return (hours > 0 ? $"{hours} hr {minutes} min" : $"{minutes} min") + " left";
    }

    private static (double Divisor, string Unit) SizeUnit(ulong bytes) =>
        bytes >= 1024 * GiB ? (TiB, "TiB") : bytes >= GiB ? (GiB, "GiB") : bytes >= MiB ? (MiB, "MiB") : (KiB, "KiB");

    // One decimal below 100 ("45.1"), none above ("612"); TiB keeps two below 10 ("1.21").
    private static string Number(double value, bool fine) =>
        value.ToString(fine && value < 10 ? "0.00" : value < 100 ? "0.0" : "0", Culture);
}
