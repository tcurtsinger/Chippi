// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chippi.Features.SystemMonitor.Tests;

public sealed class SettingsTests
{
    // ConfigStore's options.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void Defaults_match_the_design()
    {
        var s = new SystemMonitorSettings();

        Assert.Equal(2, s.UpdateSeconds);
        Assert.Equal(TileStatus.Cpu, s.TileStatus);
        Assert.All(Enum.GetValues<Reading>(), r => Assert.True(s.IsOn(r)));
        Assert.False(s.CpuLoad.On || s.Memory.On || s.LowDisk.On || s.LowBattery.On || s.GpuTemperature.On);
        Assert.Equal((90, 90, 10, 15), (s.CpuLoad.Percent, s.Memory.Percent, s.LowDisk.Percent, s.LowBattery.Percent));
        Assert.Empty(s.GpuTemperature.Limits);
        Assert.Equal(TimeSpan.FromMinutes(15), s.RepeatInterval);
        Assert.Equal(s, s.Normalized() with { Off = s.Off, GpuTemperature = s.GpuTemperature });
    }

    [Fact]
    public void Normalized_brings_every_value_into_range()
    {
        var s = new SystemMonitorSettings
        {
            UpdateSeconds = 3,
            TileStatus = (TileStatus)42,
            Off = [Reading.Gpu, Reading.Gpu, (Reading)99],
            Temperature = (TemperatureUnit)7,
            CpuLoad = new() { On = true, Percent = 93 },
            Memory = new() { Percent = 20 },
            LowDisk = new() { Percent = 50 },
            LowBattery = null!,
            GpuTemperature = new() { On = true, Limits = new() { ["PCI\\A"] = 200, ["PCI\\B"] = 10, [" "] = 80 } },
            RepeatMinutes = 7,
        }.Normalized();

        Assert.Equal(2, s.UpdateSeconds);
        Assert.Equal(TileStatus.Cpu, s.TileStatus);
        Assert.Equal([Reading.Gpu], s.Off);
        Assert.Equal(TemperatureUnit.Celsius, s.Temperature);
        Assert.Equal(new PercentAlert { On = true, Percent = 95 }, s.CpuLoad);
        Assert.Equal(50, s.Memory.Percent);
        Assert.Equal(30, s.LowDisk.Percent);
        Assert.Equal(15, s.LowBattery.Percent);
        Assert.True(s.GpuTemperature.On);
        Assert.Equal(2, s.GpuTemperature.Limits.Count);
        Assert.Equal(110, s.GpuTemperature.Limits["pci\\a"]);
        Assert.Equal(40, s.GpuTemperature.Limits["PCI\\B"]);
        Assert.Equal(15, s.RepeatMinutes);
    }

    [Theory]
    [InlineData(TileStatus.Memory, new Reading[0], TileStatus.Memory)]
    [InlineData(TileStatus.Memory, new[] { Reading.Memory }, TileStatus.Cpu)]
    [InlineData(TileStatus.Cpu, new[] { Reading.Cpu, Reading.Memory }, TileStatus.Gpu)]
    [InlineData(TileStatus.Gpu, new[] { Reading.Cpu, Reading.Memory, Reading.Gpu }, TileStatus.Network)]
    public void Tile_falls_back_to_the_first_reading_still_on(TileStatus chosen, Reading[] off, TileStatus expected)
    {
        var s = new SystemMonitorSettings { TileStatus = chosen, Off = [.. off] };

        Assert.Equal(expected, s.EffectiveTileStatus());
    }

    [Fact]
    public void Tile_has_no_status_when_its_four_readings_are_off()
    {
        var s = new SystemMonitorSettings { Off = [Reading.Cpu, Reading.Memory, Reading.Gpu, Reading.Network] };

        Assert.Null(s.EffectiveTileStatus());
    }

    [Fact]
    public void Settings_round_trip_through_the_config_format()
    {
        var s = new SystemMonitorSettings
        {
            UpdateSeconds = 5,
            TileStatus = TileStatus.Network,
            Off = [Reading.Battery],
            NetworkSpeed = NetworkUnit.Bits,
            LowDisk = new() { On = true, Percent = 20 },
            GpuTemperature = new() { On = true, Limits = new() { ["PCI\\VEN_10DE"] = 85 } },
            GpuId = "PCI\\VEN_10DE",
        };

        string json = JsonSerializer.Serialize(s, Json);
        var back = JsonSerializer.Deserialize<SystemMonitorSettings>(json, Json)!.Normalized();

        Assert.Contains("\"tileStatus\":\"network\"", json, StringComparison.Ordinal);
        Assert.Equal(5, back.UpdateSeconds);
        Assert.Equal(TileStatus.Network, back.TileStatus);
        Assert.Equal([Reading.Battery], back.Off);
        Assert.Equal(NetworkUnit.Bits, back.NetworkSpeed);
        Assert.Equal(new PercentAlert { On = true, Percent = 20 }, back.LowDisk);
        Assert.Equal(85, back.GpuTemperature.Limits["pci\\ven_10de"]);
        Assert.Equal("PCI\\VEN_10DE", back.GpuId);
    }

    [Fact]
    public void Missing_fields_keep_their_defaults()
    {
        var s = JsonSerializer.Deserialize<SystemMonitorSettings>("""{ "cpuLoad": { "on": true } }""", Json)!.Normalized();

        Assert.True(s.CpuLoad.On);
        Assert.Equal(90, s.CpuLoad.Percent);
        Assert.Equal(90, s.Memory.Percent);
        Assert.Equal(2, s.UpdateSeconds);
    }
}
