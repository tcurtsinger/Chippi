// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.Core.Island;
using Chippi.UI.Pet;
using Chippi.UI.Themes;

namespace Chippi.Island.Tests;

[Collection("ReducedMotion")]
public sealed class WidgetSplitTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Song_and_pet_open_separate_destinations(bool opensLeft, bool activity, bool invoke)
    {
        WithWidget(opensLeft, window =>
        {
            window.SetTiles(IslandPanelTests.Tiles(IslandPanelTests.Part("keep-awake", "Awake", 40)));
            window.SetLastPage("keep-awake");
            window.SetMedia(new IslandPanelTests.FakeMedia
            {
                Sessions = [new("spotify", "Spotify", "Creep", "Radiohead", true, true, true, true, true, true,
                    TimeSpan.Zero, TimeSpan.FromMinutes(4), DateTimeOffset.Now, null)],
            });
            window.SetActivities([new IslandActivity("track", IslandActivityKind.Media, "Creep", "Spotify")], PetMood.Idle);
            window.UpdateLayout();

            Assert.Equal(50, window.HomeButton.ActualWidth);
            Assert.Equal(206, window.ActivityButton.ActualWidth);
            Assert.Equal(opensLeft ? 206 : 0, Canvas.GetLeft(window.HomeButton));
            Assert.Equal("Open Chippi", window.HomeButton.ToolTip);
            Assert.Equal("Open Now Playing", window.ActivityButton.ToolTip);
            Assert.Contains("Creep", AutomationProperties.GetName(window.ActivityButton));
            AssertTargets(window);

            Activate(window, activity ? window.ActivityButton : window.HomeButton, invoke);
            Assert.True(window.IsIslandOpen);
            Assert.Equal(activity ? IslandPanelWindow.NowPlayingKey : "keep-awake", window.Panel.CurrentPage);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Custom_activity_action_runs_only_from_its_section(bool opensLeft, bool activity)
    {
        WithWidget(opensLeft, window =>
        {
            int clicks = 0;
            window.SetActivities([new IslandActivity("capture", IslandActivityKind.Timer, "Cancel capture", "3 seconds")
                { Clicked = () => clicks++ }], PetMood.Idle);
            window.UpdateLayout();
            Activate(window, activity ? window.ActivityButton : window.HomeButton, invoke: true);
            Assert.Equal(activity ? 1 : 0, clicks);
            Assert.Equal(!activity, window.IsIslandOpen);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Activity_page_shortcut_preserves_the_regular_last_page(bool opensLeft)
    {
        WithWidget(opensLeft, window =>
        {
            window.SetTiles(IslandPanelTests.Tiles(IslandPanelTests.Part("keep-awake", "Awake", 40),
                IslandPanelTests.Part("timer", "Timer", 20)));
            window.SetLastPage("keep-awake");
            window.SetActivities([new IslandActivity("timer", IslandActivityKind.Timer, "18:42", "Focus timer")
                { TileKey = "timer" }], PetMood.Idle);
            window.UpdateLayout();
            Activate(window, window.ActivityButton, invoke: true);
            Assert.Equal("timer", window.Panel.CurrentPage);
            IslandWindowTests.Settle(window.Panel);
            window.Escape();
            IslandWindowTests.Settle(window.Panel);
            Activate(window, window.HomeButton, invoke: true);
            Assert.Equal("keep-awake", window.Panel.CurrentPage);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compact_status_tab_has_one_target_clipped_to_its_outline(bool opensLeft)
    {
        WithWidget(opensLeft, window =>
        {
            window.SetActivities([new IslandActivity("mic", IslandActivityKind.Status, "Muted", "Microphone")
                { ModuleId = "mic-mute", NeedsAttention = true }], PetMood.Idle);
            window.UpdateLayout();
            Assert.False(window.BarShown);
            Assert.Equal(Visibility.Collapsed, window.ActivityButton.Visibility);
            Assert.Equal(window.Body.ActualWidth, window.HomeButton.ActualWidth);
            Assert.True(window.HomeButton.ActualWidth > 50);
            AssertTargets(window);
        });
    }

    private static void AssertTargets(IslandWindow window)
    {
        // No dead strip or overlapping target, and no clickable ink outside the curved outline.
        for (double x = 0.5; x < window.Body.ActualWidth; x += 3)
        for (double y = 0.5; y < 50; y += 3)
        {
            var point = new Point(x, y);
            var hit = window.ClickTargets.InputHitTest(point) as DependencyObject;
            while (hit is not null && hit is not Button)
                hit = VisualTreeHelper.GetParent(hit);
            bool inside = window.Body.ContainsLocal(point);
            Assert.Equal(inside, hit is Button);
            if (inside)
            {
                bool activity = window.ActivityButton.IsVisible && x >= Canvas.GetLeft(window.ActivityButton)
                    && x < Canvas.GetLeft(window.ActivityButton) + window.ActivityButton.ActualWidth;
                Assert.Same(activity ? window.ActivityButton : window.HomeButton, hit);
            }
        }
    }

    private static void Activate(IslandWindow window, Button target, bool invoke)
    {
        if (invoke)
        {
            var peer = new ButtonAutomationPeer(target);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            IslandWindowTests.Pump(TimeSpan.Zero);
            return;
        }
        var at = target.PointToScreen(new Point(target.ActualWidth / 2, 25));
        window.PointerAt = () => at;
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
    }

    private static void WithWidget(bool opensLeft, Action<IslandWindow> test) => WpfTestThread.Run(() =>
    {
        var motion = Motion.Override;
        Motion.Override = true;
        try
        {
            IslandWindowTests.WithShownIsland(window =>
            {
                window.SetPlacement(opensLeft ? 0.8 : 0.2, 0.8);
                Assert.Equal(opensLeft, window.OpensLeft);
                window.Panel.ActivateWindow = _ => { };
                window.Panel.PreviewGotKeyboardFocus += (_, e) => e.Handled = true;
                test(window);
            });
        }
        finally { Motion.Override = motion; }
    });
}
