// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;

namespace Chippi.Media;

internal static unsafe class RecordingSources
{
    public static IReadOnlyList<RecordingSource> Get(RecordingTarget target)
    {
        List<RecordingSource> sources = [];
        var handle = GCHandle.Alloc(sources);
        try
        {
        if (target == RecordingTarget.Screen)
        {
            PInvoke.EnumDisplayMonitors(default, null, &Screen, new LPARAM(GCHandle.ToIntPtr(handle)));
        }
        else
        {
            PInvoke.EnumWindows(&Window, new LPARAM(GCHandle.ToIntPtr(handle)));
        }
        return sources;
        }
        finally { handle.Free(); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Screen(HMONITOR monitor, HDC dc, RECT* bounds, LPARAM state)
    {
        try
        {
            var sources = (List<RecordingSource>)GCHandle.FromIntPtr(state.Value).Target!;
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            if (PInvoke.GetMonitorInfo(monitor, &info))
                sources.Add(new(RecordingTarget.Screen, (nint)monitor.Value, (info.dwFlags & 1) != 0 ? "Main display" : $"Display {sources.Count + 1}",
                    info.rcMonitor.right - info.rcMonitor.left, info.rcMonitor.bottom - info.rcMonitor.top));
        }
        catch { return false; }
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Window(HWND window, LPARAM state)
    {
        try
        {
                var sources = (List<RecordingSource>)GCHandle.FromIntPtr(state.Value).Target!;
                PInvoke.GetWindowThreadProcessId(window, out uint pid);
                if (pid == Environment.ProcessId || !PInvoke.IsWindowVisible(window) || PInvoke.IsIconic(window)) return true;
                int cloaked = 0;
                PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int));
                if (cloaked != 0) return true;
                int length = Math.Min(PInvoke.GetWindowTextLength(window), 1024);
                if (length <= 0) return true;
                Span<char> title = stackalloc char[length + 1];
                int count = PInvoke.GetWindowText(window, title);
                if (count <= 0) return true;
                RECT rect = default;
                if (PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(RECT)).Failed) return true;
                if (rect.right - rect.left < 2 || rect.bottom - rect.top < 2) return true;
                sources.Add(new(RecordingTarget.Window, (nint)window.Value, new string(title[..count]), rect.right - rect.left, rect.bottom - rect.top));
                return true;
        }
        catch { return false; }
    }
}
