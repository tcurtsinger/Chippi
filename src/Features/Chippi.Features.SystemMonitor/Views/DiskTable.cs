// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>
/// The Disks card (design notes #15): one 24 DIP row per physical disk with its active time and
/// transfer rates, and a line per volume under Free space: the letter, a used-space bar, the amount
/// free. The total size is only in the volume's accessible name. No graphs.
/// </summary>
internal static class DiskTable
{
    private const double RowHeight = 24;
    private static readonly double[] Widths = [150, 92, 104, 104];

    /// <param name="alerting">Whether a volume (by ID) is past its low disk space alert: its bar and free amount turn Caution.</param>
    public static Grid Build(IReadOnlyList<DiskReading> disks, Func<string, bool> alerting)
    {
        var grid = new Grid();
        foreach (double width in Widths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Typography.SetNumeralAlignment(grid, FontNumeralAlignment.Tabular);

        AddRow(grid, RowHeight, divider: false);
        Add(grid, 0, 0, SystemMonitorView.Header("Disks"));
        string[] headings = ["Active time", "Read speed", "Write speed", "Free space"];
        for (int i = 0; i < headings.Length; i++)
            Add(grid, 0, i + 1, Heading(headings[i]));

        foreach (var disk in disks)
        {
            int volumes = Math.Max(disk.Volumes.Count, 1);
            int row = AddRow(grid, RowHeight * volumes, divider: true);
            Add(grid, row, 0, Cell(disk.Name));
            Add(grid, row, 1, Cell(disk.ActivePercent is { } active ? Format.Percent(active) : ""));
            Add(grid, row, 2, Cell(disk.ReadBytesPerSecond is { } read ? Format.Rate(read, NetworkUnit.Bytes) : ""));
            Add(grid, row, 3, Cell(disk.WriteBytesPerSecond is { } write ? Format.Rate(write, NetworkUnit.Bytes) : ""));
            var free = new StackPanel();
            foreach (var volume in disk.Volumes)
                free.Children.Add(Volume(volume, alerting(volume.Id)));
            Add(grid, row, 4, free);
        }
        return grid;
    }

    private static int AddRow(Grid grid, double height, bool divider)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        int row = grid.RowDefinitions.Count - 1;
        if (divider)
        {
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
            line.SetResourceReference(Border.BackgroundProperty, "IslandBorderBrush");
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, Widths.Length + 1);
            grid.Children.Add(line);
        }
        return row;
    }

    private static void Add(Grid grid, int row, int column, FrameworkElement element)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static TextBlock Heading(string text)
    {
        var heading = SystemMonitorView.Status(text);
        heading.VerticalAlignment = VerticalAlignment.Center;
        return heading;
    }

    private static TextBlock Cell(string text)
    {
        var cell = SystemMonitorView.Line();
        cell.Text = text;
        cell.Height = RowHeight;
        cell.Padding = new Thickness(0, 4, 8, 0);
        cell.VerticalAlignment = VerticalAlignment.Top;
        return cell;
    }

    // "C:", a bar of the used share, then "612 GiB" right-aligned.
    private static Grid Volume(VolumeReading volume, bool caution)
    {
        var letter = SystemMonitorView.Line();
        letter.Text = volume.Letter;
        letter.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
        letter.VerticalAlignment = VerticalAlignment.Center;
        double used = volume.TotalBytes == 0 ? 0 : 1 - (double)volume.FreeBytes / volume.TotalBytes;
        var fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, caution ? "WarningBrush" : "AccentBrush");
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Child = fill };
        track.SetResourceReference(Border.BackgroundProperty, "IslandBorderBrush");
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Clamp(used, 0, 1);
        var amount = SystemMonitorView.Line();
        amount.Text = Format.Size(volume.FreeBytes);
        amount.HorizontalAlignment = HorizontalAlignment.Right;
        amount.VerticalAlignment = VerticalAlignment.Center;
        if (caution)
            amount.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        var line = new Grid
        {
            Height = RowHeight,
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(24) }, new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(58) } },
            Children = { letter, track, amount },
        };
        Grid.SetColumn(track, 1);
        Grid.SetColumn(amount, 2);
        AutomationProperties.SetName(line, $"{volume.Letter} {Format.Size(volume.FreeBytes)} free of {Format.Size(volume.TotalBytes)}");
        return line;
    }
}
