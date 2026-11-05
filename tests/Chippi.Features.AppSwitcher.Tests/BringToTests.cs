// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.AppSwitcher.Windows;
using static Chippi.Features.AppSwitcher.Tests.SwitcherSessionTests;

namespace Chippi.Features.AppSwitcher.Tests;

/// <summary>The real switcher PC hands the move to the shell's window moves, and leaves in place what can't move.</summary>
public sealed class BringToTests
{
    private readonly ActionPc _actions = new();

    [Fact]
    public void A_window_on_another_display_is_moved_by_the_shells_window_moves()
    {
        Assert.True(new WindowsSwitcherPc(0, _actions).BringTo(W(5, "Mail", monitor: 2), 1));
        Assert.Equal(((nint)5, (nint)1), Assert.Single(_actions.Moves));
    }

    [Fact]
    public void An_admin_window_is_not_tried()
    {
        _actions.Admin.Add(5);
        Assert.False(new WindowsSwitcherPc(0, _actions).BringTo(W(5, "Terminal", monitor: 2), 1));
        Assert.Empty(_actions.Moves);
    }

    [Theory]
    [InlineData("Fullscreen")]
    [InlineData("Snap failed")]
    [InlineData("Window closed")]
    public void A_window_that_cant_move_stays_where_it_is(string reason)
    {
        _actions.MoveFails = reason;
        Assert.False(new WindowsSwitcherPc(0, _actions).BringTo(W(5, "Game", monitor: 2), 1));
    }

    [Fact]
    public void Without_the_shells_window_moves_nothing_moves()
    {
        Assert.False(new WindowsSwitcherPc(0).BringTo(W(5, "Mail", monitor: 2), 1));
    }
}
