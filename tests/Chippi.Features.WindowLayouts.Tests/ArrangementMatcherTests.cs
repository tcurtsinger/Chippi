// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Features.WindowLayouts;

namespace Chippi.Features.WindowLayouts.Tests;

public sealed class ArrangementMatcherTests
{
    private static ArrangementCandidate Candidate(int handle, int x, string title = "Document") =>
        new(new ActionWindow(handle, 1, 1, 1, "Editor", "editor.exe", "Editor", title, false, 1),
            "editor", new(x, 0, 500, 400));

    [Fact]
    public void Nearest_is_guessed_and_reserved_before_the_next_row()
    {
        var matcher = new ArrangementMatcher();
        var windows = new[] { Candidate(1, 0), Candidate(2, 1000) };
        var destination = new ArrangementBounds(900, 0, 500, 400);
        var first = matcher.Match("EDITOR", "Editor", "Document", destination, windows)!;
        Assert.Equal((nint)2, first.Candidate.Window.Handle);
        Assert.True(first.Guessed);
        var second = matcher.Match("editor", "Editor", "Document", destination, windows)!;
        Assert.Equal((nint)1, second.Candidate.Window.Handle);
        Assert.False(second.Guessed);
        Assert.Null(matcher.Match("editor", "Editor", "Document", destination, windows));
    }

    [Fact]
    public void Exact_title_beats_distance_and_ties_keep_enumeration_order()
    {
        var windows = new[] { Candidate(1, 0, "Other"), Candidate(2, 1000), Candidate(3, 1000) };
        var match = new ArrangementMatcher().Match("editor", "Editor", "Document", new(0, 0, 500, 400), windows)!;
        Assert.Equal((nint)2, match.Candidate.Window.Handle);
        Assert.True(match.Guessed);
    }

    [Fact]
    public void Other_desktops_admin_and_ineligible_windows_are_never_selected()
    {
        var candidate = Candidate(1, 0);
        var windows = new[] { candidate with { CurrentDesktop = false },
            candidate with { Window = candidate.Window with { Elevated = true } }, candidate with { Eligible = false } };
        Assert.Null(new ArrangementMatcher().Match("editor", "Editor", "Document", candidate.Bounds, windows));
    }

    [Fact]
    public void Saved_bounds_fit_negative_origin_and_never_grow()
    {
        var saved = new ArrangementBounds(-1800, 100, 800, 600);
        Assert.Equal(new ArrangementBounds(120, 100, 800, 600),
            saved.Fit(new(-1920, 0, 1920, 1080), new(0, 0, 1920, 1080)));
        Assert.Equal(800, saved.Fit(new(-1920, 0, 1920, 1080), new(0, 0, 3840, 2160)).Width);
    }
}
