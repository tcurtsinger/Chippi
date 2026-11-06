// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Capture;
using Chippi.UI.Actions;

namespace Chippi.UI.Capture;

/// <summary>
/// The windows a freeze snaps to (capture-layer.md, The freeze, step 6): the App switcher's list (visible, not cloaked,
/// on this desktop), front first, minimized ones and the overlays left out, each with its visible frame
/// (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>, physical pixels in a per-monitor-aware process). Any thread.
/// </summary>
internal static partial class FrozenWindows
{
    private static readonly WindowsWindowList List = new(0);

    public static IReadOnlyList<FrozenWindow> Read(IReadOnlySet<nint> exclude) =>
    [
        .. List.Read()
            .Where(w => !w.Minimized && !exclude.Contains(w.Handle))
            .Select(w => new FrozenWindow(w.Handle, w.Title, w.Executable, Frame(w.Handle)))
            .Where(w => !w.Frame.IsEmpty),
    ];

    private static PixelRect Frame(nint hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var r, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(hwnd, out r))
            return default;
        return PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private const int DwmwaExtendedFrameBounds = 9;

    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetWindowRect(nint hwnd, out Rect rect);
}
