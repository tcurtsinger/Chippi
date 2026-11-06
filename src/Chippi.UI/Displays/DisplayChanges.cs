// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Chippi.UI.Displays;

/// <summary>
/// One hidden window that hears the displays change (WM_DISPLAYCHANGE) and the PC resume, so Displays, Capture and Snap
/// share it rather than each listening (docs/phase-7/displays.md, Shared code). The window exists while anyone is subscribed.
/// </summary>
public static class DisplayChanges
{
    private const int WmDisplayChange = 0x007E;
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int PbtApmResumeAutomatic = 0x0012;

    private static readonly object Gate = new();
    private static Action? s_changed;
    private static int s_count;
    private static HwndSource? s_source;
    private static DispatcherTimer? s_settle;

    /// <summary>Raised on the UI thread once the displays have settled after a change or a resume.</summary>
    public static event Action? Changed
    {
        add
        {
            lock (Gate)
            {
                s_changed += value;
                if (s_count++ == 0) OnUi(Start);
            }
        }
        remove
        {
            lock (Gate)
            {
                s_changed -= value;
                if (s_count > 0 && --s_count == 0) OnUi(Stop);
            }
        }
    }

    /// <summary>Raises <see cref="Changed"/> as the window does; for tests.</summary>
    internal static void Raise() => Settled();

    private static void OnUi(Action work)
    {
        // Without an application (a headless test) there is no window to hear Windows with.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) work();
        else dispatcher.BeginInvoke(work);
    }

    private static void Start()
    {
        if (s_source is not null) return;
        s_source = new HwndSource(new HwndSourceParameters("Chippi display changes") { Width = 0, Height = 0, WindowStyle = 0 });
        s_source.AddHook(WndProc);
        s_settle = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(300) };
        s_settle.Tick += (_, _) => Settled();
    }

    private static void Stop()
    {
        s_settle?.Stop();
        s_settle = null;
        s_source?.Dispose();
        s_source = null;
    }

    private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmDisplayChange || (msg == WmPowerBroadcast && (int)wParam is PbtApmResumeSuspend or PbtApmResumeAutomatic))
        {
            // Windows sends several in a row while displays come up; one raise after they stop.
            s_settle?.Stop();
            s_settle?.Start();
        }
        return 0;
    }

    private static void Settled()
    {
        s_settle?.Stop();
        Action? changed;
        lock (Gate) changed = s_changed;
        changed?.Invoke();
    }
}
