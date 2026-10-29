// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Flyout;
using Chippi.App.Island;
using Chippi.Core.Island;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>The real module and shell, on a fake 1280x720 monitor far off the desktop. No input injection.</summary>
public sealed class SystemMonitorSmokeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(IslandEdge.Top, false, 5)]
    [InlineData(IslandEdge.Left, false, 5)]
    [InlineData(IslandEdge.Right, false, 5)]
    [InlineData(IslandEdge.Top, true, 2)]
    [InlineData(IslandEdge.Left, true, 2)]
    [InlineData(IslandEdge.Right, true, 2)]
    public void Tile_page_settings_and_close_share_one_module_on_a_short_screen(IslandEdge edge, bool laptop, int disks) => WpfTestThread.Run(() =>
    {
        var fake = new FakeSources
        {
            Cpu = perProcessor => new CpuReading(25, 32, 3600, perProcessor ? Enumerable.Repeat(25d, 32).ToArray() : null),
            Disks = () => Enumerable.Range(0, disks).Select(i => new DiskReading(i, 5, 1024, 2048,
                [new VolumeReading($"volume-{i}", $"{(char)('C' + i)}:", 50_000_000_000, 100_000_000_000)])).ToArray(),
            Battery = () => !laptop ? null : new BatteryReading(BatteryState.OnBattery, 50, -5, TimeSpan.FromHours(1)),
            Zones = () => laptop ? [new ThermalZoneReading("zone", 45)] : [],
        };
        var module = new SystemMonitorModule(() => fake.Sources);
        var ui = new Ui();
        var settings = new Settings();
        var panel = new IslandPanelWindow();
        FrameworkElement? config = null;
        try
        {
            module.StartAsync(new ModuleContext("system-monitor", settings, NullLogger.Instance, new Island(), ui), default).GetAwaiter().GetResult();
            var tile = Assert.IsType<IslandTileContribution>(ui.Tile);
            panel.Initialize();
            panel.SetMedia(new IslandPanelTests.FakeMedia());
            panel.SetTiles(IslandTileContribution.Group([tile]));
            var area = new MonitorArea(default, new Rect(-20000, 0, 1280, 720), new Rect(-20000, 0, 1280, 672), 1);
            void Open() => panel.Open(edge, edge switch
            {
                IslandEdge.Left => new Rect(-20000, 280, 32, 112),
                IslandEdge.Right => new Rect(-18752, 280, 32, 112),
                _ => new Rect(-19416, 0, 112, 32),
            }, area);
            Open();
            Until(() => tile.Status == "CPU 25%");
            Assert.True(tile.OnScreen);
            var grid = Assert.IsType<IslandGrid>(((ContentControl)panel.FindName("Page")).Content);
            var button = Assert.Single(grid.Buttons(), b => (string)b.Tag == "system-monitor");
            Until(() => AutomationProperties.GetName(Assert.IsType<IslandGrid>(((ContentControl)panel.FindName("Page")).Content).Buttons().Single(b => (string)b.Tag == "system-monitor")).Contains("CPU 25%"));
            button = Assert.IsType<IslandGrid>(((ContentControl)panel.FindName("Page")).Content).Buttons().Single(b => (string)b.Tag == "system-monitor");
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("system-monitor", panel.CurrentPage);
            Assert.False(tile.OnScreen);
            var page = (FrameworkElement)((ContentControl)panel.FindName("Page")).Content;
            IslandPanelTests.Pump(TimeSpan.FromMilliseconds(400));
            var shape = (FrameworkElement)panel.FindName("Shape");
            Assert.Equal((520, 360), (shape.Width, shape.Height)); // one size, whatever the page holds
            Assert.Empty(Find<ScrollViewer>(page));
            var card = (FrameworkElement)panel.FindName("Card");
            var footprint = new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.ActualWidth, card.ActualHeight);
            foreach (var orbit in ((Canvas)panel.FindName("Orbit")).Children.OfType<Button>().Where(b => b.Visibility == Visibility.Visible))
                {
                    footprint.Union(new Rect(Canvas.GetLeft(orbit), Canvas.GetTop(orbit), orbit.Width, orbit.Height));
                }
            output.WriteLine($"laptop={laptop}, disks={disks}, edge={edge}: page={page.DesiredSize.Height:F2} DIP; card={card.ActualHeight:F2} DIP; card + buttons={footprint.Height:F2} DIP; top={footprint.Top:F2}, bottom={footprint.Bottom:F2} (672 DIP work area).");
            Assert.InRange(footprint.Height, 1, 672);
            Assert.InRange(footprint.Top, 0, 672);
            Assert.InRange(footprint.Bottom, 0, 672);

            Assert.Contains(Find<TextBlock>(page), b => b.Text.Contains("C:"));
            if (disks == 5 && !laptop) Assert.Contains(Find<TextBlock>(page), b => b.Text.Contains("G:"));

            // Use the actual Settings factory and controls, then return to the grid.
            config = Assert.IsAssignableFrom<FrameworkElement>(ui.SettingsFactory!(null));
            config.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Until(() => Find<ComboBox>(config).Any(c => AutomationProperties.GetName(c) == "Temperature"));
            var speed = Find<ComboBox>(config).Single(c => AutomationProperties.GetName(c) == "Update speed");
            speed.SelectedIndex = 2;
            var status = Find<ComboBox>(config).Single(c => AutomationProperties.GetName(c) == "Tile status");
            status.SelectedIndex = 1;
            Assert.Equal((5, TileStatus.Memory), (settings.Value.UpdateSeconds, settings.Value.TileStatus));
            Assert.Equal("", tile.PageNote);
            config.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            config = null;
            panel.ShowPage(null);
            Until(() => tile.Status == "Memory 25%");
            panel.CloseIsland(immediate: true);
            Until(() => module.Sampler!.Need == SensorNeed.None);
            Assert.False(tile.OnScreen);
            Open();
            Assert.Equal("system-monitor", panel.CurrentPage);
            panel.ShowPage(null);
            Until(() => tile.OnScreen && tile.Status == "Memory 25%");
        }
        finally
        {
            config?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            panel.Close();
            module.StopAsync(default).GetAwaiter().GetResult();
        }
        Assert.True(fake.Disposed);
    });

    private static void Until(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
            IslandPanelTests.Pump(TimeSpan.FromMilliseconds(10));
        Assert.True(condition(), "The fake monitor did not reach the expected UI state within 5 seconds.");
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Find<T>(child)) yield return item;
    }

    private sealed class Island : IIslandPublisher
    {
        public void Publish(IslandActivity activity) { }
        public void Clear(string key) { }
        public void ReportSuccess(string? message = null) => throw new InvalidOperationException("No celebrations in a monitor smoke test.");
    }

    private sealed class Ui : IModuleUi
    {
        public IslandTileContribution? Tile;
        public Func<string?, object>? SettingsFactory;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => Tile = new("system-monitor", spec);
        public void SetSettingsPage(Func<string?, object> createView) => SettingsFactory = createView;
    }

    private sealed class Settings : IModuleSettings
    {
        public SystemMonitorSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (SystemMonitorSettings)(object)value;
    }
}
