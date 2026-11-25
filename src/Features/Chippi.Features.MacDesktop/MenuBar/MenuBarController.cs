// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Actions;
using Chippi.Core.Diagnostics;
using Chippi.Core.Input;
using Chippi.Core.Island;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Controls;
using Chippi.UI.Displays;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>
/// The menu bars, one per display, and what they show: the app in front and its menus, the Chippi and Window menus, the
/// status items, the clock and the ink each display's wallpaper calls for. Lives on the UI thread from start to stop.
/// </summary>
internal sealed class MenuBarController : IDisposable
{
    private readonly Dictionary<nint, MenuBarWindow> _bars = [];
    private readonly Dictionary<nint, string> _signatures = [];
    private readonly Dispatcher _dispatcher;
    private readonly ForegroundApp _front = new();
    private readonly StatusReadings _status;
    private readonly DispatcherTimer _clock, _wallpaperPoll;
    private IReadOnlyList<AppMenuEntry> _titles = [];
    private nint _titlesWindow;
    private int _menusOpen;
    private bool _disposed;

    public MacShell Shell { get; }

    public MenuBarController(MacShell shell)
    {
        Shell = shell;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _status = new StatusReadings(_dispatcher);
        _status.Changed += () => ForEach(b => b.SetStatus(_status));
        _front.Changed += OnFrontChanged;
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => TickClock(), _dispatcher);
        _wallpaperPoll = new DispatcherTimer(TimeSpan.FromSeconds(60), DispatcherPriority.Background, (_, _) => _ = ReadInkAsync(onlyChanged: true), _dispatcher);
        if (shell.Activities is { } hub)
            hub.Changed += OnActivities;
    }

    public void Start()
    {
        RefreshDisplays();
        OnFrontChanged();
        TickClock();
        OnActivities(null, EventArgs.Empty);
        _status.Start();
        _clock.Start();
        _wallpaperPoll.Start();
        _ = ReadInkAsync(onlyChanged: false);
    }

    private void ForEach(Action<MenuBarWindow> act)
    {
        foreach (var bar in _bars.Values.ToArray())
            act(bar);
    }

    /// <summary>A bar for every display, and none for a display that went.</summary>
    public void RefreshDisplays()
    {
        if (_disposed)
            return;
        var monitors = DisplayCatalog.Connected().Select(d => d.Handle).ToHashSet();
        foreach (var gone in _bars.Keys.Where(m => !monitors.Contains(m)).ToArray())
        {
            _bars[gone].Close();
            _bars.Remove(gone);
            _signatures.Remove(gone);
        }
        foreach (var monitor in monitors)
        {
            if (_bars.TryGetValue(monitor, out var existing))
            {
                existing.Place();
                continue;
            }
            var bar = new MenuBarWindow(this, monitor);
            _bars[monitor] = bar;
            bar.Show();
            bar.SetInk(BarInk.Light);
            bar.SetStatus(_status);
            bar.SetApp(_front.App.Name, _titles.Select(t => t.Text).ToArray());
            bar.SetClock(ClockText(DateTime.Now));
        }
        FullscreenChanged();
        _ = ReadInkAsync(onlyChanged: false);
    }

    /// <summary>The notch moved or changed size: the bars make room for it.</summary>
    public void NotchMoved() => ForEach(b => b.Arrange());

    // ----- The app in front -----

    private void OnFrontChanged()
    {
        if (_disposed)
            return;
        if (_menusOpen > 0)
            return; // a menu of ours is open: its app stays in front
        _titlesWindow = _front.Window;
        _titles = _front.App == AppIdentity.Desktop ? [] : AppMenus.Titles(_front.Window);
        string[] titles = [.. _titles.Select(t => t.Text)];
        ForEach(b => b.SetApp(_front.App.Name, titles));
        FullscreenChanged();
    }

    /// <summary>Hides the bar of the display a fullscreen app is in front on, and shows the others.</summary>
    public void FullscreenChanged()
    {
        nint front = Native.GetForegroundWindow();
        bool fullscreen = FullscreenCheck.IsFullscreen(front);
        nint monitor = fullscreen ? Native.MonitorFromWindow(front, Native.MonitorDefaultToNearest) : 0;
        ForEach(b => b.SetHidden(fullscreen && b.Monitor == monitor));
    }

    public void MenuClosed()
    {
        _menusOpen = Math.Max(0, _menusOpen - 1);
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => _front.Refresh());
    }

    // ----- Menus -----

    private IReadOnlyList<BarMenuItem> Opening(IReadOnlyList<BarMenuItem> items)
    {
        if (items.Count > 0)
            _menusOpen++;
        return items;
    }

    public IReadOnlyList<BarMenuItem> ChippiMenu() => Opening(
    [
        new BarMenuItem("About this PC") { Run = () => Open("ms-settings:about") },
        new BarMenuItem("Chippi settings") { Run = () => Shell.Context.OpenSettings(null) },
        new BarMenuItem("Task Manager") { Hint = "Ctrl+Shift+Esc", Run = () => _ = Shell.PressAsync("Ctrl+Shift+Esc") },
        BarMenuItem.Separator,
        new BarMenuItem("Lock screen") { Hint = "Win+L", Run = PowerActions.Lock },
        new BarMenuItem("Sleep") { Run = () => PowerActions.Power(PowerActions.Choice.Sleep) },
        new BarMenuItem("Restart…") { Run = () => Confirm("Restart?", PowerActions.Choice.Restart) },
        new BarMenuItem("Shut down…") { Run = () => Confirm("Shut down?", PowerActions.Choice.ShutDown) },
        BarMenuItem.Separator,
        new BarMenuItem("Sign out…") { Run = () => Confirm("Sign out?", PowerActions.Choice.SignOut) },
    ]);

    private static void Confirm(string question, PowerActions.Choice choice)
    {
        if (MessageBox.Show(question, "Chippi", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.Cancel) == MessageBoxResult.OK)
            PowerActions.Power(choice);
    }

    public IReadOnlyList<BarMenuItem> AppMenu()
    {
        var app = _front.App;
        nint window = _front.Window;
        uint pid = _front.ProcessId;
        if (app == AppIdentity.Desktop)
            return Opening(
            [
                new BarMenuItem("About File Explorer") { Run = () => Properties(app.Path) },
                BarMenuItem.Separator,
                new BarMenuItem("New window") { Hint = "Win+E", Run = () => _ = Shell.PressAsync("Win+E") },
            ]);
        return Opening(
        [
            new BarMenuItem($"About {app.Name}") { Run = () => Properties(app.Path) },
            new BarMenuItem("Show in Explorer") { Run = () => ShowInExplorer(app.Path) },
            BarMenuItem.Separator,
            new BarMenuItem($"Hide {app.Name}") { Run = () => ForWindowsOf(pid, w => Shell.Window(w, WindowAction.Minimize)) },
            new BarMenuItem($"Quit {app.Name}") { Hint = "Alt+F4", Run = () => ForWindowsOf(pid, w => Shell.Window(w, WindowAction.Close)) },
            BarMenuItem.Separator,
            new BarMenuItem("End task") { Run = () => EndTask(pid, app) },
        ]);
    }

    /// <summary>The app's menu at <paramref name="index"/> of its titles, read as it opens.</summary>
    public IReadOnlyList<BarMenuItem> AppMenuAt(int index)
    {
        if (index < 0 || index >= _titles.Count)
            return [];
        nint window = _titlesWindow;
        AppMenus.Begin(window);
        return Opening(Entries(window, _titles[index].SubMenu, index));
    }

    /// <summary>The » menu: the titles that didn't fit, each opening its own menu to the side.</summary>
    public IReadOnlyList<BarMenuItem> OverflowMenu(int shown)
    {
        nint window = _titlesWindow;
        var rest = _titles.Select((title, i) => (title, i)).Skip(shown).Select(t =>
            new BarMenuItem(t.title.Text) { Children = () => Entries(window, t.title.SubMenu, t.i) }).ToArray();
        if (rest.Length > 0)
            AppMenus.Begin(window);
        return Opening(rest);
    }

    private static IReadOnlyList<BarMenuItem> Entries(nint window, nint subMenu, int position) =>
        AppMenus.Open(window, subMenu, position).Select((entry, i) => entry.Separator ? BarMenuItem.Separator
            : entry.SubMenu != 0 ? new BarMenuItem(entry.Text) { Enabled = entry.Enabled, Children = () => Entries(window, entry.SubMenu, i) }
            : new BarMenuItem(entry.Text)
            {
                Hint = entry.Shortcut,
                Enabled = entry.Enabled,
                Checked = entry.Checked,
                Run = () => AppMenus.Invoke(window, entry.Id),
            }).ToArray();

    public IReadOnlyList<BarMenuItem> WindowMenu()
    {
        nint window = _front.Window;
        bool has = _front.App != AppIdentity.Desktop && window != 0;
        bool maximized = has && Native.IsZoomed(window);
        var here = has ? Native.MonitorFromWindow(window, Native.MonitorDefaultToNearest) : 0;
        var displays = DisplayCatalog.Connected().Where(d => d.Handle != here).ToArray();
        var zones = (Shell.Runner?.ChippiCommands() ?? []).Where(c => c.WindowAction is { Kind: ActionKind.Window, Value: nameof(WindowAction.Zone) }).ToArray();
        var items = new List<BarMenuItem>
        {
            new("Minimize") { Hint = "Win+↓", Enabled = has, Run = () => Shell.Window(window, WindowAction.Minimize) },
            new(maximized ? "Restore" : "Maximize") { Hint = "Win+↑", Enabled = has, Run = () => Shell.Window(window, WindowAction.Maximize) },
            BarMenuItem.Separator,
            new("Snap left") { Hint = "Win+←", Enabled = has, Run = () => Shell.Window(window, WindowAction.SnapLeft) },
            new("Snap right") { Hint = "Win+→", Enabled = has, Run = () => Shell.Window(window, WindowAction.SnapRight) },
        };
        if (zones.Length > 0)
            items.Add(new BarMenuItem("Snap to Layout")
            {
                Enabled = has,
                Children = () => zones.Select(zone => new BarMenuItem(zone.Name) { Run = () => RunZone(zone.WindowAction!, window) }).ToArray(),
            });
        if (displays.Length > 0)
        {
            items.Add(BarMenuItem.Separator);
            foreach (var display in displays)
                items.Add(new BarMenuItem($"Move to {(display.Friendly.Length > 0 ? display.Friendly : display.Name)}") { Enabled = has, Run = () => MoveTo(window, display.Handle) });
        }
        items.Add(BarMenuItem.Separator);
        items.Add(new BarMenuItem("Bring all to front") { Enabled = has, Run = () => BringAllToFront(_front.ProcessId) });
        items.Add(new BarMenuItem("Close window") { Hint = "Alt+F4", Enabled = has, Run = () => Shell.Window(window, WindowAction.Close) });
        return Opening(items);
    }

    private void RunZone(ChippiAction zone, nint window)
    {
        if (Shell.Runner is not { } runner)
            return;
        _ = Run();

        async Task Run()
        {
            try { await runner.RunAsync(zone, window); }
            catch (ActionFailure ex) { Log.Warn("Mac-style desktop: Snap to layout failed", ex); }
        }
    }

    private void MoveTo(nint window, nint monitor)
    {
        if (Shell.Pc is not { } pc || pc.CaptureWindow(window) is not { } captured)
            return;
        try { pc.MoveToDisplay(captured, monitor); }
        catch (ActionFailure ex) { Log.Warn("Mac-style desktop: Move to failed", ex); }
    }

    /// <summary>Every window of the app, restored if minimized and raised above the others, keeping their order.</summary>
    private void BringAllToFront(uint pid)
    {
        var windows = new Chippi.UI.Actions.WindowsWindowList().Read().Where(w => w.ProcessId == pid || w.AppProcessId == pid).Reverse().ToArray();
        foreach (var window in windows)
        {
            Chippi.UI.Actions.ForegroundRight.Unminimize(window.Handle);
            Native.SetWindowPos(window.Handle, 0, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZOrder);
        }
    }

    private static void ForWindowsOf(uint pid, Action<nint> act)
    {
        foreach (var window in new Chippi.UI.Actions.WindowsWindowList().Read().Where(w => w.ProcessId == pid || w.AppProcessId == pid))
            act(window.Handle);
    }

    private static void EndTask(uint pid, AppIdentity app)
    {
        string exe = Path.GetFileName(app.Path);
        if (MessageBox.Show($"End {exe}?", "Chippi", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Log.Warn("Mac-style desktop: End task failed", ex);
            MessageBox.Show("End task failed", "Chippi");
        }
    }

    private void Open(string target)
    {
        try { Shell.Pc?.Open(target); }
        catch (ActionFailure ex) { Log.Warn($"Mac-style desktop: opening {target} failed", ex); }
    }

    private void ShowInExplorer(string path)
    {
        if (Shell.Pc is not { } pc)
            return;
        try { pc.LaunchExecutable(new ExecutableLaunch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{path}\"")); }
        catch (ActionFailure ex) { Log.Warn("Mac-style desktop: Show in Explorer failed", ex); }
    }

    /// <summary>The program's Properties, on its Details tab: its name, version and maker.</summary>
    private static void Properties(string path)
    {
        if (!SHObjectProperties(0, 0x2 /* SHOP_FILEPATH */, path, "Details"))
            Log.Warn("Mac-style desktop: About failed");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(nint owner, uint type, string name, string? page);

    // ----- The right side -----

    /// <summary>
    /// The taskbar's hidden icons, Windows' own flyout: Win+B gives the hidden taskbar's notification area the keyboard and
    /// Space opens it (Enter's key-up would start the first icon's app). It opens where the taskbar would be, so it's moved
    /// under the chevron. A second click closes it. Follows Muelle (MIT).
    /// </summary>
    public async Task ToggleHiddenIconsAsync(MenuBarWindow bar, FrameworkElement chevron)
    {
        if (Overflow() is var open && open != 0)
        {
            Native.SetForegroundWindow(open);
            await Shell.TapAsync(0x1B); // Esc
            return;
        }
        var anchor = bar.Below(chevron);
        await Shell.PressAsync("Win+B");
        var pressed = Stopwatch.StartNew();
        while (!IsTaskbar(Native.GetForegroundWindow()))
        {
            if (pressed.ElapsedMilliseconds > 800)
                return; // Space would press whatever else has the keyboard
            await Task.Delay(10);
        }
        int wait = 250 - (int)pressed.ElapsedMilliseconds;
        if (wait > 0)
            await Task.Delay(wait);
        await Shell.TapAsync(0x20); // Space
        var hold = Stopwatch.StartNew();
        while (hold.ElapsedMilliseconds < 1200)
        {
            if (Overflow() is var flyout && flyout != 0)
                KeepUnder(flyout, anchor, bar.Bounds);
            await Task.Delay(10);
        }
    }

    private static bool IsTaskbar(nint window) => Native.ClassOf(window) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static nint Overflow()
    {
        foreach (string cls in new[] { "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow" })
        {
            nint window = Native.FindWindow(cls, null);
            if (window != 0 && Native.IsWindowVisible(window))
                return window;
        }
        return 0;
    }

    private static void KeepUnder(nint flyout, Point anchor, Rect bar)
    {
        if (!Native.GetWindowRect(flyout, out var rect))
            return;
        int gap = (int)Math.Round(6 * (bar.Height / MenuBarWindow.Height28));
        int left = (int)Math.Clamp(anchor.X - rect.Width / 2.0, bar.Left + gap, Math.Max(bar.Left, bar.Right - gap - rect.Width));
        int top = (int)anchor.Y + gap;
        if (rect.Left != left || rect.Top != top)
            Native.SetWindowPos(flyout, 0, left, top, 0, 0, Native.SwpNoSize | Native.SwpNoZOrder | Native.SwpNoActivate);
    }

    private void OnActivities(object? sender, EventArgs e)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnActivities(sender, e));
            return;
        }
        if (_disposed || Shell.Activities is not { } hub)
            return;
        var activities = hub.Activities;
        var badges = new List<(string, Geometry, Brush, Brush, Action)>();
        Action Opens(IslandActivity a) => () => Shell.Context.OpenIslandPage(a.TileKey ?? a.OpensTile ?? a.Key);
        if (activities.FirstOrDefault(a => a.ModuleId == "mic-mute" && a.NeedsAttention) is { } mic)
            badges.Add(("Microphone muted", LucideIcons.MicOff, Frozen(Color.FromRgb(0xC4, 0x2B, 0x1C)), Brushes.White, Opens(mic)));
        if (activities.FirstOrDefault(a => a.ModuleId == "keep-awake" && a.Kind == IslandActivityKind.Status) is { } awake)
            badges.Add(("Keep awake", LucideIcons.Coffee, AccentFill, Frozen(Color.FromRgb(0x17, 0x0F, 0x2E)), Opens(awake)));
        if (activities.FirstOrDefault(a => a.Kind == IslandActivityKind.Agent) is { } agent)
            badges.Add(("Agents", Glyphs.Sparkle, Frozen(Color.FromRgb(0xE8, 0x87, 0x5B)), Brushes.White, Opens(agent)));
        ForEach(b => b.SetBadges(badges));
    }

    private static readonly Brush AccentFill = MakeAccentFill();

    private static Brush MakeAccentFill()
    {
        var fill = new LinearGradientBrush(Color.FromRgb(0xB7, 0x8F, 0xFF), Color.FromRgb(0x82, 0xAE, 0xFF), new Point(0, 0), new Point(1, 1));
        fill.Freeze();
        return fill;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void TickClock()
    {
        string text = ClockText(DateTime.Now);
        ForEach(b => b.SetClock(text));
    }

    /// <summary>"Wed Nov 25  10:02 AM", the time as the user's region writes it.</summary>
    public static string ClockText(DateTime now) =>
        now.ToString("ddd MMM d", CultureInfo.CurrentCulture) + "  " + now.ToString("t", CultureInfo.CurrentCulture);

    // ----- Ink -----

    /// <summary>Reads each display's wallpaper off the UI thread and repaints the bars whose ink changed.</summary>
    public async Task ReadInkAsync(bool onlyChanged)
    {
        var targets = _bars.Values.Where(b => b.Bounds.Width > 0).Select(b => (b.Monitor, Area: Native.Area(b.Monitor), b.Bounds.Height)).ToArray();
        foreach (var (monitor, area, height) in targets)
        {
            if (area is not { } a)
                continue;
            var reading = await Task.Run(() =>
            {
                string signature = WallpaperInk.Signature(a.Monitor);
                if (onlyChanged && _signatures.TryGetValue(monitor, out var last) && last == signature)
                    return ((BarInk, string)?)null;
                return (WallpaperInk.Read(a.Monitor, height), signature);
            });
            if (_disposed || reading is not { } read || !_bars.TryGetValue(monitor, out var bar))
                continue;
            _signatures[monitor] = read.Item2;
            bar.SetInk(read.Item1);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _clock.Stop();
        _wallpaperPoll.Stop();
        _status.Dispose();
        _front.Dispose();
        if (Shell.Activities is { } hub)
            hub.Changed -= OnActivities;
        foreach (var bar in _bars.Values)
            bar.Close();
        _bars.Clear();
    }
}

/// <summary>Lucide glyphs the shared set doesn't have (ISC).</summary>
internal static class Glyphs
{
    public static Geometry Sparkle { get; } = Frozen("M9.937,15.5 A2,2 0 0 0 8.5,14.063 l-6.135,-1.582 a.5,.5 0 0 1 0,-.962 L8.5,9.936 A2,2 0 0 0 9.937,8.5 l1.582,-6.135 a.5,.5 0 0 1 .963,0 L14.063,8.5 A2,2 0 0 0 15.5,9.937 l6.135,1.581 a.5,.5 0 0 1 0,.964 L15.5,14.063 a2,2 0 0 0 -1.437,1.437 l-1.582,6.135 a.5,.5 0 0 1 -.963,0 Z");

    public static Geometry LayoutGrid => LucideIcons.LayoutGrid;

    private static Geometry Frozen(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
