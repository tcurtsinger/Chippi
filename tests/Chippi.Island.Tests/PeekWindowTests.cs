// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.App.Island;
using Chippi.Core.Modules;

namespace Chippi.Island.Tests;

/// <summary>Real window integration: CI only while the owner is at the PC.</summary>
[Collection("ReducedMotion")]
public sealed class PeekWindowTests
{
    private static IReadOnlyList<IslandTileGroup> Tiles() => IslandTileContribution.Group([
        new("fixture", new IslandTileSpec("original", "Original", "Activity", 1, 0, () => new Grid())),
        new("fixture", new IslandTileSpec("peek-test", "Peek target", "Activity", 2, 0, () => new Grid())),
        new("fixture", new IslandTileSpec("search-test", "Search fixture", "Search", 3, 0, () => new Grid()) { TakesKeyboard = true }),
        new("fixture", new IslandTileSpec(IslandTool.AudioPages[0], "Devices", "Volume", 4, 0, () => new Grid())),
        new("fixture", new IslandTileSpec(IslandTool.AudioPages[1], "App volume", "Volume", 5, 0, () => new Grid())),
    ]);

    [Fact]
    public void Peek_stays_open_on_click_away_and_Esc_closes_it() => WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
    {
        window.SetTiles(Tiles()); int closed = 0; window.PeekClosed += () => closed++;
        Assert.True(window.OpenPeek("peek-test")); IslandWindowTests.Settle(window.Panel);
        window.PressedAt(new Point(-30000, -30000)); IslandWindowTests.Settle(window.Panel);
        Assert.True(window.IsIslandOpen); Assert.Equal("peek-test", window.Panel.CurrentPage);
        window.Escape(); IslandWindowTests.PumpUntil(() => !window.IsIslandOpen); IslandWindowTests.Settle(window.Panel);
        Assert.Equal(1, closed);
    }));

    [Fact]
    public void A_click_away_queued_before_Peek_cannot_close_the_new_visit() => WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
    {
        window.SetTiles(Tiles()); Assert.True(window.ShowPage("original")); IslandWindowTests.Settle(window.Panel);
        window.PressedAt(new Point(-30000, -30000));
        Assert.True(window.OpenPeek("peek-test")); IslandWindowTests.Settle(window.Panel);
        Assert.True(window.IsIslandOpen); window.ClosePeek();
    }));

    [Fact]
    public void Peek_visits_and_navigation_preserve_normal_destination_and_Audio_memory() => WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
    {
        window.SetTiles(Tiles()); window.SetLastPage("original", false, IslandTool.AudioPages[0]);
        int remembered = 0; window.LastPageChanged += (_, _) => remembered++;
        Assert.True(window.OpenPeek(IslandTool.AudioPages[1])); IslandWindowTests.Settle(window.Panel);
        Assert.True(window.ShowPage("peek-test")); IslandWindowTests.Settle(window.Panel);
        window.PressedAt(new Point(-30000, -30000)); IslandWindowTests.Settle(window.Panel);
        Assert.True(window.IsIslandOpen);
        window.ClosePeek();
        // Reversing the close must not reopen the temporary destination.
        Assert.True(window.ShowPages(false)); IslandWindowTests.Settle(window.Panel);
        Assert.Equal("original", window.Panel.CurrentPage); Assert.Equal(IslandTool.AudioPages[0], window.Panel.AudioPage);
        Assert.Equal(0, remembered);
    }));

    [Fact]
    public void Home_and_removed_Peek_targets_override_the_normal_last_page() => WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
    {
        window.SetTiles(Tiles()); window.SetLastPage("original");
        foreach (string? key in new string?[] { null, "uninstalled-panel" })
        {
            Assert.True(window.OpenPeek(key)); IslandWindowTests.Settle(window.Panel); Assert.Null(window.Panel.CurrentPage);
            window.ClosePeek(); IslandWindowTests.Settle(window.Panel);
        }
        Assert.True(window.OpenPeek("peek-test")); IslandWindowTests.Settle(window.Panel);
        window.SetTiles([]); Assert.Null(window.Panel.CurrentPage); window.ClosePeek();
    }));

    [Fact]
    public void Peek_over_fullscreen_does_not_take_focus_even_on_a_typing_page() => WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
    {
        window.SetTiles(Tiles()); int activated = 0;
        window.Panel.IsForegroundWindow = _ => false; window.Panel.ActivateWindow = _ => activated++;
        window.SetSuppressed(true); Assert.False(window.IsVisible);
        Assert.True(window.OpenPeek("search-test")); IslandWindowTests.Settle(window.Panel);
        Assert.True(window.IsIslandOpen); Assert.False(window.Panel.HasKeyboard); Assert.Equal(0, activated);
        window.ClosePeek(); Assert.False(window.IsVisible);
        window.SetSuppressed(false); window.SetEnabled(false); Assert.False(window.OpenPeek("peek-test"));
    }));

    [Fact]
    public void A_missing_concrete_Audio_Peek_page_falls_back_to_Home_while_the_Audio_alias_still_resolves() => WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
    {
        window.SetTiles(Tiles()); Assert.True(window.OpenPeek(IslandTool.AudioPages[1])); IslandWindowTests.Settle(window.Panel);
        window.SetTiles(Tiles().Where(t => t.Key != IslandTool.AudioPages[1]).ToArray());
        Assert.Null(window.Panel.CurrentPage); window.ClosePeek(); IslandWindowTests.Settle(window.Panel);
        Assert.True(window.OpenPeek(IslandTool.AudioPages[1])); Assert.Null(window.Panel.CurrentPage); window.ClosePeek(); IslandWindowTests.Settle(window.Panel);
        Assert.True(window.OpenPeek(IslandTool.Audio)); Assert.Equal(IslandTool.AudioPages[0], window.Panel.CurrentPage); window.ClosePeek();
    }));

    [Fact]
    public void Hold_closes_after_the_last_dialog_lease_finishes_so_owned_dialogs_stay_usable() => WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
    {
        window.SetTiles(Tiles()); Assert.True(window.OpenPeek("peek-test"));
        using var first = window.HoldIslandOpen(); using var second = window.HoldIslandOpen();
        window.ClosePeek(); Assert.True(window.IsIslandOpen);
        first.Dispose(); Assert.True(window.IsIslandOpen);
        second.Dispose(); Assert.False(window.IsIslandOpen);
    }));

    [Fact]
    public void A_new_Peek_visit_cancels_the_previous_pending_dialog_collapse() => WpfTestThread.Run(() => IslandWindowTests.WithShownIsland(window =>
    {
        window.SetTiles(Tiles()); Assert.True(window.OpenPeek("peek-test"));
        using var hold = window.HoldIslandOpen(); window.ClosePeek();
        Assert.True(window.OpenPeek("original")); hold.Dispose(); Assert.True(window.IsIslandOpen);
        window.ClosePeek(); Assert.False(window.IsIslandOpen);
    }));
}
