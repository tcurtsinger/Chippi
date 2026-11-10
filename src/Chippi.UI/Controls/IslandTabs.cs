// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Chippi.UI.Controls;

/// <summary>Island tabs remain clickable when a fullscreen app keeps the keyboard.</summary>
public sealed class IslandTabs : SegmentedControl
{
    public IslandTabs() => AddHandler(MouseLeftButtonDownEvent,
        new MouseButtonEventHandler(SelectPressedTab), handledEventsToo: true);

    private void SelectPressedTab(object sender, MouseButtonEventArgs e)
    {
        // ListBoxItem handles the press but only selects if Focus() succeeds. Let its normal
        // focus/keyboard behavior run first, then select even when Windows refused focus.
        if (!IsEnabled || e.OriginalSource is not DependencyObject source
            || ContainerFromElement(this, source) is not ListBoxItem { IsEnabled: true } item)
            return;
        int index = ItemContainerGenerator.IndexFromContainer(item);
        if (index >= 0 && SelectedIndex != index)
            SetCurrentValue(SelectedIndexProperty, index);
    }
}
