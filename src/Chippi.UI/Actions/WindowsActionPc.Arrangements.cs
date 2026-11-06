// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Chippi.Core.Actions;
using Chippi.Core.Input;
using Native = Chippi.UI.Actions.ActionNative;

namespace Chippi.UI.Actions;

public sealed partial class WindowsActionPc
{
    public void LaunchExecutable(ExecutableLaunch launch)
    {
        if (!launch.IsValid) throw new ActionFailure("Invalid launch details");
        if (!File.Exists(launch.Path)) throw new ActionFailure("Not found");
        if (launch.WorkingFolder.Length > 0 && !Directory.Exists(launch.WorkingFolder))
            throw new ActionFailure("Working folder not found");
        try
        {
            // CreateProcess, never ShellExecute: ERROR_ELEVATION_REQUIRED is returned without asking for consent.
            Process.Start(new ProcessStartInfo(launch.Path)
            {
                UseShellExecute = false,
                Arguments = launch.Arguments,
                WorkingDirectory = launch.WorkingFolder.Length == 0
                    ? Path.GetDirectoryName(launch.Path)! : launch.WorkingFolder
            })?.Dispose();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 740 or 1223)
        {
            throw new ActionFailure("Needs admin");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new ActionFailure("Launching failed");
        }
    }

    public SavedWindowPlacement? ReadPlacement(ActionWindow target)
    {
        Validate(target);
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(target.Handle, ref placement)) return null;
        var (bounds, work) = Native.Monitor(Native.MonitorFromWindow(target.Handle, 2));
        CheckWork(work);
        var normal = placement.NormalPosition;
        // WINDOWPLACEMENT uses workspace coordinates except for tool windows (excluded by capture).
        int x = normal.Left + work.Left - bounds.Left;
        int y = normal.Top + work.Top - bounds.Top;
        bool minimized = Native.IsIconic(target.Handle);
        return new(x, y, normal.Width, normal.Height, work.Left, work.Top, work.Width, work.Height,
            Native.IsZoomed(target.Handle) || (minimized && (placement.Flags & 2) != 0), minimized);
    }

    public void PlaceSaved(ActionWindow target, SavedWindowPlacement saved, nint monitor)
    {
        if (!saved.IsValid) throw new ActionFailure("Invalid placement");
        Validate(target, snapping: true);
        if (!Native.Monitors().Contains(monitor)) throw new ActionFailure("Display removed");
        nint hwnd = target.Handle;
        // SW_SHOWNOACTIVATE restores without repeatedly stealing the foreground during a run.
        if (Native.IsIconic(hwnd) || Native.IsZoomed(hwnd)) Native.ShowWindow(hwnd, 4);
        Validate(target, snapping: true);
        if (FullscreenCheck.CoversMonitor(hwnd, Native.MonitorFromWindow(hwnd, 2)))
            throw new ActionFailure("Fullscreen");
        var (_, area) = Native.Monitor(monitor);
        CheckWork(area);
        var (x, y, width, height) = DisplayMove.Fit((saved.Left, saved.Top, saved.Width, saved.Height),
            (saved.WorkLeft, saved.WorkTop, saved.WorkWidth, saved.WorkHeight),
            (area.Left, area.Top, area.Width, area.Height));
        if (!Native.SetWindowPos(hwnd, 0, x, y, width, height, 0x0004 | 0x0010))
            throw new ActionFailure("Placement failed");
        KeepReachable(hwnd);
        Validate(target, snapping: true);
        if (!Native.Monitors().Contains(monitor)) throw new ActionFailure("Display removed");
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(hwnd, ref placement)) throw new ActionFailure("Placement failed");
        placement.Flags = saved.Maximized && saved.Minimized ? placement.Flags | 2 : placement.Flags & ~2;
        // SetWindowPlacement restores the state and preserves the new normal bounds.
        placement.ShowCommand = saved.Minimized ? 7 : saved.Maximized ? 3 : 4;
        if (!SetWindowPlacement(hwnd, ref placement)) throw new ActionFailure("Placement failed");
    }
}
