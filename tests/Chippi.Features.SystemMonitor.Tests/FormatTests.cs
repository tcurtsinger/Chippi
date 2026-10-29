// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Views;

namespace Chippi.Features.SystemMonitor.Tests;

/// <summary>The page's units (design notes #19), in English formatting.</summary>
public sealed class FormatTests : IDisposable
{
    private const ulong GiB = 1UL << 30;
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public FormatTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Theory]
    [InlineData(12.4, "12%")]
    [InlineData(112, "100%")]
    [InlineData(-1, "0%")]
    public void Percent_is_whole_and_clamped(double value, string expected) => Assert.Equal(expected, Format.Percent(value));

    [Fact]
    public void Sizes_are_binary_up_to_TiB()
    {
        Assert.Equal("612 GiB", Format.Size(612 * GiB));
        Assert.Equal("45.1 GiB", Format.Size((ulong)(45.1 * GiB)));
        Assert.Equal("1.21 TiB", Format.Size((ulong)(1.21 * 1024 * GiB)));
        Assert.Equal("52.7 MiB", Format.Size((ulong)(52.7 * (1 << 20))));
        Assert.Equal("24.1 of 61.7 GiB", Format.Of((ulong)(24.1 * GiB), (ulong)(61.7 * GiB)));
        Assert.Equal("0.2 of 16.0 GiB", Format.Of((ulong)(0.2 * GiB), 16 * GiB));
    }

    [Fact]
    public void Rates_are_KiB_then_MiB_or_Kbps_then_Mbps()
    {
        Assert.Equal("640 KiB/s", Format.Rate(640 * 1024, NetworkUnit.Bytes));
        Assert.Equal("1.2 MiB/s", Format.Rate(1.2 * 1024 * 1024, NetworkUnit.Bytes));
        Assert.Equal("0 KiB/s", Format.Rate(-5, NetworkUnit.Bytes));
        Assert.Equal("310 Kbps", Format.Rate(310_000 / 8.0, NetworkUnit.Bits));
        Assert.Equal("9.6 Mbps", Format.Rate(9_600_000 / 8.0, NetworkUnit.Bits));
    }

    [Fact]
    public void Temperatures_clock_fan_and_battery()
    {
        Assert.Equal("58 °C", Format.Temperature(58.2, TemperatureUnit.Celsius));
        Assert.Equal("136 °F", Format.Temperature(58, TemperatureUnit.Fahrenheit));
        Assert.Equal("4.69 GHz", Format.Clock(4690));
        Assert.Equal("1,120 RPM", Format.Rpm(1120));
        Assert.Equal("11.8 W", Format.Watts(-11.8));
        Assert.Equal("2 hr 40 min left", Format.TimeLeft(TimeSpan.FromMinutes(160)));
        Assert.Equal("35 min left", Format.TimeLeft(TimeSpan.FromMinutes(35)));
    }
}
