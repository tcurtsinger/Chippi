// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Flyout;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Diagnostics;
using Windows.Win32.Foundation;

namespace Chippi.App.Tray;

/// <summary>The tray icon's display, and the icon itself in physical pixels when the shell knows it.</summary>
internal sealed record TrayAnchor(TrayDisplay Display, Rect? Icon);

/// <summary>
/// WS1's composition: the tray icon and its menu, wired to <see cref="ShellCommands"/>. The icon is a
/// door to the island's pages, not a panel of its own: left click toggles them (in the island while
/// it shows, otherwise docked by the tray), right click opens the menu. With Hide tray icon on, the
/// icon leaves the notification area while the island shows.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly ShellContext _shell;
    private readonly TrayIcon _icon;
    private readonly TrayMenuWindow _menu;
    private string? _lastFallback;
    private bool _disposed;

    private TrayController(ShellContext shell)
    {
        _shell = shell;
        _menu = new TrayMenuWindow();
        _menu.SettingsRequested += (_, _) => shell.Commands.OpenSettings();
        _menu.QuitRequested += (_, _) => shell.Commands.Quit();

        _icon = new TrayIcon("Chippi");
        _icon.Selected += (_, e) =>
        {
            _menu.Hide();
            shell.Commands.TogglePages(e.Keyboard);
        };
        _icon.ContextMenuRequested += (_, e) => _menu.ShowAt(e.X, e.Y, DescribeDisplay(_icon.GetRect()));

        shell.Commands.RegisterTray(Anchor);
        shell.Commands.IslandShowingChanged += (_, _) => ApplyVisibility();
        shell.Config.SectionChanged += (_, e) =>
        {
            if (e.Name == AppSettings.SectionName)
                Application.Current?.Dispatcher.BeginInvoke(ApplyVisibility);
        };
        ApplyVisibility();
        Application.Current.Exit += (_, _) => Dispose();
        Application.Current.Dispatcher.ShutdownStarted += (_, _) => Dispose();
    }

    /// <summary>The [WS1] slot: builds the tray and registers it. Returns true (the tray keeps the app reachable).</summary>
    public static bool Compose(ShellContext shell)
    {
        var tray = new TrayController(shell);
#if DEBUG
        tray.SnapshotIfRequested();
#endif
        return true;
    }

    private void ApplyVisibility()
    {
        if (_disposed)
            return;
        bool hide = _shell.Config.GetSection<AppSettings>(AppSettings.SectionName).HideTrayIcon && _shell.Commands.IslandShowing;
        if (_icon.Visible == !hide)
            return;
        _icon.Visible = !hide;
        if (hide)
            _menu.Hide();
        Log.Info(hide ? "Tray: icon hidden while the island shows" : "Tray: icon shown");
    }

    private TrayAnchor? Anchor()
    {
        if (!_icon.Visible)
            return null;
        var rect = _icon.GetRect();
        Rect? icon = rect is RECT r ? new Rect(r.left, r.top, r.right - r.left, r.bottom - r.top) : null;
        return new TrayAnchor(DescribeDisplay(rect), icon);
    }

    private TrayDisplay DescribeDisplay(RECT? rect)
    {
        var display = FlyoutPlacement.Describe(rect, out bool fallback);
        string? message = fallback
            ? $"Tray: anchor fallback; raw icon {(rect is RECT icon ? PixelRect.From(icon).ToString() : "unavailable")}; monitor {display.Monitor}"
            : null;
        if (message is not null && message != _lastFallback)
            Log.Info(message);
        _lastFallback = message;
        return display;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _icon.Dispose();
    }

#if DEBUG
    /// <summary>
    /// Debug builds: <c>--flyout-snapshot &lt;dir&gt;</c> opens the tray menu, renders it in dark and
    /// light to ws1-menu-*.png in that folder, then quits. For reviews and reports. The island's
    /// pages, docked by the tray too, are <c>--island-pages</c>.
    /// </summary>
    private void SnapshotIfRequested()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.FindIndex(args, a => string.Equals(a, "--flyout-snapshot", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
            return;
        _ = SnapshotAsync(args[index + 1]);
    }

    private async Task SnapshotAsync(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            await _shell.ModulesReady; // their tiles and pages exist once they run
            var original = Chippi.UI.Themes.ThemeManager.Mode;
            foreach (var (theme, name) in new[] { (ThemePreference.Dark, "dark"), (ThemePreference.Light, "light") })
            {
                Chippi.UI.Themes.ThemeManager.Apply(theme);
                var rect = _icon.GetRect();
                var display = DescribeDisplay(rect);
                int x = rect is RECT r ? (r.left + r.right) / 2 : display.Work.Right - 40;
                int y = rect is RECT r2 ? (r2.top + r2.bottom) / 2 : display.Work.Bottom + 20;
                _menu.ShowAt(x, y, display);
                await Idle();
                Render(_menu, System.IO.Path.Combine(directory, $"ws1-menu-{name}.png"));
                _menu.Hide();
            }
            Chippi.UI.Themes.ThemeManager.Apply(original);
        }
        catch (Exception ex)
        {
            Log.Error("Tray snapshot failed", ex);
        }
        _shell.Commands.Quit();
    }

    private static async Task Idle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(250);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Renders a window (card and lift) over the theme's Backdrop color.</summary>
    private static void Render(Window window, string path)
    {
        double scale = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * scale), (int)Math.Ceiling(window.ActualHeight * scale),
            96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        var backdrop = new System.Windows.Media.DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle((System.Windows.Media.Brush)window.FindResource("BackdropBrush"), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }
#endif
}
