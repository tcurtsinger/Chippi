// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Config;
using Chippi.Core.Modules;
using Chippi.UI.Themes;

namespace Chippi.Features.KeepAwake;

/// <summary>Temporary WS4-only host until WS1/WS2 land. Requires Debug build and DebugMode.</summary>
public static class KeepAwakeDebugHost
{
    public static async void Show(KeepAwakeModule module, ModuleRegistry registry, Action quit)
    {
        await registry.StartAsync();
        await registry.SetInstalledAsync("keep-awake", true);
        var page = new KeepAwakePage(module);
        var close = new Button { Content = "Close", Margin = new Thickness(24, 8, 24, 16) };
        close.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
        var panel = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        panel.Children.Add(page);
        var window = new Window { Title = "Chippi Keep awake (WS4 debug)", Width = 720, Height = 620, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.SetResourceReference(FrameworkElement.StyleProperty, "ChippiWindowStyle");
        close.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        window.Closed += (_, _) => quit();
        window.Show();
        string? directory = Environment.GetEnvironmentVariable("CHIPPI_WS4_VERIFY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(400);
            await module.ApplyAsync(new() { Mode = AwakeMode.Duration, DurationMinutes = 1, KeepDisplayOn = true });
            await module.ToggleAsync();
            await Task.Delay(300);
            panel.Children.Remove(page);
            page = new KeepAwakePage(module);
            panel.Children.Add(page);
            await Task.Delay(200);
            await CaptureRequests(directory, "on");
            Snapshot(window, Path.Combine(directory, "ws4-dark.png"));
            ThemeManager.Apply(ThemePreference.Light);
            await Task.Delay(300);
            Snapshot(window, Path.Combine(directory, "ws4-light.png"));
            await module.ToggleAsync();
            await CaptureRequests(directory, "stopped");
            await module.ToggleAsync();
            await registry.SetInstalledAsync("keep-awake", false);
            await CaptureRequests(directory, "uninstalled");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "ws4-verification-error.txt"), ex.ToString()); }
        finally { await registry.StopAllAsync(); window.Close(); }
    }

    private static async Task CaptureRequests(string directory, string state)
    {
        using var process = Process.Start(new ProcessStartInfo("powercfg", "/requests") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        File.WriteAllText(Path.Combine(directory, $"ws4-powercfg-{state}.txt"), $"Exit code: {process.ExitCode}\n{output}{error}");
    }

    private static void Snapshot(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
#endif
