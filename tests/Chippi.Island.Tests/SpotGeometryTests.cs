// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.Core.Island;
using Chippi.Features.MacDesktop.MenuBar;
using Chippi.Core.Modules;
using Chippi.UI.Pet;

namespace Chippi.Island.Tests;

/// <summary>
/// The Mac-style desktop's one spot: the panel under the right end of a display's menu bar, what it unrolls from, and what
/// the island shows with nothing going on. Pure arithmetic: no window is shown.
/// </summary>
public sealed class SpotGeometryTests
{
    // A 1920 × 1080 display whose menu bar has reserved its 28.
    private static readonly Rect Work = new(0, 28, 1920, 1052);

    [Fact]
    public void ThePanelHangsUnderTheBarsRightEnd()
    {
        var panel = IslandGeometry.SpotPanelRect(Work, 1);
        Assert.Equal(new Rect(1920 - 8 - IslandPage.Width, 28 + IslandGeometry.SpotDrop, IslandPage.Width, IslandPage.Height), panel);
        // A shorter home keeps the same top right corner.
        var home = IslandGeometry.SpotPanelRect(Work, 1, 280);
        Assert.Equal((panel.Right, panel.Top, 280.0), (home.Right, home.Top, home.Height));
    }

    [Fact]
    public void ItScalesWithTheDisplayAndFollowsItsPlace()
    {
        // 3440 × 1440 at 125%, to the left of the main display.
        var work = new Rect(-3440, 35, 3440, 1405);
        var panel = IslandGeometry.SpotPanelRect(work, 1.25);
        Assert.Equal(new Size(IslandPage.Width * 1.25, IslandPage.Height * 1.25), panel.Size);
        Assert.Equal(-8 * 1.25, panel.Right, 6);
        Assert.Equal(35 + IslandGeometry.SpotDrop * 1.25, panel.Top, 6);
    }

    [Fact]
    public void OnADisplayTooSmallItShrinksToFitAndStaysInside()
    {
        var work = new Rect(0, 28, 480, 300);
        var panel = IslandGeometry.SpotPanelRect(work, 1);
        Assert.True(panel.Left >= work.Left + 8 - 1e-6 && panel.Right <= work.Right - 8 + 1e-6, $"{panel}");
        Assert.True(panel.Bottom <= work.Bottom - 8 + 1e-6, $"{panel}");
        Assert.Equal(IslandPage.Width / IslandPage.Height, panel.Width / panel.Height, 6);
    }

    [Fact]
    public void ItUnrollsFromItsOwnTopEdgeAndChippiIsOnTheBarsSide()
    {
        var panel = IslandGeometry.SpotPanelRect(Work, 1.5);
        var source = IslandGeometry.SpotSource(Work, 1.5);
        Assert.Equal((panel.Left, panel.Top, panel.Width, 1.5), (source.Left, source.Top, source.Width, source.Height));
        var corner = IslandGeometry.SpotCorner(Work, 1.5);
        Assert.True(corner.Left + corner.Width / 2 > panel.Left + panel.Width / 2, "the home's Chippi sits on the right, by the bar's icons");
        Assert.Equal(panel.Top, corner.Top);
    }

}

/// <summary>What the island shows for the widget bar's activity, and the page a press on it opens. Pure: no window is shown.</summary>
public sealed class BarIslandFacesTests
{
    private static readonly IslandActivity Media = new("media", IslandActivityKind.Media, "Midnight City", "Spotify");

    private static MediaTrack Track(bool playing) =>
        new("1", "Spotify", "Midnight City", "M83", playing, false, true, true, true, false, TimeSpan.Zero, TimeSpan.FromMinutes(4), DateTimeOffset.UnixEpoch, null);

    [Fact]
    public void NothingGoingOnIsChippiAlone()
    {
        var (face, key, click) = BarIslandFaces.For([], [], null, PetMood.Sleepy);
        Assert.True(face.IsRest);
        Assert.Equal(PetMood.Sleepy, face.Mood);
        Assert.Null(key);
        Assert.Null(click);
    }

    [Fact]
    public void ASongIsItsTitleAndArtistWithBarsWhilePlayingAndAPauseMarkWhilePaused()
    {
        var (playing, key, click) = BarIslandFaces.For([Media], [], Track(playing: true), PetMood.Idle);
        Assert.Equal((IslandLead.Cover, "Midnight City", "M83", IslandTail.Equalizer, PetMood.Listening),
            (playing.Lead, playing.Text, playing.Detail, playing.Tail, playing.Mood));
        Assert.Equal(IslandPanelWindow.NowPlayingKey, key);
        Assert.Null(click);
        var (paused, _, _) = BarIslandFaces.For([Media], [], Track(playing: false), PetMood.Idle);
        Assert.Equal((IslandTail.Paused, PetMood.Idle), (paused.Tail, paused.Mood));
    }

    [Fact]
    public void ARecordingLeadsAndTheMutedMicIsTheBarsOwn()
    {
        var mic = new IslandActivity("mic", IslandActivityKind.Status, "Muted", "Microphone") { ModuleId = "mic-mute", NeedsAttention = true };
        Assert.True(BarIslandFaces.For([mic], [], null, PetMood.Idle).Face.IsRest);
        var recording = new IslandActivity("rec", IslandActivityKind.Recording, "0:42", "Screen recording") { ModuleId = "screen-recording", OpensTile = "recording" };
        var (face, key, _) = BarIslandFaces.For([mic, Media, recording], [], Track(playing: true), PetMood.Idle);
        Assert.Equal((IslandLead.Recording, "0:42", "screen-recording"), (face.Lead, face.Text, face.Module));
        Assert.Equal("recording", key);
    }

    [Fact]
    public void WhatWaitsOnYouKeepsItsColorAndOpensItsPage()
    {
        var agent = new IslandActivity("agent", IslandActivityKind.Agent, "Waiting on you", "Claude Code") { ModuleId = "agent-usage", NeedsAttention = true, OpensTile = "agent-usage" };
        var (face, key, _) = BarIslandFaces.For([agent], [], null, PetMood.Idle);
        Assert.Equal((IslandLead.Attention, "Waiting on you", "agent-usage"), (face.Lead, face.Text, face.Module));
        Assert.NotNull(face.Glyph);
        Assert.Equal("agent-usage", key);
        var working = agent with { NeedsAttention = false, Title = "Working · 42%" };
        Assert.Equal(IslandLead.Glyph, BarIslandFaces.For([working], [], null, PetMood.Idle).Face.Lead);
    }
}
