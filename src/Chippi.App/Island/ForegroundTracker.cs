// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Core.Config;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.App.Island;

/// <summary>
/// Tracks the foreground window without polling: a WinEvent hook on EVENT_SYSTEM_FOREGROUND, plus
/// EVENT_OBJECT_LOCATIONCHANGE scoped to the foreground window's own thread (so a window dragged to
/// another monitor, or one that goes fullscreen in place, is seen). Reports the monitor the island
/// belongs on and whether something fullscreen is in front. Create and use on the UI thread: the
/// out-of-context hooks deliver through its message loop.
/// </summary>
internal sealed class ForegroundTracker : IDisposable
{
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;

    private readonly Dispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly WINEVENTPROC _callback; // kept alive for as long as the hooks exist
    private HWINEVENTHOOK _foregroundHook;
    private HWINEVENTHOOK _locationHook;
    private uint _locationThread;
    private HWND _foreground;
    private bool _pending;
    private bool _disposed;
    private IslandDisplay _display = AppSettings.DefaultIslandDisplay;
    private string? _monitorId;
    private HMONITOR? _chosen; // the chosen monitor's live handle; re-resolved when the setting or the displays change
    private bool _chosenResolved;
    private bool _loggedMissingMonitor;

    public ForegroundTracker(Dispatcher dispatcher, ILogger logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        _callback = OnWinEvent;
    }

    /// <summary>Raised on the UI thread when <see cref="Monitor"/> or <see cref="FullscreenInFront"/> may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The monitor the island belongs on: with <see cref="IslandDisplay.FollowActiveWindow"/> the one
    /// holding the foreground window (or, for the desktop, the pointer); otherwise the main display or
    /// the chosen monitor, falling back to the main display while that one is disconnected.
    /// </summary>
    public HMONITOR Monitor { get; private set; }

    /// <summary>Applies the island display setting and re-evaluates. Call on the UI thread.</summary>
    public void SetPlacement(IslandDisplay display, string? monitorId)
    {
        if (display == _display && string.Equals(monitorId, _monitorId, StringComparison.OrdinalIgnoreCase))
            return;
        _display = display;
        _monitorId = monitorId;
        _chosenResolved = false;
        _loggedMissingMonitor = false;
        Evaluate();
    }

    /// <summary>Monitors were added, removed or rearranged: find the chosen one again, then re-evaluate.</summary>
    public void OnDisplaysChanged()
    {
        _chosenResolved = false;
        Evaluate();
    }

    /// <summary>A fullscreen app, D3D game or presentation is in front, so the island hides.</summary>
    public bool FullscreenInFront { get; private set; }

    /// <summary>The island's own windows (the tab, and the open island once the user clicks into it), never treated as the foreground.</summary>
    public IReadOnlyCollection<HWND> Ignore { get; set; } = [];

    public void Start()
    {
        _foregroundHook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND,
            default(HMODULE), _callback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        Evaluate();
    }

    /// <summary>Re-reads the foreground state now (also after display changes).</summary>
    public void Evaluate()
    {
        if (_disposed)
            return;
        var foreground = PInvoke.GetForegroundWindow();
        if (Ignore.Contains(foreground))
            foreground = _foreground;
        if (foreground != _foreground)
        {
            _foreground = foreground;
            WatchLocation(foreground);
        }

        string cls = ClassOf(foreground);
        bool isDesktop = cls is "Progman" or "WorkerW";
        bool isShell = isDesktop || cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

        var active = foreground.IsNull || isDesktop
            ? MonitorUnderPointer()
            : PInvoke.MonitorFromWindow(foreground, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (active == default)
            active = MonitorUnderPointer();
        var monitor = IslandMonitor(active);

        // Fullscreen hides the island only on its own monitor. A D3D game counts where it runs (the
        // foreground's monitor); presentation mode is system-wide, so it hides the island anywhere.
        var state = NotificationState();
        string? reason =
            !foreground.IsNull && !isShell && CoversMonitor(foreground, monitor) ? $"the foreground window ({cls}) covers the island's monitor"
            : state is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN && active == monitor ? "a D3D fullscreen app is running on the island's monitor (QUNS_RUNNING_D3D_FULL_SCREEN)"
            : state is QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE ? "presentation mode is on (QUNS_PRESENTATION_MODE)"
            : null;

        bool fullscreen = reason is not null;
        if (fullscreen != FullscreenInFront)
        {
            if (fullscreen)
                _logger.LogInformation("Island hidden for fullscreen: {Reason}", reason);
            else
                _logger.LogInformation("Island shown again: nothing fullscreen in front (foreground {Class}, notification state {State})", cls, state);
        }

        Monitor = monitor;
        FullscreenInFront = fullscreen;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// SHQueryUserNotificationState, or 0 when it fails. Only D3D fullscreen and presentation mode
    /// hide the island: QUNS_BUSY alone is not trusted, because some systems report it while an
    /// ordinary maximized window is in front. A borderless fullscreen window is caught by
    /// <see cref="CoversMonitor"/> instead.
    /// </summary>
    internal static QUERY_USER_NOTIFICATION_STATE NotificationState() =>
        PInvoke.SHQueryUserNotificationState(out var state).Succeeded ? state : 0;

    /// <summary>
    /// The window covers its whole monitor and is not merely maximized: a maximized captioned window
    /// leaves the island in place (it covers the monitor only when the taskbar auto-hides).
    /// </summary>
    internal static bool CoversMonitor(HWND window, HMONITOR monitor)
    {
        if (monitor == default || !PInvoke.IsWindowVisible(window) || PInvoke.IsIconic(window))
            return false;
        if (PInvoke.IsZoomed(window) && (PInvoke.GetWindowLong(window, (WINDOW_LONG_PTR_INDEX)GWL_STYLE) & WS_CAPTION) == WS_CAPTION)
            return false;
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!PInvoke.GetMonitorInfo(monitor, ref info) || !PInvoke.GetWindowRect(window, out var rect))
            return false;
        var m = info.rcMonitor;
        return rect.left <= m.left && rect.top <= m.top && rect.right >= m.right && rect.bottom >= m.bottom;
    }

    /// <summary>The island's monitor for the current setting, given the active window's monitor.</summary>
    private HMONITOR IslandMonitor(HMONITOR active)
    {
        switch (_display)
        {
            case IslandDisplay.MainDisplay:
                return Displays.Main;
            case IslandDisplay.SpecificMonitor:
                if (!_chosenResolved)
                {
                    _chosen = Displays.Find(_monitorId);
                    _chosenResolved = true;
                }
                if (_chosen is HMONITOR chosen)
                {
                    _loggedMissingMonitor = false;
                    return chosen;
                }
                if (!_loggedMissingMonitor)
                {
                    _logger.LogInformation("Island monitor {Id} is not connected; using the main display", _monitorId ?? "(none chosen)");
                    _loggedMissingMonitor = true;
                }
                return Displays.Main;
            default:
                return active;
        }
    }

    private static HMONITOR MonitorUnderPointer() =>
        PInvoke.GetCursorPos(out var point)
            ? PInvoke.MonitorFromPoint(point, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY)
            : PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);

    private static string ClassOf(HWND window)
    {
        if (window.IsNull)
            return "";
        Span<char> buffer = stackalloc char[256];
        int length = PInvoke.GetClassName(window, buffer);
        return length > 0 ? new string(buffer[..length]) : "";
    }

    /// <summary>Location changes are hooked for the foreground window's thread only, so the hook stays quiet.</summary>
    private void WatchLocation(HWND foreground)
    {
        uint process = 0;
        uint thread = foreground.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(foreground, out process);
        if (thread == _locationThread && !_locationHook.IsNull)
            return;
        if (!_locationHook.IsNull)
        {
            PInvoke.UnhookWinEvent(_locationHook);
            _locationHook = default;
        }
        _locationThread = thread;
        if (thread == 0)
            return;
        _locationHook = PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_LOCATIONCHANGE, PInvoke.EVENT_OBJECT_LOCATIONCHANGE,
            default(HMODULE), _callback, process, thread, PInvoke.WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Location changes: only the foreground window itself (OBJID_WINDOW, CHILDID_SELF).
        if (@event == PInvoke.EVENT_OBJECT_LOCATIONCHANGE && (hwnd != _foreground || idObject != 0 || idChild != 0))
            return;
        if (_pending)
            return;
        _pending = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _pending = false;
            Evaluate();
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (!_locationHook.IsNull)
            PInvoke.UnhookWinEvent(_locationHook);
        if (!_foregroundHook.IsNull)
            PInvoke.UnhookWinEvent(_foregroundHook);
        _locationHook = _foregroundHook = default;
    }
}
