// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Views;
using Chippi.Island.Tests;

namespace Chippi.Features.SystemMonitor.Tests;

public sealed class CompactPageTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;
    internal static SystemSnapshot Snapshot(int disks = 8, bool laptop = false)
    {
        var at = DateTimeOffset.UtcNow;
        return SystemSnapshot.Empty(at) with
        {
            Cpu = new(ReadingStatus.Ok, new(18, 8, 5290), at),
            Gpus = new(ReadingStatus.Ok, [new("gpu", "Graphics", Load: 23, DedicatedUsedBytes: 2 * GiB, DedicatedTotalBytes: 16 * GiB, TemperatureC: 52)], at),
            Memory = new(ReadingStatus.Ok, new(29 * GiB, 62 * GiB, 40 * GiB, 80 * GiB), at),
            Network = new(ReadingStatus.Ok, [new("net", "Ethernet", true, 1024, 0)], at),
            Disks = new(ReadingStatus.Ok, [new(0, null, null, null, [.. Enumerable.Range(0, disks).Reverse().Select(i => new VolumeReading("v" + i, ((char)('C' + i)) + ":", 4 * GiB, 10 * GiB))])], at),
            Battery = new(laptop ? ReadingStatus.Ok : ReadingStatus.NotSupported, laptop ? new(BatteryState.OnBattery, 50, -5, TimeSpan.FromHours(1)) : null, at),
            ThermalZones = new(laptop ? ReadingStatus.Ok : ReadingStatus.NotSupported, laptop ? [new("zone", 45)] : [], at)
        };
    }

    // The tile keeps CPU and memory current; the last page's GPU, network and disks fill the rest until the page's own arrive.
    [Fact]
    public void A_page_opens_with_the_last_pages_readings_and_the_tiles_current_cpu()
    {
        var at = DateTimeOffset.UtcNow;
        var tile = SystemSnapshot.Empty(at) with { Cpu = new(ReadingStatus.Ok, new(61, 8, 4000), at) };
        var last = Snapshot();

        Assert.Same(tile, SystemMonitorView.Opening(tile, null));
        var opening = SystemMonitorView.Opening(tile, last);
        Assert.Same(tile.Cpu, opening.Cpu);
        Assert.Same(last.Memory, opening.Memory); // the tile didn't read it
        Assert.Same(last.Disks, opening.Disks);
        Assert.Same(last.Network, opening.Network);
        Assert.Same(last.Gpus, opening.Gpus);
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child)) yield return nested;
    }
    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(496, 304)); view.Arrange(new Rect(0, 0, 496, view.DesiredSize.Height)); view.UpdateLayout();
    }

    [Fact]
    public void Desktop_has_seven_whole_drives_at_22_in_a_304_body() => WpfTestThread.Run(() =>
    {
        var view = new SystemMonitorView(new SystemMonitorModule());
        view.Show(Snapshot()); Layout(view);
        var table = Find<Grid>(view).Single(g => g.ColumnDefinitions.Count == 4);
        Assert.Equal(8, table.RowDefinitions.Count); // header and seven drives
        Assert.All(table.RowDefinitions, r => Assert.Equal(22, r.Height.Value));
        Assert.InRange(view.DesiredSize.Height, 1, 304);
        Assert.DoesNotContain(Find<TextBlock>(view), t => t.Text == "J:");
        Assert.Empty(Find<ScrollViewer>(view));
    });

    [Fact]
    public void Battery_and_thermal_rows_leave_out_drives_without_cutting_them() => WpfTestThread.Run(() =>
    {
        var view = new SystemMonitorView(new SystemMonitorModule());
        view.Show(Snapshot(laptop: true)); Layout(view);
        var table = Find<Grid>(view).Single(g => g.ColumnDefinitions.Count == 4);
        Assert.Equal(5, table.RowDefinitions.Count);
        Assert.True(view.DesiredSize.Height <= 304);
        Assert.Contains(Find<TextBlock>(view), t => t.Text == "Battery");
        Assert.Contains(Find<TextBlock>(view), t => t.Text == "Thermal zone");
    });

    [Fact]
    public void Disk_values_are_used_free_total_sorted_by_letter_and_only_free_warns() => WpfTestThread.Run(() =>
    {
        var table = DiskTable.Build(Snapshot(3).Disks.Value!, id => id == "v1");
        var rows = table.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) > 0).ToArray();
        Assert.Equal(["C:", "D:", "E:"], rows.Where(t => Grid.GetColumn(t) == 0).Select(t => t.Text));
        Assert.All(rows.Where(t => Grid.GetColumn(t) == 1), t => Assert.Equal(Format.Size(6 * GiB), t.Text));
        Assert.All(rows.Where(t => Grid.GetColumn(t) == 2), t => Assert.Equal(Format.Size(4 * GiB), t.Text));
        Assert.All(rows.Where(t => Grid.GetColumn(t) == 3), t => Assert.Equal(Format.Size(10 * GiB), t.Text));
        var warning = rows.Single(t => Grid.GetRow(t) == 2 && Grid.GetColumn(t) == 2);
        Assert.Same(warning.FindResource("WarningTextBrush"), warning.Foreground);
    });

    [Fact]
    public void Failed_and_restarting_readings_clear_old_values() => WpfTestThread.Run(() =>
    {
        var view = new SystemMonitorView(new SystemMonitorModule());
        var snapshot = Snapshot(1);
        view.Show(snapshot);
        Assert.Contains(Find<Run>(view), r => r.Text.Contains("KiB/s"));
        view.Show(snapshot with
        {
            Cpu = new(ReadingStatus.Pending, null, snapshot.At),
            Network = new(ReadingStatus.Pending, [new("net", "Ethernet", true, null, null)], snapshot.At)
        });
        Assert.DoesNotContain(Find<TextBlock>(view), t => t.Text == "18%");
        Assert.DoesNotContain(Find<Run>(view), r => r.Text.Contains("KiB/s"));
        view.Show(snapshot with { Cpu = new(ReadingStatus.Unavailable, null, snapshot.At), Disks = new(ReadingStatus.Unavailable, null, snapshot.At) });
        Assert.Equal(2, Find<TextBlock>(view).Count(t => t.Text == "Unavailable"));
    });

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    public void Many_thermal_zones_keep_whole_rows_and_hide_a_disk_card_that_cannot_fit(int zones) => WpfTestThread.Run(() =>
    {
        var view = new SystemMonitorView(new SystemMonitorModule());
        var snapshot = Snapshot();
        view.Show(snapshot with { ThermalZones = new(ReadingStatus.Ok,
            Enumerable.Range(0, zones).Select(i => new ThermalZoneReading("zone" + i, 45)).ToArray(), snapshot.At) });
        Layout(view);
        var table = Find<Grid>(view).Single(g => g.ColumnDefinitions.Count == 4);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)table.Parent).Visibility);
        Assert.True(view.DesiredSize.Height <= 304);
        Assert.All(Find<Grid>(view).Where(g => g.ColumnDefinitions.Count == 3), row => Assert.Equal(28, row.ActualHeight));
    });
    [Fact]
    public void Settings_keeps_every_drive_in_the_same_columns() => WpfTestThread.Run(() =>
    {
        var page = new SystemMonitorSettingsPage(new SystemMonitorModule());
        page.Hardware(Snapshot());
        var table = Find<Grid>(page).Single(g => g.ColumnDefinitions.Count == 4);
        Assert.Equal(9, table.RowDefinitions.Count);
        Assert.Contains(Find<TextBlock>(table), t => t.Text == "J:");
    });
    [Fact]
    public void Settings_readings_keep_all_thermal_zones_beyond_the_island_limit() => WpfTestThread.Run(() =>
    {
        var view = new SystemMonitorView(new SystemMonitorModule(), settingsPage: true);
        var s = Snapshot(laptop: true);
        view.Show(s with { ThermalZones = new(ReadingStatus.Ok,
            Enumerable.Range(0, 12).Select(i => new ThermalZoneReading("zone" + i, 45)).ToArray(), s.At) });
        view.Measure(new Size(768, double.PositiveInfinity));
        view.Arrange(new Rect(view.DesiredSize));
        Assert.Contains(Find<TextBlock>(view), t => t.Text == "Thermal zone 12");
        Assert.Equal(17, Find<Grid>(view).Count(g => g.ColumnDefinitions.Count == 3 && g.Height == 48));
    });

    [Fact]
    public void No_enabled_readings_leaves_no_empty_Settings_card() => WpfTestThread.Run(() =>
    {
        var module = new SystemMonitorModule();
        module.Save(module.Settings with { Off = Enum.GetValues<Reading>().ToList() });
        var view = new SystemMonitorView(module, settingsPage: true);
        view.Show(Snapshot());
        view.Measure(new Size(768, double.PositiveInfinity));
        Assert.Equal(0, view.DesiredSize.Height);
    });

}
