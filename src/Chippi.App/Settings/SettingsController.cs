// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Shell;

namespace Chippi.App.Settings;

/// <summary>
/// WS2's composition: one settings window at a time. <see cref="ShellCommands.OpenSettings"/> opens
/// it on a page (or the last page used), or brings the open one forward (restoring it if minimized)
/// and switches page.
/// </summary>
internal sealed class SettingsController
{
    private readonly ShellContext _shell;
    private readonly Action<SettingsWindow>? _beforeShow;
    private SettingsWindow? _window;

    /// <param name="beforeShow">Tests: places the window before it shows (off screen, not activated).</param>
    internal SettingsController(ShellContext shell, Action<SettingsWindow>? beforeShow = null)
    {
        _shell = shell;
        _beforeShow = beforeShow;
    }

    /// <summary>The open window, or null.</summary>
    internal SettingsWindow? Window => _window;

    /// <summary>The page showing when Settings last closed, kept across restarts (the window saves it on close); empty for none.</summary>
    private string LastPage => SettingsUi.ReadApp(_shell.Config).SettingsWindow?.Page ?? "";

    /// <summary>The [WS2] settings slot.</summary>
    public static void Compose(ShellContext shell)
    {
        var controller = new SettingsController(shell);
        shell.Commands.RegisterSettings(controller.Open);
        shell.Services.SettingsPasteTarget = () => controller.Window?.PasteTarget;
#if DEBUG
        controller.SnapshotIfRequested();
#endif
    }

    /// <summary>
    /// Opens <paramref name="page"/>, or for null (the tray icon) the last page used: the open window
    /// stays where it is, and a new one opens on the page showing when Settings last closed.
    /// </summary>
    internal void Open(string? page)
    {
        if (_window is null)
        {
            var window = _window = page is null ? new SettingsWindow(_shell, LastPage, remembered: true) : new SettingsWindow(_shell, page);
            window.Closed += (_, _) => _window = null;
            _beforeShow?.Invoke(window);
            window.Show();
        }
        else if (page is not null)
        {
            _window.Navigate(page);
        }

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        if (_beforeShow is null && !_window.Activate()) // tests never take the foreground
            Core.Diagnostics.Log.Warn($"Settings request '{page}' was shown, but Windows did not bring Settings to the foreground.");
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
            await _shell.ModulesReady; // their tiles and pages exist once they run
            var original = SettingsUi.ReadApp(_shell.Config);
            try
            {
                foreach (var accent in new[] { Core.Config.AccentChoice.FollowWindows, Core.Config.AccentChoice.Violet })
                {
                    SettingsUi.SaveApp(_shell.Config, s => s.Accent = accent);
                    foreach (var (theme, name) in new[] { (Core.Config.ThemePreference.Dark, "dark"), (Core.Config.ThemePreference.Light, "light") })
                    {
                        _window?.Close();
                        SettingsUi.SaveApp(_shell.Config, s => s.Theme = theme);
                        UI.Themes.ThemeManager.Apply(theme);
                        // The tray's open (null): the page showing when Settings closed, or the first one.
                        string last = LastPage;
                        Open(null);
                        string expected = last.Length > 0 ? last : _window!.NavigationPages.First().Id;
                        if (_window!.CurrentPage != expected)
                            throw new InvalidOperationException($"Settings opened on '{_window.CurrentPage}', not the last page used ('{expected}').");
                        Open("island");
                        Open(null);
                        if (_window.CurrentPage != "island")
                            throw new InvalidOperationException("Settings left the page it was showing.");
                        foreach (var page in _window.NavigationPages.ToList())
                        {
                            Open(page.Id);
                            await Idle();
                            string file = page.Id.Replace(':', '-');
                            Render(_window!, System.IO.Path.Combine(directory, $"r2-{file}-{name}-{accent}.png"));
                            if (page.Id == "shortcuts")
                            {
                                // The whole window, tall enough for the whole page.
                                double height = _window.Height;
                                _window.WindowState = WindowState.Normal;
                                _window.Height = 2100;
                                await Idle();
                                Render(_window, System.IO.Path.Combine(directory, $"r6-shortcuts-full-{name}-{accent}.png"));
                                var scroller = Descendants<System.Windows.Controls.ScrollViewer>(_window).FirstOrDefault(v => v.ScrollableHeight > 0);
                                if (scroller is not null)
                                {
                                    scroller.ScrollToEnd();
                                    await Idle();
                                    Render(_window, System.IO.Path.Combine(directory, $"r6-shortcuts-end-{name}-{accent}.png"));
                                }
                                double width = _window.Width;
                                _window.Width = 860;
                                _window.Height = 1300;
                                await Idle();
                                Render(_window, System.IO.Path.Combine(directory, $"r6-shortcuts-narrow-{name}-{accent}.png"));
                                // The editor's states, at full and narrow width, without recording.
                                if (Descendants<Pages.ShortcutsPage>(_window).FirstOrDefault() is { } shortcutsPage)
                                {
                                    foreach (var (w, size) in new[] { (width, "full"), (860.0, "narrow") })
                                    {
                                        _window.Width = w;
                                        _window.Height = 1300;
                                        foreach (string state in new[] { "new", "listening", "caps", "ready", "refused" })
                                        {
                                            shortcutsPage.ShowEditorForShot(state);
                                            await Idle();
                                            Render(_window, System.IO.Path.Combine(directory, $"r6-editor-{state}-{size}-{name}-{accent}.png"));
                                        }
                                    }
                                    shortcutsPage.ShowEditorForShot(null);
                                }
                                _window.Width = width;
                                _window.Height = height;
                            }
                            if (page.Id == "module:text-snippets")
                            {
                                // The narrowest window, where the trigger and Expand columns are tightest (wave 3).
                                double width = _window.Width, height = _window.Height;
                                _window.WindowState = WindowState.Normal;
                                _window.Width = 860;
                                _window.Height = 1300;
                                await Idle();
                                Render(_window, System.IO.Path.Combine(directory, $"r6-snippets-narrow-{name}-{accent}.png"));
                                _window.Width = width;
                                _window.Height = height;
                            }
                            if (page.Id == "module:downloads-organizer")
                            {
                                // The organizer's other tabs and Preview (R4).
                                foreach (string section in new[] { "rules", "history", "review" })
                                {
                                    Open($"{page.Id}/{section}");
                                    await Idle();
                                    Render(_window.PageContent, System.IO.Path.Combine(directory, $"r4-downloads-{section}-{name}-{accent}.png"), full: true);
                                    // The first two rules, each open in turn: an open rule's chevron reads Close, so the next Edit is the next rule.
                                    for (int rule = 1; section == "rules" && rule <= 2 && _window.InvokeSnapshotActionStartingWith("Edit "); rule++)
                                    {
                                        await Idle();
                                        Render(_window.PageContent, System.IO.Path.Combine(directory, $"r4-downloads-rule{rule}-{name}-{accent}.png"), full: true);
                                    }
                                    // The list right after a redraw (closing the open rule), before any animation runs:
                                    // On switches must already sit at On.
                                    if (section == "rules" && _window.InvokeSnapshotActionStartingWith("Close "))
                                        Render(_window.PageContent, System.IO.Path.Combine(directory, $"r4-downloads-redraw-{name}-{accent}.png"), full: true);
                                    // A new rule's draft open with Save and Cancel, then collapsed (Unsaved changes),
                                    // then the ask on leaving; Discard goes on to History.
                                    if (section == "rules" && _window.InvokeSnapshotActionStartingWith("Add rule"))
                                    {
                                        await Idle();
                                        Render(_window.PageContent, System.IO.Path.Combine(directory, $"r4-downloads-draft-{name}-{accent}.png"), full: true);
                                        _window.InvokeSnapshotAction("Close New rule");
                                        await Idle();
                                        Render(_window.PageContent, System.IO.Path.Combine(directory, $"r4-downloads-unsaved-{name}-{accent}.png"), full: true);
                                        Open($"{page.Id}/history");
                                        await Idle();
                                        Render(_window.PageContent, System.IO.Path.Combine(directory, $"r4-downloads-leave-{name}-{accent}.png"), full: true);
                                        _window.InvokeSnapshotAction("Discard unsaved rules");
                                        await Idle();
                                    }
                                    // History: the first run opened (every run starts collapsed), then its Undo confirm bar.
                                    if (section == "history" && _window.InvokeSnapshotActionStartingWith("Show "))
                                    {
                                        await Idle();
                                        Render(_window.PageContent, System.IO.Path.Combine(directory, $"r4-downloads-history-open-{name}-{accent}.png"), full: true);
                                        if (_window.InvokeSnapshotActionStartingWith("Undo run"))
                                        {
                                            await Idle();
                                            Render(_window.PageContent, System.IO.Path.Combine(directory, $"r4-downloads-history-confirm-{name}-{accent}.png"), full: true);
                                            _window.InvokeSnapshotAction("Cancel");
                                        }
                                    }
                                }
                            }
                            if (page.Id == "features")
                            {
                                _window.VerifyFeatureSearchSnapshot();
                                _window.ExpandSnapshotFeatures(true);
                                await Idle();
                                Render(_window.PageContent, System.IO.Path.Combine(directory, $"r2-features-full-{name}-{accent}.png"), full: true);
                                _window.ExpandSnapshotFeatures(false);
                            }
                            if (page.Id == "about")
                            {
                                foreach (string action in new[] { "View license", "View third-party notices" })
                                {
                                    _window.InvokeSnapshotAction(action);
                                    await Idle();
                                    Render(_window, System.IO.Path.Combine(directory, $"r2-{action.Replace(' ', '-')}-{name}-{accent}.png"));
                                    _window.InvokeSnapshotAction("Back");
                                }
                            }
                        }
                        _window.SearchSnapshot("accent");
                        await Idle();
                        Render(_window, System.IO.Path.Combine(directory, $"r2-search-{name}-{accent}.png"));
                        _window.InvokeSnapshotAction("Accent color");
                        if (_window.CurrentPage != "general")
                            throw new InvalidOperationException("The search result did not open General.");
                        foreach (var module in _window.NavigationPages.Where(p => p.Id.StartsWith("module:")).ToList())
                        {
                            Open(module.Id + "/history");
                            if (_window.CurrentPage != module.Id + "/history")
                                throw new InvalidOperationException("The module deep link was lost.");
                        }
                    }
                }
            }
            finally
            {
                SettingsUi.SaveApp(_shell.Config, s => { s.Accent = original.Accent; s.Theme = original.Theme; });
                UI.Themes.ThemeManager.Apply(original.Theme);
            }
            var installed = _shell.Registry.Features.FirstOrDefault(f => f.IsInstalled && f.SettingsPage is not null);
            if (installed is not null)
            {
                try
                {
                    await _shell.Registry.SetInstalledAsync(installed.Id, false);
                    await Idle();
                    if (_window!.NavigationPages.Any(p => p.Id == "module:" + installed.Id))
                        throw new InvalidOperationException("An uninstalled module stayed in navigation.");
                }
                finally
                {
                    await _shell.Registry.SetInstalledAsync(installed.Id, true);
                    await Idle();
                }
                if (!_window!.NavigationPages.Any(p => p.Id == "module:" + installed.Id))
                    throw new InvalidOperationException("A reinstalled module did not return to navigation.");
            }
            Core.Diagnostics.Log.Info("R2 snapshot checks passed: caller/default navigation, label search, deep links, module removal/reinstall, notices and license.");
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

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
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
        var drawing = new System.Windows.Media.DrawingVisual();
        using (var dc = drawing.RenderOpen())
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(element), null, new Rect(0, 0, width, height));
        bitmap.Render(drawing);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
        if (full)
            element.InvalidateMeasure();
    }
#endif
}
