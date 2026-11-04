// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Island;
using Chippi.Core.Clipboard;

namespace Chippi.Island.Tests;

/// <summary>Fake foreground ownership only: these tests never create a window or invoke native activation.</summary>
public sealed class IslandKeyboardHandoffTests
{
    [Fact]
    public void A_refused_activation_retries_on_the_next_press_then_stops_when_foreground()
    {
        var handoff = new IslandKeyboardHandoff();
        bool foreground = false;
        int attempts = 0;
        bool Press() => handoff.Take(default, _ => foreground, null, _ =>
        {
            foreground = ++attempts == 2;
        });

        Assert.True(Press());
        Assert.True(handoff.Requested);
        Assert.False(foreground);
        Assert.True(Press());
        Assert.True(foreground);
        Assert.False(Press());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void A_game_reclaiming_the_foreground_allows_another_deliberate_handoff()
    {
        var handoff = new IslandKeyboardHandoff();
        bool foreground = false;
        int attempts = 0, captures = 0;
        var original = new PasteTarget(17, 23);
        bool Press() => handoff.Take(default, _ => foreground,
            () => ++captures == 1 ? original : new PasteTarget(99, 100),
            _ => { attempts++; foreground = true; });

        Assert.True(Press());
        Assert.False(Press());
        foreground = false;
        Assert.True(Press());
        Assert.Same(original, handoff.PasteTarget);
        Assert.Equal(1, captures);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Refused_handoffs_preserve_the_original_paste_target_even_when_capture_was_null()
    {
        foreach (var original in new PasteTarget?[] { new(17, 23), null })
        {
            var handoff = new IslandKeyboardHandoff();
            int captures = 0;
            for (int press = 0; press < 3; press++)
                Assert.True(handoff.Take(default, _ => false,
                    () => ++captures == 1 ? original : new PasteTarget(99, 100), _ => { }));
            Assert.Equal(1, captures);
            Assert.Same(original, handoff.PasteTarget);
        }
    }

    [Fact]
    public void An_already_foreground_panel_needs_no_activation_and_does_not_capture_itself()
    {
        var handoff = new IslandKeyboardHandoff();
        bool Press() => handoff.Take(default, _ => true,
            () => throw new InvalidOperationException("The island cannot be its own paste destination."),
            _ => throw new InvalidOperationException("Already in front."));

        Assert.True(Press()); // Initial focus still needs placing.
        Assert.True(handoff.Requested);
        Assert.Null(handoff.PasteTarget);
        Assert.False(Press());
    }

    [Fact]
    public void Closing_and_reopening_captures_a_new_destination()
    {
        var handoff = new IslandKeyboardHandoff();
        var first = new PasteTarget(17, 23);
        var second = new PasteTarget(99, 100);
        handoff.Take(default, _ => false, () => first, _ => { });
        handoff.Reset();
        Assert.False(handoff.Requested);
        Assert.Null(handoff.PasteTarget);
        Assert.True(handoff.Take(default, _ => false, () => second, _ => { }));
        Assert.Same(second, handoff.PasteTarget);
    }
}
