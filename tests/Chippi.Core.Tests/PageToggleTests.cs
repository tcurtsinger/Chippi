// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;

namespace Chippi.Core.Tests;

/// <summary>A button outside the panel that opens a page closes it on a second press, though the press closes the panel first.</summary>
public sealed class PageToggleTests
{
    [Fact]
    public void A_press_on_the_open_pages_button_finds_it_open()
    {
        var toggle = new PageToggle();
        // The panel is still open on the page when the button hears of the press.
        Assert.True(toggle.WasOn("clipboard", open: true, current: "clipboard", now: 1000));
        // Open on another page, or closed: the button opens its own.
        Assert.False(toggle.WasOn("clipboard", open: true, current: "shelf", now: 1000));
        Assert.False(toggle.WasOn("clipboard", open: false, current: "clipboard", now: 1000));
        // The home is the page with no key.
        Assert.True(toggle.WasOn(null, open: true, current: null, now: 1000));
        Assert.False(toggle.WasOn(null, open: true, current: "clipboard", now: 1000));
    }

    [Fact]
    public void A_press_that_closed_the_panel_first_still_counts()
    {
        var toggle = new PageToggle();
        toggle.ClosedByPress("clipboard", now: 1000);
        Assert.True(toggle.WasOn("clipboard", open: false, current: "clipboard", now: 1010));
        // Once: the next press opens it.
        Assert.False(toggle.WasOn("clipboard", open: false, current: "clipboard", now: 1020));
    }

    [Fact]
    public void A_press_that_closed_another_page_or_an_old_one_opens_the_buttons_own()
    {
        var toggle = new PageToggle();
        toggle.ClosedByPress("shelf", now: 1000);
        Assert.False(toggle.WasOn("clipboard", open: false, current: "shelf", now: 1010));
        // A press somewhere else closed it a while ago: this press is a new one.
        toggle.ClosedByPress("clipboard", now: 2000);
        Assert.False(toggle.WasOn("clipboard", open: false, current: "clipboard", now: 2000 + PageToggle.Milliseconds + 1));
        // The home closed by the press on Controls.
        toggle.ClosedByPress(null, now: 3000);
        Assert.True(toggle.WasOn(null, open: false, current: null, now: 3005));
    }
}
