// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.AppSwitcher.Tests;

public sealed class SwitcherSessionTests
{
    internal static SwitcherWindow W(nint h, string title, string app = "Notepad", nint monitor = 1, bool minimized = false) =>
        new(h, (uint)(1000 + h), app, title, app == "" ? "" : $@"C:\Apps\{app}.exe", minimized, monitor);

    [Fact]
    public void The_window_you_came_from_is_first_and_the_next_is_selected()
    {
        var s = new SwitcherSession([W(1, "Notes"), W(2, "Mail"), W(3, "Code")], original: 1, monitor: 1, currentDisplayOnly: false);
        Assert.Equal(1, s.Selected);
        Assert.Equal(2, s.Selection!.Handle);
    }

    [Fact]
    public void When_the_original_isnt_listed_the_first_window_is_selected()
    {
        Assert.Equal(0, new SwitcherSession([W(2, "Mail"), W(3, "Code")], original: 9, monitor: 1, false).Selected);
        Assert.Equal(0, new SwitcherSession([W(1, "Notes")], original: 1, monitor: 1, false).Selected);
        var empty = new SwitcherSession([], 0, 1, false);
        Assert.Equal(-1, empty.Selected);
        Assert.Null(empty.Selection);
    }

    [Fact]
    public void Search_needs_every_word_in_the_title_or_app_and_selects_the_first_match()
    {
        var s = new SwitcherSession([W(1, "Notes - draft"), W(2, "Inbox", "Outlook"), W(3, "draft plan.txt", "Notepad"), W(4, "Wiki", "Edge")], 1, 1, false);
        int changed = 0;
        s.Changed += () => changed++;
        s.Type("DRAFT note");
        Assert.Equal([1, 3], s.Shown.Select(w => w.Handle));
        Assert.Equal(0, s.Selected);
        s.Type("outlook");
        Assert.Equal([2], s.Shown.Select(w => w.Handle));
        s.Type("w q"); // W and Q are search letters, never commands
        Assert.Empty(s.Shown);
        Assert.Equal(-1, s.Selected);
        s.Type("");
        Assert.Equal(4, s.Shown.Count);
        Assert.Equal(4, changed);
    }

    [Fact]
    public void Arrows_wrap_at_either_end()
    {
        var s = new SwitcherSession([W(1, "a"), W(2, "b"), W(3, "c")], 9, 1, false);
        s.Move(-1);
        Assert.Equal(2, s.Selected);
        s.Move(1);
        Assert.Equal(0, s.Selected);
        s.Select(1);
        Assert.Equal(1, s.Selected);
        s.Select(7);
        Assert.Equal(1, s.Selected);
    }

    [Fact]
    public void Current_display_only_keeps_the_selection_when_it_is_still_shown()
    {
        var s = new SwitcherSession([W(1, "a", monitor: 1), W(2, "b", monitor: 2), W(3, "c", monitor: 1)], 1, monitor: 1, false);
        s.Select(2);
        s.ShowCurrentDisplayOnly(true);
        Assert.Equal([1, 3], s.Shown.Select(w => w.Handle));
        Assert.Equal(3, s.Selection!.Handle);
        s.ShowCurrentDisplayOnly(false);
        Assert.Equal(3, s.Selection!.Handle);
        Assert.True(new SwitcherSession([W(1, "a", monitor: 2)], 0, 1, currentDisplayOnly: true).Shown.Count == 0);
    }

    [Fact]
    public void A_closed_window_leaves_and_the_selection_stays_in_place_without_skipping_ahead()
    {
        var s = new SwitcherSession([W(1, "a"), W(2, "b"), W(3, "c"), W(4, "d")], 1, 1, false);
        Assert.Equal(2, s.Selection!.Handle);
        Assert.True(s.Remove(2));
        Assert.Equal(3, s.Selection!.Handle); // its neighbour, which only a later Enter activates
        s.Select(2); // d, the last
        Assert.True(s.Remove(4));
        Assert.Equal(3, s.Selection!.Handle);
        Assert.True(s.Remove(1)); // above the selection
        Assert.Equal(3, s.Selection!.Handle);
        Assert.False(s.Remove(42));
        s.Type("");
        s.Type("b");
        Assert.Empty(s.Shown); // a closed window stays out after a new search
        s.Type("");
        Assert.True(s.Remove(3));
        Assert.Equal(-1, s.Selected);
    }

    [Fact]
    public void Fifty_windows_with_long_and_duplicate_titles_and_no_app_path()
    {
        string long1 = new string('x', 400) + " report";
        var windows = Enumerable.Range(1, 50).Select(i => W(i, i % 10 == 0 ? "Untitled" : i == 7 ? long1 : $"Doc {i}", i == 3 ? "" : "Word")).ToList();
        var s = new SwitcherSession(windows, 1, 1, false);
        Assert.Equal(50, s.Shown.Count);
        s.Type("untitled");
        Assert.Equal(5, s.Shown.Count);
        s.Type("report");
        Assert.Equal(7, s.Selection!.Handle);
        s.Type("Doc 3");
        Assert.Contains(s.Shown, w => w.Handle == 3 && w.App == "");
    }
}

public sealed class RecentWindowsTests
{
    [Fact]
    public void Most_recently_in_front_first_and_the_rest_keep_z_order()
    {
        var recent = new RecentWindows();
        recent.Activated(3);
        recent.Activated(1);
        recent.Activated(3);
        recent.Activated(0);
        var z = new[] { 5, 1, 4, 3, 2 }.Select(h => SwitcherSessionTests.W(h, $"w{h}")).ToList();
        Assert.Equal([3, 1, 5, 4, 2], recent.Order(z).Select(w => w.Handle));
    }

    [Fact]
    public void The_oldest_drop_off_past_capacity()
    {
        var recent = new RecentWindows();
        for (int i = 1; i <= RecentWindows.Capacity + 1; i++)
            recent.Activated(i);
        var z = new[] { 1, 2, RecentWindows.Capacity + 1 }.Select(h => SwitcherSessionTests.W(h, "w")).ToList();
        // 1 dropped off, so it falls back to z-order behind the two that are still known.
        Assert.Equal([RecentWindows.Capacity + 1, 2, 1], recent.Order(z).Select(w => w.Handle));
    }
}
