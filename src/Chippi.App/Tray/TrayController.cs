// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Flyout;
using Chippi.App.Shell;
using Chippi.Core.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Chippi.App.Tray;

/// <summary>
/// WS1's composition: the tray icon, the flyout and the tray menu, wired to <see cref="ShellCommands"/>.
/// Left click toggles the flyout, right click opens the menu, a second launch shows the flyout.
/// </summary>
public sealed class TrayController : IDisposable
{
    /// <summary>
    /// A click on the icon while the flyout is open first deactivates (and hides) the flyout, then
    /// arrives as a select. A select this soon after a deactivation-hide with the cursor on the
    /// icon is that same click, and must not reopen it.
    /// </summary>
    private const long ReopenGuardMs = 500;

    private readonly ShellContext _shell;
    private readonly TrayIcon _icon;
    private readonly FlyoutWindow _flyout;
    private readonly TrayMenuWindow _menu;
    private long _deactivatedAt = long.MinValue / 2;
    private bool _deactivatedOverIcon;
    private System.Drawing.Point _lastClick;
    private bool _disposed;

    private TrayController(ShellContext shell)
    {
        _shell = shell;
        _flyout = new FlyoutWindow(shell);
        _flyout.Dismissed += OnFlyoutDismissed;
        _menu = new TrayMenuWindow();
        _menu.SettingsRequested += (_, _) => shell.Commands.OpenSettings();
        _menu.QuitRequested += (_, _) => shell.Commands.Quit();

        _icon = new TrayIcon("Chippi");
        _icon.Selected += OnSelected;
        _icon.ContextMenuRequested += OnContextMenu;

        shell.Commands.RegisterFlyout(Toggle, ShowFlyout);
        Application.Current.Exit += (_, _) => Dispose();
        Application.Current.Dispatcher.ShutdownStarted += (_, _) => Dispose();
    }

    /// <summary>The [WS1] slot: builds the tray and flyout and registers them. Returns true (the tray keeps the app reachable).</summary>
    public static bool Compose(ShellContext shell)
    {
        var tray = new TrayController(shell);
#if DEBUG
        tray.SnapshotIfRequested();
#endif
        return true;
    }

    private void Toggle()
    {
        if (_flyout.IsVisible)
            _flyout.Dismiss();
        else
            ShowFlyout();
    }

    private void ShowFlyout()
    {
        _menu.Hide();
        _flyout.ShowAnchored(DescribeDisplay);
        if (_shell.DebugMode)
            Log.Info("Flyout: shown; " + _flyout.DescribePlacement());
    }

    private TrayDisplay DescribeDisplay()
    {
        var rect = _icon.GetRect();
        var fallback = _lastClick;
        if (rect is null && fallback.X == 0 && fallback.Y == 0)
            PInvoke.GetCursorPos(out fallback);
        return FlyoutPlacement.Describe(rect, fallback);
    }

    private void OnSelected(object? sender, TrayEventArgs e)
    {
        _lastClick = new System.Drawing.Point { X = e.X, Y = e.Y };
        if (!e.Keyboard && !_flyout.IsVisible && _deactivatedOverIcon
            && Environment.TickCount64 - _deactivatedAt < ReopenGuardMs)
        {
            _deactivatedOverIcon = false;
            return;
        }
        Toggle();
    }

    private void OnFlyoutDismissed(object? sender, bool byDeactivation)
    {
        if (!byDeactivation)
            return;
        _deactivatedAt = Environment.TickCount64;
        _deactivatedOverIcon = false;
        if (_icon.GetRect() is RECT icon && PInvoke.GetCursorPos(out var cursor))
            _deactivatedOverIcon = cursor.X >= icon.left && cursor.X < icon.right && cursor.Y >= icon.top && cursor.Y < icon.bottom;
    }

    private void OnContextMenu(object? sender, TrayEventArgs e)
    {
        _lastClick = new System.Drawing.Point { X = e.X, Y = e.Y };
        _flyout.Dismiss();
        _menu.ShowAt(e.X, e.Y, DescribeDisplay());
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
    /// Debug builds: <c>--flyout-snapshot &lt;dir&gt;</c> opens the flyout and the menu, renders
    /// each in dark and light to ws1-*.png in that folder, then quits. For reviews and reports.
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
            await Task.Delay(1500); // let the registry start the modules
            var original = Chippi.UI.Themes.ThemeManager.Mode;
            foreach (var (theme, name) in new[] { (Core.Config.ThemePreference.Dark, "dark"), (Core.Config.ThemePreference.Light, "light") })
            {
                Chippi.UI.Themes.ThemeManager.Apply(theme);
                ShowFlyout();
                await Idle();
                Render(_flyout, System.IO.Path.Combine(directory, $"ws1-flyout-{name}.png"));
                if (_shell.DebugMode)
                {
                    _flyout.SelectTab(Core.Modules.FlyoutTab.Sound);
                    await Idle();
                    Render(_flyout, System.IO.Path.Combine(directory, $"ws1-flyout-placeholder-{name}.png"));
                    _flyout.SelectTab(Core.Modules.FlyoutTab.Tools);
                }
                _flyout.Dismiss();

                var display = DescribeDisplay();
                var icon = _icon.GetRect();
                int x = icon is RECT r ? (r.left + r.right) / 2 : display.Work.Right - 40;
                int y = icon is RECT r2 ? (r2.top + r2.bottom) / 2 : display.Work.Bottom + 20;
                _menu.ShowAt(x, y, display);
                await Idle();
                Render(_menu, System.IO.Path.Combine(directory, $"ws1-menu-{name}.png"));
                _menu.Hide();
            }
            Chippi.UI.Themes.ThemeManager.Apply(original);
        }
        catch (Exception ex)
        {
            Log.Error("Flyout snapshot failed", ex);
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
