// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Input;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Shortcuts.Tests;

public sealed class ModuleTests
{
    private const ShortcutModifiers CtrlAlt = ShortcutModifiers.Control | ShortcutModifiers.Alt;

    private static ShortcutBinding Key(ShortcutModifiers mods, uint key, string name = "Snap left") => new()
    {
        Door = BindingDoor.Key,
        Keys = new KeyShortcut(mods, key),
        Action = new ChippiAction { Kind = ActionKind.Window, Value = "SnapLeft", Name = name },
    };

    private static ShortcutBinding Mouse(MouseTrigger button, string name = "Maximize") => new()
    {
        Door = BindingDoor.Mouse,
        Button = button,
        Action = new ChippiAction { Kind = ActionKind.Window, Value = "Maximize", Name = name },
    };

    private sealed class Rig
    {
        public FakeRegistrar Registrar { get; } = new();
        public FakeHub Hub { get; } = new();
        public Store Settings { get; } = new();
        public FakeActions Actions { get; } = new();
        public ShortcutService Service { get; }
        public ShortcutsModule Module { get; }
        public bool RadialHolds;

        public Rig(ShortcutsSettings? start = null, bool actions = true)
        {
            Service = new ShortcutService(Registrar);
            Module = new ShortcutsModule(() => actions ? Actions : null, _ => RadialHolds) { InlinePosts = true };
            if (start is not null)
                Settings.Value = start;
        }

        public async Task StartAsync() => await Module.StartAsync(
            new ModuleContext(ShortcutsModule.Id, Settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher(ShortcutsModule.Id), new Ui())
            { Input = Hub, Shortcuts = Service }, default);
    }

    [Fact]
    public async Task A_key_binding_is_one_of_the_services_shortcuts_and_pressing_it_runs_its_action_on_the_window_in_front()
    {
        var rig = new Rig();
        await rig.StartAsync();
        var binding = Key(CtrlAlt, 0x4B);
        Assert.True(rig.Module.Add(binding).Ok);

        var entry = Assert.Single(rig.Service.List);
        Assert.Equal(("shortcuts", "Snap left"), (entry.Owner, entry.Name));
        Assert.Equal(binding.Keys, entry.Shortcut);
        Assert.Equal(BindingState.Active, rig.Module.StatusOf(binding.Id).State);

        rig.Registrar.Press(binding.Keys!);
        await rig.Actions.Ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("Snap left", WindowTarget.InFront), rig.Actions.Runs.Single());
        Assert.Empty(rig.Hub.Filters); // a key needs no hook
    }

    [Fact]
    public async Task Another_app_holding_the_keys_shows_in_use_by_another_app_and_the_row_stays()
    {
        var rig = new Rig();
        rig.Registrar.Refused.Add(new KeyShortcut(CtrlAlt, 0x4B));
        await rig.StartAsync();
        var binding = Key(CtrlAlt, 0x4B);
        Assert.True(rig.Module.Add(binding).Ok);
        Assert.Equal(new BindingStatus(BindingState.InUseByApp, "In use by another app"), rig.Module.StatusOf(binding.Id));
        Assert.Single(rig.Module.Settings.Bindings);
    }

    [Fact]
    public async Task A_key_used_twice_by_shortcuts_is_refused_with_the_other_ones_name()
    {
        var rig = new Rig();
        await rig.StartAsync();
        Assert.True(rig.Module.Add(Key(CtrlAlt, 0x4B, "Snap left")).Ok);
        var second = rig.Module.Add(Key(CtrlAlt, 0x4B, "Maximize"));
        Assert.Equal("Snap left already uses this.", second.Error);
        Assert.Single(rig.Module.Settings.Bindings);
    }

    [Fact]
    public async Task A_key_another_Chippi_feature_holds_shows_the_service_s_words()
    {
        var rig = new Rig();
        var mic = rig.Service.Add("mic-mute", "Mute microphone");
        Assert.True(mic.Register(new KeyShortcut(CtrlAlt, 0x4D), () => { }));
        await rig.StartAsync();
        var binding = Key(CtrlAlt, 0x4D);
        Assert.True(rig.Module.Add(binding).Ok);
        Assert.Equal(new BindingStatus(BindingState.UsedTwice, "Used by Mute microphone"), rig.Module.StatusOf(binding.Id));
    }

    [Theory]
    [InlineData(ShortcutModifiers.Windows, 0x4C)]
    [InlineData(ShortcutModifiers.Alt, 0x09)]
    [InlineData(ShortcutModifiers.Alt, 0x73)]
    public async Task A_combination_Windows_keeps_saves_shows_in_use_by_Windows_and_is_never_registered(ShortcutModifiers mods, int key)
    {
        var rig = new Rig();
        await rig.StartAsync();
        var binding = Key(mods, (uint)key);
        Assert.True(rig.Module.Add(binding).Ok);
        Assert.Equal(new BindingStatus(BindingState.InUseByWindows, "In use by Windows"), rig.Module.StatusOf(binding.Id));
        Assert.Empty(rig.Registrar.Registered);
        Assert.Empty(rig.Service.List);
        Assert.Empty(rig.Hub.Filters);
    }

    [Fact]
    public async Task A_free_Win_combination_binds_through_RegisterHotKey_and_one_it_refuses_goes_through_the_hub()
    {
        var rig = new Rig();
        await rig.StartAsync();
        var free = Key(ShortcutModifiers.Windows, 0x4A, "Snap left"); // Win+J
        Assert.True(rig.Module.Add(free).Ok);
        Assert.Equal(BindingState.Active, rig.Module.StatusOf(free.Id).State);
        Assert.Contains(free.Keys!, rig.Registrar.Registered);
        Assert.Empty(rig.Hub.Filters);

        rig.Registrar.Refused.Add(new KeyShortcut(ShortcutModifiers.Windows, 0x59));
        var refused = Key(ShortcutModifiers.Windows, 0x59, "Maximize"); // Win+Y
        Assert.True(rig.Module.Add(refused).Ok);
        Assert.Equal(BindingState.Active, rig.Module.StatusOf(refused.Id).State);
        var filter = Assert.Single(rig.Hub.Filters);
        Assert.Equal(InputDevices.Keyboard, filter.Devices);

        // Win+Y in the hub runs the action on the window in front, and masks Win when it comes up.
        filter.Key(new KeyInput(0x5B, 0, true, false, false, false, 0));
        Assert.True(filter.Key(new KeyInput(0x59, 0, true, false, false, false, 0)));
        await rig.Actions.Ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("Maximize", WindowTarget.InFront), rig.Actions.Runs.Single());
        filter.Key(new KeyInput(0x5B, 0, false, false, false, false, 0));
        Assert.Equal(1, rig.Hub.Masks);
    }

    [Fact]
    public async Task A_shortcut_that_needs_no_Ctrl_Alt_or_Win_is_refused()
    {
        var rig = new Rig();
        await rig.StartAsync();
        Assert.False(rig.Module.Add(Key(ShortcutModifiers.Shift, 0x4B)).Ok);
        Assert.Empty(rig.Module.Settings.Bindings);
    }

    [Fact]
    public async Task The_mouse_section_starts_off_so_a_mouse_binding_is_inactive_and_no_hook_is_in()
    {
        var rig = new Rig();
        await rig.StartAsync();
        var binding = Mouse(MouseTrigger.Back);
        Assert.True(rig.Module.Add(binding).Ok);
        Assert.Equal(BindingState.Inactive, rig.Module.StatusOf(binding.Id).State);
        Assert.Empty(rig.Hub.Filters);

        rig.Module.SetMouseEnabled(true);
        Assert.Equal(BindingState.Active, rig.Module.StatusOf(binding.Id).State);
        var filter = Assert.Single(rig.Hub.Filters);
        Assert.Equal(InputDevices.Mouse, filter.Devices);
        Assert.True(rig.Settings.Value.MouseEnabled);

        rig.Module.SetMouseEnabled(false);
        Assert.Empty(rig.Hub.Filters);
    }

    [Fact]
    public async Task A_bound_button_runs_its_action_on_the_window_under_the_pointer_and_the_press_is_kept()
    {
        var rig = new Rig(new ShortcutsSettings { MouseEnabled = true, Bindings = [Mouse(MouseTrigger.Forward, "Maximize")] });
        await rig.StartAsync();
        var filter = Assert.Single(rig.Hub.Filters);
        Assert.True(filter.Mouse(new MouseInput(MouseKind.Down, MouseButton.Forward, 0, 0, 0, false, 0)));
        await rig.Actions.Ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("Maximize", WindowTarget.UnderPointer), rig.Actions.Runs.Single());
    }

    [Fact]
    public async Task A_button_the_Radial_menu_holds_is_refused_when_added_and_waits_when_the_menu_takes_it_later()
    {
        var rig = new Rig();
        await rig.StartAsync();
        rig.RadialHolds = true;
        Assert.Equal(ShortcutsModule.RadialText, rig.Module.Add(Mouse(MouseTrigger.Middle)).Error);
        Assert.True(rig.Module.Add(Mouse(MouseTrigger.WheelLeft)).Ok); // the wheel is never the Radial menu's

        rig.RadialHolds = false;
        var middle = Mouse(MouseTrigger.Middle);
        rig.Module.SetMouseEnabled(true);
        Assert.True(rig.Module.Add(middle).Ok);
        Assert.Equal(BindingState.Active, rig.Module.StatusOf(middle.Id).State);

        rig.RadialHolds = true; // the owner turns the menu's Middle button on
        rig.Module.SetMouseEnabled(true); // any change rereads
        var status = rig.Module.StatusOf(middle.Id);
        Assert.Equal(BindingState.HeldByRadial, status.State);
        Assert.Equal("This button opens the Radial menu now, so the shortcut waits.", status.Text);
        Assert.False(rig.Hub.Filters.Single().Mouse(new MouseInput(MouseKind.Down, MouseButton.Middle, 0, 0, 0, false, 0)));
    }

    [Fact]
    public async Task A_button_twice_is_refused()
    {
        var rig = new Rig();
        await rig.StartAsync();
        Assert.True(rig.Module.Add(Mouse(MouseTrigger.Back)).Ok);
        Assert.Equal(ShortcutsModule.ButtonTakenText, rig.Module.Add(Mouse(MouseTrigger.Back, "Minimize")).Error);
    }

    [Fact]
    public async Task With_no_action_runner_or_a_missing_target_every_binding_shows_Unavailable_and_nothing_is_hooked()
    {
        var none = new Rig(new ShortcutsSettings { MouseEnabled = true, Bindings = [Key(CtrlAlt, 0x4B), Mouse(MouseTrigger.Back)] }, actions: false);
        await none.StartAsync();
        Assert.All(none.Module.Settings.Bindings, b => Assert.Equal(new BindingStatus(BindingState.Unavailable, "Unavailable"), none.Module.StatusOf(b.Id)));
        Assert.Empty(none.Registrar.Registered);
        Assert.Empty(none.Hub.Filters);

        var gone = new Rig(new ShortcutsSettings { Bindings = [Key(CtrlAlt, 0x4B, "Gone")] });
        gone.Actions.Missing.Add("Gone");
        await gone.StartAsync();
        Assert.Equal(BindingState.Unavailable, gone.Module.StatusOf(gone.Module.Settings.Bindings[0].Id).State);
    }

    [Fact]
    public async Task A_pause_frees_the_keys_and_the_hub_filters_pause_too_and_the_rows_stay()
    {
        var rig = new Rig(new ShortcutsSettings { MouseEnabled = true, Bindings = [Key(CtrlAlt, 0x4B), Mouse(MouseTrigger.Back)] });
        await rig.StartAsync();
        Assert.Single(rig.Registrar.Registered);
        Assert.True(rig.Hub.Filters.Single().Pauses);

        rig.Hub.Paused = true;
        rig.Hub.RaisePause();
        Assert.Empty(rig.Registrar.Registered); // the game gets the keys
        Assert.Single(rig.Service.List);

        rig.Hub.Paused = false;
        rig.Hub.RaisePause();
        Assert.Single(rig.Registrar.Registered);
    }

    [Fact]
    public async Task A_failed_action_is_reported_with_its_reason_and_remove_frees_the_keys_and_stop_takes_everything_out()
    {
        var rig = new Rig();
        await rig.StartAsync();
        string? said = null;
        rig.Module.ActionFailed += (_, reason) => said = reason;
        var key = Key(CtrlAlt, 0x4B);
        var button = Mouse(MouseTrigger.Middle);
        rig.Module.Add(key);
        rig.Module.SetMouseEnabled(true);
        rig.Module.Add(button);
        rig.Actions.Fail = "No window";
        rig.Registrar.Press(key.Keys!);
        await rig.Actions.Ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 100 && said is null; i++)
            await Task.Delay(20);
        Assert.Equal("No window", said);

        rig.Module.Remove(key.Id);
        Assert.Empty(rig.Registrar.Registered);
        Assert.Empty(rig.Service.List);
        Assert.Single(rig.Hub.Filters);

        await rig.Module.StopAsync(default);
        Assert.Empty(rig.Hub.Filters);
    }

    [Fact]
    public async Task Change_keeps_the_place_and_the_id_and_refuses_a_door_that_is_taken()
    {
        var rig = new Rig();
        await rig.StartAsync();
        var a = Key(CtrlAlt, 0x4B, "A");
        var b = Key(CtrlAlt, 0x4C, "B");
        rig.Module.Add(a);
        rig.Module.Add(b);
        Assert.Equal("B already uses this.", rig.Module.Change(a with { Keys = b.Keys }).Error);
        Assert.True(rig.Module.Change(a with { Keys = new KeyShortcut(CtrlAlt, 0x4D) }).Ok);
        Assert.Equal([a.Id, b.Id], rig.Module.Settings.Bindings.Select(x => x.Id));
        Assert.True(rig.Module.Change(a with { Keys = a.Keys }).Ok); // its own door is not a clash
    }

    [Fact]
    public async Task Bindings_come_back_from_the_saved_settings()
    {
        var saved = new ShortcutsSettings { MouseEnabled = true, MouseIgnoredApps = ["chrome.exe"], Bindings = [Key(CtrlAlt, 0x4B), Mouse(MouseTrigger.WheelRight)] };
        var rig = new Rig(saved);
        await rig.StartAsync();
        Assert.Equal(2, rig.Module.Settings.Bindings.Count);
        Assert.Single(rig.Registrar.Registered);
        Assert.Single(rig.Hub.Filters);
    }

    private sealed class FakeActions : IActionHost
    {
        public List<(string Name, WindowTarget Target)> Runs { get; } = [];
        public HashSet<string> Missing { get; } = [];
        public string? Fail { get; set; }
        public TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunAsync(ChippiAction action, WindowTarget target)
        {
            Runs.Add((action.Name, target));
            Ran.TrySetResult();
            return Fail is null ? Task.CompletedTask : Task.FromException(new ActionFailure(Fail));
        }

        public bool IsMissing(ChippiAction action) => Missing.Contains(action.Name);
    }

    private sealed class FakeRegistrar : IHotkeyRegistrar
    {
        private readonly Dictionary<int, KeyShortcut> _ids = [];
        public HashSet<KeyShortcut> Refused { get; } = [];
        public IReadOnlyCollection<KeyShortcut> Registered => _ids.Values;

        public bool Register(int id, KeyShortcut shortcut)
        {
            if (Refused.Contains(shortcut))
                return false;
            _ids[id] = shortcut;
            return true;
        }

        public void Unregister(int id) => _ids.Remove(id);

        public event Action<int>? Pressed;

        public void Press(KeyShortcut shortcut) => Pressed?.Invoke(_ids.First(p => p.Value == shortcut).Key);
    }

    private sealed class FakeHub : IInputHub
    {
        public List<IInputFilter> Filters { get; } = [];
        public int Masks { get; private set; }
        public bool Paused { get; set; }
        public bool FullscreenInFront => false;
        public event Action? PauseChanged;
        public void RaisePause() => PauseChanged?.Invoke();

        public IInputRegistration Add(IInputFilter filter)
        {
            Filters.Add(filter);
            return new Registration(this, filter);
        }

        private sealed class Registration(FakeHub hub, IInputFilter filter) : IInputRegistration
        {
            public void Dispose() => hub.Filters.Remove(filter);
            public void Refresh() { }
            public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers) { }
            public void Mask() => hub.Masks++;
        }
    }

    private sealed class Store : IModuleSettings
    {
        public ShortcutsSettings Value { get; set; } = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T settings) where T : class => Value = (ShortcutsSettings)(object)settings;
    }

    private sealed class Ui : IModuleUi
    {
        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("Shortcuts has no tile");
        public void SetSettingsPage(Func<string?, object> page) { }
    }
}

public class WindowsKeysTests
{
    [Theory]
    [InlineData(ShortcutModifiers.Windows, 0x4C, true)]
    [InlineData(ShortcutModifiers.Windows, 0x44, true)]
    [InlineData(ShortcutModifiers.Windows, 0x25, true)]
    [InlineData(ShortcutModifiers.Windows, 0x4A, false)]
    [InlineData(ShortcutModifiers.Windows, 0x59, false)]
    [InlineData(ShortcutModifiers.Alt, 0x09, true)]
    [InlineData(ShortcutModifiers.Alt, 0x73, true)]
    [InlineData(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x2E, true)]
    [InlineData(ShortcutModifiers.Control | ShortcutModifiers.Shift, 0x1B, true)]
    [InlineData(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x4B, false)]
    [InlineData(ShortcutModifiers.Control, 0x4B, false)]
    public void Windows_keeps_the_combinations_it_uses(ShortcutModifiers mods, int key, bool reserved) =>
        Assert.Equal(reserved, WindowsKeys.IsReserved(new KeyShortcut(mods, (uint)key)));
}
