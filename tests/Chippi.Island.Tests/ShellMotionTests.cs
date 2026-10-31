// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

    [Fact]
    public void A_page_comes_in_from_the_right_and_the_home_from_the_left() => WpfTestThread.Run(() => WithMotion(false, () => WithPanel(panel =>
    {
        panel.Open(IslandGeometry.Start(Offscreen.Work, 1), Offscreen);
        Pump(TimeSpan.FromMilliseconds(400));
        panel.ShowPage("keep-awake");
        Assert.Equal(12, panel.PageShift.X, 1); // into a page: from the right
        Assert.Equal(0, panel.Card.Opacity, 2);
        Pump(TimeSpan.FromMilliseconds(400));
        Assert.Equal(0, panel.PageShift.X, 3);
        Assert.Equal(1, panel.Card.Opacity, 3);
        panel.ShowPage(null, remember: false);
        Assert.Equal(-12, panel.PageShift.X, 1); // back home: from the left
        Pump(TimeSpan.FromMilliseconds(400));
        Assert.Equal(0, panel.PageShift.X, 3);
    })));

    [Fact]
    public void Under_reduced_motion_the_panel_fades_in_and_out_where_it_is() => WpfTestThread.Run(() => WithMotion(true, () => WithPanel(panel =>
    {
        panel.Open(IslandGeometry.Start(Offscreen.Work, 1), Offscreen);
        Assert.Equal(IslandPage.Width, panel.Shape.Width, 1); // no growing: it's the panel from the first frame
        Assert.True(panel.Root.Opacity < 1);
        Pump(TimeSpan.FromMilliseconds(300));
        Assert.Equal(1, panel.Root.Opacity, 3);
        panel.ShowPage("keep-awake");
        Assert.Equal(0, panel.PageShift.X); // no slide, only the fade
        Pump(TimeSpan.FromMilliseconds(300));
        panel.CloseIsland();
        Assert.True(panel.IsVisible); // fading out where it is
        Assert.Equal(IslandPage.Width, panel.Shape.Width, 1);
        Pump(TimeSpan.FromMilliseconds(300));
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
        // The rim blends the same way the controls' fills do (FillFade.Blend).
        window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        IslandWindowTests.Pump(TimeSpan.FromMilliseconds(200));
        window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        var blend = Assert.IsType<DrawingBrush>(window.Body.BorderBrush);
        var layers = ((DrawingGroup)blend.Drawing).Children.Cast<DrawingGroup>().ToList();
        IslandWindowTests.Pump(TimeSpan.FromMilliseconds(40));
        Assert.InRange(layers[0].Opacity, 0, 0.99); // the old one fading out
        Assert.InRange(layers[1].Opacity, 0.01, 1); // as the new one fades in
        IslandWindowTests.Pump(TimeSpan.FromMilliseconds(200));
        Assert.Same(window.FindResource("IslandWidgetBorderBrush"), window.Body.BorderBrush);
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
            Pump(TimeSpan.FromMilliseconds(400));
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
            PumpUntil(() => frames > at);
        }
        void Advance(double ms)
        {
            now += (long)(ms * Stopwatch.Frequency / 1000);
            Frame();
        }

        var puck = window.PuckRect;
        var pointer = new Point(puck.Left + puck.Width / 2, puck.Top + puck.Height / 2);
        window.PointerAt = () => pointer;
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        Assert.True(window.Panel.PetMeetsPuck);
        Frame(); // the clock starts on the first frame
        Advance(130); // past the first 45% of the 220ms open
        Assert.Equal(1, window.PuckEnd.Opacity); // Chippi doesn't fade…
        Assert.Equal(0.7, window.PetScale.ScaleX, 2); // …it's the footer's size
        Advance(300);
        Assert.Equal(0, window.PuckEnd.Opacity); // and the footer's Chippi has it now

        window.Escape();
        Frame();
        Advance(20);
        Assert.Equal(1, window.PuckEnd.Opacity); // closing, the widget's Chippi takes over at once, at the footer's size
        Assert.Equal(0.7, window.PetScale.ScaleX, 2);
        Advance(300);
        Assert.Equal(1, window.PetScale.ScaleX, 3);
    })));

    // Runs the dispatcher until ready (frames included), for at most 5 seconds.
    private static void PumpUntil(Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < until)
            IslandWindowTests.Pump(TimeSpan.FromMilliseconds(5));
        Assert.True(ready());
    }

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
            Pump(TimeSpan.FromMilliseconds(400));
            panel.ShowPage("keep-awake");
            Pump(TimeSpan.FromMilliseconds(400));
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
}
