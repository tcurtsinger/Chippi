// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.Core.Island;
using Chippi.UI.Pet;

namespace Chippi.Island.Tests;

public sealed class NowPlayingPuckTests
{
    private static readonly IslandActivity Music = new("track", IslandActivityKind.Media, "Song", "Player")
        { TileKey = IslandPanelWindow.NowPlayingKey };
    private static MediaTrack Track(string title, bool playing = true) =>
        new("player", "Player", title, "Artist", playing, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTimeOffset.Now, null);

    private static Point PetEnd(IslandWindow window) =>
        new(window.PuckRect.Left + window.PuckRect.Width / 2, window.PuckRect.Top + window.PuckRect.Height / 2);
    private static void Move(IslandWindow window, Point point, RoutedEvent routed)
    {
        window.PointerAt = () => point;
        window.PuckButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = routed });
    }

    [Fact]
    public void Always_show_switches_live_and_off_keeps_music_on_the_puck_until_hover() =>
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            var pet = PetEnd(window);
            var outside = new Point(-20000, -20000);
            Move(window, outside, Mouse.MouseLeaveEvent);
            var media = new IslandPanelTests.FakeMedia { Sessions = [Track("Song")] };
            window.SetMedia(media);
            window.SetActivities([Music], PetMood.Idle);
            Assert.True(window.BarShown); // default

            window.SetNowPlayingAlwaysShow(false);
            Assert.False(window.BarShown);
            Assert.Equal(PetMood.Listening, window.Pet.Mood);
            Assert.Equal("Now Playing · Song", AutomationProperties.GetHelpText(window.PuckButton));
            media.Sessions = [Track("Next song")];
            media.Raise();
            Assert.False(window.BarShown);
            Assert.Equal("Now Playing · Next song", AutomationProperties.GetHelpText(window.PuckButton));

            Move(window, pet, Mouse.MouseEnterEvent);
            Assert.True(window.BarShown);
            Assert.Equal("Next song · Artist", window.ChipText);
            var barEnd = new Point(pet.X + (window.OpensLeft ? -1 : 1) * window.PuckRect.Width * 2, pet.Y);
            Move(window, barEnd, Mouse.PreviewMouseMoveEvent);
            media.Raise(); // Refresh while the pointer is over the song, away from Chippi.
            Assert.True(window.BarShown);
            var feedback = new IslandActivity("alert", IslandActivityKind.Status, "Alert", "Agents") { IsFeedback = true };
            window.SetActivities([Music, feedback], PetMood.Idle);
            Assert.Equal("Alert · Agents", window.ChipText);
            window.SetActivities([Music], PetMood.Idle);
            Assert.Equal("Next song · Artist", window.ChipText);
            Move(window, outside, Mouse.MouseLeaveEvent);
            Assert.False(window.BarShown);
            Assert.Equal(PetMood.Listening, window.Pet.Mood);

            window.SetNowPlayingAlwaysShow(true);
            Assert.True(window.BarShown);
            Assert.Equal("Next song · Artist", window.ChipText);
        }));

    [Fact]
    public void Off_leaves_other_activities_visible_until_the_pointer_reaches_Chippi() =>
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            var outside = new Point(-20000, -20000);
            Move(window, outside, Mouse.MouseLeaveEvent);
            window.SetNowPlayingAlwaysShow(false);
            var media = new IslandPanelTests.FakeMedia { Sessions = [Track("Song")] };
            window.SetMedia(media);
            var timer = new IslandActivity("timer", IslandActivityKind.Timer, "Focus", "Timer");
            window.SetActivities([Music, timer], PetMood.Idle);
            Assert.Equal("Focus · Timer", window.ChipText);

            var pet = PetEnd(window);
            var text = new Point(pet.X + (window.OpensLeft ? -1 : 1) * window.PuckRect.Width * 2, pet.Y);
            Move(window, text, Mouse.MouseEnterEvent); // Entering another bar through its text keeps that activity.
            Assert.Equal("Focus · Timer", window.ChipText);
            Move(window, pet, Mouse.PreviewMouseMoveEvent);
            Assert.Equal("Song · Artist", window.ChipText);
            Move(window, outside, Mouse.MouseLeaveEvent);
            Assert.Equal("Focus · Timer", window.ChipText);

            var agent = new IslandActivity("working", IslandActivityKind.Agent, "Working", "Codex") { ProviderId = "Codex" };
            window.SetActivities([Music, agent], PetMood.Idle);
            Assert.Equal("Working · Codex", window.ChipText);
        }));

    [Fact]
    public void Paused_music_is_available_only_while_hovered_and_until_the_pause_hold_expires() =>
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            var timers = new Dictionary<TimeSpan, Action>();
            window.CreateTimer = (interval, tick) => { timers[interval] = tick; return new StillTimer(); };
            var pet = PetEnd(window);
            var outside = new Point(-20000, -20000);
            Move(window, outside, Mouse.MouseLeaveEvent);
            window.SetNowPlayingAlwaysShow(false);
            var media = new IslandPanelTests.FakeMedia { Sessions = [Track("Song")] };
            window.SetMedia(media);
            window.SetActivities([Music], PetMood.Idle);
            media.Sessions = [Track("Song", playing: false)];
            window.SetActivities([], PetMood.Sleepy);
            Assert.False(window.BarShown);
            Assert.NotEqual(PetMood.Listening, window.Pet.Mood);
            Move(window, pet, Mouse.MouseEnterEvent);
            Assert.True(window.BarShown);
            Assert.Equal(Visibility.Visible, window.PausedMark.Visibility);
            timers[IslandWindow.PauseHold]();
            Assert.False(window.BarShown);
            Assert.Empty(window.ChipText);

            media.Sessions = [];
            media.Raise();
            window.SetNowPlayingAlwaysShow(true);
            Assert.False(window.BarShown);
        }));

    private sealed class StillTimer : IIslandTimer
    {
        public void Start() { }
        public void Stop() { }
    }
}
