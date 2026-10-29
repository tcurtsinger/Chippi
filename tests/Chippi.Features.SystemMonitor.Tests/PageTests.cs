// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Views;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.SystemMonitor.Tests;

/// <summary>The page with fake sources, on a WPF thread, without a window (Karl's review of 6b0b142).</summary>
public sealed class PageTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static (SystemMonitorModule Module, SystemMonitorView View) Start()
    {
        var module = new SystemMonitorModule(() => new FakeSources().Sources);
        var ui = new Ui();
        module.StartAsync(new ModuleContext("system-monitor", new Settings(), NullLogger.Instance, new Island(), ui), default).GetAwaiter().GetResult();
        return (module, (SystemMonitorView)ui.Spec!.CreateSection());
    }

    private static SystemSnapshot Snapshot(ReadingStatus cpuStatus, CpuReading? cpu, GpuReading gpu, BatteryReading battery) =>
        SystemSnapshot.Empty(At) with
        {
            Cpu = new(cpuStatus, cpu, At),
            Gpus = new(ReadingStatus.Ok, [gpu], At),
            Battery = new(ReadingStatus.Ok, battery, At),
        };

    // The table values use 12 DIP semibold tabular figures.
    private static List<string> Headlines(DependencyObject root)
    {
        var found = new List<string>();
        if (root is TextBlock { FontSize: 12, FontWeight: var weight, Text.Length: > 0 } text && weight == FontWeights.SemiBold)
            found.Add(text.Text);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            found.AddRange(Headlines(child));
        return found;
    }

    [Fact]
    public void A_missing_reading_clears_its_headline_and_a_new_one_brings_it_back()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            WpfTestThread.Run(() =>
            {
                var (module, view) = Start();
                try
                {
                    view.Show(Snapshot(ReadingStatus.Ok, new CpuReading(25, 4, null), new GpuReading("dGPU", "RTX", Load: 31),
                        new BatteryReading(BatteryState.OnBattery, 50, null, null)));
                    Assert.Equal(["25%", "31%", "50%"], Headlines(view));

                    view.Show(Snapshot(ReadingStatus.Pending, null, new GpuReading("dGPU", "RTX", TemperatureC: 50),
                        new BatteryReading(BatteryState.OnBattery, null, null, null)));
                    Assert.Empty(Headlines(view));

                    view.Show(Snapshot(ReadingStatus.Unavailable, null, new GpuReading("dGPU", "RTX", Load: 7),
                        new BatteryReading(BatteryState.Charging, 51, null, null)));
                    Assert.Equal(["7%", "51%"], Headlines(view));
                }
                finally { module.StopAsync(default).GetAwaiter().GetResult(); }
            });
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void A_page_unloading_after_its_module_stopped_lets_go_cleanly()
    {
        WpfTestThread.Run(() =>
        {
            var (module, view) = Start();
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.NotEqual(SensorNeed.None, module.Sampler!.Need);

            module.StopAsync(default).GetAwaiter().GetResult();

            Assert.Null(Record.Exception(() => view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent))));
        });
    }

    private sealed class Ui : IModuleUi
    {
        public IslandTileSpec? Spec;
        public IIslandTile AddIslandTile(IslandTileSpec spec) { Spec = spec; return new IslandTileContribution("system-monitor", spec); }
        public void SetSettingsPage(Func<string?, object> createView) { }
    }

    private sealed class Settings : IModuleSettings
    {
        public T Get<T>() where T : class, new() => new();
        public void Save<T>(T value) where T : class { }
    }

    private sealed class Island : IIslandPublisher
    {
        public void Publish(IslandActivity activity) { }
        public void Clear(string key) { }
        public void ReportSuccess(string? message = null) { }
    }
}
