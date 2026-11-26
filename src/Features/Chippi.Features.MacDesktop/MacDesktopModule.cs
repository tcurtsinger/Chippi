// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Threading;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.MacDesktop.Dock;
using Chippi.Features.MacDesktop.MenuBar;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.MacDesktop;

/// <summary>
/// Mac-style desktop (docs/mac-style/brief.md): a menu bar across the top of every display, a dock on the main one and the
/// widget as an island in the menu bar, with the Windows taskbar hidden meanwhile. Every page is the island's own; this adds the three
/// surfaces around them. Off (Desktop style: Windows), quitting, uninstalling or a crash gives the taskbar back.
/// </summary>
public sealed class MacDesktopModule : IFeatureModule
{
    public const string Id = "mac-desktop";

    /// <summary><c>Chippi.exe --restore</c>.</summary>
    public const string RestoreVerb = "restore";

    private readonly IslandActivityHub? _activities;
    private readonly Func<bool> _canTakeTaskbar;
    private readonly Func<ITaskbarShell> _taskbars;
    private ModuleContext? _context;
    private MacShell? _shell;
    private ShellEvents? _events;
    private TaskbarTakeover? _takeover;
    private DispatcherTimer? _repair;
    private MenuBarController? _menuBar;
    private DockController? _dock;
    private Dispatcher? _dispatcher;

    /// <param name="activities">The island's activities, for the menu bar's badges.</param>
    /// <param name="canTakeTaskbar">
    /// Whether this run may hide the real taskbar: false in dev profiles and tests, which leave it as it is and still show
    /// the bars.
    /// </param>
    public MacDesktopModule(IslandActivityHub? activities = null, Func<bool>? canTakeTaskbar = null, Func<ITaskbarShell>? taskbars = null)
    {
        _activities = activities;
        _canTakeTaskbar = canTakeTaskbar ?? (() => false);
        _taskbars = taskbars ?? (() => new WindowsTaskbars());
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Mac-style desktop", "A menu bar, a dock and an island", FeatureGroup.Windows)
    {
        Icon = nameof(LucideIcons.PanelBottom),
        Presets = PresetMembership.PowerUser,
    };

    public MacDesktopSettings Settings { get; private set; } = new();

    public bool IsRunning => _context is not null;

    /// <summary>Why the taskbar couldn't be hidden, for the Settings page; null when it's fine.</summary>
    public string? Error { get; private set; }

    /// <summary>The island in the menu bar takes the widget's place: the mode is on with its menu bar and its Island switch.</summary>
    public bool IslandActive => IsRunning && Settings.ShowsIsland;

    /// <summary>Raised on the UI thread when the settings or the surfaces change.</summary>
    public event Action? Changed;

    /// <summary>Opens the home in the spot: set by the shell, which owns the panel.</summary>
    public Action OpenHome
    {
        set
        {
            (_pending ??= new()).OpenHome = value;
            if (_shell is not null) _shell.OpenHome = value;
        }
    }

    /// <summary>A press on the island: its activity's page in the spot, or the home; set by the shell.</summary>
    public Action IslandPressed
    {
        set
        {
            (_pending ??= new()).IslandPressed = value;
            if (_shell is not null) _shell.IslandPressed = value;
        }
    }

    /// <summary>The island's right-click menu (the tray icon's); set by the shell.</summary>
    public Func<IReadOnlyList<(string Label, Action Action)?>> IslandMenu
    {
        set
        {
            (_pending ??= new()).IslandMenu = value;
            if (_shell is not null) _shell.IslandMenu = value;
        }
    }

    /// <summary>A press on a bar item for an island page: whether the page was open and is closed now; set by the shell.</summary>
    public Func<string?, bool> ClosePage
    {
        set
        {
            (_pending ??= new()).ClosePage = value;
            if (_shell is not null) _shell.ClosePage = value;
        }
    }

    /// <summary>The menu bar's items as they show now, left to right.</summary>
    public IReadOnlyList<string> BarItemsShown => _shell is { } shell ? BarItems.Shown(Settings.BarItems, shell.Availability()) : [];

    /// <summary>The items Add item offers now.</summary>
    public IReadOnlyList<string> BarItemsToAdd
    {
        get
        {
            if (_shell is not { } shell)
                return [];
            var available = shell.Availability();
            return BarItems.CanAdd(BarItems.Shown(Settings.BarItems, available), available);
        }
    }

    private sealed class PendingShell
    {
        public Action? OpenHome, IslandPressed;
        public Func<string?, bool>? ClosePage;
        public Func<IReadOnlyList<(string Label, Action Action)?>>? IslandMenu;
    }

    private PendingShell? _pending;
    private BarIslandFace? _face;
    private (bool Open, string? Page, nint Monitor) _page;

    /// <summary>What the island shows now: the shell tells it as the activities change.</summary>
    public void SetIsland(BarIslandFace face)
    {
        _face = face;
        _menuBar?.SetIsland(face);
    }

    /// <summary>
    /// The page showing in the spot on <paramref name="monitor"/>'s display (null for the home), or none: that bar lights
    /// the page's icon, or Controls.
    /// </summary>
    public void SetPage(bool open, string? page, nint monitor)
    {
        _page = (open, page, monitor);
        _menuBar?.SetPage(open, page, monitor);
    }

    /// <summary>
    /// Whether a press at <paramref name="physical"/> is on the island or on a bar icon that opens a page: the panel stays
    /// as it is under it, and the press changes its page or closes it.
    /// </summary>
    public bool KeepsPanel(System.Windows.Point physical) => _menuBar?.KeepsPanel(physical) == true;

    /// <summary>The display whose bar was just pressed for a page, once: 0 when the page was asked for another way.</summary>
    public nint TakeSpot()
    {
        if (_shell is not { } shell)
            return 0;
        nint monitor = shell.PressedOn;
        shell.PressedOn = 0;
        return monitor;
    }

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Settings = context.Settings.Get<MacDesktopSettings>();
        try { Settings.Validate(); }
        catch (ArgumentException) { Settings = new(); }
        _shell = new MacShell(context, _activities);
        if (_pending is { } pending)
        {
            if (pending.OpenHome is { } home) _shell.OpenHome = home;
            if (pending.IslandPressed is { } pressed) _shell.IslandPressed = pressed;
            if (pending.IslandMenu is { } menu) _shell.IslandMenu = menu;
            if (pending.ClosePage is { } close) _shell.ClosePage = close;
        }
        _shell.IslandOn = () => Settings.ShowsIsland;
        _shell.BarItems = () => Settings.BarItems;
        // A dev profile or a test never touches the real taskbar, not even to give it back.
        _takeover = _canTakeTaskbar() ? new TaskbarTakeover(_taskbars(), new TaskbarJournal(TaskbarJournal.DefaultPath)) : null;
        _events = new ShellEvents();
        _events.ExplorerRestarted += OnExplorerRestarted;
        _events.DisplaysChanged += OnDisplaysChanged;
        _events.WallpaperChanged += () => _ = _menuBar?.ReadInkAsync(onlyChanged: false);
        context.Ui.SetSettingsPage(() => new MacDesktopRows(this));
        // Chippi.exe --restore: the Windows desktop again, taskbar and all (Program gave the taskbar back already).
        context.Activation.Register(RestoreVerb, _args =>
        {
            if (Settings.IsMac)
                _ = ApplyAsync(Settings with { Style = DesktopStyle.Windows });
        });
        context.Ui.AddCommands(() =>
        [
            new ChippiCommand("mac-desktop/toggle", Settings.IsMac ? "Windows desktop" : "Mac-style desktop", nameof(LucideIcons.PanelBottom),
                () => ApplyAsync(Settings with { Style = Settings.IsMac ? DesktopStyle.Windows : DesktopStyle.MacStyle }))
            { Words = "mac dock menu bar island taskbar desktop style" },
        ]);
        Apply();
        return Task.CompletedTask;
    }

    /// <summary>Saves new settings and brings the desktop in line with them.</summary>
    public Task ApplyAsync(MacDesktopSettings settings)
    {
        settings.Validate();
        if (_context is null)
            throw new InvalidOperationException("Mac-style desktop uninstalled");
        _context.Settings.Save(settings);
        Settings = settings;
        if (_dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            return dispatcher.InvokeAsync(Apply).Task;
        Apply();
        return Task.CompletedTask;
    }

    /// <summary>Shows and hides the surfaces and the taskbar to match <see cref="Settings"/>.</summary>
    private void Apply()
    {
        if (_context is null || _shell is null)
            return;
        Error = null;
        // The taskbar first: the bars' AppBar strips are reserved once the taskbar has let go of its own.
        if (Settings.HidesTaskbar && _takeover is not null)
        {
            if (!_takeover.TakeOver())
                Error = "Taskbar unavailable";
            StartRepair();
        }
        else
        {
            StopRepair();
            _takeover?.GiveBack();
        }

        if (Settings.IsMac && Settings.MenuBar)
        {
            if (_menuBar is null)
            {
                _menuBar = new MenuBarController(_shell);
                _menuBar.Start();
                if (_face is { } face)
                    _menuBar.SetIsland(face);
                _menuBar.SetPage(_page.Open, _page.Page, _page.Monitor);
            }
            else
                _menuBar.SettingsChanged();
        }
        else
        {
            _menuBar?.Dispose();
            _menuBar = null;
        }

        if (Settings.IsMac && Settings.Dock)
        {
            if (_dock is null)
            {
                _dock = new DockController(_shell, this);
                _dock.Start();
            }
            else
                _dock.SettingsChanged();
        }
        else
        {
            _dock?.Dispose();
            _dock = null;
        }
        if (_menuBar is null && _dock is null)
            MenuFocus.CloseShared(); // the window their menus hold the keyboard with
        _context.Logger.LogInformation("Mac-style desktop: {Style}, menu bar {MenuBar}, dock {Dock}, island {Island}", Settings.Style,
            _menuBar is not null, _dock is not null, IslandActive);
        Changed?.Invoke();
    }

    /// <summary>Explorer shows a taskbar again now and then (a display or theme change): hide it again within 2 seconds.</summary>
    private void StartRepair()
    {
        if (_repair is not null || _dispatcher is null)
            return;
        _repair = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
        {
            if (_takeover?.NeedsRepair() == true)
                _takeover.TakeOver();
        }, _dispatcher);
        _repair.Start();
    }

    private void StopRepair()
    {
        _repair?.Stop();
        _repair = null;
    }

    private void OnExplorerRestarted()
    {
        _context?.Logger.LogInformation("Mac-style desktop: Explorer restarted");
        if (Settings.HidesTaskbar)
            _takeover?.TakeOver();
        _menuBar?.RefreshDisplays();
        _dock?.RefreshDisplays();
    }

    private void OnDisplaysChanged()
    {
        _menuBar?.RefreshDisplays();
        _dock?.RefreshDisplays();
        Changed?.Invoke();
    }

    public void OpenSettings() => _context?.OpenSettings(null);

    public Task StopAsync(CancellationToken ct)
    {
        StopRepair();
        _menuBar?.Dispose();
        _menuBar = null;
        _dock?.Dispose();
        _dock = null;
        MenuFocus.CloseShared();
        _events?.Dispose();
        _events = null;
        // Signing out or shutting down: the taskbar stays hidden for the next start, which takes it again or gives it back;
        // a start without the mode gives it back (App), and uninstalling does too.
        if (!TaskbarTakeover.SessionEnding)
            _takeover?.GiveBack();
        _takeover = null;
        _context = null;
        _shell = null;
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}
