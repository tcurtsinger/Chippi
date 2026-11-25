// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>
/// The app in front, for the menu bar's app name and menus. Follows the foreground with a WinEvent hook on the UI thread.
/// Chippi's own windows (the bar, the dock, a menu) never count: the app before them stays.
/// </summary>
internal sealed class ForegroundApp : IDisposable
{
    private readonly Native.WinEventProc _proc; // held, so the hook's callback isn't collected
    private nint _hook;
    private readonly uint _self = (uint)Environment.ProcessId;

    public nint Window { get; private set; }
    public uint ProcessId { get; private set; }
    public AppIdentity App { get; private set; } = AppIdentity.Desktop;

    public event Action? Changed;

    public ForegroundApp()
    {
        _proc = OnEvent;
        _hook = Native.SetWinEventHook(Native.EventSystemForeground, Native.EventSystemForeground, 0, _proc, 0, 0, Native.WinEventOutOfContext);
        Read(Native.GetForegroundWindow());
    }

    private void OnEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time) => Read(window);

    /// <summary>Reads the window in front again, as after a window closed or a menu ran.</summary>
    public void Refresh() => Read(Native.GetForegroundWindow());

    private void Read(nint window)
    {
        if (window == 0)
            return;
        Native.GetWindowThreadProcessId(window, out uint pid);
        if (pid == _self)
            return;
        string cls = Native.ClassOf(window);
        if (PassesOver(cls))
            return; // Start, Search, Quick Settings and the taskbar's flyouts pass over the app in front
        if (cls is "Progman" or "WorkerW")
        {
            Set(window, pid, AppIdentity.Desktop);
            return;
        }
        if (AppNames.Of(window) is { } found)
            Set(window, found.ProcessId, found.App);
    }

    /// <summary>Windows' own surfaces that come and go over the app in front without replacing it.</summary>
    public static bool PassesOver(string windowClass) => windowClass is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
        or "TopLevelWindowForOverflowXamlIsland" or "NotifyIconOverflowWindow" or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow";

    private void Set(nint window, uint pid, AppIdentity app)
    {
        if (window == Window && app == App)
            return;
        Window = window;
        ProcessId = pid;
        App = app;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_hook != 0)
            Native.UnhookWinEvent(_hook);
        _hook = 0;
    }
}
