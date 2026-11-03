// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Themes;
using static Chippi.Island.Tests.IslandPanelTests;

namespace Chippi.Island.Tests;

/// <summary>
/// The shell's motion (DESIGN.md §5 Island, Motion): pages coming in from their side, the panel fading in place
/// under reduced motion, a new track arriving, the badge fading out, the press, and the panel laid out ahead of its
/// first open. Each sets reduced motion, a process-wide setting, so they run alone (the ReducedMotion collection).
/// </summary>
[Collection("ReducedMotion")]
public sealed class ShellMotionTests
{
    private static readonly MonitorArea Offscreen = new(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);

    private static MediaTrack Track(string title, bool playing = true) =>
        new("spotify", "Spotify", title, "Radiohead", playing, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(4), DateTimeOffset.Now, null);

    private static void WithMotion(bool reduced, Action test)
    {
        var motion = Motion.Override;
        try
        {
            Motion.Override = reduced;
            test();
        }
        finally
        {
            Motion.Override = motion;
        }
    }

    private static void WithPanel(Action<IslandPanelWindow> test)
    {
        var panel = new IslandPanelWindow();
        try
        {
            panel.Initialize();
            panel.SetMedia(new FakeMedia());
            panel.SetTiles(Tiles(Part("keep-awake", "Keep awake", 40), Part("sound.mixer", "Volume mixer", 10)));
            test(panel);
        }
        finally
        {
            panel.Close();
        }
    }

    [Theory]
    [InlineData(50)]
    [InlineData(256)]
    public void The_first_visible_frame_is_the_source_and_ink_arrives_while_the_outline_is_still_growing(double width) =>
        WpfTestThread.Run(() => WithMotion(false, () => WithPanel(panel =>
        {
            long now = 0;
            int frames = 0;
            panel.Clock = () => now;
            panel.Morphing += (_, _) => frames++;
            var puck = IslandGeometry.Start(Offscreen.Work, 1);
            var source = new Rect(puck.Right - width, puck.Top, width, puck.Height);
            Size? first = null;
            panel.IsVisibleChanged += (_, _) =>
            {
                if (panel.IsVisible)
                    first = new Size(panel.Shape.Width, panel.Shape.Height);
            };
            panel.Open(puck, source, Offscreen);
            Assert.Equal(source.Size, first);
            void Frame()
            {
                int seen = frames;
                IslandWindowTests.PumpUntil(() => frames > seen);
            }
            Frame(); // establish zero even on a cold first composition
            now += (long)(Stopwatch.Frequency * IslandPanelWindow.GrowTime.TotalSeconds / 2);
            Frame();
            double across = (panel.Shape.Width - width) / (IslandPage.Width - width);
            double down = (panel.Shape.Height - puck.Height) / (panel.Card.Height - puck.Height);
            Assert.InRange(down, 0.49, 0.51);
            Assert.InRange(across, down + 0.1, 0.8);
            Assert.InRange(panel.Card.Opacity, 0.2, 0.8);
            Assert.Equal(Canvas.GetLeft(panel.Card) + IslandPage.Width, Canvas.GetLeft(panel.Shape) + panel.Shape.Width, 3);
            Assert.Equal(Canvas.GetTop(panel.Card) + panel.Card.Height, Canvas.GetTop(panel.Shape) + panel.Shape.Height, 3);
        })));

    [Fact]
    public void Outside_clicks_follow_the_visible_edge_while_a_sparse_gallery_changes_height() =>
        WpfTestThread.Run(() => WithMotion(false, () => WithPanel(panel =>
        {
            panel.Open(IslandGeometry.Start(Offscreen.Work, 1), Offscreen);
            IslandWindowTests.Settle(panel);
            foreach (var destination in new string?[] { "keep-awake", null })
            {
                long now = Stopwatch.GetTimestamp();
                int frames = 0;
                panel.Clock = () => { frames++; return now; };
                double startHeight = panel.Shape.Height;
                panel.ShowPage(destination);
                IslandWindowTests.PumpUntil(() => frames > 0);
                now += Stopwatch.Frequency / 12;
                IslandWindowTests.PumpUntil(() => Math.Abs(panel.Shape.Height - startHeight) > 1);
                Assert.InRange(panel.Shape.Height, 170, 359);

                // This bottom-right anchored panel moves its top edge. The final target would swallow
                // the outside point during growth or reject the inside point while shrinking.
                var window = IslandNative.WindowRect(panel.Handle);
                var top = new Point(window.X + Canvas.GetLeft(panel.Shape) + 100, window.Y + Canvas.GetTop(panel.Shape));
                Assert.True(panel.Covers(new Point(top.X, top.Y + 2)));
                Assert.False(panel.Covers(new Point(top.X, top.Y - 2)));
                IslandWindowTests.Settle(panel);
            }
        })));

    [Fact]
    public void A_page_comes_in_from_the_right_and_the_home_from_the_left() => WpfTestThread.Run(() => WithMotion(false, () => WithPanel(panel =>
    {
        panel.Open(IslandGeometry.Start(Offscreen.Work, 1), Offscreen);
        IslandWindowTests.Settle(panel);
        panel.ShowPage("keep-awake");
        Assert.Equal(12, panel.PageShift.X, 1); // into a page: from the right
        Assert.Equal(0, panel.Card.Opacity, 2);
        IslandWindowTests.Settle(panel);
        Assert.Equal(0, panel.PageShift.X, 3);
        Assert.Equal(1, panel.Card.Opacity, 3);
        panel.ShowPage(null, remember: false);
        Assert.Equal(-12, panel.PageShift.X, 1); // back home: from the left
        IslandWindowTests.Settle(panel);
        Assert.Equal(0, panel.PageShift.X, 3);
    })));

    [Fact]
    public void Under_reduced_motion_the_panel_fades_in_and_out_where_it_is() => WpfTestThread.Run(() => WithMotion(true, () => WithPanel(panel =>
    {
        panel.Open(IslandGeometry.Start(Offscreen.Work, 1), Offscreen);
        Assert.Equal(IslandPage.Width, panel.Shape.Width, 1); // no growing: it's the panel from the first frame
        Assert.True(panel.Root.Opacity < 1);
        IslandWindowTests.Settle(panel);
        Assert.Equal(1, panel.Root.Opacity, 3);
        panel.ShowPage("keep-awake");
        Assert.Equal(0, panel.PageShift.X); // no slide, only the fade
        IslandWindowTests.Settle(panel);
        panel.CloseIsland();
        Assert.True(panel.IsVisible); // fading out where it is
        Assert.Equal(IslandPage.Width, panel.Shape.Width, 1);
        IslandWindowTests.Settle(panel);
        Assert.False(panel.IsVisible);
        Assert.Equal(1, panel.Root.Opacity); // ready for the next open
    })));

    [Fact]
    public void Under_reduced_motion_Chippi_only_fades_into_the_panel_and_back() => WpfTestThread.Run(() => WithMotion(true, () => IslandWindowTests.WithShownIsland(window =>
    {
        var puck = window.PuckRect;
        var pointer = new Point(puck.Left + puck.Width / 2, puck.Top + puck.Height / 2);
        window.PointerAt = () => pointer;
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        Assert.True(window.IsIslandOpen);
        for (int i = 0; i < 8; i++)
        {
            IslandWindowTests.Pump(TimeSpan.FromMilliseconds(20));
            Assert.Equal(1, window.PetScale.ScaleX); // no shrinking: it fades
        }
        window.Escape();
        for (int i = 0; i < 8; i++)
        {
            IslandWindowTests.Pump(TimeSpan.FromMilliseconds(20));
            Assert.Equal(1, window.PetScale.ScaleX);
        }
    })));

    [Fact]
    public void A_fill_going_back_to_clear_fades_out_rather_than_snapping() => WpfTestThread.Run(() => WithMotion(false, () => IslandWindowTests.WithShownIsland(window =>
    {
        // The rim blends the same way the controls' fills do (FillFade.Blend). The blend's clock moves only when the
        // test moves it, so a slow runner's late frames can't leave it unstarted.
        var wall = FillFade.Clock;
        long now = Stopwatch.GetTimestamp();
        int frames = 0;
        EventHandler counted = (_, _) => frames++;
        void Frame()
        {
            int at = frames;
            IslandWindowTests.PumpUntil(() => frames > at);
        }
        void Advance(double ms)
        {
            now += (long)(ms * Stopwatch.Frequency / 1000);
            Frame();
        }

        FillFade.Clock = () => now;
        CompositionTarget.Rendering += counted;
        try
        {
            window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            Frame(); // the fade starts on the first frame
            Advance(100);
            Assert.Same(window.FindResource("IslandWidgetHoverBorderBrush"), window.Body.BorderBrush);
            window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            var blend = Assert.IsType<DrawingBrush>(window.Body.BorderBrush);
            var layers = ((DrawingGroup)blend.Drawing).Children.Cast<DrawingGroup>().ToList();
            Frame();
            Advance(40); // about half of the 83ms
            Assert.Equal(1 - 40 / 83.0, layers[0].Opacity, 3); // the old one fading out
            Assert.Equal(40 / 83.0, layers[1].Opacity, 3); // as the new one fades in
            Advance(60);
            Assert.Same(window.FindResource("IslandWidgetBorderBrush"), window.Body.BorderBrush);
        }
        finally
        {
            CompositionTarget.Rendering -= counted;
            FillFade.Clock = wall;
        }
    })));

    [Fact]
    public void The_footers_Chippi_sits_on_the_pucks_side_and_meets_the_pucks_own_on_a_bottom_corner() => WpfTestThread.Run(() => WithPanel(panel =>
    {
        // 24 in from each corner, as the puck starts; flush in a corner the panel is kept 8 inside the work area, off the puck's corner.
        var work = Offscreen.Work;
        Rect At(bool right, bool bottom, double inset = 24) =>
            new(right ? work.Right - inset - 50 : work.Left + inset, bottom ? work.Bottom - inset - 50 : work.Top + inset, 50, 50);
        foreach (var (puck, meets, right) in new[]
        {
            (At(right: true, bottom: true), true, true), (At(right: false, bottom: true), true, false),
            (At(right: true, bottom: false), false, true), (At(right: false, bottom: false), false, false),
            (At(right: true, bottom: true, inset: 0), false, true),
        })
        {
            panel.Open(puck, Offscreen);
            IslandWindowTests.Settle(panel);
            Assert.Equal(meets, panel.PetMeetsPuck);
            var grid = Assert.IsType<IslandGrid>(panel.Page.Content);
            var pet = grid.Pet.PointToScreen(new Point(grid.Pet.ActualWidth / 2, grid.Pet.ActualHeight / 2));
            var footprint = panel.Footprint;
            Assert.Equal(right, pet.X > footprint.Left + footprint.Width / 2); // on the puck's side
            if (meets) // exactly where the puck's center is
            {
                Assert.Equal(puck.Left + puck.Width / 2, pet.X, 1);
                Assert.Equal(puck.Top + puck.Height / 2, pet.Y, 1);
            }
            panel.CloseIsland(immediate: true);
            Pump(TimeSpan.FromMilliseconds(100));
        }
    }));

    [Fact]
    public void Opening_on_a_bottom_corner_Chippi_shrinks_onto_the_footers_and_closing_it_takes_over_again() => WpfTestThread.Run(() => WithMotion(false, () => IslandWindowTests.WithShownIsland(window =>
    {
        // The morph's clock moves only when the test moves it, so a slow runner's late frames can't leave it short.
        long now = Stopwatch.GetTimestamp();
        window.Panel.Clock = () => now;
        int frames = 0;
        window.Panel.Morphing += (_, _) => frames++;
        void Frame()
        {
            int at = frames;
            IslandWindowTests.PumpUntil(() => frames > at);
        }
        void Advance(double ms)
        {
            now += (long)(ms * Stopwatch.Frequency / 1000);
            Frame();
        }

        var puck = window.PuckRect;
        double scale = window.CurrentArea!.Value.Scale;
        var pointer = new Point(puck.Left + 8 * scale, puck.Top + 14 * scale);
        window.PointerAt = () => pointer;
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        IslandWindowTests.PumpUntil(() => Math.Abs(window.PuckScale.ScaleX - 0.96) < 0.0001);
        var button = window.PuckButton;
        var pressed = new Rect(button.PointToScreen(default), button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight)));
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        var shape = window.Panel.Shape;
        var panelWindow = IslandNative.WindowRect(window.Panel.Handle);
        Assert.Equal(pressed.Width / scale, shape.Width, 3);
        Assert.Equal(pressed.Height / scale, shape.Height, 3);
        Assert.Equal(pressed.Left, panelWindow.Left + System.Windows.Controls.Canvas.GetLeft(shape) * scale, 3);
        Assert.Equal(pressed.Top, panelWindow.Top + System.Windows.Controls.Canvas.GetTop(shape) * scale, 3);
        Assert.True(window.Panel.PetMeetsPuck);
        var footer = Assert.IsType<IslandGrid>(window.Panel.Page.Content).Pet;
        Frame(); // the clock starts on the first frame
        Advance(130); // half of the 260ms open
        Assert.Equal(1, window.PuckEnd.Opacity);
        Assert.Equal(0, footer.Opacity);
        Assert.Equal(0.85, window.PetScale.ScaleX, 2); // halfway from 40 to 28
        Advance(300);
        Assert.Equal(0, window.PuckEnd.Opacity);
        Assert.Equal(1, footer.Opacity); // only the footer draws its pet

        window.Escape();
        Frame();
        Assert.Equal(1, window.PuckEnd.Opacity); // closing, the widget owns the pet from frame zero
        Assert.Equal(0, footer.Opacity);
        Assert.Equal(0.7, window.PetScale.ScaleX, 2);
        Advance(95); // half of the 190ms close, the same size at the same physical phase
        Assert.Equal(0.85, window.PetScale.ScaleX, 2);
        Advance(300);
        Assert.Equal(1, window.PetScale.ScaleX, 3);
    })));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reversing_an_open_from_a_partial_bar_hands_back_its_visible_width_before_applying_changes(bool click) => WpfTestThread.Run(() => WithMotion(false, () => IslandWindowTests.WithShownIsland(window =>
    {
        window.SetTiles(Tiles(Part("timer", "Focus timer", 0)));
        window.SetActivities([new IslandActivity("timer", IslandActivityKind.Timer, "18:42", "Focus timer") { TileKey = "timer" }], Chippi.UI.Pet.PetMood.Idle);
        var button = window.PuckButton;
        const double partial = 140; // hold an actual animated width between the authored 50 and 256
        button.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(partial, partial, TimeSpan.FromMilliseconds(240)));
        IslandWindowTests.PumpUntil(() => Math.Abs(button.ActualWidth - partial) < 1 && window.Bar.Opacity == 1);
        var visible = new Rect(button.PointToScreen(default), button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight)));
        long now = Stopwatch.GetTimestamp();
        int frames = 0;
        window.Panel.Clock = () => now;
        window.Panel.Morphing += (_, _) => frames++;
        void Frame()
        {
            int at = frames;
            IslandWindowTests.PumpUntil(() => frames > at);
        }
        if (click)
        {
            var pointer = button.PointToScreen(new Point(70, 25));
            window.PointerAt = () => pointer;
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        }
        else
            Assert.True(window.ShowPage("timer"));
        Assert.Equal("timer", window.Panel.CurrentPage);
        Frame();
        now += (long)(0.052 * Stopwatch.Frequency); // one fifth of the open
        Frame();
        double bar = window.BarLayer.Opacity, pet = window.PuckEnd.Opacity;
        Assert.InRange(bar, 0.42, 0.50);
        Assert.InRange(pet, 0.60, 0.68); // the bar's lines fade before the pet
        string originalInk = window.ChipText;
        window.SetActivities([new IslandActivity("update", IslandActivityKind.Timer, "10:00", "Countdown")], Chippi.UI.Pet.PetMood.Idle);
        Assert.Equal(originalInk, window.ChipText); // an update cannot replace the still-visible source lines

        window.ClosePages();
        Frame();
        Assert.Equal(bar, window.BarLayer.Opacity, 4);
        Assert.Equal(pet, window.PuckEnd.Opacity, 4);
        window.SetActivities([], Chippi.UI.Pet.PetMood.Sleepy);
        Assert.True(window.BarShown); // still returning to the activity bar that started the close
        IslandWindowTests.Settle(window.Panel);
        Assert.False(window.BarShown); // the widget handles the change after the handoff
        double scale = window.CurrentArea!.Value.Scale;
        var panelWindow = IslandNative.WindowRect(window.Panel.Handle);
        var last = window.Panel.Shape;
        Assert.Equal(visible.Left, panelWindow.Left + Canvas.GetLeft(last) * scale, 3);
        Assert.Equal(visible.Width, last.Width * scale, 3);
        Assert.Equal(partial, (double)button.GetAnimationBaseValue(FrameworkElement.WidthProperty));
        Assert.True(DependencyPropertyHelper.GetValueSource(button, FrameworkElement.WidthProperty).IsAnimated); // folds from 140 after landing
        Assert.Equal(1, window.BarLayer.Opacity);
        Assert.Equal(1, window.PuckEnd.Opacity);
    })));

    [Fact]
    public void Laying_the_home_out_ahead_leaves_the_panel_closed() => WpfTestThread.Run(() => WithPanel(panel =>
    {
        panel.Prewarm();
        Assert.False(panel.IsOpen);
        Assert.False(panel.IsVisible);
        Assert.Null(panel.Page.Content);
    }));

    [Fact]
    public void Laying_out_ahead_builds_the_last_page_and_its_first_open_uses_it() => WpfTestThread.Run(() => WithMotion(false, () =>
    {
        int built = 0;
        var page = new IslandTileContribution("keep-awake", new IslandTileSpec("keep-awake", "Keep awake", "Coffee", 40, 0, () =>
        {
            built++;
            return new System.Windows.Controls.Border { Height = 120 };
        }));
        var panel = new IslandPanelWindow();
        try
        {
            panel.Initialize();
            panel.SetMedia(new FakeMedia());
            panel.SetTiles(Tiles(page, Part("sound.mixer", "Volume mixer", 10)));
            // Now Playing and a page that's gone are let be.
            foreach (var gone in new[] { IslandPanelWindow.NowPlayingKey, "gone" })
            {
                panel.SetLastPage(gone);
                panel.Prewarm();
            }
            Assert.Equal(0, built);

            panel.SetLastPage("keep-awake");
            panel.Prewarm();
            Assert.Equal(1, built); // built and laid out while closed
            Assert.False(panel.IsVisible);
            Assert.Null(panel.Page.Content); // and let go: nothing is attached until it opens

            panel.SetLastPage(null);
            panel.Open(IslandGeometry.Start(Offscreen.Work, 1), Offscreen);
            IslandWindowTests.Settle(panel);
            panel.ShowPage("keep-awake");
            IslandWindowTests.Settle(panel);
            Assert.Equal(1, built); // the first open shows the one built ahead
            Assert.NotNull(panel.PageBody);
        }
        finally
        {
            panel.Close();
        }
    }));

    [Fact]
    public void A_new_song_on_the_bar_arrives_and_the_old_one_fades_where_it_was() => WpfTestThread.Run(() => WithMotion(false, () => IslandWindowTests.WithShownIsland(window =>
    {
        var media = new FakeMedia { Sessions = [Track("Creep")] };
        window.SetMedia(media);
        var playing = new IslandActivity("track", IslandActivityKind.Media, "Song", "Spotify") { TileKey = IslandPanelWindow.NowPlayingKey };
        window.SetActivities([playing], Chippi.UI.Pet.PetMood.Idle);
        IslandWindowTests.Pump(TimeSpan.FromMilliseconds(400)); // the unfold
        Assert.Null(window.LinesWas.Source);

        media.Sessions = [Track("Karma Police")];
        media.Raise();
        Assert.Equal("Karma Police", window.SongText.Text); // the new song is there at once
        Assert.NotNull(window.LinesWas.Source); // and a picture of the old one fades out over it
        Assert.NotNull(window.LeadWas.Source);
        IslandWindowTests.Pump(TimeSpan.Zero);
        // The pictures sit exactly where the cover and the lines are (the lines may still be rising).
        Assert.Equal(window.Cover.TranslatePoint(default, window.Bar), window.LeadWas.TranslatePoint(default, window.Bar));
        Assert.Equal(window.Lines.TranslatePoint(default, window.Bar).X, window.LinesWas.TranslatePoint(default, window.Bar).X, 1);
        IslandWindowTests.Pump(TimeSpan.FromMilliseconds(400));
        Assert.Null(window.LinesWas.Source);
        Assert.Equal(1, window.Lines.Opacity, 3);

        // Paused, the same song doesn't arrive again: only the pause mark takes over.
        media.Sessions = [Track("Karma Police", playing: false)];
        window.SetActivities([], Chippi.UI.Pet.PetMood.Sleepy);
        Assert.Null(window.LinesWas.Source);
        Assert.Equal(Visibility.Visible, window.PausedMark.Visibility);
    })));

    [Fact]
    public void The_badge_fades_out_before_it_goes_and_the_press_dips_the_puck() => WpfTestThread.Run(() => WithMotion(false, () => IslandWindowTests.WithShownIsland(window =>
    {
        var muted = new IslandActivity("mic", IslandActivityKind.Status, "Muted", "Microphone") { ModuleId = "mic-mute", NeedsAttention = true };
        window.SetActivities([muted], Chippi.UI.Pet.PetMood.Sleepy);
        Assert.Equal(Visibility.Visible, window.MicBadge.Visibility);
        IslandWindowTests.Pump(TimeSpan.FromMilliseconds(300));
        window.SetActivities([], Chippi.UI.Pet.PetMood.Sleepy);
        Assert.Equal(Visibility.Visible, window.MicBadge.Visibility); // fading
        IslandWindowTests.Pump(TimeSpan.FromMilliseconds(300));
        Assert.Equal(Visibility.Collapsed, window.MicBadge.Visibility);

        var puck = window.PuckRect;
        var pointer = new Point(puck.Left + puck.Width / 2, puck.Top + puck.Height / 2);
        window.PointerAt = () => pointer;
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        Assert.Equal(0.96, window.PuckScaleTarget, 3); // pressed in
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        Assert.Equal(1, window.PuckScaleTarget, 3); // and back as it opens
        window.Escape();
        IslandWindowTests.Pump(TimeSpan.FromMilliseconds(300));
    })));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Grabbing_a_widget_during_its_edge_slide_holds_that_frame_until_the_next_drag_or_release(bool dragAgain) =>
        WpfTestThread.Run(() => WithMotion(false, () => IslandWindowTests.WithShownIsland(window =>
        {
            var area = window.CurrentArea!.Value;
            long now = 0;
            window.SlideClock = () => now;
            window.Panel.Clock = () => now;
            window.SetTiles(Tiles(Part("timer", "Focus timer", 0)));
            window.SetActivities([new IslandActivity("timer", IslandActivityKind.Timer, "18:42", "Focus timer") { TileKey = "timer" }], Chippi.UI.Pet.PetMood.Idle);
            IslandWindowTests.PumpUntil(() => window.PuckButton.ActualWidth >= 255.99 && window.Bar.Opacity == 1);
            var grip = new Point(128, 25);
            var pointer = window.PuckButton.PointToScreen(grip);
            window.PointerAt = () => pointer;
            int saves = 0;
            window.PlacementChosen += (_, _) => saves++;
            void Button(RoutedEvent routed) => window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routed });
            void Move() => window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            Button(UIElement.PreviewMouseLeftButtonDownEvent);
            pointer = new Point(area.Work.Left + 1, area.Work.Top + 90 * area.Scale);
            Move();
            Button(UIElement.PreviewMouseLeftButtonUpEvent);
            var firstDrop = window.PuckRect;
            var start = IslandNative.WindowRect(window.Handle);
            now += Stopwatch.Frequency / 30;
            IslandWindowTests.PumpUntil(() => IslandNative.WindowRect(window.Handle) != start);
            var held = IslandNative.WindowRect(window.Handle);
            pointer = window.PuckButton.PointToScreen(grip);
            Button(UIElement.PreviewMouseLeftButtonDownEvent);
            now += Stopwatch.Frequency; // longer than the whole slide while the pointer stays still
            bool drawn = false;
            EventHandler frame = (_, _) => drawn = true;
            CompositionTarget.Rendering += frame;
            try { IslandWindowTests.PumpUntil(() => drawn); }
            finally { CompositionTarget.Rendering -= frame; }
            Assert.Equal(held, IslandNative.WindowRect(window.Handle));
            Assert.Equal(0, saves);

            if (dragAgain)
            {
                pointer.X += 160 * area.Scale;
                Move();
                Assert.InRange((window.PuckButton.PointToScreen(grip) - pointer).Length, 0, 2);
            }
            var button = window.PuckButton;
            var visible = new Rect(button.PointToScreen(default), button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight)));
            Button(UIElement.PreviewMouseLeftButtonUpEvent);
            if (!dragAgain)
            {
                Assert.Equal("timer", window.Panel.CurrentPage);
                window.Panel.UpdateLayout();
                var shape = window.Panel.Shape;
                Assert.InRange((shape.PointToScreen(default) - visible.TopLeft).Length, 0, 2);
                Assert.InRange((shape.PointToScreen(new Point(shape.ActualWidth, shape.ActualHeight)) - visible.BottomRight).Length, 0, 2);
            }
            now += Stopwatch.Frequency;
            IslandWindowTests.PumpUntil(() => saves > 0);
            Assert.Equal(1, saves);
            Assert.Equal(!dragAgain, window.IsIslandOpen);
            Assert.Equal(!dragAgain, firstDrop == window.PuckRect);
        })));
}
