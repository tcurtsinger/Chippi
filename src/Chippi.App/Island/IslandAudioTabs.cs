// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>Audio's tabs remain clickable when a fullscreen app keeps the keyboard.</summary>
internal sealed class IslandAudioTabs : SegmentedControl
{
    public IslandAudioTabs() => AddHandler(MouseLeftButtonDownEvent,
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
