// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using Chippi.App.Island;
using Chippi.App.Pet;
using Chippi.Core.Island;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

/// <summary>Unshown windows only, no HWND initialization or desktop input.</summary>
public sealed class MicMuteTabTests
{
    [Fact]
    public void The_red_badge_shows_beside_the_song_and_goes_when_unmuted()
    {
        WpfTestThread.Run(() =>
        {
            var window = new IslandWindow();
            try
            {
                var music = new IslandActivity("music", IslandActivityKind.Media, "Track", "Player");
                var muted = new IslandActivity("muted", IslandActivityKind.Status, "Muted", "Microphone")
                    { ModuleId = "mic-mute", NeedsAttention = true, TileKey = "sound.microphone" };
                window.SetMedia(new IslandPanelTests.FakeMedia
                {
                    Sessions = [new("player", "Player", "Track", "", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTimeOffset.Now, null)],
                });
                window.SetActivities([music, muted], PetMood.Idle);
                Assert.Equal(Visibility.Visible, window.MicBadge.Visibility);
                Assert.Equal(Visibility.Visible, window.Equalizer.Visibility); // the song keeps the bar; the mic is the badge
                Assert.Equal("Track", window.ChipText);
                Assert.Equal("Microphone muted", AutomationProperties.GetHelpText(window.PuckButton));
                window.SetActivities([music, muted with { Title = "Some microphones muted" }], PetMood.Idle);
                Assert.Equal("Some microphones muted", AutomationProperties.GetHelpText(window.PuckButton));
                window.SetActivities([muted], PetMood.Idle);
                Assert.False(window.BarShown); // muted alone: the puck, with only the badge
                Assert.Equal(Visibility.Visible, window.MicBadge.Visibility);
                window.SetActivities([music], PetMood.Idle);
                Assert.Equal(Visibility.Collapsed, window.MicBadge.Visibility);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Keep_awake_shows_as_a_badge_while_a_song_holds_the_bar_and_as_the_bar_otherwise()
    {
        WpfTestThread.Run(() =>
        {
            var window = new IslandWindow();
            try
            {
                var music = new IslandActivity("music", IslandActivityKind.Media, "Track", "Player");
                var awake = new IslandActivity("awake", IslandActivityKind.Status, "Keep awake", "Until 6:00 PM")
                    { ModuleId = IslandHost.KeepAwakeModuleId, TileKey = "keep-awake" };
                window.SetMedia(new IslandPanelTests.FakeMedia
                {
                    Sessions = [new("player", "Player", "Track", "", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTimeOffset.Now, null)],
                });
                window.SetActivities([awake], PetMood.WideAwake);
                Assert.True(window.BarShown); // Keep awake has the bar: no badge
                Assert.Equal(Visibility.Collapsed, window.AwakeBadge.Visibility);
                window.SetActivities([music, awake], PetMood.WideAwake);
                Assert.Equal("Track", window.ChipText); // the song takes the bar, and Keep awake stays in sight as the badge
                Assert.Equal(Visibility.Visible, window.AwakeBadge.Visibility);
                window.SetActivities([music], PetMood.Idle);
                Assert.Equal(Visibility.Collapsed, window.AwakeBadge.Visibility);
            }
            finally { window.Close(); }
        });
    }
}