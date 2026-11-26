// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;
using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>One row of a menu bar or dock menu. <see cref="Children"/> is read when its submenu opens.</summary>
public sealed record BarMenuItem(string Text)
{
    public string? Hint { get; init; }
    public Action? Run { get; init; }
    public bool Enabled { get; init; } = true;
    public bool Checked { get; init; }
    public ImageSource? Icon { get; init; }
    public Func<IReadOnlyList<BarMenuItem>>? Children { get; init; }
    public bool IsSeparator { get; private init; }

    /// <summary>
    /// The row acts in the app that was in front (a command of its own, closing or minimizing its window): that app gets
    /// the keyboard back before the row runs, so a dialog it opens comes up with the keyboard. Any other row runs while
    /// the menu's hold lasts, which is what lets it bring another window forward or open a question of Chippi's own.
    /// </summary>
    public bool InFront { get; init; }

    public static BarMenuItem Separator { get; } = new("") { IsSeparator = true };
}

/// <summary>
/// Opens <see cref="BarMenuItem"/> lists as the menus of the menu bar and the dock (BarMenuStyles.xaml). A menu holds the
/// keyboard while it's open (<see cref="MenuFocus"/>), so Esc, the arrow keys and a click elsewhere work as in any menu.
/// </summary>
internal static class BarMenu
{
    private static ResourceDictionary? _styles;

    internal static ResourceDictionary Styles => _styles ??= new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/Chippi.Features.MacDesktop;component/MenuBar/BarMenuStyles.xaml"),
    };

    /// <summary>The resource a menu and its submenus take their greatest height from: the display's work area, less a margin.</summary>
    internal const string MaxHeightKey = "BarMenuMaxHeight";

    /// <summary>
    /// Opens the menu under (or above) <paramref name="anchor"/>, its left edge at the anchor's. <paramref name="closed"/>
    /// runs once it closes, chosen or not.
    /// </summary>
    public static ContextMenu Open(FrameworkElement anchor, IReadOnlyList<BarMenuItem> items, PlacementMode placement = PlacementMode.Bottom,
        double horizontalOffset = -12, double verticalOffset = -2, Action? closed = null)
    {
        // Neither the bar nor the dock ever takes the foreground, and a menu in a process that hasn't it can't close on a
        // click elsewhere or on Esc. MenuFocus takes the keyboard before the menu opens, as an app's window holds it for
        // the app's own menus, and the menu then closes as any menu does. Windows refusing (rare, right after a click)
        // leaves a menu that works with the mouse; only then is a press elsewhere watched for.
        var focus = MenuFocus.Shared;
        var holder = new object(); // the menu itself isn't made yet
        ContextMenu? opened = null;
        bool held = focus.Hold(holder, Native.Cursor(), () => { if (opened is { IsOpen: true }) opened.IsOpen = false; });
        DispatcherTimer? watch = null;
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = placement,
            HorizontalOffset = horizontalOffset,
            VerticalOffset = verticalOffset,
        };
        menu.Resources.MergedDictionaries.Add(Styles);
        menu.Resources[MaxHeightKey] = MaxHeight(anchor);
        menu.Style = (Style)Styles["BarContextMenuStyle"];
        Fill(menu.Items, items, () => focus.Release(holder));
        menu.Closed += (_, _) =>
        {
            watch?.Stop();
            // A menu reports this once it has faded out. By then its row has run, and what that opened (a question,
            // another menu) may hold the keyboard: this lets go only of its own hold.
            focus.Release(holder);
            closed?.Invoke();
        };
        opened = menu;
        try { menu.IsOpen = true; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn("Mac-style desktop: a menu didn't open", ex);
            focus.Release(holder);
            closed?.Invoke();
            return menu;
        }
        if (!menu.IsOpen)
            focus.Release(holder);
        else if (!held)
            watch = Watch(menu);
        return menu;
    }

    /// <summary>A menu no taller than its display's work area, so a long one scrolls instead of running off the screen.</summary>
    private static double MaxHeight(FrameworkElement anchor)
    {
        if (PresentationSource.FromVisual(anchor) is null)
            return 720;
        var at = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, anchor.ActualHeight / 2));
        nint monitor = Native.MonitorFromPoint(new Native.Point { X = (int)at.X, Y = (int)at.Y }, Native.MonitorDefaultToNearest);
        return Native.Area(monitor) is { } area ? Math.Max(240, area.Work.Height / area.Scale - 48) : 720;
    }

    /// <summary>
    /// Only for a menu that opened without the keyboard (Windows refused it): a press outside it closes it, which its own
    /// hold on the mouse can't see from a process that isn't in front.
    /// </summary>
    private static DispatcherTimer Watch(ContextMenu menu)
    {
        bool pressed = Pressing();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Input, (_, _) =>
        {
            if (!menu.IsOpen)
                return;
            bool pressing = Pressing();
            if (pressing && !pressed && PresentationSource.FromVisual(menu) is not null
                && !new Rect(menu.RenderSize).Contains(menu.PointFromScreen(Native.Cursor())) && !SubmenuUnder(menu.Items))
                menu.IsOpen = false;
            pressed = pressing;
        }, menu.Dispatcher);
        timer.Start();
        return timer;
    }

    private static bool Pressing() => (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;

    /// <summary>Whether the pointer is over one of the open submenus, which are popups of their own.</summary>
    private static bool SubmenuUnder(ItemCollection items) => items.OfType<MenuItem>().Any(item =>
        item.IsSubmenuOpen && ((item.Template?.FindName("PART_Popup", item) is Popup { Child: FrameworkElement child }
            && PresentationSource.FromVisual(child) is not null && new Rect(child.RenderSize).Contains(child.PointFromScreen(Native.Cursor())))
        || SubmenuUnder(item.Items)));

    private static void Fill(ItemCollection target, IReadOnlyList<BarMenuItem> items, Action release)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(Row(item, release));
    }

    private static object Row(BarMenuItem item, Action release)
    {
        if (item.IsSeparator)
            return new Separator();
        var row = new MenuItem
        {
            Header = new TextBlock { Text = item.Text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 },
            InputGestureText = item.Hint ?? "",
            IsEnabled = item.Enabled,
            IsChecked = item.Checked,
        };
        if (item.Icon is { } icon)
            row.Icon = new Image { Source = icon, Width = 16, Height = 16 };
        if (item.Children is { } children)
        {
            row.Items.Add(new MenuItem { Header = "", Visibility = Visibility.Collapsed }); // makes it a submenu header
            row.SubmenuOpened += (_, e) =>
            {
                if (e.OriginalSource != row)
                    return;
                try { Fill(row.Items, children(), release); }
                catch (Exception ex)
                {
                    Log.Warn("Mac-style desktop: reading a submenu failed", ex);
                    row.Items.Clear();
                    row.Items.Add(new MenuItem { Header = new TextBlock { Text = "Failed" }, IsEnabled = false });
                }
            };
        }
        else if (item.Run is { } run)
        {
            row.Click += (_, _) =>
            {
                // After the click has closed the menu. A row that acts in the app that was in front gives it the keyboard
                // back first; the others run while the menu's hold lasts, and the hold goes when the menu has faded out.
                row.Dispatcher.BeginInvoke(() =>
                {
                    if (item.InFront)
                        release();
                    try { run(); }
                    catch (Exception ex) { Log.Warn($"Mac-style desktop: {item.Text} failed", ex); }
                });
            };
        }
        return row;
    }
}
