// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Chippi.Features.SystemMonitor.Sensors;
using Chippi.Features.SystemMonitor.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.SystemMonitor.Tests;

public sealed class SettingsPageTests
{
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        if (root is ExpanderRow { Value: DependencyObject header })
            foreach (var nested in Find<T>(header)) yield return nested;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find<T>(child)) yield return nested;
    }

    private static T Control<T>(DependencyObject root, string name) where T : DependencyObject =>
        Find<T>(root).Single(c => AutomationProperties.GetName(c) == name);

    private static void Commit(TextBox box) => box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });

    private static SystemSnapshot Laptop()
    {
        var at = DateTimeOffset.UtcNow;
        return SystemSnapshot.Empty(at) with
        {
            Battery = new(ReadingStatus.Ok, new BatteryReading(BatteryState.OnBattery, 50, -5, null), at),
            ThermalZones = new(ReadingStatus.Ok, new[] { new ThermalZoneReading("zone", 45) }, at),
            Gpus = new(ReadingStatus.Ok, new[] { new GpuReading("GPU-A", "Test GPU", TemperatureC: 50) }, at),
        };
    }

    [Fact]
    public void Reading_switches_update_the_tile_choices_without_replacing_controls() => WpfTestThread.Run(() =>
    {
        var module = new SystemMonitorModule(() => new FakeSources().Sources);
        var page = new SystemMonitorSettingsPage(module);
        var tile = Control<ComboBox>(page, "Tile status");
        Assert.Equal(4, tile.Items.Count);
        Control<ToggleButton>(page, "CPU").IsChecked = false;
        Assert.False(module.Settings.IsOn(Reading.Cpu));
        Assert.Same(tile, Control<ComboBox>(page, "Tile status"));
        Assert.Equal(TileStatus.Memory, ((ComboBoxItem)tile.SelectedItem).Tag);
        foreach (string name in new[] { "Memory", "GPU", "Network" }) Control<ToggleButton>(page, name).IsChecked = false;
        Assert.Empty(tile.Items);
        Assert.False(tile.IsEnabled);
        Control<ToggleButton>(page, "CPU").IsChecked = true;
        Assert.True(tile.IsEnabled);
        Assert.Equal(TileStatus.Cpu, ((ComboBoxItem)tile.SelectedItem).Tag);
    });

    [Fact]
    public void Sliders_follow_their_alert_and_all_choices_apply_immediately() => WpfTestThread.Run(() =>
    {
        var module = new SystemMonitorModule(() => new FakeSources().Sources);
        var page = new SystemMonitorSettingsPage(module);
        page.Measure(new Size(700, double.PositiveInfinity));
        var slider = Control<Slider>(page, "CPU load threshold");
        Assert.False(slider.IsEnabled);
        Assert.All(Find<SettingRow>(page).Where(r => r.Header is "Threshold"), r => Assert.Equal(new Thickness(0), r.Margin));
        Control<ToggleButton>(page, "CPU load").IsChecked = true;
        Assert.True(slider.IsEnabled);
        slider.Value = 95;
        Assert.Equal(new PercentAlert { On = true, Percent = 95 }, module.Settings.CpuLoad);
        Assert.Equal((50d, 100d, 5d), (slider.Minimum, slider.Maximum, slider.TickFrequency));
        var disk = Control<Slider>(page, "Low disk space threshold");
        Assert.Equal((5d, 30d), (disk.Minimum, disk.Maximum));
        Control<ComboBox>(page, "Update speed").SelectedIndex = 2;
        Control<ComboBox>(page, "Repeat interval").SelectedIndex = 4;
        Control<ComboBox>(page, "Network speed").SelectedIndex = 1;
        Assert.Equal((5, 60, NetworkUnit.Bits), (module.Settings.UpdateSeconds, module.Settings.RepeatMinutes, module.Settings.NetworkSpeed));
    });

    [Fact]
    public void Conditional_rows_follow_discovered_capabilities_and_gpu_limits_validate() => WpfTestThread.Run(() =>
    {
        var module = new SystemMonitorModule(() => new FakeSources().Sources);
        var page = new SystemMonitorSettingsPage(module);
        Assert.DoesNotContain(Find<SettingRow>(page), r => r.Header is "Battery" or "Thermal zone" or "Temperature" or "Low battery" or "GPU temperature");
        page.Hardware(Laptop());
        Assert.Contains(Find<SettingRow>(page), r => r.Header == "Battery");
        Assert.Contains(Find<SettingRow>(page), r => r.Header == "Thermal zone");
        Assert.Equal((5d, 50d), (Control<Slider>(page, "Low battery threshold").Minimum, Control<Slider>(page, "Low battery threshold").Maximum));
        var toggle = Control<ToggleButton>(page, "GPU temperature");
        Assert.False(toggle.IsEnabled);
        var field = Control<TextBox>(page, "Test GPU temperature limit");
        Assert.Equal("Not set", field.Text);
        Assert.True(field.IsEnabled);
        field.Text = "39";
        Commit(field);
        Assert.Empty(module.Settings.GpuTemperature.Limits);
        Assert.Contains(Find<TextBlock>(page), b => b.Text == "40 to 110 °C" && b.Visibility == Visibility.Visible);
        field.Text = "80";
        Commit(field);
        Assert.Equal(80, module.Settings.GpuTemperature.Limits["gpu-a"]);
        Assert.True(toggle.IsEnabled);
        toggle.IsChecked = true;
        Assert.True(module.Settings.GpuTemperature.On);
        Control<ComboBox>(page, "Temperature").SelectedIndex = 1;
        Assert.Equal(TemperatureUnit.Fahrenheit, module.Settings.Temperature);
        Assert.Equal(80, module.Settings.GpuTemperature.Limits["gpu-a"]);
        field.Text = "";
        Commit(field);
        Assert.Empty(module.Settings.GpuTemperature.Limits);
        Assert.False(module.Settings.GpuTemperature.On);
        Assert.False(toggle.IsEnabled);
        page.Hardware(SystemSnapshot.Empty(DateTimeOffset.UtcNow));
        Assert.Same(field, Control<TextBox>(page, "Test GPU temperature limit"));
    });

    [Fact]
    public void Capability_discovery_keeps_only_disks_live_and_settings_unload_detaches() => WpfTestThread.Run(() =>
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chippi-settings-" + Guid.NewGuid().ToString("N"));
        var config = new Chippi.Core.Config.ConfigStore(System.IO.Path.Combine(root, "settings.json"));
        config.Load();
        var fake = new FakeSources();
        var module = new SystemMonitorModule(() => fake.Sources);
        var services = new Chippi.Core.Modules.ModuleServices { DataRoot = System.IO.Path.Combine(root, "data") };
        var registry = new Chippi.Core.Modules.ModuleRegistry(config, new Chippi.Core.Island.IslandActivityHub(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, services);
        registry.Register(module);
        SystemMonitorSettingsPage? page = null;
        try
        {
            registry.StartAsync().GetAwaiter().GetResult();
            registry.SetInstalledAsync("system-monitor", true).GetAwaiter().GetResult();
            page = new SystemMonitorSettingsPage(module);
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            bool timedOut = false;
            timer.Tick += (_, _) =>
            {
                timedOut = elapsed.Elapsed > TimeSpan.FromSeconds(5);
                if (module.Sampler!.Need == SensorNeed.Disks || timedOut) frame.Continue = false;
            };
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            timer.Stop();
            Assert.False(timedOut);
            Assert.Equal(SensorNeed.Disks, module.Sampler!.Need);
            Assert.Equal(0, fake.CpuReads);
            Assert.Equal(0, fake.NetworkReads);
            Assert.False(Control<ToggleButton>(page, "GPU temperature").IsEnabled);
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.Equal(SensorNeed.None, module.Sampler!.Need);
            int oldIndex = Control<ComboBox>(page, "Update speed").SelectedIndex;
            module.Save(module.Settings with { UpdateSeconds = 5 });
            Assert.Equal(oldIndex, Control<ComboBox>(page, "Update speed").SelectedIndex);
        }
        finally
        {
            page?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            registry.StopAllAsync().GetAwaiter().GetResult();
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    });

    [Fact]
    public void Both_themes_render_at_the_minimum_settings_width() => WpfTestThread.Run(() =>
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var original = dictionaries[0];
        try
        {
            foreach (string theme in new[] { "Dark", "Light" })
            {
                dictionaries[0] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/Themes/{theme}.xaml") };
                var module = new SystemMonitorModule(() => new FakeSources().Sources);
                var page = new SystemMonitorSettingsPage(module);
                page.Hardware(Laptop());
                foreach (double width in new[] { 508d, 768d })
                {
                    var surface = new Border { Child = page, Padding = new Thickness(24) };
                    surface.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
                    surface.Measure(new Size(width + 48, double.PositiveInfinity));
                    surface.Arrange(new Rect(0, 0, width + 48, surface.DesiredSize.Height));
                    surface.UpdateLayout();
                    foreach (var control in Find<Control>(page).Where(c => c is ComboBox or Slider or TextBox or ToggleButton))
                    {
                        var point = control.TranslatePoint(new Point(), page);
                        Assert.InRange(point.X, 0, width);
                        Assert.InRange(point.X + control.ActualWidth, 0, width + 1);
                    }
                    if (Environment.GetEnvironmentVariable("CHIPPI_MONITOR_SHOTS") is { Length: > 0 } folder)
                    {
                        System.IO.Directory.CreateDirectory(folder);
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)surface.ActualWidth, (int)Math.Ceiling(surface.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(surface);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var stream = System.IO.File.Create(System.IO.Path.Combine(folder, $"settings-{theme.ToLowerInvariant()}-{width}.png"));
                        encoder.Save(stream);
                    }
                    surface.Child = null;
                }
            }
        }
        finally { dictionaries[0] = original; }
    });}
