// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;

namespace Chippi.Core.Tests;

/// <summary>Island tiles and shared flyout tabs (docs/phase-3/architecture.md §5).</summary>
public sealed class IslandTileTests : IDisposable
{
    private readonly RegistryFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static IslandTileSpec Spec(string key, string name, int order, int section, double width = IslandTileSpec.DefaultPageWidth) =>
        new(key, name, "Mic", order, section, () => new object()) { PageWidth = width };

    [Fact]
    public async Task A_running_module_s_tiles_are_removed_when_it_stops()
    {
        var module = FakeModule.Create("m");
        module.OnStart = ctx => ctx.Ui.AddIslandTile(Spec("t", "Tile", 10, 0));
        _f.Registry.Register(module);
        await _f.Registry.StartAsync();

        await _f.Registry.SetInstalledAsync("m", true);
        var entry = _f.Registry.Find("m")!;
        Assert.Equal("t", Assert.Single(entry.IslandTiles).Spec.Key);

        await _f.Registry.SetInstalledAsync("m", false);
        Assert.Empty(entry.IslandTiles);
    }

    [Fact]
    public void Parts_with_one_key_make_one_tile_named_by_its_first_section()
    {
        var mute = new IslandTileContribution("mic-mute", Spec("sound.microphone", "Mic mute", 30, 2));
        var routing = new IslandTileContribution("audio-routing", Spec("sound.microphone", "Microphone", 30, 0));
        var mixer = new IslandTileContribution("app-volume", Spec("sound.mixer", "Volume mixer", 10, 0, width: 344));
        var processing = new IslandTileContribution("mic-processing", Spec("sound.microphone", "Processing", 30, 1, width: 300));

        var tiles = IslandTileContribution.Group([mute, routing, mixer, processing]);

        Assert.Equal(["sound.mixer", "sound.microphone"], tiles.Select(t => t.Key));
        var microphone = tiles[1];
        Assert.Equal([routing, processing, mute], microphone.Parts);
        Assert.Equal("Microphone", microphone.Name);
        Assert.Equal(IslandTileSpec.DefaultPageWidth, microphone.PageWidth);
        Assert.Equal(344, tiles[0].PageWidth);
    }

    [Fact]
    public void A_shared_tile_shows_the_highest_priority_status()
    {
        var processing = new IslandTileContribution("mic-processing", Spec("sound.microphone", "Microphone", 30, 1));
        var mute = new IslandTileContribution("mic-mute", Spec("sound.microphone", "Microphone", 30, 2) with { StatusPriority = 10 });
        var tile = IslandTileContribution.Group([processing, mute]).Single();

        processing.SetStatus("Live");
        Assert.Equal("Live", tile.Status);
        mute.SetStatus("Muted");
        Assert.Equal("Muted", tile.Status);
        mute.SetStatus("");
        Assert.Equal("Live", tile.Status);
    }

    [Fact]
    public void Status_changes_are_raised_once_per_change()
    {
        var tile = new IslandTileContribution("m", Spec("t", "Tile", 0, 0));
        int raised = 0;
        tile.StatusChanged += (_, _) => raised++;

        tile.SetStatus("On");
        tile.SetStatus("On");
        tile.SetStatus("Off");

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Orbit_comes_from_whichever_part_sets_one()
    {
        var plain = new IslandTileContribution("a", Spec("t", "Tile", 0, 0));
        var left = new IslandTileContribution("b", Spec("t", "Tile", 0, 1) with { Orbit = IslandOrbit.Left });

        Assert.Equal(IslandOrbit.Left, IslandTileContribution.Group([plain, left]).Single().Orbit);
    }

    [Fact]
    public void A_feature_needs_nothing_outside_Chippi_by_default()
    {
        Assert.Null(FakeModule.Create("m").Descriptor.Requirement);
    }

    [Fact]
    public void A_page_note_replaces_the_status_in_the_header_and_notifies_the_host()
    {
        var part = new IslandTileContribution("system-monitor", Spec("system-monitor", "System monitor", 60, 0));
        int changes = 0;
        part.StatusChanged += (_, _) => changes++;

        part.SetPageNote("Last 4 min");
        part.SetPageNote("Last 4 min");
        var tile = Assert.Single(IslandTileContribution.Group([part]));

        Assert.Equal("Last 4 min", tile.PageNote);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void On_screen_changes_are_reported_once_each()
    {
        var part = new IslandTileContribution("system-monitor", Spec("system-monitor", "System monitor", 60, 0));
        var seen = new List<bool>();
        part.OnScreenChanged += (_, _) => seen.Add(part.OnScreen);

        part.SetOnScreen(true);
        part.SetOnScreen(true);
        part.SetOnScreen(false);

        Assert.Equal([true, false], seen);
    }

    [Fact]
    public async Task A_module_opens_its_island_page_through_the_shell()
    {
        string? opened = null;
        using var dir = new TempDir();
        var config = new Config.ConfigStore(dir.File("settings.json"));
        config.Load();
        var registry = new ModuleRegistry(config, new Island.IslandActivityHub(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            new ModuleServices { OpenIslandPage = key => opened = key, DataRoot = dir.File("data") });
        var module = FakeModule.Create("m");
        module.OnStart = ctx => ctx.OpenIslandPage("t");
        registry.Register(module);
        try
        {
            await registry.StartAsync();
            await registry.SetInstalledAsync("m", true);

            Assert.Equal("t", opened);
        }
        finally { await registry.StopAllAsync(); }
    }
}
