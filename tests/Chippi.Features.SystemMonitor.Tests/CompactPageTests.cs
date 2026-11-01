// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Views;

namespace Chippi.Features.SystemMonitor.Tests;

/// <summary>The System page's small rules: names, numbers, top apps, the graphs' scales and the overview's rows.</summary>
public sealed class CompactPageTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 4080", "GeForce RTX 4080")]
    [InlineData("AMD Radeon(TM) Graphics", "Radeon Graphics")]
    [InlineData("Intel(R) Arc(TM) Graphics", "Arc Graphics")]
    [InlineData("Intel(R) UHD Graphics 770", "UHD Graphics 770")]
    [InlineData("Microsoft Basic Render Driver", "Microsoft Basic Render Driver")]
    public void Gpu_names_drop_their_makers_marks(string name, string shown) => Assert.Equal(shown, Parts.ShortName(name));

    [Theory]
    [InlineData("AMD Ryzen 9 7950X 16-Core Processor", "AMD Ryzen 9 7950X")]
    [InlineData("Intel(R) Core(TM) i7-8700K CPU @ 3.70GHz", "Intel Core i7-8700K")]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H", "Intel Core Ultra 7 155H")]
    [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", "AMD Ryzen 7 7840HS w/ Radeon 780M Graphics")]
    [InlineData("AMD Ryzen 5 5600G with Radeon Graphics", "AMD Ryzen 5 5600G")]
    [InlineData("Snapdragon(R) X Elite - X1E78100 - Qualcomm(R) Oryon(TM) CPU", "Snapdragon X Elite - X1E78100 - Qualcomm Oryon")]
    public void Processor_names_drop_marks_and_core_counts(string name, string shown) => Assert.Equal(shown, Parts.ProcessorName(name));

    [Fact]
    public void Identical_device_names_are_numbered() =>
        Assert.Equal(["GeForce RTX 4080", "GeForce RTX 4080 #2", "Radeon Graphics"], Parts.Distinct(["GeForce RTX 4080", "GeForce RTX 4080", "Radeon Graphics"]));

    [Theory]
    [InlineData("23%", "23", "%", false)]
    [InlineData("2.4 MiB/s", "2.4", "MiB/s", true)]
    [InlineData("52 °C", "52", "°C", true)]
    [InlineData("Not connected", "Not connected", "", false)]
    public void Values_split_into_number_and_unit(string value, string number, string unit, bool spaced) =>
        Assert.Equal((number, unit, spaced), Parts.Split(value));

    [Theory]
    [InlineData(12, "12 min")]
    [InlineData(192, "3 h 12 min")]
    [InlineData(2760, "1 d 22 h")]
    public void Up_time_reads_in_days_hours_or_minutes(int minutes, string shown) => Assert.Equal(shown, Parts.UpTime(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Initials_stand_in_for_an_app_without_an_icon() =>
        Assert.Equal(["GC", "DI", "7Z", "?"], new[] { "Google Chrome", "Discord", "7-Zip", "" }.Select(Parts.Initials));

    [Fact]
    public void Top_apps_add_up_each_programs_processes_and_keep_the_top_three()
    {
        IReadOnlyList<ProcessReading> processes =
        [
            new(1, "chrome.exe", "Google Chrome", @"C:\chrome.exe", 2, 100), new(2, "chrome.exe", "Google Chrome", null, 3, 300),
            new(3, "code.exe", "Visual Studio Code", null, 4, 900), new(4, "teams.exe", "Teams", null, 1, 200),
            new(5, "idle.exe", "Idle", null, 0.01, 50), new(6, "new.exe", "New", null, null, 10),
        ];
        Assert.Equal([("chrome.exe", 5d, @"C:\chrome.exe"), ("code.exe", 4d, null), ("teams.exe", 1d, null)],
            TopApps.Cpu(processes).Select(a => (a.App, a.Amount, a.Path)));
        Assert.Equal(["code.exe", "chrome.exe", "teams.exe"], TopApps.Memory(processes).Select(a => a.App));
        Assert.Equal(["teams.exe", "chrome.exe"], TopApps.Gpu(processes, new Dictionary<int, double> { [4] = 30, [1] = 5, [99] = 50 }).Select(a => a.App));
        Assert.Empty(TopApps.Gpu(processes, null));
    }

    [Theory]
    [InlineData(0, 64 * 1024)]
    [InlineData(100_000, 128 * 1024)]
    [InlineData(2.4 * 1024 * 1024, 4 * 1024 * 1024)]
    public void The_network_graph_scales_to_the_next_doubling_above_its_busiest_point(double busiest, double scale) =>
        Assert.Equal(scale, GraphScale.Rate(busiest));

    // In bits the steps read whole: 500 Kbps, then 1, 2 and 5 of each power of ten.
    [Theory]
    [InlineData(0, "500 Kbps")]
    [InlineData(80_000, "1 Mbps")]
    [InlineData(2_000_000, "20 Mbps")]
    [InlineData(100 * 1024 * 1024, "1000 Mbps")]
    [InlineData(300 * 1024 * 1024, "5000 Mbps")]
    public void In_bits_the_network_graph_scales_by_1_2_and_5(double busiest, string scale)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try { Assert.Equal(scale, Format.Rate(GraphScale.Rate(busiest, NetworkUnit.Bits), NetworkUnit.Bits, whole: true)); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(new double[0], 10)]
    [InlineData(new[] { 3.0, 4.2, 9.9 }, 10)]
    [InlineData(new[] { 4.0, 12 }, 25)]
    [InlineData(new[] { 30.0 }, 50)]
    [InlineData(new[] { 20.0, 80 }, 100)]
    public void Load_graphs_fit_their_history(double[] values, double scale)
    {
        var at = DateTimeOffset.UtcNow;
        Assert.Equal(scale, GraphScale.Load([.. values.Select(v => new HistoryPoint(at, v)), new HistoryPoint(at, null)]));
    }

    [Theory]
    [InlineData(52, 75)]
    [InlineData(20, 25)]
    [InlineData(100, 100)]
    public void Temperature_graphs_go_by_25_degrees(double hottest, double scale) => Assert.Equal(scale, GraphScale.Temperature(hottest));

    [Theory]
    [InlineData(220, 250)]
    [InlineData(40, 50)]
    public void In_fahrenheit_temperature_graphs_go_by_50_degrees(double hottest, double scale) => Assert.Equal(scale, GraphScale.Temperature(hottest, 50));

    // Rows of 20 for two drives each, up to 3, giving one up while a graph row would be under 104.
    [Theory]
    [InlineData(true, "Network", 6, 3)] // a desktop: loads 116, Network 72, Drives 100
    [InlineData(true, "Network", 8, 3)]
    [InlineData(true, "Network", 3, 2)]
    [InlineData(true, "Network Battery Temperature", 1, 1)] // a laptop
    [InlineData(true, "Network Temperature", 6, 2)] // two graph rows: Drives give one up
    [InlineData(false, "", 8, 3)]
    public void The_overviews_drives_take_the_rows_the_graphs_can_spare(bool loads, string rest, int volumes, int rows) =>
        Assert.Equal(rows, OverviewPage.DriveRows(loads, [.. rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<Detail>)], volumes));
}
