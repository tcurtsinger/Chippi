// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.App.Island.Media;

namespace Chippi.Island.Tests;

/// <summary>Which session Now Playing shows after the user picks one.</summary>
public sealed class MediaSessionsTests
{
    private static MediaTrack Track(string id, bool playing) =>
        new(id, id, "Title", "Artist", playing, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTimeOffset.Now, null);

    [Fact]
    public void A_pick_holds_until_another_app_starts_playing_or_the_picked_app_goes()
    {
        MediaTrack[] picked = [Track("spotify", false), Track("firefox", true)];
        Assert.Equal("spotify", SystemMediaSessions.KeepPick("spotify", picked, picked)); // already playing elsewhere: picking Spotify to start it
        Assert.Equal("spotify", SystemMediaSessions.KeepPick("spotify", picked, [Track("spotify", true), Track("firefox", true)])); // the picked app starts
        Assert.Null(SystemMediaSessions.KeepPick("spotify", [Track("spotify", true), Track("firefox", false)], [Track("spotify", true), Track("firefox", true)]));
        Assert.Null(SystemMediaSessions.KeepPick("spotify", picked, [.. picked, Track("vlc", true)])); // a new app arrives playing
        Assert.Null(SystemMediaSessions.KeepPick("spotify", picked, [Track("firefox", true)]));
        Assert.Null(SystemMediaSessions.KeepPick(null, picked, picked));
    }

    [Fact]
    public void A_players_title_is_one_line_and_white_space_alone_is_no_title()
    {
        Assert.Equal("Karma Police (Live)", SystemMediaSessions.OneLine("  Karma Police\r\n(Live)\t"));
        Assert.Equal("", SystemMediaSessions.OneLine(" \n\t "));
        Assert.Equal("", SystemMediaSessions.OneLine(null));
        Assert.Equal("Radiohead", SystemMediaSessions.OneLine("Radiohead"));
    }
}
