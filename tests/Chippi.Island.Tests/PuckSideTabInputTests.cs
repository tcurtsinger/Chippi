// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Chippi.App.Island;
using Chippi.Core.Island;
using Chippi.UI.Pet;
using Chippi.UI.Themes;

namespace Chippi.Island.Tests;

[Collection("ReducedMotion")]
public sealed class PuckSideTabInputTests
{
    private static readonly IslandActivity Muted = new("mic", IslandActivityKind.Status, "Muted", "Microphone")
        { ModuleId = "mic-mute", NeedsAttention = true };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Clicking_the_tab_opens_from_its_outline_on_either_side(int edge) =>
        WithWidget(false, window =>
        {
            window.SetPlacement(edge, 0.5);
            var anchor = window.PuckRect;
            window.SetActivities([Muted], PetMood.Idle);
            var body = window.Body;
            IslandWindowTests.PumpUntil(() => body.ActualWidth > IslandGeometry.Puck
                && Math.Abs(body.ActualWidth - (IslandGeometry.Puck + body.TabExtension)) < 0.01);
            Assert.Equal(anchor, window.PuckRect);
            Assert.False(window.BarShown);
            double x = window.OpensLeft ? body.TabExtension / 2 : body.ActualWidth - body.TabExtension / 2;
            var middle = new Point(x, 25);
            var shoulder = new Point(x, 4);
            var pointer = body.PointToScreen(middle);
            Assert.True(window.CoversWidget(pointer));
            Assert.False(window.CoversWidget(body.PointToScreen(shoulder)));
            Assert.NotEqual(-1, NativeHit(window, pointer));
            Assert.Equal(-1, NativeHit(window, body.PointToScreen(shoulder)));
            window.PointerAt = () => pointer;
            var panel = window.Panel;
            panel.Clock = () => 0; // inspect the source before the two native surfaces separate
            Press(window, UIElement.PreviewMouseLeftButtonDownEvent);
            Press(window, UIElement.PreviewMouseLeftButtonUpEvent);
            Assert.True(panel.IsOpen);
            panel.UpdateLayout();
            Assert.Equal(body.TabExtension, panel.ShapeFill.TabExtension);
            Assert.Equal(body.ActualWidth, panel.ShapeFill.ActualWidth, 2);
            Assert.True(panel.ShapeFill.ContainsLocal(middle));
            Assert.False(panel.ShapeFill.ContainsLocal(shoulder));
            window.Escape(); // reverse the open while the tab and widget still coincide
            IslandWindowTests.Settle(panel);
            Assert.False(panel.IsVisible);
            Assert.Equal(anchor, window.PuckRect);
            Assert.True(window.CoversWidget(pointer));
        });

    [Fact]
    public void Dragging_the_tab_across_center_turns_it_inward_without_moving_Chippi_on_release() =>
        WithWidget(true, window =>
        {
            var area = window.CurrentArea!.Value;
            window.SetPlacement(1, 0.5);
            window.SetActivities([Muted], PetMood.Idle);
            window.UpdateLayout();
            double width = window.Body.ActualWidth;
            Assert.True(width > IslandGeometry.Puck);
            Assert.True(window.OpensLeft);
            var grip = new Point((width - IslandGeometry.Puck) / 2, 25);
            var pointer = window.Body.PointToScreen(grip);
            window.PointerAt = () => pointer;
            Press(window, UIElement.PreviewMouseLeftButtonDownEvent);
            pointer = new Point(area.Work.Left + area.Work.Width / 4, area.Work.Top + area.Work.Height / 2);
            window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            window.UpdateLayout();
            Assert.True(window.CoversWidget(pointer));
            Assert.InRange((window.Body.PointToScreen(grip) - pointer).Length, 0, 2);
            Assert.Equal(width, window.Body.ActualWidth, 2);
            var heldPet = window.PuckRect;
            Press(window, UIElement.PreviewMouseLeftButtonUpEvent);
            Assert.Equal(heldPet, window.PuckRect);
            Assert.False(window.OpensLeft);
            Assert.False(window.Panel.IsOpen);
            Assert.True(area.Work.Contains(window.WidgetRect));
        });

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1.25, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    public void The_tab_lands_inside_the_target_display_after_a_DPI_change(double scale, int edge)
    {
        var work = new Rect(-1920, -1200, 1920, 1160);
        // Grab one of three icons on the original side, then release just inside the opposite display edge.
        var grip = new Vector(edge == 1 ? 105 : 12, 25);
        var pointer = new Point(edge == 1 ? work.Right - 1 : work.Left + 1, work.Bottom - 1);
        var held = IslandGeometry.HeldRect(pointer, grip, 117, scale);
        bool startedLeft = edge == 0;
        var (bounds, left) = IslandGeometry.LandWidget(held, work, startedLeft, compactTab: true);
        Assert.Equal(!startedLeft, left);
        Assert.True(work.Contains(bounds));
        Assert.Equal(117 * scale, bounds.Width);
        Assert.Equal(IslandGeometry.Inside(IslandGeometry.PuckEnd(held, startedLeft), work), IslandGeometry.PuckEnd(bounds, left));
    }

    private static void WithWidget(bool reduced, Action<IslandWindow> test) => WpfTestThread.Run(() =>
    {
        var was = Motion.Override;
        try
        {
            Motion.Override = reduced;
            IslandWindowTests.WithShownIsland(window =>
            {
                window.PointerAt = () => new Point(-32000, -32000);
                test(window);
            });
        }
        finally { Motion.Override = was; }
    });

    private static void Press(IslandWindow window, RoutedEvent routed) =>
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routed });

    private static nint NativeHit(IslandWindow window, Point screen) =>
        SendMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0x0084, 0, ((int)screen.Y << 16) | ((int)screen.X & 0xffff));

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
