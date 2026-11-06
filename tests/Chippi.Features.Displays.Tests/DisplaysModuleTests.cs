// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.UI.Displays;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Displays.Tests;

/// <summary>The module's start, shortcuts, rows and stop, over a fake monitor bus and a fake hotkey registrar.</summary>
public sealed class DisplaysModuleTests
{
    private sealed class Monitor : IMonitor
    {
        public int Level = 50;
        public readonly List<int> Writes = [];
        public bool TryRead(Vcp code, out int current, out int max) { current = code == Vcp.Brightness ? Level : 0; max = 100; return code == Vcp.Brightness; }
        public bool TryWrite(Vcp code, int raw) { lock (Writes) Writes.Add(raw); Level = raw; return true; }
        public void Dispose() { }
    }

    private sealed class Bus : IMonitorBus
    {
        public readonly Monitor Monitor = new();
        public IMonitor? Open(DisplayInfo display) => Monitor;
    }

    private sealed class Registrar : IHotkeyRegistrar
    {
        public readonly Dictionary<int, KeyShortcut> Held = [];
        public bool Register(int id, KeyShortcut shortcut) { Held[id] = shortcut; return true; }
        public void Unregister(int id) => Held.Remove(id);
        public event Action<int>? Pressed;
        public void Press(int id) => Pressed?.Invoke(id);
    }

    private sealed class Store : IModuleSettings
    {
        public DisplaysSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T settings) where T : class => Value = (DisplaysSettings)(object)settings;
    }

    private sealed class TileHandle : IIslandTile
    {
        public void SetStatus(string status) { }
        public void SetPageNote(string note) { }
        public bool OnScreen => false;
        public void Refresh() { }
        public event EventHandler? OnScreenChanged { add { } remove { } }
    }

    private sealed class Ui : IModuleUi
    {
        public Func<IReadOnlyList<ChippiCommand>> Commands = () => [];
        public Func<string, IReadOnlyList<ChippiCommand>> Search = _ => [];
        public bool Page;
        public IslandTileSpec? Tile;
        public IIslandTile AddIslandTile(IslandTileSpec spec)
        {
            Tile = spec;
            return new TileHandle();
        }
        public void SetSettingsPage(Func<string?, object> createView) => Page = true;
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
        public void AddCommandSearch(Func<string, IReadOnlyList<ChippiCommand>> search) => Search = search;
    }

    private sealed class Rig
    {
        public readonly Bus Bus = new();
        public readonly Registrar Registrar = new();
        public readonly Store Store = new();
        public readonly Ui Ui = new();
        public readonly IslandActivityHub Hub = new();
        public readonly ShortcutService Shortcuts;
        public readonly DisplaysModule Module;
        public readonly ModuleContext Context;

        public Rig()
        {
            Shortcuts = new ShortcutService(Registrar);
            var displays = new[] { new DisplayInfo("A", "OMEN 34c (3440×1440, main)", 1, true) { Friendly = "OMEN 34c" } };
            Module = new DisplaysModule(() => Bus, () => displays, () => 1);
            Context = new ModuleContext(DisplaysModule.Id, Store, NullLogger.Instance, Hub.CreatePublisher(DisplaysModule.Id), Ui) { Shortcuts = Shortcuts };
        }
    }

    private static void Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Thread.Sleep(2);
        }
    }

    [Fact]
    public async Task Starts_with_three_unset_slots_a_page_and_rows()
    {
        var rig = new Rig();
        await rig.Module.StartAsync(rig.Context, default);
        Assert.Equal(["Brightness up", "Brightness down", "Open Displays"], rig.Shortcuts.List.Select(s => s.Name));
        Assert.All(rig.Shortcuts.List, s => { Assert.Null(s.Shortcut); Assert.True(s.Editable); Assert.Equal(DisplaysModule.Id, s.Owner); });
        Assert.True(rig.Ui.Page);
        Assert.Equal(["Brightness up", "Brightness down"], rig.Ui.Commands().Select(c => c.Name));
        await rig.Module.StopAsync(default);
        Assert.Empty(rig.Shortcuts.List);
    }

    [Fact]
    public async Task The_island_gets_a_Displays_tile_and_the_module_answers_the_panel()
    {
        var rig = new Rig();
        await rig.Module.StartAsync(rig.Context, default);
        Until(() => rig.Module.Displays is [{ Brightness: 50 }]);
        IDisplaysPanelModel panel = rig.Module;
        Assert.Equal(("displays", "Displays", 65), (rig.Ui.Tile!.Key, rig.Ui.Tile.Name, rig.Ui.Tile.Order));
        Assert.NotNull(rig.Ui.Tile.CreateHeader);
        Assert.Single(panel.PanelDisplays);
        Assert.Equal(rig.Module.Displays[0].Id, panel.UnderPointer());
        panel.Set(panel.PanelDisplays[0].Id, DisplayControl.Brightness, 35);
        Until(() => rig.Bus.Monitor.Writes.Count == 1);
    }

    [Fact]
    public async Task A_key_steps_the_display_and_the_row_runs_too()
    {
        var rig = new Rig();
        await rig.Module.StartAsync(rig.Context, default);
        Until(() => rig.Module.Displays is [{ Brightness: 50 }]);
        var up = rig.Shortcuts.List[0];
        Assert.Null(rig.Shortcuts.Change(up.Id, new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x26)));
        Assert.Equal(new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x26), rig.Store.Value.BrightnessUp);
        Assert.Single(rig.Registrar.Held);
        rig.Registrar.Press(rig.Registrar.Held.Keys.Single());
        Until(() => rig.Bus.Monitor.Writes.Count == 1);
        Assert.Equal(60, rig.Bus.Monitor.Writes[0]);
        await rig.Ui.Commands()[1].Run();
        Until(() => rig.Bus.Monitor.Writes.Count == 2);
        Assert.Equal(50, rig.Bus.Monitor.Writes[1]);
        rig.Shortcuts.Remove(up.Id);
        Assert.Null(rig.Store.Value.BrightnessUp);
        Assert.Empty(rig.Registrar.Held);
        await rig.Module.StopAsync(default);
    }

    [Fact]
    public async Task Saved_keys_are_held_at_start_and_the_step_setting_applies()
    {
        var rig = new Rig();
        rig.Store.Value = new DisplaysSettings { Step = 20, BrightnessDown = new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x28) };
        await rig.Module.StartAsync(rig.Context, default);
        Until(() => rig.Module.Displays is [{ Brightness: 50 }]);
        rig.Registrar.Press(rig.Registrar.Held.Keys.Single());
        Until(() => rig.Bus.Monitor.Writes.Count == 1);
        Assert.Equal(30, rig.Bus.Monitor.Writes[0]);
        Assert.False(rig.Module.SetStep(7));
        Assert.True(rig.Module.SetStep(5));
        Assert.True(rig.Module.SetActOn(BrightnessTarget.AllDisplays));
        Assert.Equal(new DisplaysSettings { Step = 5, ActOn = BrightnessTarget.AllDisplays, BrightnessDown = rig.Store.Value.BrightnessDown }, rig.Store.Value);
        await rig.Module.StopAsync(default);
    }

    [Fact]
    public async Task Search_values_give_a_value_row_then_a_row_per_display()
    {
        var rig = new Rig();
        await rig.Module.StartAsync(rig.Context, default);
        Until(() => rig.Module.Displays is [{ Brightness: 50 }]);
        var rows = rig.Ui.Search("brightness 40");
        Assert.Equal(["Brightness to 40%", "OMEN 34c to 40%"], rows.Select(r => r.Name));
        Assert.All(rows, r => Assert.Equal(ChippiCommandKind.Value, r.Kind));
        await rows[0].Run();
        Until(() => rig.Bus.Monitor.Writes.Count == 1);
        Assert.Equal(40, rig.Bus.Monitor.Writes[0]);
        Assert.Equal("OMEN 34c · 40%", Assert.Single(rig.Ui.Search("brightness")).Name);
        Assert.Empty(rig.Ui.Search("volume 40"));
        await rig.Module.StopAsync(default);
    }

    [Fact]
    public async Task A_key_on_a_display_that_doesnt_answer_shows_the_bar_line()
    {
        var rig = new Rig();
        var failing = new DisplaysModule(() => new FailingBus(), () => [new DisplayInfo("A", "OMEN", 1, true)], () => 1, sleep: _ => { });
        await failing.StartAsync(new ModuleContext(DisplaysModule.Id, rig.Store, NullLogger.Instance, rig.Hub.CreatePublisher(DisplaysModule.Id), rig.Ui) { Shortcuts = rig.Shortcuts }, default);
        Until(() => failing.Displays is [{ Failed: true }]);
        await rig.Ui.Commands()[0].Run();
        Until(() => rig.Hub.Current is not null);
        var line = rig.Hub.Current!;
        Assert.Equal("Brightness control failed", line.Title);
        Assert.True(line.IsFeedback);
        await failing.StopAsync(default);
    }

    private sealed class FailingBus : IMonitorBus
    {
        public IMonitor? Open(DisplayInfo display) => null;
    }
}
