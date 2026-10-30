// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

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
    public void Laying_the_home_out_ahead_leaves_the_panel_closed() => WpfTestThread.Run(() => WithPanel(panel =>
    {
        panel.Prewarm();
        Assert.False(panel.IsOpen);
        Assert.False(panel.IsVisible);
        Assert.Null(panel.Page.Content);
    }));

    [Fact]
    public void A_new_song_on_the_bar_arrives_and_the_old_one_fades_where_it_was() => WpfTestThread.Run(() => WithMotion(false, () => IslandWindowTests.WithShownIsland(window =>
    {
        var media = new FakeMedia { Sessions = [Track("Creep")] };
        window.SetMedia(media);
        var playing = new IslandActivity("track", IslandActivityKind.Media, "Song", "Spotify") { TileKey = IslandPanelWindow.NowPlayingKey };
        window.SetActivities([playing], Chippi.App.Pet.PetMood.Idle);
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
        window.SetActivities([], Chippi.App.Pet.PetMood.Sleepy);
        Assert.Null(window.LinesWas.Source);
        Assert.Equal(Visibility.Visible, window.PausedMark.Visibility);
    })));

    [Fact]
    public void The_badge_fades_out_before_it_goes_and_the_press_dips_the_puck() => WpfTestThread.Run(() => WithMotion(false, () => IslandWindowTests.WithShownIsland(window =>
    {
        var muted = new IslandActivity("mic", IslandActivityKind.Status, "Muted", "Microphone") { ModuleId = "mic-mute", NeedsAttention = true };
        window.SetActivities([muted], Chippi.App.Pet.PetMood.Sleepy);
        Assert.Equal(Visibility.Visible, window.MicBadge.Visibility);
        IslandWindowTests.Pump(TimeSpan.FromMilliseconds(300));
        window.SetActivities([], Chippi.App.Pet.PetMood.Sleepy);
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
