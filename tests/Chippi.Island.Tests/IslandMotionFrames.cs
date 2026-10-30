// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.App.Pet;
using Chippi.Core.Island;
using static Chippi.Island.Tests.IslandWindowTests;

namespace Chippi.Island.Tests;

/// <summary>
/// Filmstrips of the shell's motion on the primary display (motion-&lt;theme&gt;-&lt;name&gt;-&lt;ms&gt;.png): the bar
/// unfolding as Chippi's headphones go on, a new song arriving, playing to paused, the mic badge, and a page
/// coming in and the home coming back. The times are about right: each frame is saved as the dispatcher reaches it.
/// Runs only when CHIPPI_ISLAND_SHOTS names an output folder.
/// </summary>
public sealed class IslandMotionFrames
{
    private static readonly int[] Times = [0, 40, 80, 120, 160, 240];

    private static MediaTrack Track(string title, bool playing = true) =>
        new("spotify", "Spotify", title, "Radiohead", playing, true, true, true, true, true, TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(232), DateTimeOffset.Now, null);

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
            window.SetTiles(IslandPanelShots.HomeTiles());
            var song = new IslandActivity("track", IslandActivityKind.Media, "Song", "Spotify") { TileKey = IslandPanelWindow.NowPlayingKey };
            var muted = new IslandActivity("mic", IslandActivityKind.Status, "Muted", "Microphone") { ModuleId = "mic-mute", NeedsAttention = true };
            void Strip(string name, Action start)
            {
                start();
                long began = System.Diagnostics.Stopwatch.GetTimestamp();
                foreach (int ms in Times)
                {
                    while (System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds < ms)
                        Pump(TimeSpan.Zero);
                    window.SaveScene(Path.Combine(folder, $"motion-{theme}-{name}-{ms:000}.png"), backdrop);
                }
                Pump(TimeSpan.FromMilliseconds(400));
            }
            Pump(TimeSpan.FromMilliseconds(400));
            Strip("unfold", () => { media.Sessions = [Track("Creep")]; media.Raise(); window.SetActivities([song], PetMood.Idle); });
            Strip("arrive", () => { media.Sessions = [Track("Karma Police")]; media.Raise(); });
            Strip("pause", () => { media.Sessions = [Track("Karma Police", playing: false)]; window.SetActivities([], PetMood.Sleepy); });
            Strip("badge", () => window.SetActivities([muted], PetMood.Sleepy));
            window.SetActivities([], PetMood.Sleepy);
            media.Sessions = [];
            media.Raise();
            Pump(TimeSpan.FromMilliseconds(600));
            window.SetLastPage(null);
            window.ForceOpen(true);
            Pump(TimeSpan.FromMilliseconds(500));
            Strip("page", () => window.Panel.ShowPage("keep-awake", remember: false));
            Strip("back", () => window.Panel.ShowPage(null, remember: false));
            window.ForceOpen(false);
            Pump(TimeSpan.FromMilliseconds(400));
        }));
    }
}
#endif
