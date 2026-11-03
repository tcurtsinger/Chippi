// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Island;
using Chippi.Core.Config;
using Chippi.Core.Modules;

namespace Chippi.Island.Tests;

public sealed class AudioLayoutTests
{
    private static IslandTileGroup Tool(string key) => new(key,
        [new IslandTileContribution(key, new IslandTileSpec(key, key, "Sliders", 0, 0, () => new object()))]);

    [Fact]
    public void Legacy_audio_entries_become_one_tool_at_the_first_visible_entry()
    {
        var layout = new IslandLayout(IslandOpensOn.AllFeatures,
            ["sound.mixer", "clipboard", "sound"], new HashSet<string>());
        var tools = layout.Tools([Tool("sound"), Tool("clipboard"), Tool("sound.mixer")]);
        Assert.Equal(["audio", "clipboard"], tools.Select(t => t.Key));
        Assert.Equal("sound", tools[0].Source.Key);
        Assert.Equal("Audio", tools[0].Name);
        Assert.Equal("Speaker", tools[0].Icon);
        Assert.True(tools[0].Visible);

        tools = (layout with { Hidden = new HashSet<string> { "sound.mixer" } })
            .Tools([Tool("sound"), Tool("clipboard"), Tool("sound.mixer")]);
        Assert.Equal(["clipboard", "audio"], tools.Select(t => t.Key));
        Assert.All(tools, t => Assert.True(t.Visible));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void Legacy_hide_choices_keep_audio_visible_when_either_view_was_visible(bool devicesHidden, bool mixerHidden, bool visible)
    {
        var hidden = new HashSet<string>();
        if (devicesHidden) hidden.Add("sound");
        if (mixerHidden) hidden.Add("sound.mixer");
        var layout = IslandLayout.Default with { Hidden = hidden };
        Assert.Equal(visible, Assert.Single(layout.Tools([Tool("sound"), Tool("sound.mixer")])).Visible);
    }

    [Theory]
    [InlineData("sound")]
    [InlineData("sound.mixer")]
    public void Single_installed_view_respects_its_previous_visibility(string key)
    {
        var layout = IslandLayout.Default with { Hidden = new HashSet<string> { key } };
        var audio = Assert.Single(layout.Tools([Tool(key)]));
        Assert.Equal("audio", audio.Key);
        Assert.False(audio.Visible);
        var shown = IslandLayout.Switched(layout.Hidden, "audio", true);
        Assert.True(Assert.Single((layout with { Hidden = shown.ToHashSet() }).Tools([Tool(key)])).Visible);
    }

    [Fact]
    public void Editing_the_audio_switch_replaces_legacy_hide_flags_and_preserves_other_choices()
    {
        var hidden = IslandLayout.Switched(["sound", "sound.mixer", "clipboard"], "audio", false);
        Assert.Equal(["clipboard", "audio"], hidden);
        Assert.False(Assert.Single((IslandLayout.Default with { Hidden = hidden.ToHashSet() }).Tools([Tool("sound.mixer")])).Visible);
        Assert.Equal(["clipboard"], IslandLayout.Switched(hidden, "audio", true));
    }

    [Fact]
    public void An_explicit_audio_position_works_with_both_old_module_keys()
    {
        var layout = IslandLayout.Default with { Order = ["clipboard", "audio"] };
        Assert.Equal(["clipboard", "audio"], layout.Tools([Tool("sound.mixer"), Tool("clipboard"), Tool("sound")]).Select(t => t.Key));
    }
}
