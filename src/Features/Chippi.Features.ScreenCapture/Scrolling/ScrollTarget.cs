// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Capture;

namespace Chippi.Features.ScreenCapture.Scrolling;

/// <summary>Whether Auto can scroll the area (docs/phase-7/scrolling-capture.md, Auto). Anything but Available turns Auto off.</summary>
public enum ScrollAvailability
{
    Available,
    /// <summary>No window under the area's center.</summary>
    NoWindow,
    /// <summary>The area isn't inside one top-level window.</summary>
    NotInOneWindow,
    /// <summary>The window is elevated, and refuses a standard process's input.</summary>
    Elevated,
    /// <summary>UI Automation finds no vertically scrollable pane under the area.</summary>
    NoScrollPane,
}

/// <summary>
/// A scrollable pane's position, read through UI Automation (never changed). <see cref="Percent"/> is the vertical scroll
/// position, 0 to 100; <see cref="ViewSize"/> is how much of the content the pane shows, 0 to 100; <see cref="PaneHeight"/> is
/// the pane's height in physical pixels.
/// </summary>
public readonly record struct ScrollPosition(double Percent, double ViewSize, double PaneHeight)
{
    /// <summary>The end: 99.999% scrolled.</summary>
    public const double EndPercent = 99.999;

    public bool IsEnd => Percent >= EndPercent;

    /// <summary>Whether this reading can predict a shift: the content is taller than the pane.</summary>
    public bool CanPredict => ViewSize is > 0 and < 100 && PaneHeight > 0;

    /// <summary>The scrollable distance in pixels: the content's height less the pane's.</summary>
    public double RangePixels => CanPredict ? PaneHeight * (100 / ViewSize - 1) : 0;

    /// <summary>
    /// How far the content moved up, in pixels, since the earlier reading (positive is down the page), or null when it can't
    /// be told. Uses this reading's view size, so a page that grew while scrolled predicts roughly, which the stitcher's
    /// narrow search tolerates.
    /// </summary>
    public int? ShiftSince(ScrollPosition earlier) => CanPredict ? (int)Math.Round((Percent - earlier.Percent) / 100 * RangePixels) : null;
}

/// <summary>A pane whose position can be read. Reads only; scrolling it through UI Automation is never done.</summary>
public interface IScrollPane
{
    /// <summary>The position now, or null when the pane no longer answers.</summary>
    ScrollPosition? Read();
}

/// <summary>What <see cref="IScrollTarget.Find"/> learned about the area's window.</summary>
public sealed record ScrollTargetInfo(
    ScrollAvailability Availability, nint TopLevel, nint Deepest, IReadOnlyList<string> Classes, bool Elevated, IScrollPane? Pane)
{
    public bool IsAvailable => Availability == ScrollAvailability.Available;

    /// <summary>The window that takes the posted wheel: the deepest child under the area's center.</summary>
    public nint WheelWindow => Deepest != 0 ? Deepest : TopLevel;

    /// <summary>The refusal for a window that isn't there: nothing to scroll.</summary>
    public static ScrollTargetInfo None(ScrollAvailability reason) => new(reason, 0, 0, [], false, null);
}

/// <summary>Finds what Auto would scroll for an area. Reads only: no window is moved, activated or sent anything.</summary>
public interface IScrollTarget
{
    /// <summary>The window and pane under the area's center, with whether Auto is offered. Never null.</summary>
    ScrollTargetInfo Find(PixelRect area);
}

/// <summary>The rules of <see cref="IScrollTarget"/>, free of Windows.</summary>
public static class ScrollRules
{
    /// <summary>The WinUI 3 input site: the child that takes input in a XAML window.</summary>
    public const string InputSiteClass = "InputSiteWindowClass";

    /// <summary>
    /// Top-level window classes of browsers and Electron apps (Chromium, Firefox). They honor a posted wheel (checked on Edge in
    /// the Sandbox), but their accessibility tree is huge and slow to build, so the pane search stays near the point.
    /// </summary>
    public static readonly IReadOnlySet<string> BrowserClasses =
        new HashSet<string>(StringComparer.Ordinal) { "Chrome_WidgetWin_1", "MozillaWindowClass" };

    public static bool IsBrowser(string windowClass) => BrowserClasses.Contains(windowClass);

    /// <summary>
    /// The index in a path (top-level window first, deepest last) of the window that takes the wheel: the deepest window of
    /// the input site class when there is one, else the deepest window.
    /// </summary>
    public static int WheelIndex(IReadOnlyList<string> classesOnPath)
    {
        if (classesOnPath.Count == 0) return -1;
        for (int i = classesOnPath.Count - 1; i >= 0; i--)
            if (classesOnPath[i] == InputSiteClass) return i;
        return classesOnPath.Count - 1;
    }

    /// <summary>
    /// Auto's conditions in the brief's order, from what was found: a window under the center, the whole area in one top-level
    /// window, not elevated, and a scrollable pane. A window that ignores the posted wheel is found by Auto's first step.
    /// </summary>
    public static ScrollAvailability Decide(bool hasWindow, bool areaInOneWindow, bool elevated, bool hasPane)
    {
        if (!hasWindow) return ScrollAvailability.NoWindow;
        if (!areaInOneWindow) return ScrollAvailability.NotInOneWindow;
        if (elevated) return ScrollAvailability.Elevated;
        return hasPane ? ScrollAvailability.Available : ScrollAvailability.NoScrollPane;
    }

    /// <summary>
    /// The point a posted wheel is addressed to: the area's center, in physical screen pixels. Windows applies the wheel to the
    /// window under it, so it is the point UI Automation also reads.
    /// </summary>
    public static PixelPoint Center(PixelRect area) => new(area.X + area.Width / 2, area.Y + area.Height / 2);
}

/// <summary>The Win32 and UI Automation reads <see cref="ScrollTargetFinder"/> makes. A fake stands in for it in tests.</summary>
internal interface IScrollNative
{
    /// <summary>The top-level window under the screen point, or 0 (the desktop, the taskbar, a window of Chippi's).</summary>
    nint TopLevelAt(PixelPoint at);

    /// <summary>The child of <paramref name="parent"/> under the screen point (visible, not transparent), or 0 when the point is on the parent itself.</summary>
    nint ChildAt(nint parent, PixelPoint at);

    string ClassOf(nint window);

    /// <summary>The window's visible frame, or null when it's gone.</summary>
    PixelRect? FrameOf(nint window);

    bool IsElevated(nint window);

    /// <summary>The vertically scrollable pane at the point, else the largest in the window under the area; null when there is none. Read only.</summary>
    IScrollPane? FindPane(nint topLevel, PixelPoint at, PixelRect area);
}

/// <summary>The scroll target: the deepest child under the area's center, the classes on the path, elevation and the UIA pane.</summary>
internal sealed class ScrollTargetFinder(IScrollNative native) : IScrollTarget
{
    private const int MaxDepth = 32;

    public ScrollTargetInfo Find(PixelRect area)
    {
        if (area.IsEmpty) return ScrollTargetInfo.None(ScrollAvailability.NoWindow);
        var center = ScrollRules.Center(area);
        nint top = native.TopLevelAt(center);
        if (top == 0) return ScrollTargetInfo.None(ScrollAvailability.NoWindow);

        var path = new List<nint> { top };
        var classes = new List<string> { native.ClassOf(top) };
        nint current = top;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            nint child = native.ChildAt(current, center);
            if (child == 0 || child == current || path.Contains(child)) break;
            path.Add(child); classes.Add(native.ClassOf(child));
            current = child;
        }
        int wheel = ScrollRules.WheelIndex(classes);
        nint deepest = wheel < 0 ? top : path[wheel];

        bool inOne = InOneWindow(top, area);
        bool elevated = native.IsElevated(top);
        // The pane is read last, and only when the cheaper rules haven't refused: UI Automation is the slow part.
        var before = ScrollRules.Decide(true, inOne, elevated, hasPane: true);
        IScrollPane? pane = null;
        var availability = before;
        if (before == ScrollAvailability.Available)
        {
            pane = native.FindPane(top, center, area);
            availability = ScrollRules.Decide(true, inOne, elevated, pane != null);
        }
        return new ScrollTargetInfo(availability, top, deepest, classes, elevated, pane);
    }

    // The frame holds the area, and each corner of it belongs to the same top-level window (nothing else lies over it).
    private bool InOneWindow(nint top, PixelRect area)
    {
        if (native.FrameOf(top) is not { } frame || !frame.Contains(area)) return false;
        return native.TopLevelAt(new PixelPoint(area.X, area.Y)) == top
            && native.TopLevelAt(new PixelPoint(area.Right - 1, area.Y)) == top
            && native.TopLevelAt(new PixelPoint(area.X, area.Bottom - 1)) == top
            && native.TopLevelAt(new PixelPoint(area.Right - 1, area.Bottom - 1)) == top;
    }
}
