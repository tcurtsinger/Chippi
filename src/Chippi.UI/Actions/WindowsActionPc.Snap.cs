// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Chippi.Core.Actions;
using Chippi.Core.Input;
using Native = Chippi.UI.Actions.ActionNative;

namespace Chippi.UI.Actions;

public sealed partial class WindowsActionPc : IDisposable
{
    public Func<ActionWindow, bool>? SnapPaused { get; set; }
    public Action<ActionWindow>? SnapFailed { get; set; }
    public Func<string, string, WindowZone?>? SnapZones { get; set; }
    private sealed record SnapHistory(ActionWindow Target, Placement Baseline, Native.Rect Expected, bool Maximized);
    private readonly Dictionary<nint, SnapHistory> _snapHistory = [];
    private (ActionWindow Target, Placement Baseline)? _dragBaseline;
    private readonly Dictionary<nint, long> _lifetimes = [];
    private long _nextLifetime;
    private nint _destroyHook;
    private WindowEvent? _destroyCallback;

    // WinEvent callbacks return to the registering UI thread. A handle reused by the same process/thread/class
    // receives a new lifetime as well. No state is written into another application's window.
    private long Lifetime(nint window)
    {
        if (_destroyHook == 0)
        {
            _destroyCallback = (_, _, hwnd, objectId, childId, _, _) =>
            {
                if (objectId != 0 || childId != 0) return;
                _lifetimes.Remove(hwnd);
                _snapHistory.Remove(hwnd);
                if (_dragBaseline?.Target.Handle == hwnd) _dragBaseline = null;
            };
            _destroyHook = SetWinEventHook(0x8001, 0x8001, 0, _destroyCallback, 0, 0, 0);
            if (_destroyHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        if (!_lifetimes.TryGetValue(window, out long lifetime))
            _lifetimes[window] = lifetime = ++_nextLifetime;
        return lifetime;
    }

    public void Dispose()
    {
        if (_destroyHook != 0) UnhookWinEvent(_destroyHook);
        _destroyHook = 0;
        _destroyCallback = null;
        _lifetimes.Clear();
        _snapHistory.Clear();
        _dragBaseline = null;
    }

    public void BeginWindowDrag(ActionWindow target)
    {
        Validate(target);
        Validate(target, snapping: true);
        _dragBaseline = (target, Baseline(target));
    }

    public void EndWindowDrag(ActionWindow target)
    {
        if (_dragBaseline?.Target == target) _dragBaseline = null;
    }

    private static bool IsSnapAction(WindowAction action) => action is WindowAction.SnapLeft or WindowAction.SnapRight
        or WindowAction.Maximize or WindowAction.OtherScreen or WindowAction.Center or WindowAction.Restore
        or WindowAction.LeftThird or WindowAction.MiddleThird or WindowAction.RightThird
        or WindowAction.LeftTwoThirds or WindowAction.RightTwoThirds
        or WindowAction.TopLeft or WindowAction.TopRight or WindowAction.BottomLeft or WindowAction.BottomRight;

    public ActionWindow? CaptureWindow(nint window)
    {
        if (AppWindow(window) == 0 || !Native.IsWindow(window))
            return null;
        uint thread = Native.GetWindowThreadProcessId(window, out uint pid);
        if (thread == 0) return null;
        nint handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION, including admin windows
        if (handle == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var path = new StringBuilder(32768);
            int length = path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref length)) return null;
            string exe = path.ToString();
            var title = new StringBuilder(1024);
            GetWindowText(window, title, title.Capacity);
            string app = FileVersionInfo.GetVersionInfo(exe).FileDescription is { Length: > 0 } description
                ? description : System.IO.Path.GetFileNameWithoutExtension(exe);
            return new(window, pid, thread, process.StartTime.ToUniversalTime().Ticks, Native.ClassOf(window),
                exe, app, title.ToString(), IsElevated(handle), Lifetime(window));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or System.IO.IOException)
        { return null; }
        finally { CloseHandle(handle); }
    }

    private void Validate(ActionWindow target, bool snapping = false)
    {
        var current = CaptureWindow(target.Handle);
        if (current is null || current.Lifetime != target.Lifetime || current.ProcessId != target.ProcessId || current.ThreadId != target.ThreadId
            || current.ProcessStarted != target.ProcessStarted || current.ClassName != target.ClassName
            || !string.Equals(current.Executable, target.Executable, StringComparison.OrdinalIgnoreCase))
            throw new ActionFailure("Window closed");
        if (current.Elevated || target.Elevated)
            throw new ActionFailure("Access denied");
        if (snapping && SnapPaused?.Invoke(current) == true)
            throw new ActionFailure("Paused");
    }

    public bool CanSnap(ActionWindow target)
    {
        try { Validate(target, snapping: true); return true; }
        catch (ActionFailure) { return false; }
    }
    public void Window(ActionWindow target, WindowAction action)
    {
        try
        {
            Validate(target);
            if (IsSnapAction(action)) SnapWindow(target, action);
            else Window(target.Handle, action);
        }
        catch (ActionFailure ex)
        {
            Chippi.Core.Diagnostics.Log.Warn($"Window {action} failed for {target.App}: {ex.Message}");
            if (IsSnapAction(action)) SnapFailed?.Invoke(target);
            throw;
        }
    }
    public bool CanRestore(ActionWindow target)
    {
        try
        {
            Validate(target);
            return History(target) is not null;
        }
        catch (ActionFailure) { return false; }
    }

    private SnapHistory? History(ActionWindow target)
    {
        if (!_snapHistory.TryGetValue(target.Handle, out var history)) return null;
        if (history.Target.Lifetime != target.Lifetime || history.Target.ProcessId != target.ProcessId || history.Target.ProcessStarted != target.ProcessStarted
            || history.Target.ThreadId != target.ThreadId || history.Target.ClassName != target.ClassName
            || !Native.GetWindowRect(target.Handle, out var current) || !Same(current, history.Expected)
            || Native.IsZoomed(target.Handle) != history.Maximized)
        {
            _snapHistory.Remove(target.Handle);
            return null;
        }
        return history;
    }

    private static bool Same(Native.Rect a, Native.Rect b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    private Placement Baseline(ActionWindow target)
    {
        var history = History(target);
        if (history is not null) return history.Baseline;
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(target.Handle, ref placement)) throw new ActionFailure("Snap failed");
        return placement;
    }

    private void Remember(ActionWindow target, Placement baseline)
    {
        if (!Native.GetWindowRect(target.Handle, out var rect)) throw new ActionFailure("Snap failed");
        // Bound history on a long-running tray process. Only the latest 256 windows retain Restore.
        if (_snapHistory.Count >= 256 && !_snapHistory.ContainsKey(target.Handle))
            _snapHistory.Remove(_snapHistory.Keys.First());
        _snapHistory[target.Handle] = new(target, baseline, rect, Native.IsZoomed(target.Handle));
    }

    private void SnapWindow(ActionWindow target, WindowAction action)
    {
        Validate(target);
        Validate(target, snapping: true);
        nint hwnd = target.Handle;
        if (action == WindowAction.Restore)
        {
            var history = History(target) ?? throw new ActionFailure("No placement");
            var original = history.Baseline;
            // WINDOWPLACEMENT uses workspace coordinates. Round-trip it through the matching API.
            if (!SetWindowPlacement(hwnd, ref original)) throw new ActionFailure("Snap failed");
            KeepReachable(hwnd);
            _snapHistory.Remove(hwnd);
            return;
        }
        var baseline = Baseline(target);
        if (action == WindowAction.Maximize)
        {
            Native.ShowWindow(hwnd, Native.IsZoomed(hwnd) ? 9 : 3);
            Remember(target, baseline);
            return;
        }
        if (action == WindowAction.OtherScreen)
        {
            var monitors = Native.Monitors();
            if (monitors.Count < 2) throw new ActionFailure("One screen");
            Carry(target, monitors[(monitors.IndexOf(Native.MonitorFromWindow(hwnd, 2)) + 1) % monitors.Count]);
            Remember(target, baseline);
            return;
        }
        if (Native.IsZoomed(hwnd) || Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, 9);
        var (_, work) = Native.Monitor(Native.MonitorFromWindow(hwnd, 2));
        CheckWork(work);
        if (!Visible(hwnd, out var visible) && visible.Width <= 0) throw new ActionFailure("Snap failed");
        Native.Rect destination;
        if (action == WindowAction.Center)
        {
            int width = Math.Min(work.Width, visible.Width), height = Math.Min(work.Height, visible.Height);
            int x = work.Left + (work.Width - width) / 2, y = work.Top + (work.Height - height) / 2;
            destination = new() { Left = x, Top = y, Right = x + width, Bottom = y + height };
        }
        else
        {
            var zone = action switch
            {
                WindowAction.SnapLeft => new WindowZone(0, 0, .5, 1, 0, 0),
                WindowAction.SnapRight => new WindowZone(.5, 0, .5, 1, 0, 0),
                WindowAction.LeftThird => new WindowZone(0, 0, 1d / 3, 1, 0, 0),
                WindowAction.MiddleThird => new WindowZone(1d / 3, 0, 1d / 3, 1, 0, 0),
                WindowAction.RightThird => new WindowZone(2d / 3, 0, 1d / 3, 1, 0, 0),
                WindowAction.LeftTwoThirds => new WindowZone(0, 0, 2d / 3, 1, 0, 0),
                WindowAction.RightTwoThirds => new WindowZone(1d / 3, 0, 2d / 3, 1, 0, 0),
                WindowAction.TopLeft => new WindowZone(0, 0, .5, .5, 0, 0),
                WindowAction.TopRight => new WindowZone(.5, 0, .5, .5, 0, 0),
                WindowAction.BottomLeft => new WindowZone(0, .5, .5, .5, 0, 0),
                WindowAction.BottomRight => new WindowZone(.5, .5, .5, .5, 0, 0),
                _ => throw new ActionFailure("No action"),
            };
            destination = ZoneRect(work, zone);
        }
        Validate(target);
        Place(hwnd, destination);
        KeepReachable(hwnd);
        Remember(target, baseline);
    }

    /// <summary>The App switcher's move. Snap's pause list doesn't apply, and Snap's Restore doesn't remember it.</summary>
    public void MoveToDisplay(ActionWindow target, nint monitor)
    {
        try
        {
            Validate(target);
            if (!Native.Monitors().Contains(monitor)) throw new ActionFailure("Display removed");
            nint from = Native.MonitorFromWindow(target.Handle, 2);
            if (from == monitor) return;
            if (FullscreenCheck.CoversMonitor(target.Handle, from)) throw new ActionFailure("Fullscreen");
            Carry(target, monitor);
        }
        catch (ActionFailure ex)
        {
            Chippi.Core.Diagnostics.Log.Info($"Move to display skipped for {target.App}: {ex.Message}");
            throw;
        }
    }

    /// <summary>Snap's Next monitor and the App switcher: restored, moved across, fitted, then maximized again when it was.</summary>
    private void Carry(ActionWindow target, nint to)
    {
        nint hwnd = target.Handle;
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, 9);
        // Read once it's restored: a window minimized from maximized comes back maximized.
        bool maximized = Native.IsZoomed(hwnd);
        if (maximized) Native.ShowWindow(hwnd, 9);
        var (_, work) = Native.Monitor(Native.MonitorFromWindow(hwnd, 2));
        CheckWork(work);
        var (_, area) = Native.Monitor(to);
        CheckWork(area);
        if (!Visible(hwnd, out var visible) && visible.Width <= 0) throw new ActionFailure("Snap failed");
        var (x, y, width, height) = DisplayMove.Fit((visible.Left, visible.Top, visible.Width, visible.Height),
            (work.Left, work.Top, work.Width, work.Height), (area.Left, area.Top, area.Width, area.Height));
        // Cross the DPI boundary before sizing. The app can process WM_DPICHANGED between calls.
        if (!Native.SetWindowPos(hwnd, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010))
            throw new ActionFailure("Snap failed");
        Validate(target);
        Place(hwnd, new() { Left = x, Top = y, Right = x + width, Bottom = y + height });
        KeepReachable(hwnd);
        if (maximized) Native.ShowWindow(hwnd, 3);
    }

    public void WindowZone(ActionWindow target, string layoutId, string zoneId, nint monitor = 0)
    {
        try { PlaceZone(target, layoutId, zoneId, monitor); }
        catch (ActionFailure) { SnapFailed?.Invoke(target); throw; }
    }

    private void PlaceZone(ActionWindow target, string layoutId, string zoneId, nint monitor)
    {
        Validate(target);
        Validate(target, snapping: true);
        var zone = SnapZones?.Invoke(layoutId, zoneId) ?? throw new ActionFailure("Zone removed");
        if (monitor != 0 && !Native.Monitors().Contains(monitor)) throw new ActionFailure("Display removed");
        var baseline = _dragBaseline is { } drag && drag.Target == target ? drag.Baseline : Baseline(target);
        if (Native.IsZoomed(target.Handle) || Native.IsIconic(target.Handle)) Native.ShowWindow(target.Handle, 9);
        var (_, work) = Native.Monitor(monitor == 0 ? Native.MonitorFromWindow(target.Handle, 2) : monitor);
        CheckWork(work);
        var destination = ZoneRect(work, zone);
        Validate(target);
        if (monitor != 0 && Native.MonitorFromWindow(target.Handle, 2) != monitor
            && !Native.SetWindowPos(target.Handle, 0, destination.Left, destination.Top, 0, 0, 0x0001 | 0x0004 | 0x0010))
            throw new ActionFailure("Snap failed");
        Place(target.Handle, destination);
        KeepReachable(target.Handle);
        Remember(target, baseline);
    }

    private static Native.Rect ZoneRect(Native.Rect work, WindowZone zone)
    {
        try
        {
            var (left, top, right, bottom) = zone.Bounds(work.Width, work.Height);
            return new() { Left = work.Left + left, Top = work.Top + top, Right = work.Left + right, Bottom = work.Top + bottom };
        }
        catch (ArgumentException ex) { throw new ActionFailure(ex.Message); }
    }
    private static void CheckWork(Native.Rect work)
    {
        if (work.Width <= 0 || work.Height <= 0) throw new ActionFailure("Display removed");
    }

    private static void KeepReachable(nint hwnd)
    {
        if (Native.IsZoomed(hwnd)) return;
        var (_, work) = Native.Monitor(Native.MonitorFromWindow(hwnd, 2));
        CheckWork(work);
        if (!Visible(hwnd, out var actual) && actual.Width <= 0) throw new ActionFailure("Snap failed");
        int x = Math.Clamp(actual.Left, work.Left, work.Left + Math.Max(0, work.Width - actual.Width));
        int y = Math.Clamp(actual.Top, work.Top, work.Top + Math.Max(0, work.Height - actual.Height));
        if (!Native.GetWindowRect(hwnd, out var outer)) throw new ActionFailure("Snap failed");
        if ((x != actual.Left || y != actual.Top) && !Native.SetWindowPos(hwnd, 0,
            outer.Left + x - actual.Left, outer.Top + y - actual.Top, 0, 0, 0x0001 | 0x0004 | 0x0010))
            throw new ActionFailure("Snap failed");
    }

    private static bool IsElevated(nint process)
    {
        if (!OpenProcessToken(process, 8, out nint token)) return true;
        nint buffer = 0;
        try
        {
            // UIPI follows integrity, as the input/clipboard guards do. TokenElevation can remain true on
            // a restricted medium token in a UAC-disabled session (Windows Sandbox).
            GetTokenInformation(token, 25, 0, 0, out int needed); // TokenIntegrityLevel
            if (needed <= 0) return true;
            buffer = Marshal.AllocHGlobal(needed);
            if (!GetTokenInformation(token, 25, buffer, needed, out _)) return true;
            nint sid = Marshal.ReadIntPtr(buffer);
            if (!IsValidSid(sid)) return true;
            int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count == 0 || Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))) >= 0x3000; // high
        }
        finally
        {
            if (buffer != 0) Marshal.FreeHGlobal(buffer);
            CloseHandle(token);
        }
    }
    private delegate void WindowEvent(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WindowEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);

    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public int Length, Flags, ShowCommand;
        public Native.Point MinPosition, MaxPosition;
        public Native.Rect NormalPosition;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(nint hwnd, ref Placement placement);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int length);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(nint token, int cls, nint value, int size, out int needed);
    [DllImport("advapi32.dll")] private static extern bool IsValidSid(nint sid);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] private static extern nint GetSidSubAuthority(nint sid, uint index);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}


