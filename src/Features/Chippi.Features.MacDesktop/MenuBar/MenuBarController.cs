// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Actions;
using Chippi.Core.Diagnostics;
using Chippi.Core.Input;
using Chippi.Core.Island;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Actions;
using Chippi.UI.Controls;
using Chippi.UI.Displays;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>
/// The menu bars, one per display, and what they show: the app in front and its menus, the Chippi and Window menus,
/// Chippi's items and what each does on a press, the clock and the ink each display's wallpaper calls for. Lives on the UI
/// thread from start to stop.
/// </summary>
internal sealed class MenuBarController : IDisposable
{
    private readonly Dictionary<nint, MenuBarWindow> _bars = [];
    private readonly Dictionary<nint, string> _signatures = [];
    private readonly FullscreenHolds _fullscreen = new();
    private readonly FrontGate _gate = new();
    private readonly uint _self = (uint)Environment.ProcessId;
    private readonly Dispatcher _dispatcher;
    private readonly ForegroundApp _front = new();
    private readonly StatusReadings _status;
    private readonly DispatcherTimer _clock, _wallpaperPoll;
    private IReadOnlyList<AppMenuEntry> _titles = [];
    private IReadOnlyList<string> _items = [];
    private BarStates _states;
    private nint _titlesWindow;
    private bool _elevated, _disposed;

    /// <summary>The newest reading of any display: a new bar starts with it, not with a guess. Kept while the mode is off and on again.</summary>
    private static InkStrip? _lastStrip;

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
        RefreshItems();
        RefreshDisplays();
        OnFrontChanged();
        TickClock();
        OnActivities(null, EventArgs.Empty);
        _status.Start();
        _clock.Start();
        _wallpaperPoll.Start();
        // What the Start menu calls each app ("Steam", not its window's helper program): read once, off this thread, and
        // the bar names the app in front again when it's in.
        AppNames.CatalogRead += OnCatalogRead;
        AppNames.ReadCatalog();
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
            var bar = new MenuBarWindow(this, monitor, _lastStrip);
            _bars[monitor] = bar;
            bar.Show();
            bar.SetItems(_items);
            bar.SetStates(_states);
            bar.SetStatus(_status);
            bar.SetApp(_front.App.Name, _titles.Select(t => t.Text).ToArray());
            bar.SetClock(ClockText(DateTime.Now));
        }
        FullscreenChanged();
        _ = ReadInkAsync(onlyChanged: false);
    }

    private void OnCatalogRead() => _dispatcher.BeginInvoke(() =>
    {
        if (_disposed)
            return;
        _front.Reread();
    });

    /// <summary>The notch moved or changed size: the bars make room for it.</summary>
    public void NotchMoved() => ForEach(b => b.Arrange());

    // ----- The app in front -----

    private void OnFrontChanged()
    {
        if (_disposed)
            return;
        if (!_gate.Changed())
            return; // a menu of ours is open: its app stays, and the bar catches up when the menu closes
        _titlesWindow = _front.Window;
        bool desktop = _front.IsDesktop;
        // An app running as administrator takes no commands from Chippi (Windows refuses them): its menus aren't offered,
        // and the rows that act on its window are greyed out.
        _elevated = !desktop && WindowsActionPc.IsWindowElevated(_front.Window);
        _titles = desktop || _elevated ? [] : AppMenus.Titles(_front.Window);
        string[] titles = [.. _titles.Select(t => t.Text)];
        ForEach(b => b.SetApp(_front.App.Name, titles));
        FullscreenChanged();
    }

    /// <summary>
    /// Hides the bar of each display a fullscreen app covers. A bar stays hidden while that app still covers its display,
    /// whichever display the keyboard is on (FullscreenHolds).
    /// </summary>
    public void FullscreenChanged()
    {
        if (_disposed)
            return;
        nint front = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(front, out uint pid);
        bool ordinary = front != 0 && pid != _self && !FullscreenCheck.IsShell(front) && !ForegroundApp.PassesOver(Native.ClassOf(front));
        bool fullscreen = ordinary && FullscreenCheck.IsFullscreen(front);
        nint display = front == 0 ? 0 : Native.MonitorFromWindow(front, Native.MonitorDefaultToNearest);
        var hidden = _fullscreen.Update(front, fullscreen, display, ordinary, FullscreenCheck.CoversMonitor);
        ForEach(b => b.SetHidden(hidden.Contains(b.Monitor)));
    }

    public void MenuClosed()
    {
        bool missed = _gate.Closed();
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _front.Refresh();
            // What changed while the menu was open (its own row minimized or closed the window) is read now.
            if (missed && !_gate.MenuOpen)
                OnFrontChanged();
        });
    }

    // ----- Menus -----

    private IReadOnlyList<BarMenuItem> Opening(IReadOnlyList<BarMenuItem> items)
    {
        if (items.Count > 0)
            _gate.Opened();
        return items;
    }

    public IReadOnlyList<BarMenuItem> ChippiMenu(FrameworkElement anchor) => Opening(
    [
        new BarMenuItem("About this PC") { Run = () => Open("ms-settings:about") },
        new BarMenuItem("Chippi settings") { Run = () => Shell.Context.OpenSettings(null) },
        new BarMenuItem("Task Manager") { Hint = "Ctrl+Shift+Esc", Run = () => _ = Shell.PressAsync("Ctrl+Shift+Esc") },
        BarMenuItem.Separator,
        new BarMenuItem("Lock screen") { Hint = "Win+L", Run = PowerActions.Lock },
        new BarMenuItem("Sleep") { Run = () => PowerActions.Power(PowerActions.Choice.Sleep) },
        new BarMenuItem("Restart…") { Run = () => Ask(anchor, "Restart?", "Restart", () => PowerActions.Power(PowerActions.Choice.Restart)) },
        new BarMenuItem("Shut down…") { Run = () => Ask(anchor, "Shut down?", "Shut down", () => PowerActions.Power(PowerActions.Choice.ShutDown)) },
        BarMenuItem.Separator,
        new BarMenuItem("Sign out…") { Run = () => Ask(anchor, "Sign out?", "Sign out", () => PowerActions.Power(PowerActions.Choice.SignOut)) },
    ]);

    /// <summary>The question under the title that asked: the verb and its object, Cancel and the verb.</summary>
    private static void Ask(FrameworkElement anchor, string question, string? verb, Action? yes = null) =>
        BarQuestion.Ask(anchor, PlacementMode.Bottom, question, verb, yes);

    public IReadOnlyList<BarMenuItem> AppMenu(FrameworkElement anchor)
    {
        var app = _front.App;
        uint pid = _front.ProcessId;
        if (_front.IsDesktop)
            return Opening(
            [
                new BarMenuItem("New File Explorer window") { Hint = "Win+E", Run = () => _ = Shell.PressAsync("Win+E") },
                BarMenuItem.Separator,
                new BarMenuItem("Display settings") { Run = () => Open("ms-settings:display") },
                new BarMenuItem("Personalize") { Run = () => Open("ms-settings:personalization") },
            ]);
        bool can = !_elevated;
        return Opening(
        [
            new BarMenuItem($"About {app.Name}") { Run = () => Properties(app.Path) },
            new BarMenuItem("Show in Explorer") { Run = () => ShowInExplorer(app.Path) },
            BarMenuItem.Separator,
            new BarMenuItem($"Hide {app.Name}") { Enabled = can, InFront = true, Run = () => ForWindowsOf(pid, w => Shell.Window(w, WindowAction.Minimize)) },
            new BarMenuItem($"Quit {app.Name}") { Enabled = can, InFront = true, Run = () => ForWindowsOf(pid, w => Shell.Window(w, WindowAction.Close)) },
            BarMenuItem.Separator,
            new BarMenuItem("End task…") { Enabled = can, Run = () => Ask(anchor, $"End {Path.GetFileName(app.Path)}?", "End", () => EndTask(anchor, pid)) },
        ]);
    }

    /// <summary>
    /// The app's menu at <paramref name="index"/> of its titles, read as it opens. The app's own Window menu starts with the
    /// bar's Window items.
    /// </summary>
    public IReadOnlyList<BarMenuItem> AppMenuAt(int index)
    {
        if (index < 0 || index >= _titles.Count)
            return [];
        nint window = _titlesWindow;
        AppMenus.Begin(window);
        return Opening(Under(window, _titles[index]));
    }

    /// <summary>The » menu: the titles that didn't fit, each opening its own menu to the side.</summary>
    public IReadOnlyList<BarMenuItem> OverflowMenu(int shown)
    {
        nint window = _titlesWindow;
        var rest = _titles.Skip(shown).Select(title => new BarMenuItem(title.Text) { Children = () => Under(window, title) }).ToArray();
        if (rest.Length > 0)
            AppMenus.Begin(window);
        return Opening(rest);
    }

    /// <summary>What opens under one of the app's titles: its entries, after the bar's Window items when it's the app's Window menu.</summary>
    private IReadOnlyList<BarMenuItem> Under(nint window, AppMenuEntry title)
    {
        var entries = Entries(window, title.SubMenu, title.Position);
        if (!AppMenus.IsWindowTitle(title.Text))
            return entries;
        var items = WindowItems();
        return entries.Count == 0 ? items : [.. items, BarMenuItem.Separator, .. entries];
    }

    private static IReadOnlyList<BarMenuItem> Entries(nint window, nint subMenu, int position) =>
        AppMenus.Open(window, subMenu, position).Select(entry => entry.Separator ? BarMenuItem.Separator
            : entry.SubMenu != 0 ? new BarMenuItem(entry.Text) { Enabled = entry.Enabled, Children = () => Entries(window, entry.SubMenu, entry.Position) }
            : new BarMenuItem(entry.Text)
            {
                Hint = entry.Shortcut,
                Enabled = entry.Enabled,
                Checked = entry.Checked,
                InFront = true, // the app's own command: it runs with the app in front, as from the app's own menu
                Run = () =>
                {
                    if (!AppMenus.Invoke(window, entry.Id))
                        Log.Warn($"Mac-style desktop: the app didn't take {entry.Text}");
                },
            }).ToArray();

    public IReadOnlyList<BarMenuItem> WindowMenu() => Opening(WindowItems());

    /// <summary>The bar's Window items, for the window in front.</summary>
    private List<BarMenuItem> WindowItems()
    {
        nint window = _front.Window;
        bool has = !_front.IsDesktop && window != 0 && !_elevated;
        bool maximized = has && Native.IsZoomed(window);
        var here = has ? Native.MonitorFromWindow(window, Native.MonitorDefaultToNearest) : 0;
        var displays = DisplayCatalog.Connected().Where(d => d.Handle != here).ToArray();
        var zones = (Shell.Runner?.ChippiCommands() ?? []).Where(c => c.WindowAction is { Kind: ActionKind.Window, Value: nameof(WindowAction.Zone) }).ToArray();
        var items = new List<BarMenuItem>
        {
            new("Minimize") { Hint = "Win+↓", Enabled = has, InFront = true, Run = () => Shell.Window(window, WindowAction.Minimize) },
            new(maximized ? "Restore" : "Maximize") { Hint = "Win+↑", Enabled = has, InFront = true, Run = () => Shell.Window(window, WindowAction.Maximize) },
            BarMenuItem.Separator,
            new("Snap left") { Hint = "Win+←", Enabled = has, InFront = true, Run = () => Shell.Window(window, WindowAction.SnapLeft) },
            new("Snap right") { Hint = "Win+→", Enabled = has, InFront = true, Run = () => Shell.Window(window, WindowAction.SnapRight) },
        };
        if (zones.Length > 0)
            items.Add(new BarMenuItem("Snap to layout")
            {
                Enabled = has,
                Children = () => zones.Select(zone => new BarMenuItem(zone.Name) { InFront = true, Run = () => RunZone(zone.WindowAction!, window) }).ToArray(),
            });
        if (displays.Length > 0)
        {
            items.Add(BarMenuItem.Separator);
            var names = DisplayNames(displays.Select(d => (d.Friendly, d.Name)).ToArray());
            for (int i = 0; i < displays.Length; i++)
            {
                nint target = displays[i].Handle;
                items.Add(new BarMenuItem($"Move to {names[i]}") { Enabled = has, InFront = true, Run = () => MoveTo(window, target) });
            }
        }
        items.Add(BarMenuItem.Separator);
        uint pid = _front.ProcessId;
        items.Add(new BarMenuItem("Bring all to front") { Enabled = has, Run = () => BringAllToFront(pid) });
        items.Add(new BarMenuItem("Close window") { Hint = "Alt+F4", Enabled = has, InFront = true, Run = () => Shell.Window(window, WindowAction.Close) });
        return items;
    }

    /// <summary>
    /// What "Move to" calls each display: its own name ("OMEN 34c"), or its full name with its size where two share one
    /// ("DELL U2720Q (3840×2160)"), numbered when even that is the same.
    /// </summary>
    public static IReadOnlyList<string> DisplayNames(IReadOnlyList<(string Friendly, string Full)> displays)
    {
        var own = displays.Select(d => d.Friendly.Length > 0 ? d.Friendly : d.Full).ToArray();
        var names = own.Select((name, i) => own.Count(n => n == name) > 1 && displays[i].Full.Length > 0 ? displays[i].Full : name).ToArray();
        var result = (string[])names.Clone();
        for (int i = 0; i < names.Length; i++)
            if (names.Count(n => n == names[i]) > 1)
                result[i] = $"{names[i]} {names.Take(i + 1).Count(n => n == names[i])}";
        return result;
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

    private static void EndTask(FrameworkElement anchor, uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Log.Warn("Mac-style desktop: End task failed", ex);
            Ask(anchor, "End task failed", null);
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

    /// <summary>The clock's own id for <see cref="Press"/>: it isn't one of the items.</summary>
    public const string Clock = "clock";

    /// <summary>The saved items changed: the bars show them.</summary>
    public void SettingsChanged() => RefreshItems();

    private void RefreshItems()
    {
        if (_disposed)
            return;
        var shown = BarItems.Shown(Shell.BarItems(), Shell.Availability());
        _status.WantsCpu = shown.Contains(BarItems.System);
        if (shown.SequenceEqual(_items))
            return;
        _items = shown;
        ForEach(b => b.SetItems(shown));
    }

    /// <summary>
    /// A press on one of the right side's items, or on the clock: each does one thing at once. An item that opens an
    /// island page closes it on a second press.
    /// </summary>
    public void Press(string id)
    {
        if (_disposed)
            return;
        if (id == Clock)
        {
            // Windows' own notifications, where Windows opens them.
            _ = Shell.PressAsync("Win+N");
            return;
        }
        var press = BarItems.Press(id);
        switch (press.Kind)
        {
            case BarPressKind.Command:
                Run(press.Target);
                break;
            case BarPressKind.Page:
                if (!Shell.ClosePage(press.Target))
                    Shell.Context.OpenIslandPage(press.Target);
                break;
            case BarPressKind.Search:
                if (!Shell.ClosePage(press.Target))
                    Run(BarItems.SearchCommand);
                break;
            case BarPressKind.Home:
                if (!Shell.ClosePage(null))
                    Shell.OpenNotchHome();
                break;
            case BarPressKind.Record:
                // While recording, the item is the Stop button.
                if (_states.Recording is not null)
                    Run(BarItems.StopRecordingCommand);
                else if (!Shell.ClosePage(press.Target))
                    Shell.Context.OpenIslandPage(press.Target);
                break;
            case BarPressKind.Sound:
                OpenSound();
                break;
            case BarPressKind.HiddenIcons:
                _ = ToggleHiddenIconsAsync();
                break;
            case BarPressKind.Keys:
                _ = Shell.PressAsync(press.Target);
                break;
            case BarPressKind.Open:
                Open(press.Target);
                break;
        }
    }

    /// <summary>Runs one of Chippi's commands by its key, as Search and a shortcut would.</summary>
    private void Run(string key)
    {
        var command = Shell.Context.Commands().FirstOrDefault(c => c.Key == key);
        if (command is null)
        {
            Log.Warn($"Mac-style desktop: nothing runs {key}");
            return;
        }
        _ = Do();

        async Task Do()
        {
            try { await command.Run(); }
            catch (Exception ex) { Log.Warn($"Mac-style desktop: {key} failed", ex); }
        }
    }

    /// <summary>Chippi Settings on Output; on App volume without the Output feature; Windows' own sound settings without either.</summary>
    private void OpenSound()
    {
        var context = Shell.Context;
        if (context.IsFeatureRunning("audio-routing"))
            context.OpenSettingsPage("page:sound-output");
        else if (context.IsFeatureRunning("app-volume"))
            context.OpenSettingsPage("module:app-volume");
        else
            Open("ms-settings:sound");
    }

    /// <summary>
    /// The taskbar's hidden icons, Windows' own flyout, where Windows opens it: Win+B gives the hidden taskbar's
    /// notification area the keyboard and Space opens it (Enter's key-up would start the first icon's app). A second press
    /// closes it, and the app that was in front gets the keyboard back. Follows Muelle (MIT).
    /// </summary>
    private bool _openingHiddenIcons;

    private async Task ToggleHiddenIconsAsync()
    {
        if (Overflow() is var open && open != 0)
        {
            Native.SetForegroundWindow(open);
            await Shell.TapAsync(0x1B); // Esc
            return; // the first press's wait gives the keyboard back
        }
        if (_openingHiddenIcons)
            return; // a second press before the flyout shows: its keys would land on top of the first's
        _openingHiddenIcons = true;
        nint before = _front.Window;
        bool seen = false;
        try
        {
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
            // Until it has shown.
            var waited = Stopwatch.StartNew();
            while (!_disposed && !seen && waited.ElapsedMilliseconds <= 1500)
            {
                seen = Overflow() != 0;
                if (!seen)
                    await Task.Delay(20);
            }
        }
        finally
        {
            _openingHiddenIcons = false;
        }
        // And until it's gone again.
        while (!_disposed && seen && Overflow() != 0)
            await Task.Delay(100);
        // Closed with the keyboard still on the hidden taskbar (Esc, or a second press): the app that was in front gets it
        // back. Anything else that came forward meanwhile keeps it.
        if (!_disposed && IsTaskbar(Native.GetForegroundWindow()) && before != 0 && Native.IsWindow(before) && !Native.IsIconic(before))
            ForegroundRight.Take(before);
    }

    private static bool IsTaskbar(nint window) => Native.ClassOf(window) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static readonly string[] OverflowClasses = ["TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow"];

    private static nint Overflow()
    {
        foreach (string cls in OverflowClasses)
        {
            nint window = Native.FindWindow(cls, null);
            if (window != 0 && Native.IsShown(window))
                return window;
        }
        return 0;
    }

    /// <summary>The island's activities the items stand for: a muted mic, keep awake, an agent at work, a recording.</summary>
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
        var states = new BarStates(
            activities.Any(a => a.ModuleId == "mic-mute" && a.NeedsAttention),
            activities.Any(a => a.ModuleId == "keep-awake" && a.Kind == IslandActivityKind.Status),
            activities.Any(a => a.Kind == IslandActivityKind.Agent),
            activities.FirstOrDefault(a => a.Kind == IslandActivityKind.Recording)?.Title);
        if (states == _states)
            return;
        _states = states;
        ForEach(b => b.SetStates(states));
    }

    private void TickClock()
    {
        string text = ClockText(DateTime.Now);
        ForEach(b => b.SetClock(text));
        // A feature installed or removed, a battery or a second input language that came or went: the items follow.
        RefreshItems();
        // Each second, too: an app can leave fullscreen with the same window still in front, and Windows' notice of it to
        // the bar isn't one to rely on alone.
        FullscreenChanged();
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
            string? last = onlyChanged && _signatures.TryGetValue(monitor, out var known) ? known : null;
            var reading = await Task.Run(() =>
            {
                string signature = WallpaperInk.Signature(a.Monitor);
                return last == signature ? ((InkStrip, string)?)null : (WallpaperInk.Read(a.Monitor, height), signature);
            });
            if (_disposed || reading is not { } read || !_bars.TryGetValue(monitor, out var bar))
                continue;
            _signatures[monitor] = read.Item2;
            _lastStrip = read.Item1;
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
        AppNames.CatalogRead -= OnCatalogRead;
        if (Shell.Activities is { } hub)
            hub.Changed -= OnActivities;
        BarQuestion.Close();
        foreach (var bar in _bars.Values)
            bar.Close();
        _bars.Clear();
        _fullscreen.Clear();
    }
}

/// <summary>The menu bar items' glyphs: Lucide's (ISC), the ones the shared set doesn't have drawn here.</summary>
internal static class Glyphs
{
    /// <summary>Lucide's "focus": the capture selector's corners around its target.</summary>
    public static Geometry Capture { get; } = Frozen("M3,7 V5 a2,2 0 0 1 2,-2 h2 M17,3 h2 a2,2 0 0 1 2,2 v2 M21,17 v2 a2,2 0 0 1 -2,2 h-2 M7,21 H5 a2,2 0 0 1 -2,-2 v-2 M15,12 a3,3 0 1 0 -6,0 a3,3 0 1 0 6,0 Z");

    /// <summary>Lucide's "battery-medium", for the Settings row; the bar draws its own with the charge in it.</summary>
    public static Geometry Battery { get; } = Frozen("M4,6 h12 a2,2 0 0 1 2,2 v8 a2,2 0 0 1 -2,2 H4 a2,2 0 0 1 -2,-2 V8 a2,2 0 0 1 2,-2 Z M22,10.5 v3 M6,10 v4 M10,10 v4");

    /// <summary>Lucide's "languages".</summary>
    public static Geometry Language { get; } = Frozen("M5,8 l6,6 M4,14 l6,-6 l2,-3 M2,5 h12 M7,2 h1 M22,22 l-5,-10 l-5,10 M14,18 h6");

    /// <summary>An item's glyph at rest, on the bar and in Settings.</summary>
    public static Geometry Of(string id) => id switch
    {
        BarItems.Search => LucideIcons.Search,
        BarItems.Capture => Capture,
        BarItems.Clipboard => LucideIcons.ClipboardList,
        BarItems.Microphone => LucideIcons.Mic,
        BarItems.Sound => LucideIcons.Volume,
        BarItems.HiddenIcons => LucideIcons.Up,
        BarItems.Controls => LucideIcons.Sliders,
        BarItems.Record => LucideIcons.Video,
        BarItems.CopyText => LucideIcons.ScanText,
        BarItems.PickColor => LucideIcons.Pipette,
        BarItems.Scratchpad => LucideIcons.NotebookPen,
        BarItems.Shelf => LucideIcons.Inbox,
        BarItems.KeepAwake => LucideIcons.Coffee,
        BarItems.Displays => LucideIcons.Sun,
        BarItems.System => LucideIcons.Activity,
        BarItems.Agents => LucideIcons.Gauge,
        BarItems.Actions => LucideIcons.ToggleRight,
        BarItems.Network => LucideIcons.Network,
        BarItems.Battery => Battery,
        BarItems.Language => Language,
        MenuBarController.Clock => LucideIcons.Clock,
        _ => LucideIcons.LayoutGrid,
    };

    private static Geometry Frozen(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
