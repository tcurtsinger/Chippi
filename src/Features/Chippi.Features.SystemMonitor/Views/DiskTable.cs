// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.Features.SystemMonitor.Sensors;

namespace Chippi.Features.SystemMonitor.Views;

/// <summary>Volume space, sorted by letter. Tight mode has 22 DIP header and data rows, as in fixed-size f1.</summary>
internal static class DiskTable
{
    internal static IReadOnlyList<VolumeReading> Volumes(IReadOnlyList<DiskReading> disks) =>
        [.. disks.SelectMany(d => d.Volumes).DistinctBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v.Letter.Length == 0).ThenBy(v => v.Letter, StringComparer.OrdinalIgnoreCase)];

    public static Grid Build(IReadOnlyList<DiskReading> disks, Func<string, bool> alerting,
        double available = double.PositiveInfinity, bool island = true)
    {
        var volumes = Volumes(disks);
        bool tight = 24 + volumes.Count * 28 > available;
        double header = !island ? 40 : tight ? 22 : 24, height = !island ? 48 : tight ? 22 : 28;
        int count = double.IsPositiveInfinity(available) ? volumes.Count : Math.Max(0, (int)Math.Floor((available - header) / height));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        void AddRow(string[] values, double rowHeight, bool heading = false, bool caution = false)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowHeight) });
            if (row > 0)
            {
                var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
                line.SetResourceReference(Border.BackgroundProperty, island ? "IslandHairBrush" : "BorderBrush");
                Grid.SetRow(line, row); Grid.SetColumnSpan(line, 4); grid.Children.Add(line);
            }
            for (int column = 0; column < values.Length; column++)
            {
                var cell = SystemMonitorView.Text(values[column], island ? heading ? 11 : 12 : heading ? 12 : 14, heading || column == 0);
                if (heading && column == 0) cell.FontWeight = FontWeights.SemiBold;
                cell.TextAlignment = column == 0 ? TextAlignment.Left : TextAlignment.Right;
                cell.Margin = new Thickness(0, 0, column == 3 ? 0 : 8, 0);
                if (!island) cell.SetResourceReference(TextBlock.ForegroundProperty, heading || column == 0 ? "TextSecondaryBrush" : "TextPrimaryBrush");
                if (caution && column == 2) cell.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
                Grid.SetRow(cell, row); Grid.SetColumn(cell, column); grid.Children.Add(cell);
            }
        }
        if (available < header) return grid;
        AddRow(["Disks", "Used", "Free", "Total"], header, true);
        foreach (var volume in volumes.Take(count))
            AddRow([volume.Letter,Format.Size(volume.TotalBytes>=volume.FreeBytes ? volume.TotalBytes-volume.FreeBytes : 0),
                Format.Size(volume.FreeBytes),Format.Size(volume.TotalBytes)], height, caution: alerting(volume.Id));
        return grid;
    }

    internal static FrameworkElement Unavailable(bool island = true)
    {
        var row = new Grid { Height = 28 };
        var text = SystemMonitorView.Text("Unavailable", 12, true);
        if (!island) text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        row.Children.Add(text);
        return row;
    }
}
