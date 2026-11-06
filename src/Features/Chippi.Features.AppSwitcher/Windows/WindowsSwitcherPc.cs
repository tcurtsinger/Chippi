// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Core.Actions;

namespace Chippi.Features.AppSwitcher.Windows;

/// <summary>
/// The real windows and the foreground. The list follows Alt+Tab's rules (the same as Quit apps' <c>AltTabApps</c>):
/// visible, not cloaked (which also hides other virtual desktops), its own app, not a tool window, with a title.
/// The focus hand-off is the spike's (task 376, Windows Sandbox, 81 runs): activate the target while the overlay still
/// holds the foreground, then hide the overlay; Esc activates the original window first, as hiding alone handed the
/// keyboard to another Chippi window.
/// </summary>
public sealed partial class WindowsSwitcherPc : ISwitcherPc
{
    private readonly Chippi.UI.Actions.WindowsWindowList _windows;
    private readonly IActionPc? _actions;


    /// <param name="actions">The shell's window moves, which bring a window to the switcher's display; without them, nothing moves.</param>
    public WindowsSwitcherPc(IActionPc? actions = null) : this((uint)Environment.ProcessId, actions)
    {
    }

    /// <param name="self">The process whose windows are left out: Chippi's own, or 0 in tests that list their own windows.</param>
    internal WindowsSwitcherPc(uint self, IActionPc? actions = null)
    {
        _windows = new(self);
        _actions = actions;
    }

    public IReadOnlyList<SwitcherWindow> Windows() => _windows.Read()
        .Select(w => new SwitcherWindow(w.Handle, w.ProcessId, w.App, w.Title, w.Executable, w.Minimized, w.Monitor)).ToArray();
    public nint Foreground() => GetForegroundWindow();

    public nint MonitorOf(nint hwnd) => hwnd == 0 ? 0 : MonitorFromWindow(hwnd, MonitorDefaultToNull);

    public nint PointerMonitor() => GetCursorPos(out var p) ? MonitorFromPoint(p, MonitorDefaultToNearest) : 0;

    public bool IsSame(nint hwnd, uint processId) =>
        IsWindow(hwnd) && GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid == processId;

    /// <summary>Gives the overlay the keyboard, with the 0x07 tap when the door left no right to it (<see cref="Chippi.UI.Actions.ForegroundRight"/>).</summary>
    public bool TakeForeground(nint overlay) => Chippi.UI.Actions.ForegroundRight.Take(overlay);

    /// <summary>Snap's Next monitor move (<see cref="IActionPc.MoveToDisplay"/>). An admin window isn't tried: Chippi can't move it.</summary>
    public bool BringTo(SwitcherWindow window, nint monitor)
    {
        if (_actions?.CaptureWindow(window.Handle) is not { Elevated: false } target)
            return false;
        try
        {
            _actions.MoveToDisplay(target, monitor);
            return true;
        }
        catch (ActionFailure)
        {
            return false;
        }
    }

    public bool Activate(SwitcherWindow window)
    {
        if (IsIconic(window.Handle))
            ShowWindow(window.Handle, SwRestore);
        // True also for an app that's busy: the foreground stays empty until it answers, as with Windows' own switch.
        return SetForegroundWindow(window.Handle);
    }

    public void GiveBack(nint original)
    {
        if (original != 0 && IsWindow(original))
            SetForegroundWindow(original);
    }


    private const uint MonitorDefaultToNull = 0, MonitorDefaultToNearest = 2;

    private const int SwRestore = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }


    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint hwnd, int cmd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromPoint(Point point, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out Point point);
}
