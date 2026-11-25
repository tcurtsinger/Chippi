// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.Core.Diagnostics;

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

    public static BarMenuItem Separator { get; } = new("") { IsSeparator = true };
}

/// <summary>Opens <see cref="BarMenuItem"/> lists as the menus of the menu bar and the dock (BarMenuStyles.xaml).</summary>
internal static class BarMenu
{
    private static ResourceDictionary? _styles;

    internal static ResourceDictionary Styles => _styles ??= new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/Chippi.Features.MacDesktop;component/MenuBar/BarMenuStyles.xaml"),
    };

    /// <summary>
    /// Opens the menu under (or above) <paramref name="anchor"/>, its left edge at the anchor's. <paramref name="closed"/>
    /// runs once it closes, chosen or not.
    /// </summary>
    public static ContextMenu Open(FrameworkElement anchor, IReadOnlyList<BarMenuItem> items, PlacementMode placement = PlacementMode.Bottom,
        double horizontalOffset = -12, double verticalOffset = -2, Action? closed = null)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = placement,
            HorizontalOffset = horizontalOffset,
            VerticalOffset = placement == PlacementMode.Top ? 14 : verticalOffset,
        };
        menu.Resources.MergedDictionaries.Add(Styles);
        menu.Style = (Style)Styles["BarContextMenuStyle"];
        Fill(menu.Items, items);
        menu.Closed += (_, _) => closed?.Invoke();
        menu.IsOpen = true;
        return menu;
    }

    private static void Fill(ItemCollection target, IReadOnlyList<BarMenuItem> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(Row(item));
    }

    private static object Row(BarMenuItem item)
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
                try { Fill(row.Items, children()); }
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
                // After the menu has closed, so the app in front has the keyboard back when it runs.
                row.Dispatcher.BeginInvoke(() =>
                {
                    try { run(); }
                    catch (Exception ex) { Log.Warn($"Mac-style desktop: {item.Text} failed", ex); }
                });
            };
        }
        return row;
    }
}
