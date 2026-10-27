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

namespace Chippi.Island.Tests;

/// <summary>The real module and shell, on a fake 1280x720 monitor far off the desktop. No input injection.</summary>
public sealed class SystemMonitorSmokeTests
{
    [Theory]
    [InlineData(false, false, false, 5)]
    [InlineData(false, true, false, 5)]
    [InlineData(true, false, false, 5)]
    [InlineData(false, false, true, 1)]
    [InlineData(true, false, true, 1)]
    [InlineData(false, false, true, 5)]
    public void Tile_page_settings_and_close_share_one_module_on_a_short_screen(bool docked, bool side, bool laptop, int disks) => WpfTestThread.Run(() =>
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
            void Open()
            {
                if (docked)
                    panel.OpenDocked(new TrayDisplay(new PixelRect(-20000, 0, -18720, 720),
                        new PixelRect(-20000, 0, -18720, 672), TaskbarEdge.Bottom, 672, -18760, 96));
                else
                    panel.Open(side ? IslandEdge.Left : IslandEdge.Top,
                        side ? new Rect(-20000, 280, 32, 112) : new Rect(-19416, 0, 112, 32), area);
            }
            Open();
            Until(() => tile.Status == "CPU 25%");
            Assert.True(tile.OnScreen);
            var grid = Assert.IsType<IslandGrid>(((ContentControl)panel.FindName("Page")).Content);
            var button = Assert.Single(grid.Buttons(), b => (string)b.Tag == "system-monitor");
            Until(() => AutomationProperties.GetName(Assert.IsType<IslandGrid>(((ContentControl)panel.FindName("Page")).Content).Buttons().Single(b => (string)b.Tag == "system-monitor")).Contains("CPU 25%"));
            button = Assert.IsType<IslandGrid>(((ContentControl)panel.FindName("Page")).Content).Buttons().Single(b => (string)b.Tag == "system-monitor");
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Until(() => Find<Button>((DependencyObject)((ContentControl)panel.FindName("Page")).Content)
                .Any(b => AutomationProperties.GetName(b) == "32 logical processors"));
            Assert.Equal("system-monitor", panel.CurrentPage);
            Assert.False(tile.OnScreen);
            Assert.Equal(docked, panel.IsDocked);
            var page = (FrameworkElement)((ContentControl)panel.FindName("Page")).Content;
            var processors = Find<Button>(page).Single(b => AutomationProperties.GetName(b) == "32 logical processors");
            processors.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Until(() => module.Sampler!.Latest.Cpu.Value?.PerProcessor?.Count == 32);
            IslandPanelTests.Pump(TimeSpan.FromMilliseconds(400));
            var shape = (FrameworkElement)panel.FindName("Shape");
            Assert.Equal(640, shape.Width, 3);
            Assert.InRange(page.DesiredSize.Height, 1, 549.999);
            Assert.Empty(Find<ScrollViewer>(page));
            var root = (Canvas)panel.FindName("Root");
            var card = (FrameworkElement)panel.FindName("Card");
            Assert.InRange(Canvas.GetTop(card), 0, root.Height - card.ActualHeight);
            if (!docked)
                foreach (var orbit in ((Canvas)panel.FindName("Orbit")).Children.OfType<Button>().Where(b => b.Visibility == Visibility.Visible))
                    Assert.InRange(Canvas.GetTop(orbit), 0, root.Height - orbit.Height);
            if (docked)
            {
                Assert.Equal(Visibility.Visible, ((FrameworkElement)panel.FindName("Footer")).Visibility);
                Assert.Contains(Find<Button>((DependencyObject)panel.FindName("FooterButtons")), b => AutomationProperties.GetName(b) == "All features");
            }
            Assert.Contains(Find<TextBlock>(page), b => b.Text.Contains("C:"));
            if (disks == 5) Assert.Contains(Find<TextBlock>(page), b => b.Text.Contains("G:"));

            // Use the actual Settings factory and controls, then return to the grid.
            config = Assert.IsAssignableFrom<FrameworkElement>(ui.SettingsFactory!(null));
            config.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Until(() => Find<ComboBox>(config).Any(c => AutomationProperties.GetName(c) == "Temperature"));
            var speed = Find<ComboBox>(config).Single(c => AutomationProperties.GetName(c) == "Update speed");
            speed.SelectedIndex = 2;
            var status = Find<ComboBox>(config).Single(c => AutomationProperties.GetName(c) == "Tile status");
            status.SelectedIndex = 1;
            Assert.Equal((5, TileStatus.Memory), (settings.Value.UpdateSeconds, settings.Value.TileStatus));
            Assert.Equal("Last 10 min", tile.PageNote);
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
