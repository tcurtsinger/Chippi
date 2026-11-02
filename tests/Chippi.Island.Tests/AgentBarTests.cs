// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Chippi.UI.Pet;

namespace Chippi.Island.Tests;

/// <summary>AI agent usage on the bar (docs/phase-5.5/ai-agent-usage.md, Surfaces 3 and 4): the agent's letters, its own lines, its alerts.</summary>
public sealed class AgentBarTests
{
    private const string Key = "agent-usage";

    private static IReadOnlyList<IslandTileGroup> AgentsTile(out IslandTileContribution tile)
    {
        tile = new IslandTileContribution(Key, new IslandTileSpec(Key, "Agents", "Gauge", 85, 0, () => new Border()));
        tile.SetStatus("Claude 42%");
        return IslandPanelTests.Tiles(tile);
    }

    private static IslandActivity Session(bool waiting) =>
        new(waiting ? "waiting" : "working", IslandActivityKind.Agent, waiting ? "Waiting on you" : "Working · 42%", waiting ? "Claude Code · packrat" : "Claude Code · chippi")
        {
            Monogram = "CC", NeedsAttention = waiting, OpensTile = Key, ModuleId = Key,
        };

    private static string? ChipKey(IslandWindow window) =>
        (string?)typeof(IslandWindow).GetField("_chipKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    private static Color Color(IslandWindow window, string key) => ((SolidColorBrush)window.FindResource(key)).Color;

    [Theory]
    [InlineData(false, "IslandAccentBrush", "Working · 42% · Claude Code · chippi")]
    [InlineData(true, "IslandCautionBrush", "Waiting on you · Claude Code · packrat")]
    public void An_agent_leads_with_its_letters_over_its_own_lines_and_opens_Agents(bool waiting, string brush, string lines) => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(AgentsTile(out _));
            window.SetActivities([Session(waiting)], PetMood.Idle);

            Assert.True(window.BarShown);
            Assert.Equal(Visibility.Visible, window.Monogram.Visibility);
            Assert.Equal("CC", window.Monogram.Text);
            Assert.Equal(Color(window, brush), ((SolidColorBrush)window.Monogram.Foreground).Color);
            Assert.Equal(Visibility.Collapsed, window.GlyphIcon.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Ring.Visibility);
            // Its own lines, not the tile's status over its name.
            Assert.Equal(lines, window.ChipText);
            Assert.Equal(Key, ChipKey(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_glyph_after_the_letters_hides_them() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(AgentsTile(out _));
            window.SetActivities([Session(waiting: false)], PetMood.Idle);
            window.SetActivities([new IslandActivity("t", IslandActivityKind.Timer, "Focus", "24:10")], PetMood.Idle);
            Assert.Equal(Visibility.Collapsed, window.Monogram.Visibility);
            Assert.Equal(Visibility.Visible, window.Ring.Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void An_alert_shows_over_a_song_with_the_gauge_and_gives_the_bar_back() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(AgentsTile(out _));
            window.SetMedia(new IslandPanelTests.FakeMedia
            {
                Sessions = [new("player", "Player", "Invented track", "", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTimeOffset.Now, null)],
            });
            var media = new IslandActivity("track", IslandActivityKind.Media, "Invented track", "Player");
            var alert = new IslandActivity("alert", IslandActivityKind.Status, "80% used", "Claude Code · 5-hour")
            {
                IsFeedback = true, NeedsAttention = true, OpensTile = Key, ModuleId = Key,
            };
            window.SetActivities([media, alert], PetMood.Idle);

            Assert.Same(LucideIcons.Gauge, window.GlyphIcon.Data);
            Assert.Equal(Color(window, "IslandCautionBrush"), ((SolidColorBrush)window.GlyphIcon.Brush).Color);
            Assert.Equal("80% used · Claude Code · 5-hour", window.ChipText);
            Assert.Equal(Key, ChipKey(window));

            window.SetActivities([media], PetMood.Idle);
            Assert.Equal("Invented track", window.ChipText);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Plain_pastes_feedback_keeps_its_glyph_and_one_line() => WpfTestThread.Run(() =>
    {
        var window = new IslandWindow();
        try
        {
            window.SetTiles(AgentsTile(out _));
            window.SetActivities([new IslandActivity("plain-paste", IslandActivityKind.Status, "Plain text copied", "") { ModuleId = "paste-plain-text" }], PetMood.Idle);
            Assert.Same(LucideIcons.ClipboardType, window.GlyphIcon.Data);
            Assert.Equal("Plain text copied", window.ChipText);
            Assert.Null(ChipKey(window));
        }
        finally { window.Close(); }
    });
}
