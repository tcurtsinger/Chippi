// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows.Automation;
using Chippi.Capture;
using Chippi.UI.Actions;

namespace Chippi.Features.ScreenCapture.Scrolling;

/// <summary>The real scroll target. Reads windows and UI Automation only: nothing is moved, activated, scrolled or sent.</summary>
public sealed class WindowsScrollTarget : IScrollTarget
{
    private readonly ScrollTargetFinder _finder = new(new WindowsScrollNative());

    public ScrollTargetInfo Find(PixelRect area) => _finder.Find(area);
}

internal sealed class WindowsScrollNative : IScrollNative
{
    private const int ExtendedFrameBounds = 9; // DWMWA_EXTENDED_FRAME_BOUNDS

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern nint ChildWindowFromPointEx(nint parent, Point at, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(nint window, ref Point at);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, [Out] char[] name, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect value, int size);

    public nint TopLevelAt(PixelPoint at) => WindowsActionPc.WindowAt(at.X, at.Y);

    public nint ChildAt(nint parent, PixelPoint at)
    {
        var client = new Point { X = at.X, Y = at.Y };
        if (!ScreenToClient(parent, ref client)) return 0;
        // CWP_SKIPINVISIBLE | CWP_SKIPTRANSPARENT: the child that is drawn and takes the point, not a click-through overlay.
        nint child = ChildWindowFromPointEx(parent, client, 0x0001 | 0x0004);
        return child == parent ? 0 : child;
    }

    public string ClassOf(nint window)
    {
        var name = new char[256];
        int length = GetClassName(window, name, name.Length);
        return length > 0 ? new string(name, 0, length) : "";
    }

    public PixelRect? FrameOf(nint window)
    {
        if (!IsWindow(window)) return null;
        if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out var frame, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(window, out frame)) return null;
        return PixelRect.FromEdges(frame.Left, frame.Top, frame.Right, frame.Bottom);
    }

    public bool IsElevated(nint window) => WindowsActionPc.IsWindowElevated(window);

    /// <summary>The walk up from the point stops after this long: a slow provider (a browser building its tree) must not hold Auto's offer.</summary>
    internal static readonly TimeSpan WalkBudget = TimeSpan.FromMilliseconds(400);

    /// <summary>The fallback search over the window's whole tree gives up after this long.</summary>
    internal static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(150);

    public IScrollPane? FindPane(nint topLevel, PixelPoint at, PixelRect area)
    {
        try
        {
            // Up from the element under the point to the nearest pane that scrolls vertically.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var walker = TreeWalker.ControlViewWalker;
            var element = AutomationElement.FromPoint(new System.Windows.Point(at.X, at.Y));
            for (int depth = 0; element != null && depth < 64 && clock.Elapsed < WalkBudget; depth++)
            {
                if (Scrolls(element)) return new UiaPane(element);
                if (element.Current.NativeWindowHandle == (int)topLevel) break;
                element = walker.GetParent(element);
            }
            // A browser's tree is too big to search: only the walk up from the point (above) is tried.
            if (ScrollRules.IsBrowser(ClassOf(topLevel))) return null;
            // Else the largest one in the window that meets the area, within a time budget (the search can't be cancelled, so a
            // slow one is left to finish alone and its answer dropped).
            var search = Task.Run(() => LargestPane(topLevel, area));
            return search.Wait(SearchBudget) ? search.Result : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    private static UiaPane? LargestPane(nint topLevel, PixelRect area)
    {
        try
        {
            var root = AutomationElement.FromHandle(topLevel);
            var found = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
            AutomationElement? best = null; double bestArea = 0;
            foreach (AutomationElement candidate in found)
            {
                if (!Scrolls(candidate)) continue;
                var bounds = candidate.Current.BoundingRectangle;
                if (bounds.IsEmpty) continue;
                var rect = new PixelRect((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
                if (rect.Intersect(area).IsEmpty) continue;
                if (rect.Area > bestArea) { best = candidate; bestArea = rect.Area; }
            }
            return best == null ? null : new UiaPane(best);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    private static bool Scrolls(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern)) return false;
        return ((ScrollPattern)pattern).Current.VerticallyScrollable;
    }

    /// <summary>Reads <see cref="ScrollPattern.ScrollPatternInformation"/> only. Scroll and SetScrollPercent are never called.</summary>
    private sealed class UiaPane(AutomationElement element) : IScrollPane
    {
        public ScrollPosition? Read()
        {
            try
            {
                if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern)) return null;
                var info = ((ScrollPattern)pattern).Current;
                if (!info.VerticallyScrollable || info.VerticalScrollPercent < 0) return null; // NoScroll is -1
                return new ScrollPosition(info.VerticalScrollPercent, info.VerticalViewSize, element.Current.BoundingRectangle.Height);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or System.ComponentModel.Win32Exception)
            { return null; }
        }
    }
}
