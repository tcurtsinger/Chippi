// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

/// <summary>Real WPF pointer routing without a Window, HWND, presentation source, or desktop input.</summary>
public sealed class IslandAudioTabsTests
{
    private static (T Tabs, ListBoxItem Devices, ListBoxItem Apps) Create<T>() where T : SegmentedControl, new()
    {
        var tabs = new T { Style = (Style)Application.Current.FindResource(typeof(SegmentedControl)) };
        var devices = new ListBoxItem { Content = new TextBlock { Text = "Devices" } };
        var apps = new ListBoxItem { Content = new TextBlock { Text = "App volume" } };
        tabs.Items.Add(devices);
        tabs.Items.Add(apps);
        tabs.SelectedIndex = 1;
        tabs.Measure(new Size(240, 32));
        tabs.Arrange(new Rect(0, 0, 240, 32));
        tabs.UpdateLayout();
        Assert.Null(PresentationSource.FromVisual(tabs));
        Assert.Same(devices, tabs.ItemContainerGenerator.ContainerFromIndex(0));
        return (tabs, devices, apps);
    }

    private static void Press(UIElement source, MouseButton button = MouseButton.Left) =>
        source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button) { RoutedEvent = Mouse.MouseDownEvent });

    [Fact]
    public void Wpf_list_selection_stays_put_when_its_item_cannot_take_focus() => WpfTestThread.Run(() =>
    {
        var (tabs, devices, _) = Create<SegmentedControl>();
        Press(devices);
        Assert.False(devices.IsKeyboardFocused);
        Assert.Equal(1, tabs.SelectedIndex);
    });

    [Fact]
    public void Audio_tabs_select_both_directions_even_when_focus_is_unavailable() => WpfTestThread.Run(() =>
    {
        var (tabs, devices, apps) = Create<IslandTabs>();
        int changes = 0;
        tabs.SelectionChanged += (_, _) => changes++;

        Press((TextBlock)devices.Content);
        Assert.False(devices.IsKeyboardFocused);
        Assert.Equal(0, tabs.SelectedIndex);
        Press(devices);
        Assert.Equal(1, changes);
        Press((TextBlock)apps.Content);
        Assert.False(apps.IsKeyboardFocused);
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Equal(2, changes);
        Assert.Null(PresentationSource.FromVisual(tabs));
    });

    [Fact]
    public void Disabled_tabs_and_non_left_presses_do_not_change_the_page() => WpfTestThread.Run(() =>
    {
        var (tabs, devices, _) = Create<IslandTabs>();
        devices.IsEnabled = false;
        Press(devices);
        Assert.Equal(1, tabs.SelectedIndex);
        devices.IsEnabled = true;
        tabs.IsEnabled = false;
        Press(devices);
        Assert.Equal(1, tabs.SelectedIndex);
        tabs.IsEnabled = true;
        Press(devices, MouseButton.Right);
        Assert.Equal(1, tabs.SelectedIndex);
        Press(tabs); // Empty trough, not an item.
        Assert.Equal(1, tabs.SelectedIndex);
    });
}
