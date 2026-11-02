// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Core.Tests;

/// <summary>Chippi's commands (docs/phase-5.5/command-bar.md): every running feature's, the island's pages and Settings.</summary>
public sealed class ChippiCommandTests
{
    [Fact]
    public async Task The_list_holds_each_running_features_commands_its_pages_and_Settings()
    {
        string? opened = null;
        int settings = 0;
        using var dir = new TempDir();
        var config = new Config.ConfigStore(dir.File("settings.json"));
        config.Load();
        var services = new ModuleServices { OpenIslandPage = key => opened = key, OpenSettings = _ => settings++, DataRoot = dir.File("data") };
        var registry = new ModuleRegistry(config, new Island.IslandActivityHub(), NullLoggerFactory.Instance, services);
        var awake = new FakeModule(new FeatureDescriptor("awake", "Keep awake", "Test feature", FeatureGroup.System))
        {
            OnStart = ctx =>
            {
                ctx.Ui.AddIslandTile(new IslandTileSpec("awake", "Awake", "Coffee", 40, 0, () => new object()));
                ctx.Ui.AddCommands(() => [new ChippiCommand("awake/toggle", "Keep awake", "Coffee", () => Task.CompletedTask)]);
                ctx.Ui.AddCommandSearch(text => text == "awake 2" ? [new ChippiCommand("awake/value", "Keep awake for 2 hr", "Coffee", () => Task.CompletedTask) { Kind = ChippiCommandKind.Value }] : []);
            },
        };
        var bar = FakeModule.Create("bar");
        bar.OnStart = ctx =>
        {
            ctx.Ui.AddIslandTile(new IslandTileSpec("bar", "Search", "Search", 110, 0, () => new object()) { TakesKeyboard = true });
            ctx.Ui.AddCommands(() => throw new InvalidOperationException("broken"));
        };
        registry.Register(awake);
        registry.Register(bar);
        try
        {
            await registry.StartAsync();
            await registry.SetInstalledAsync("awake", true);
            await registry.SetInstalledAsync("bar", true);

            var all = bar.Context!.Commands();
            Assert.Contains(all, c => c.Key == "awake/toggle");
            var page = Assert.Single(all, c => c.Kind == ChippiCommandKind.Page && c.Key == "page/awake");
            Assert.Equal(("Awake", "Chippi · page"), (page.Name, page.Detail));
            Assert.Contains("Keep awake", page.Words); // found by the feature's full name too
            Assert.DoesNotContain(all, c => c.Key == "page/bar"); // the page that takes the keyboard is the Command bar itself
            await page.Run();
            Assert.Equal("awake", opened);
            await Assert.Single(all, c => c.Key == "page/settings").Run();
            Assert.Equal(1, settings);

            var values = bar.Context.SearchCommands("awake 2");
            Assert.Equal("Keep awake for 2 hr", Assert.Single(values).Name);
            Assert.Empty(bar.Context.SearchCommands("nothing"));

            await registry.SetInstalledAsync("awake", false);
            Assert.DoesNotContain(bar.Context.Commands(), c => c.Key.StartsWith("awake", StringComparison.Ordinal) || c.Key == "page/awake");
        }
        finally { await registry.StopAllAsync(); }
    }
}
