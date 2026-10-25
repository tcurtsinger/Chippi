// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Flyout;
using Chippi.Core.Modules;

namespace Chippi.Island.Tests;

/// <summary>How module panels and shared sections become flyout tabs.</summary>
public sealed class FlyoutTabTests
{
    private static FlyoutPanelContribution Panel(string module, string name) => new(module, name, "Zap", () => new object());

    private static FlyoutPanelContribution Section(string module, int order) =>
        new(module, "Sound", "AudioLines", () => new object(), TabKey: "sound", Order: order);

    [Fact]
    public void Sections_with_one_key_make_one_tab_where_the_first_sits()
    {
        var routing = Section("audio-routing", 1);
        var keepAwake = Panel("keep-awake", "Keep awake");
        var mute = Section("mic-mute", 3);
        var processing = Section("mic-processing", 2);

        var tabs = FlyoutTab.Group([routing, keepAwake, mute, processing]);

        Assert.Equal(["tab:sound", "keep-awake/Keep awake"], tabs.Select(t => t.Key));
        Assert.Equal([routing, processing, mute], tabs[0].Parts);
        Assert.Equal("Sound", tabs[0].Name);
    }

    [Fact]
    public void A_panel_is_a_tab_of_its_own_keyed_as_before()
    {
        var tidy = Panel("downloads-organizer", "Tidy");

        var tab = Assert.Single(FlyoutTab.Group([tidy]));

        Assert.Equal("downloads-organizer/Tidy", tab.Key);
        Assert.Equal([tidy], tab.Parts);
    }
}
