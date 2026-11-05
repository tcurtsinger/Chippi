// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Mute;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.QuickActions.Tests;

public sealed class LayoutPersistenceTests
{
    [Fact]
    public void Old_settings_keep_the_packed_gallery_order()
    {
        var layout = QuickActionsLayout.Normalize(null, ["power", "dark", "explorer"]);
        Assert.Equal(new[] { "dark", "explorer", "power" }, layout.Take(3));
        Assert.Equal(20, layout.Count);
        Assert.Equal(QuickAction.All.Count(a => a.InGallery), layout.OfType<string>().Distinct().Count());
    }

    [Fact]
    public void The_four_moved_tiles_close_up_their_slots_and_the_rest_keep_their_order_and_own_gaps()
    {
        // A layout saved before the regroup: all 19 tiles in a custom order, with one gap of his own at slot 3.
        string?[] saved =
        [
            "power", "quit", "lock", null, "dark", "frozen", "wifi", "displays", "plan", "hidden",
            "icons", "screensaver", "auto-hide", "extend", "bluetooth", "camera", "recycle", "eject", "explorer", "cleaning",
        ];
        var layout = QuickActionsLayout.Normalize(saved, null);
        string?[] expected = ["power", "quit", null, "dark", "wifi", "plan", "hidden", "icons", "auto-hide", "extend", "bluetooth", "camera", "recycle", "eject", "explorer", "cleaning"];
        Assert.Equal(expected, layout.Take(expected.Length));
        Assert.All(layout.Skip(expected.Length), Assert.Null);
        Assert.DoesNotContain(layout, key => key is "lock" or "displays" or "screensaver" or "frozen");
        Assert.Equal(20, layout.Count);
        // Closing up again changes nothing.
        Assert.Equal(layout, QuickActionsLayout.Normalize(layout, null));
    }

    [Fact]
    public void Malformed_layout_is_repaired_without_moving_valid_positions()
    {
        string?[] saved = [null, "explorer", "explorer", "retired", null, "dark"];
        var repaired = QuickActionsLayout.Normalize(saved, ["explorer", "dark"]);
        Assert.Equal("explorer", repaired[1]);
        Assert.Equal("dark", repaired[5]);
        Assert.Equal(15, repaired.OfType<string>().Distinct().Count());
        Assert.Equal(QuickActionsLayout.Slots - 15, repaired.Count(key => key is null));
    }

    [Fact]
    public async Task Gaps_and_unavailable_choices_survive_edit_save_json_and_restart()
    {
        var store = new Store { Value = new() { Shown = ["dark", "bluetooth"] } };
        var pc = new FakePc { State = QuickState.Empty with { Unsupported = new HashSet<QuickActionId> { QuickActionId.Bluetooth } } };
        var module = await Start(store, pc);
        try
        {
            var layout = module.Layout.ToArray();
            (layout[0], layout[19]) = (layout[19], layout[0]);
            module.SaveLayout([QuickActionId.Dark], layout);
            Assert.Contains("bluetooth", store.Value.Shown!);
            Assert.Null(store.Value.Layout![0]);
            Assert.Equal("dark", store.Value.Layout[19]);
            module.SetShown(QuickActionId.Explorer, true);
            Assert.Equal(layout, store.Value.Layout);
        }
        finally { await module.StopAsync(default); }
        store.Value = JsonSerializer.Deserialize<QuickActionsSettings>(JsonSerializer.Serialize(store.Value))!;
        pc.State = pc.State with { Unsupported = new HashSet<QuickActionId>() };
        var restarted = await Start(store, pc);
        try
        {
            Assert.Null(restarted.Layout[0]);
            Assert.Equal("dark", restarted.Layout[19]);
            Assert.Contains(restarted.Shown, action => action.Id == QuickActionId.Bluetooth);
        }
        finally { await restarted.StopAsync(default); }
    }

    [Fact]
    public async Task Bulk_quit_uses_the_reviewed_snapshot_when_another_app_opens()
    {
        var pc = new FakePc { State = QuickState.Empty with { Apps = [FakePc.Chrome, FakePc.Spotify] } };
        var module = await Start(new Store { Value = new() { Kept = [FakePc.Spotify.Path] } }, pc);
        try
        {
            var reviewed = module.Quitting(keepFront: false);
            pc.State = pc.State with { Apps = [.. pc.State.Apps, FakePc.Discord] };
            await module.RefreshAsync();
            await module.QuitAllAsync(reviewed);
            Assert.Equal(new[] { "quit Google Chrome" }, pc.Calls);
            Assert.Equal(new[] { FakePc.Spotify, FakePc.Discord }, module.State.Apps);
        }
        finally { await module.StopAsync(default); }
    }

    private static async Task<QuickActionsModule> Start(Store store, FakePc pc)
    {
        var module = new QuickActionsModule(() => pc, () => new NoHotkey());
        await module.StartAsync(new ModuleContext(QuickActionsModule.Id, store, NullLogger.Instance,
            new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), new Ui()), default);
        return module;
    }

    private sealed class Store : IModuleSettings
    {
        public QuickActionsSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (QuickActionsSettings)(object)value;
    }
    private sealed class Ui : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(QuickActionsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> factory) { }
    }
    private sealed class NoHotkey : IMicHotkey
    {
        public bool Register(MicShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
