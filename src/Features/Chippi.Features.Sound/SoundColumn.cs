// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Features.Sound.Routing;

namespace Chippi.Features.Sound;

/// <summary>
/// A Sound section flags the warning line it shows under its card. The routing rows in the same
/// column then leave out one row per warning, so every row still fits whole (DESIGN.md §5 Island).
/// </summary>
internal static class SoundColumn
{
    public static readonly DependencyProperty WarningProperty = DependencyProperty.RegisterAttached(
        "Warning", typeof(bool), typeof(SoundColumn), new(false, (d, _) => Changed(d)));

    public static void SetWarning(DependencyObject section, bool shown) => section.SetValue(WarningProperty, shown);

    public static bool GetWarning(DependencyObject section) => (bool)section.GetValue(WarningProperty);

    /// <summary>The warning lines the other sections in <paramref name="section"/>'s column show.</summary>
    public static int WarningsBeside(FrameworkElement section) =>
        section.Parent is Panel column ? column.Children.OfType<UIElement>().Count(c => c != section && GetWarning(c)) : 0;

    private static void Changed(DependencyObject section)
    {
        if (section is FrameworkElement { Parent: Panel column })
            foreach (var rows in column.Children.OfType<RoutingQuickView>())
                rows.Refit();
    }
}
