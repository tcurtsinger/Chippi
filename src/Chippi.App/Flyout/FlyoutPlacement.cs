// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using Chippi.App.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace Chippi.App.Flyout;

internal enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>A rectangle in physical screen pixels.</summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public static PixelRect From(RECT r) => new(r.left, r.top, r.right, r.bottom);

    public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
}

/// <summary>Everything the placement math needs about the display holding the tray icon.</summary>
/// <param name="Monitor">The monitor's full bounds.</param>
/// <param name="Work">Its work area (the monitor minus the taskbar and other app bars).</param>
/// <param name="Edge">Which edge the taskbar is on.</param>
/// <param name="TaskbarInner">The taskbar's inner edge on that axis (its top edge for a bottom taskbar).</param>
/// <param name="AnchorAlong">The icon's center along the taskbar (x for a horizontal taskbar).</param>
/// <param name="Dpi">The monitor's effective DPI.</param>
internal readonly record struct TrayDisplay(PixelRect Monitor, PixelRect Work, TaskbarEdge Edge, int TaskbarInner, int AnchorAlong, uint Dpi)
{
    public double Scale => Dpi / 96.0;
}

/// <summary>
/// Where the flyout card goes (DESIGN.md §5): 12px off the taskbar and 16px from the screen edge
/// at the tray's end of it, on whichever edge the taskbar sits, in the monitor's own DPI.
/// </summary>
internal static class FlyoutPlacement
{
    /// <summary>DIPs between the card and the taskbar.</summary>
    public const double TaskbarGap = 12;

    /// <summary>DIPs between the card and the screen edge at the tray's end of the taskbar.</summary>
    public const double ScreenInset = 16;

    /// <summary>
    /// Uses the notification area's taskbar monitor, never the pointer's monitor. Closed overflow
    /// icons can report stale or off-screen rectangles, so only visible shell surfaces are trusted.
    /// </summary>
    public static TrayDisplay Describe(RECT? iconRect, out bool fallback)
    {
        var taskbar = PInvoke.FindWindow("Shell_TrayWnd", null);
        var monitor = PInvoke.MonitorFromWindow(taskbar, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        RECT? barRect = !taskbar.IsNull && PInvoke.GetWindowRect(taskbar, out var bar) ? bar : null;
        RECT? overflowRect = null;
        foreach (string className in new[] { "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow" })
        {
            var overflow = PInvoke.FindWindow(className, null);
            if (!overflow.IsNull && PInvoke.IsWindowVisible(overflow)
                && PInvoke.MonitorFromWindow(overflow, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST) == monitor
                && PInvoke.GetWindowRect(overflow, out var panel))
            {
                overflowRect = panel;
                break;
            }
        }
        fallback = !IsTrustedIcon(iconRect, barRect, overflowRect);

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        PInvoke.GetMonitorInfo(monitor, ref info);
        var bounds = PixelRect.From(info.rcMonitor);
        var work = PixelRect.From(info.rcWork);

        int anchorX = fallback ? bounds.Right - 1 : (iconRect!.Value.left + iconRect.Value.right) / 2;
        int anchorY = fallback ? bounds.Bottom - 1 : (iconRect!.Value.top + iconRect.Value.bottom) / 2;
        var (edge, inner) = FindTaskbar(bounds, work, anchorX, anchorY);
        int along = edge is TaskbarEdge.Bottom or TaskbarEdge.Top ? anchorX : anchorY;
        return new TrayDisplay(bounds, work, edge, inner, along, MonitorDpi.Of(monitor));
    }

    /// <summary>A nonempty icon must fit entirely inside the taskbar or its visible overflow panel.</summary>
    internal static bool IsTrustedIcon(RECT? icon, RECT? taskbar, RECT? overflow) =>
        icon is RECT r && r.right > r.left && r.bottom > r.top
        && (Contains(taskbar, r) || Contains(overflow, r));

    private static bool Contains(RECT? container, RECT icon) => container is RECT r
        && icon.left >= r.left && icon.top >= r.top && icon.right <= r.right && icon.bottom <= r.bottom;

    /// <summary>
    /// The card's top-left in physical pixels for a card of <paramref name="cardWidth"/> by
    /// <paramref name="cardHeight"/> DIPs. Pure, so the 100/150/200% cases can be checked by hand.
    /// </summary>
    public static (int X, int Y) Place(TrayDisplay display, double cardWidth, double cardHeight)
    {
        double scale = display.Scale;
        double w = cardWidth * scale, h = cardHeight * scale;
        double gap = TaskbarGap * scale, inset = ScreenInset * scale;
        var work = display.Work;
        double x, y;

        if (display.Edge is TaskbarEdge.Bottom or TaskbarEdge.Top)
        {
            // The tray sits at the right end in left-to-right layouts; follow the icon if not.
            bool rightEnd = display.AnchorAlong >= (display.Monitor.Left + display.Monitor.Right) / 2;
            x = rightEnd ? work.Right - inset - w : work.Left + inset;
            y = display.Edge == TaskbarEdge.Bottom ? display.TaskbarInner - gap - h : display.TaskbarInner + gap;
        }
        else
        {
            bool bottomEnd = display.AnchorAlong >= (display.Monitor.Top + display.Monitor.Bottom) / 2;
            y = bottomEnd ? work.Bottom - inset - h : work.Top + inset;
            x = display.Edge == TaskbarEdge.Right ? display.TaskbarInner - gap - w : display.TaskbarInner + gap;
        }

        // Never off the work area, even on a display too small for the whole card.
        x = Math.Max(work.Left, Math.Min(x, work.Right - w));
        y = Math.Max(work.Top, Math.Min(y, work.Bottom - h));
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    /// <summary>
    /// The transparent margin around a card for its window lift, trimmed so the window never
    /// reaches over the taskbar or past the work area. A shadow pixel is not click-through, so an
    /// untrimmed margin would swallow clicks meant for the tray icon; trimmed, the taskbar simply
    /// appears to sit over the end of the shadow. Card position in pixels, size in DIPs.
    /// </summary>
    public static Thickness ShadowMargin(Thickness wanted, TrayDisplay display, int x, int y, double cardWidth, double cardHeight)
    {
        double scale = display.Scale;
        var work = display.Work;
        double left = work.Left, top = work.Top, right = work.Right, bottom = work.Bottom;
        switch (display.Edge)
        {
            case TaskbarEdge.Bottom:
                bottom = Math.Min(bottom, display.TaskbarInner);
                break;
            case TaskbarEdge.Top:
                top = Math.Max(top, display.TaskbarInner);
                break;
            case TaskbarEdge.Left:
                left = Math.Max(left, display.TaskbarInner);
                break;
            case TaskbarEdge.Right:
                right = Math.Min(right, display.TaskbarInner);
                break;
        }
        return new Thickness(
            Fit(wanted.Left, x - left),
            Fit(wanted.Top, y - top),
            Fit(wanted.Right, right - (x + (cardWidth * scale))),
            Fit(wanted.Bottom, bottom - (y + (cardHeight * scale))));

        double Fit(double want, double roomPixels) => Math.Max(0, Math.Min(want, Math.Floor(roomPixels / scale)));
    }

    /// <summary>The tallest card that fits between the taskbar gap and the far side of the work area, in DIPs.</summary>
    public static double MaxCardHeight(TrayDisplay display)
    {
        double scale = display.Scale;
        return display.Edge switch
        {
            TaskbarEdge.Bottom => ((display.TaskbarInner - display.Work.Top) / scale) - TaskbarGap - ScreenInset,
            TaskbarEdge.Top => ((display.Work.Bottom - display.TaskbarInner) / scale) - TaskbarGap - ScreenInset,
            _ => (display.Work.Height / scale) - (2 * ScreenInset),
        };
    }

    private static (TaskbarEdge Edge, int Inner) FindTaskbar(PixelRect monitor, PixelRect work, int iconX, int iconY)
    {
        // A taskbar that reserves space shows up as the work area pulled in from one edge.
        if (work.Bottom < monitor.Bottom)
            return (TaskbarEdge.Bottom, work.Bottom);
        if (work.Top > monitor.Top)
            return (TaskbarEdge.Top, work.Top);
        if (work.Left > monitor.Left)
            return (TaskbarEdge.Left, work.Left);
        if (work.Right < monitor.Right)
            return (TaskbarEdge.Right, work.Right);

        // Auto-hide: ask the shell where the taskbar is, if it is on this monitor.
        var bar = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        if (PInvoke.SHAppBarMessage(PInvoke.ABM_GETTASKBARPOS, ref bar) != 0)
        {
            var r = PixelRect.From(bar.rc);
            bool onThisMonitor = r.Right > monitor.Left && r.Left < monitor.Right && r.Bottom > monitor.Top && r.Top < monitor.Bottom;
            if (onThisMonitor)
            {
                return bar.uEdge switch
                {
                    PInvoke.ABE_TOP => (TaskbarEdge.Top, Math.Max(r.Bottom, monitor.Top)),
                    PInvoke.ABE_LEFT => (TaskbarEdge.Left, Math.Max(r.Right, monitor.Left)),
                    PInvoke.ABE_RIGHT => (TaskbarEdge.Right, Math.Min(r.Left, monitor.Right)),
                    _ => (TaskbarEdge.Bottom, Math.Min(r.Top, monitor.Bottom)),
                };
            }
        }

        // Last resort: the monitor edge nearest the icon.
        int toBottom = monitor.Bottom - iconY, toTop = iconY - monitor.Top;
        int toLeft = iconX - monitor.Left, toRight = monitor.Right - iconX;
        int nearest = Math.Min(Math.Min(toBottom, toTop), Math.Min(toLeft, toRight));
        if (nearest == toBottom)
            return (TaskbarEdge.Bottom, monitor.Bottom);
        if (nearest == toTop)
            return (TaskbarEdge.Top, monitor.Top);
        return nearest == toLeft ? (TaskbarEdge.Left, monitor.Left) : (TaskbarEdge.Right, monitor.Right);
    }
}
