// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Controls;
using Chippi.App.Island;
using Chippi.Core.Modules;

namespace Chippi.Island.Tests;

/// <summary>The island's part in the Command bar (docs/phase-5.5/command-bar.md, Opening it).</summary>
public sealed class CommandBarIslandTests
{
    private const string Key = "command-bar";

    private static IReadOnlyList<IslandTileGroup> Tiles(TextBox field) => IslandTileContribution.Group(
    [
        new IslandTileContribution("command-bar", new IslandTileSpec(Key, "Search", "Search", 110, 0, () => new Grid())
        {
            CreateHeader = () => field,
            InitialFocus = () => field,
            TakesKeyboard = true,
            HeaderHeight = 32,
        }),
        new IslandTileContribution("other", new IslandTileSpec("other", "Other", "Activity", 50, 0, () => new Grid())),
    ]);

    [Fact]
    public void Its_page_takes_the_keyboard_as_it_shows_and_other_pages_wait_for_a_click()
    {
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            int activated = 0;
            bool foreground = false;
            window.Panel.IsForegroundWindow = _ => foreground;
            window.Panel.ActivateWindow = _ => { activated++; foreground = true; };
            var field = new TextBox { Height = 32 };
            window.SetTiles(Tiles(field));
            Assert.True(window.ShowPage("other"));
            Assert.False(window.Panel.HasKeyboard);
            Assert.True(window.ShowPage(Key));
            Assert.True(window.Panel.HasKeyboard);
            Assert.Equal(1, activated);
            // The 32-tall field gets its own header row: Back beside it, the body under it.
            Assert.Equal(32, ((System.Windows.FrameworkElement)field.Parent).Height);
        }));
    }

    [Fact]
    public void The_shortcut_opens_it_over_a_fullscreen_app_and_the_widget_hides_again_after()
    {
        WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
        {
            bool foreground = false;
            window.Panel.IsForegroundWindow = _ => foreground;
            window.Panel.ActivateWindow = _ => foreground = true;
            window.SetTiles(Tiles(new TextBox { Height = 32 }));
            window.SetSuppressed(true);
            Assert.False(window.IsVisible);
            Assert.False(window.ShowPage(Key)); // a notice click still doesn't show it
            Assert.True(window.SummonPage(Key));
            Assert.True(window.IsVisible);
            Assert.True(window.IsIslandOpen);
            Assert.Equal(Key, window.Panel.CurrentPage);
            Assert.True(window.Panel.HasKeyboard);
            window.ClosePages();
            Assert.False(window.IsVisible); // the fullscreen app is still in front
            window.SetSuppressed(false);
            Assert.True(window.IsVisible);
            window.SetEnabled(false);
            Assert.False(window.SummonPage(Key)); // off is off
        }));
    }
}
