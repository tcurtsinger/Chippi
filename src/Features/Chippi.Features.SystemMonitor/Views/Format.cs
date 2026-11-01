// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// The page's numbers in DESIGN.md's units (design notes #19): binary sizes up to TiB, KiB/s then MiB/s
/// (or Kbps then Mbps), "58 °C" with a space, RPM with a thousands separator. A number that would
/// round up to the next unit is written in it ("1.0 MiB/s", never "1024 KiB/s"), and a value that isn't
/// a number (a counter's NaN) is written as nothing.
/// </summary>
internal static class Format
{
    private const double KiB = 1024, MiB = KiB * 1024, GiB = MiB * 1024, TiB = GiB * 1024;

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>"12%": whole percent, clamped to 0–100 (utility above 100 is a clock boost, not more than all of it).</summary>
    public static string Percent(double value) => double.IsFinite(value) ? Math.Clamp(Math.Round(value), 0, 100).ToString("0", Culture) + "%" : "";

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

    /// <summary>"640 KiB/s", "1.2 MiB/s"; in bits, "310 Kbps", "9.6 Mbps". <paramref name="whole"/> writes a graph's scale ("128 MiB/s", "2 Mbps").</summary>
    public static string Rate(double bytesPerSecond, NetworkUnit unit, bool whole = false)
    {
        if (!double.IsFinite(bytesPerSecond))
            return "";
        bytesPerSecond = Math.Max(bytesPerSecond, 0);
        var (small, step, smallUnit, bigUnit) = unit == NetworkUnit.Bits
            ? (bytesPerSecond * 8 / 1000, 1000.0, " Kbps", " Mbps")
            : (bytesPerSecond / KiB, 1024.0, " KiB/s", " MiB/s");
        return Round(small, 0) < step
            ? small.ToString("0", Culture) + smallUnit
            : (whole ? (small / step).ToString("0", Culture) : Number(small / step, false)) + bigUnit;
    }

    /// <summary>"58 °C" or "136 °F".</summary>
    public static string Temperature(double celsius, TemperatureUnit unit) =>
        !double.IsFinite(celsius) ? ""
        : unit == TemperatureUnit.Fahrenheit
            ? (celsius * 9 / 5 + 32).ToString("0", Culture) + " °F"
            : celsius.ToString("0", Culture) + " °C";

    /// <summary>"4.69 GHz".</summary>
    public static string Clock(double mhz) => double.IsFinite(mhz) ? (mhz / 1000).ToString("0.00", Culture) + " GHz" : "";

    /// <summary>"1,120 RPM".</summary>
    public static string Rpm(uint rpm) => rpm.ToString("N0", Culture) + " RPM";

    /// <summary>"11.8 W", without a sign: the state says which way it flows.</summary>
    public static string Watts(double watts) => double.IsFinite(watts) ? Math.Abs(watts).ToString("0.0", Culture) + " W" : "";

    /// <summary>"2 hr 40 min left", "35 min left".</summary>
    public static string TimeLeft(TimeSpan left)
    {
        int minutes = (int)Math.Round(left.TotalMinutes);
        int hours = minutes / 60;
        minutes %= 60;
        return (hours > 0 ? $"{hours} hr {minutes} min" : $"{minutes} min") + " left";
    }

    private static readonly (double Divisor, string Unit)[] SizeUnits = [(KiB, "KiB"), (MiB, "MiB"), (GiB, "GiB"), (TiB, "TiB")];

    // The largest unit the size reaches, or the next one when it would round up to 1024 of this one.
    private static (double Divisor, string Unit) SizeUnit(ulong bytes)
    {
        int i = bytes >= TiB ? 3 : bytes >= GiB ? 2 : bytes >= MiB ? 1 : 0;
        double value = bytes / SizeUnits[i].Divisor;
        if (i < 3 && Round(value, Decimals(value, false)) >= 1024)
            i++;
        return SizeUnits[i];
    }

    // One decimal below 100 ("45.1"), none above ("612"); TiB keeps two below 10 ("1.21"). Zero is
    // just "0", and a value that rounds up to the next step takes its format ("100", not "100.0").
    private static string Number(double value, bool fine) =>
        value == 0 ? "0" : value.ToString(Decimals(value, fine) switch { 2 => "0.00", 1 => "0.0", _ => "0" }, Culture);

    private static int Decimals(double value, bool fine) =>
        fine && Round(value, 2) < 10 ? 2 : Round(value, 1) < 100 ? 1 : 0;

    private static double Round(double value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
