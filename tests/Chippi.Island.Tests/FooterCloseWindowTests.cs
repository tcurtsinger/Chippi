// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.App.Island;

namespace Chippi.Island.Tests;

/// <summary>Native window integration is for CI, never the owner's active desktop.</summary>
[Collection("ReducedMotion")]
public sealed class FooterCloseWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_footer_pet_closes_normal_and_pinned_Peek_without_taking_foreground(bool peek) => WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
    {
        int activated = 0, peekClosed = 0;
        window.Panel.IsForegroundWindow = _ => false; window.Panel.ActivateWindow = _ => activated++;
        window.PeekClosed += () => peekClosed++;
        Assert.True(peek ? window.OpenPeek(null) : window.ShowPages(false)); IslandWindowTests.Settle(window.Panel);
        var close = FindClose(window.Panel);
        var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        ((UIElement)close.Content).RaiseEvent(press);
        Assert.True(press.Handled); Assert.False(window.IsIslandOpen); Assert.Equal(0, activated);
        Assert.Equal(peek ? 1 : 0, peekClosed); IslandWindowTests.Settle(window.Panel);

        // The normal Button action remains available to keyboard and automation callers.
        Assert.True(peek ? window.OpenPeek(null) : window.ShowPages(false)); IslandWindowTests.Settle(window.Panel);
        FindClose(window.Panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(window.IsIslandOpen); Assert.Equal(peek ? 2 : 0, peekClosed);
    }));

    private static Button FindClose(DependencyObject root) => All(root).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Close Chippi");
    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in All(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
