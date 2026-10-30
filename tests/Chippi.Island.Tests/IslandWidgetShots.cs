// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.App.Pet;
using Chippi.Core.Island;
using static Chippi.Island.Tests.IslandWindowTests;

namespace Chippi.Island.Tests;

/// <summary>
/// Renders the widget on the primary display, at its start and on the left edge: the puck, the muted
/// mic badge, and the bar with a short and a long song, paused, and an activity (widget-&lt;side&gt;-*.png).
/// Runs only when CHIPPI_ISLAND_SHOTS names an output folder; CHIPPI_SHOTS_THEME=light renders it light.
/// </summary>
public sealed class IslandWidgetShots
{
    private static MediaTrack Track(string title, string artist, bool playing = true) =>
        new("spotify", "Spotify", title, artist, playing, true, true, true, true, true, TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(232), DateTimeOffset.Now, null);

    private static readonly IslandActivity Song = new("track", IslandActivityKind.Media, "Song", "Spotify") { TileKey = IslandPanelWindow.NowPlayingKey };
    private static readonly IslandActivity Awake = new("awake", IslandActivityKind.Timer, "1 hr left", "Keep awake") { ModuleId = "keep-awake", TileKey = "keep-awake" };
    private static readonly IslandActivity Muted = new("mic", IslandActivityKind.Status, "Muted", "Microphone") { ModuleId = "mic-mute", NeedsAttention = true };

    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        var backdrop = WpfTestThread.ShotsTheme == "Light" ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20);
        string theme = WpfTestThread.ShotsTheme.ToLowerInvariant();
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var media = new IslandPanelTests.FakeMedia();
            window.SetMedia(media);
            foreach (var (side, x) in new[] { ("right", (double?)null), ("left", 0.0) })
            {
                window.SetPlacement(x, x is null ? null : 1);
                void Shoot(string name, IReadOnlyList<IslandActivity> activities, PetMood mood, MediaTrack? track = null)
                {
                    media.Sessions = track is null ? [] : [track];
                    media.Raise();
                    window.SetActivities(activities, mood);
                    Pump(TimeSpan.FromMilliseconds(500)); // the unfold, well inside the title's 2-second hold
                    window.SaveScene(Path.Combine(folder, $"widget-{theme}-{side}-{name}.png"), backdrop);
                }
                Shoot("puck", [], PetMood.Sleepy);
                Shoot("puck-mic", [Muted], PetMood.Sleepy);
                Shoot("song", [Song], PetMood.Idle, Track("Creep", "Radiohead"));
                Shoot("song-long", [Song], PetMood.Idle, Track("Everything In Its Right Place (Live at the Ancient Theatre)", "Radiohead"));
                Shoot("song-mic", [Song, Muted], PetMood.Idle, Track("Creep", "Radiohead"));
                Shoot("paused", [], PetMood.Sleepy, Track("Creep", "Radiohead", playing: false));
                Shoot("activity", [Awake], PetMood.Idle);
                Shoot("puck-again", [], PetMood.Sleepy);
            }
        }));
    }
}
#endif
