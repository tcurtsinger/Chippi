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
/// widget as a notch, with the Windows taskbar hidden meanwhile. Every page is the island's own; this adds the three
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

    public FeatureDescriptor Descriptor { get; } = new(Id, "Mac-style desktop", "A menu bar, a dock and a notch", FeatureGroup.Windows)
    {
        Icon = nameof(LucideIcons.PanelBottom),
        Presets = PresetMembership.PowerUser,
    };

    public MacDesktopSettings Settings { get; private set; } = new();

    public bool IsRunning => _context is not null;

    /// <summary>Why the taskbar couldn't be hidden, for the Settings page; null when it's fine.</summary>
    public string? Error { get; private set; }

    /// <summary>The widget is the notch: the mode is on with its Notch switch.</summary>
    public bool NotchActive => IsRunning && Settings.IsMac && Settings.Notch;

    /// <summary>Raised on the UI thread when the settings or the surfaces change.</summary>
    public event Action? Changed;

    /// <summary>Opens the notch's home: set by the shell, which owns the notch.</summary>
    public Action OpenNotchHome
    {
        set
        {
            (_pending ??= new()).OpenNotchHome = value;
            if (_shell is not null) _shell.OpenNotchHome = value;
        }
    }

    /// <summary>The notch's monitor and edges in physical pixels, for the menu bar to keep clear of; set by the shell.</summary>
    public Func<(nint Monitor, double Left, double Right)?> NotchBounds
    {
        set
        {
            (_pending ??= new()).Notch = value;
            if (_shell is not null) _shell.Notch = value;
        }
    }

    private sealed class PendingShell
    {
        public Action? OpenNotchHome;
        public Func<(nint Monitor, double Left, double Right)?>? Notch;
    }

    private PendingShell? _pending;

    /// <summary>The notch moved (or is being dragged): the menu bars make room for it at once.</summary>
    public void NotchMoved() => _menuBar?.NotchMoved();

    /// <summary>The notch's display (null for the main one) and its place across the bar, 0 to 1, as the shell saves it.</summary>
    public void SaveNotchPlace(string? display, double place)
    {
        if (_context is null)
            return;
        var next = Settings with { NotchDisplay = display, NotchPlace = Math.Clamp(place, 0, 1) };
        if (next == Settings)
            return;
        Settings = next;
        try { _context.Settings.Save(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { _context.Logger.LogWarning(ex, "Saving the notch's place failed"); }
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
            if (pending.OpenNotchHome is { } home) _shell.OpenNotchHome = home;
            if (pending.Notch is { } notch) _shell.Notch = notch;
        }
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
            { Words = "mac dock menu bar notch taskbar desktop style" },
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
            }
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
        _context.Logger.LogInformation("Mac-style desktop: {Style}, menu bar {MenuBar}, dock {Dock}, notch {Notch}", Settings.Style,
            _menuBar is not null, _dock is not null, NotchActive);
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
