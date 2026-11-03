// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
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

    [Theory]
    [InlineData(false)] // pick up Chippi off-center
    [InlineData(true)]  // pick up his status badge
    public void Dragging_a_tabbed_puck_previews_each_direction_without_a_jump_on_release(bool fromBadge) =>
        WithWidget(true, window =>
        {
            var area = window.CurrentArea!.Value;
            window.SetPlacement(1, 0.5);
            window.SetActivities([Muted], PetMood.Idle);
            window.UpdateLayout();
            Assert.True(window.OpensLeft);
            var petGrip = fromBadge ? new Point(25, 25) : new Point(15, 32);
            var pointer = fromBadge ? window.MicBadge.PointToScreen(new Point(11, 11)) : window.PuckEnd.PointToScreen(petGrip);
            window.PointerAt = () => pointer;
            Press(window, UIElement.PreviewMouseLeftButtonDownEvent);
            int saves = 0;
            window.PlacementChosen += (_, _) => saves++;

            foreach (double fraction in new[] { 0.25, 0.75, 0.25 })
            {
                pointer = new Point(area.Work.Left + fraction * area.Work.Width, area.Work.Top + area.Work.Height / 2);
                window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
                window.UpdateLayout();
                bool opensLeft = fraction > 0.5;
                Assert.Equal(opensLeft, window.OpensLeft);
                Assert.Equal(75, window.Body.ActualWidth, 2);
                Assert.True(window.CoversWidget(pointer));
                Assert.InRange((window.PuckEnd.PointToScreen(petGrip) - pointer).Length, 0, 2);
                var icon = window.MicBadge.PointToScreen(new Point(11, 11));
                Assert.True(opensLeft ? icon.X < window.PuckRect.Left : icon.X > window.PuckRect.Right);
                Assert.Equal(0, saves);
            }
            var preview = window.WidgetRect;
            Press(window, UIElement.PreviewMouseLeftButtonUpEvent);
            Assert.Equal(preview, window.WidgetRect);
            Assert.Equal(1, saves);
            Assert.False(window.Panel.IsOpen);
            Assert.True(area.Work.Contains(preview));
        });

    [Fact]
    public void Picking_up_a_half_opened_agent_notice_immediately_shows_only_the_puck_and_current_badges() =>
        WithWidget(false, window =>
        {
            var agent = new IslandActivity("working", IslandActivityKind.Agent, "Working · 2%", "Claude Code")
                { ProviderId = "Claude", ModuleId = "agent-usage" };
            window.SetPlacement(1, 0.5);
            window.SetActivities([agent, Muted], PetMood.Idle);
            var button = window.PuckButton;
            const double partial = 140;
            // Hold a frame of the bar unfold so the press is reproducible even on a busy runner.
            button.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(partial, partial, TimeSpan.FromSeconds(1)));
            IslandWindowTests.PumpUntil(() => Math.Abs(window.Body.ActualWidth - partial) < 0.01);
            var pointer = window.PuckEnd.PointToScreen(new Point(17, 27));
            window.PointerAt = () => pointer;
            Press(window, UIElement.PreviewMouseLeftButtonDownEvent);
            Assert.Equal(partial, window.Body.ActualWidth, 2);
            // Move beyond the four-DIP threshold without crossing the current monitor edge.
            pointer.X -= 15 * window.CurrentArea!.Value.Scale;
            window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            window.UpdateLayout();

            Assert.Equal(96, window.Body.ActualWidth, 2);
            Assert.Equal(0, window.Bar.Opacity);
            Assert.Equal(Visibility.Visible, window.AgentMark.Visibility);
            Assert.Equal(Visibility.Visible, window.MicBadge.Visibility);
            Assert.NotNull(window.AgentBadgeProvider.Source);
            Assert.InRange((window.PuckEnd.PointToScreen(new Point(17, 27)) - pointer).Length, 0, 2);
            Assert.False(window.Panel.IsVisible);
            Press(window, UIElement.PreviewMouseLeftButtonUpEvent);
            Assert.False(window.Panel.IsOpen);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Crossing_the_drag_threshold_dismisses_a_panel_mid_open_or_mid_close(bool closing) =>
        WithWidget(false, window =>
        {
            window.SetActivities([Muted], PetMood.Idle);
            IslandWindowTests.PumpUntil(() => Math.Abs(window.Body.ActualWidth - 75) < 0.01);
            var panel = window.Panel;
            long now = Stopwatch.GetTimestamp();
            panel.Clock = () => now;
            int frames = 0;
            panel.Morphing += (_, _) => frames++;
            void Frame(int milliseconds)
            {
                int before = frames;
                now += milliseconds * Stopwatch.Frequency / 1000;
                IslandWindowTests.PumpUntil(() => frames > before);
            }
            var pointer = window.PuckEnd.PointToScreen(new Point(25, 25));
            window.PointerAt = () => pointer;
            Press(window, UIElement.PreviewMouseLeftButtonDownEvent);
            Press(window, UIElement.PreviewMouseLeftButtonUpEvent);
            Frame(0);
            if (closing)
            {
                Frame(500);
                window.ClosePages();
                Frame(0);
            }
            Frame(80);
            Assert.True(panel.IsMorphing);
            Assert.True(panel.IsVisible);
            Assert.True(panel.Shape.Width > window.Body.ActualWidth);

            Press(window, UIElement.PreviewMouseLeftButtonDownEvent);
            double scale = window.CurrentArea!.Value.Scale;
            pointer.X -= 2 * scale;
            window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            Assert.True(panel.IsVisible);
            pointer.X -= 8 * scale;
            window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            window.UpdateLayout();
            Assert.False(panel.IsVisible);
            Assert.False(panel.IsMorphing);
            Assert.False(window.IsHooked);
            Assert.Equal(75, window.Body.ActualWidth, 2);
            Assert.True(window.CoversWidget(pointer));
            Press(window, UIElement.PreviewMouseLeftButtonUpEvent);
            Assert.False(panel.IsOpen);
        });

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1.25, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    public void The_tab_lands_inside_the_target_display_after_a_DPI_change(double scale, int edge)
    {
        var work = new Rect(-1920, -1200, 1920, 1160);
        // A status tab with three slots still leaves the entire outline on the target display.
        var grip = new Vector(18, 25);
        var pointer = new Point(edge == 1 ? work.Right - 1 : work.Left + 1, work.Bottom - 1);
        var held = IslandGeometry.HeldPuck(pointer, grip, scale);
        var (bounds, left) = IslandGeometry.LandWidget(held, work, 117 * scale);
        Assert.Equal(edge == 1, left);
        Assert.True(work.Contains(bounds));
        Assert.Equal(117 * scale, bounds.Width);
        Assert.Equal(IslandGeometry.Inside(held, work), IslandGeometry.PuckEnd(bounds, left));
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
