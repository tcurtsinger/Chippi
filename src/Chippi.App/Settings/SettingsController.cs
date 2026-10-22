// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Shell;

namespace Chippi.App.Settings;

/// <summary>
/// WS2's composition: one settings window at a time. <see cref="ShellCommands.OpenSettings"/> opens
/// it on a page, or brings the open one forward (restoring it if minimized) and switches page.
/// </summary>
internal sealed class SettingsController
{
    private readonly ShellContext _shell;
    private SettingsWindow? _window;
    private string _lastPage = "general";

    private SettingsController(ShellContext shell) => _shell = shell;

    /// <summary>The [WS2] settings slot.</summary>
    public static void Compose(ShellContext shell)
    {
        var controller = new SettingsController(shell);
        shell.Commands.RegisterSettings(controller.Open);
#if DEBUG
        controller.SnapshotIfRequested();
#endif
    }

    private void Open(string? page)
    {
        if (_window is null)
        {
            _window = new SettingsWindow(_shell, page ?? _lastPage);
            _window.Closed += (_, _) =>
            {
                _lastPage = _window.CurrentPage;
                _window = null;
            };
            _window.Show();
        }
        else if (page is not null)
        {
            _window.Navigate(page);
        }

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

#if DEBUG
    /// <summary>
    /// Debug builds: <c>--settings-snapshot &lt;dir&gt;</c> opens Settings, renders every page in dark
    /// and light to ws2-*.png (the hub also at full length), then quits. For reviews and reports.
    /// </summary>
    private void SnapshotIfRequested()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.FindIndex(args, a => string.Equals(a, "--settings-snapshot", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length)
            _ = SnapshotAsync(args[index + 1]);
    }

    private async Task SnapshotAsync(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            await Task.Delay(1500); // let the registry start the modules
            var original = UI.Themes.ThemeManager.Mode;
            foreach (var (theme, name) in new[] { (Core.Config.ThemePreference.Dark, "dark"), (Core.Config.ThemePreference.Light, "light") })
            {
                UI.Themes.ThemeManager.Apply(theme);
                foreach (var page in SettingsWindow.Pages)
                {
                    Open(page.Id);
                    await Idle();
                    Render(_window!, System.IO.Path.Combine(directory, $"ws2-{page.Id}-{name}.png"));
                    if (page.Id == "features")
                        Render(_window!.PageContent, System.IO.Path.Combine(directory, $"ws2-features-full-{name}.png"), full: true);
                }
            }
            UI.Themes.ThemeManager.Apply(original);
            _window?.Close();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Settings snapshot failed", ex);
        }
        _shell.Commands.Quit();
    }

    private static async Task Idle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(400);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static void Render(FrameworkElement element, string path, bool full = false)
    {
        double width = element.ActualWidth, height = element.ActualHeight;
        if (full)
        {
            element.Measure(new Size(width, double.PositiveInfinity));
            height = element.DesiredSize.Height;
            element.Arrange(new Rect(0, 0, width, height));
        }
        double scale = System.Windows.Media.VisualTreeHelper.GetDpi(element).DpiScaleX;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        var backdrop = new System.Windows.Media.DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle((System.Windows.Media.Brush)element.FindResource("WindowBackgroundBrush"), null, new Rect(0, 0, width, height));
        bitmap.Render(backdrop);
        bitmap.Render(element);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
        if (full)
            element.InvalidateMeasure();
    }
#endif
}
