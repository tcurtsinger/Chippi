// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Features.AppSwitcher.Tests.SwitcherSessionTests;

namespace Chippi.Features.AppSwitcher.Tests;

public sealed class SwitcherTests
{
    private readonly FakePc _pc = new() { Open = [W(1, "Notes"), W(2, "Mail", monitor: 2), W(3, "Code", minimized: true)], Front = 1 };
    private readonly FakeView _view;
    private readonly Switcher _switcher;

    public SwitcherTests()
    {
        _view = new FakeView(_pc);
        _switcher = new Switcher(_pc, new RecentWindows(), _ => _view);
    }

    [Fact]
    public void Opening_shows_the_list_on_the_original_windows_display_then_takes_the_keyboard()
    {
        Assert.True(_switcher.Open(original: 2, currentDisplayOnly: false));
        Assert.Equal(["show", $"take {FakeView.Handle}"], _pc.Calls);
        Assert.Equal(2, _view.Monitor);
        Assert.Same(_switcher.Session, _view.Shown);
        Assert.Equal(2, _switcher.Session!.Original);
    }

    [Fact]
    public void Without_an_original_it_opens_on_the_pointers_display()
    {
        Assert.True(_switcher.Open(0, false));
        Assert.Equal(2, _view.Monitor);
    }

    [Fact]
    public void Refused_the_keyboard_it_closes_again_so_no_key_reaches_another_app()
    {
        _pc.RefuseOverlay = true;
        Assert.False(_switcher.Open(1, false));
        Assert.False(_switcher.IsOpen);
        Assert.Equal(["show", $"take {FakeView.Handle}", "hide"], _pc.Calls);
    }

    [Fact]
    public void Commit_activates_while_the_overlay_holds_the_foreground_then_hides_it()
    {
        _switcher.Open(1, false);
        _pc.Calls.Clear();
        Assert.Equal(SwitchOutcome.Switched, _switcher.Commit());
        Assert.Equal(["activate 2", "hide"], _pc.Calls);
        Assert.Equal(2, _pc.Front);
        Assert.False(_switcher.IsOpen);
        Assert.Equal(SwitchOutcome.None, _switcher.Commit());
    }

    [Fact]
    public void A_minimized_window_is_the_pcs_to_restore_and_is_committed_like_any()
    {
        _switcher.Open(1, false);
        _switcher.Session!.Select(2);
        Assert.True(_switcher.Session.Selection!.Minimized);
        Assert.Equal(SwitchOutcome.Switched, _switcher.Commit());
        Assert.Equal(3, _pc.Front);
    }

    [Fact]
    public void A_window_that_closed_or_whose_handle_was_reused_is_dropped_and_nothing_else_is_activated()
    {
        _switcher.Open(1, false);
        _pc.Closed.Add(2);
        _pc.Calls.Clear();
        Assert.Equal(SwitchOutcome.Gone, _switcher.Commit());
        Assert.Empty(_pc.Calls);
        Assert.True(_switcher.IsOpen);
        Assert.DoesNotContain(_switcher.Session!.Shown, w => w.Handle == 2);

        // Handle 3 now belongs to another process.
        _pc.Open[2] = _pc.Open[2] with { ProcessId = 77 };
        Assert.Equal(3, _switcher.Session.Selection!.Handle);
        Assert.Equal(SwitchOutcome.Gone, _switcher.Commit());
        Assert.Empty(_pc.Calls);
    }

    [Fact]
    public void A_refused_switch_says_so_and_stays_open_without_trying_again()
    {
        _switcher.Open(1, false);
        _pc.RefuseSwitch = true;
        _pc.Calls.Clear();
        Assert.Equal(SwitchOutcome.Failed, _switcher.Commit());
        Assert.Equal(["activate 2"], _pc.Calls);
        Assert.Equal(2, _view.Failed!.Handle);
        Assert.True(_switcher.IsOpen);
    }

    [Fact]
    public void Esc_gives_the_original_window_the_keyboard_before_hiding()
    {
        _switcher.Open(1, false);
        _pc.Calls.Clear();
        _switcher.Cancel();
        Assert.Equal(["give back 1", "hide"], _pc.Calls);
        Assert.False(_switcher.IsOpen);
    }

    [Fact]
    public void Dismiss_closes_without_moving_the_foreground()
    {
        _switcher.Open(1, false);
        _pc.Calls.Clear();
        _switcher.Dismiss();
        Assert.Equal(["hide"], _pc.Calls);
    }

    [Fact]
    public void Opening_again_while_open_steps_the_selection()
    {
        _switcher.Open(1, false);
        Assert.True(_switcher.Open(1, false));
        Assert.Equal(1, _view.Shows);
        Assert.Equal(3, _switcher.Session!.Selection!.Handle);
    }

    [Fact]
    public void Deactivated_while_handing_the_foreground_on_it_closes_once()
    {
        int closing = 0;
        _switcher.Closing += () => closing++;
        _pc.Deactivates = _switcher.Dismiss;
        _switcher.Open(1, false);
        _pc.Calls.Clear();
        Assert.Equal(SwitchOutcome.Switched, _switcher.Commit());
        Assert.Equal(["activate 2", "hide"], _pc.Calls);
        _switcher.Open(1, false);
        _pc.Calls.Clear();
        _switcher.Cancel();
        Assert.Equal(["give back 1", "hide"], _pc.Calls);
        Assert.Equal(2, closing);
    }

    [Fact]
    public void Closing_is_raised_however_it_closes()
    {
        int closing = 0;
        _switcher.Closing += () => closing++;
        _switcher.Open(1, false);
        _switcher.Commit();
        _switcher.Open(1, false);
        _switcher.Cancel();
        _switcher.Open(1, false);
        _switcher.Dismiss();
        Assert.Equal(3, closing);
    }
}

public sealed class AppSwitcherModuleTests : IAsyncLifetime
{
    private static readonly KeyShortcut Shortcut = new(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x4A);
    private readonly FakePc _pc = new() { Open = [W(1, "Notes"), W(2, "Mail"), W(3, "Code")], Front = 1 };
    private readonly FakeEvents _events = new();
    private readonly SettingsStore _settings = new();
    private readonly Ui _ui = new();
    private readonly Hotkey _hotkey = new();
    private readonly Hub _hub = new();
    private readonly ActionPc _actions = new();
    private readonly FakeView _view;
    private readonly AppSwitcherModule _module;
    private nint _islandTarget;
    private int _islandClosed;

    public AppSwitcherModuleTests()
    {
        _view = new FakeView(_pc);
        _module = new AppSwitcherModule(_ => _view, () => _pc, () => _events, () => _hotkey);
    }

    public Task InitializeAsync() => _module.StartAsync(new ModuleContext(AppSwitcherModule.Id, _settings, NullLogger.Instance,
        new Chippi.Core.Island.IslandActivityHub().CreatePublisher(AppSwitcherModule.Id), _ui)
    {
        Input = _hub,
        Actions = _actions,
        IslandPasteTarget = () => _islandTarget == 0 ? null : new Chippi.Core.Clipboard.PasteTarget(_islandTarget, 1),
        CloseIsland = () => _islandClosed++,
    }, default);

    public Task DisposeAsync() => _module.StopAsync(default);

    private ChippiCommand Command => Assert.Single(_ui.Commands!());

    [Fact]
    public void Installed_it_offers_the_open_command_as_one_Switcher_tile_and_its_shortcut_starts_unset()
    {
        Assert.Equal(AppSwitcherModule.OpenKey, Command.Key);
        Assert.Equal(new ActionsTile("switcher", "Switcher", "AppWindow"), Command.Tile);
        Assert.False(Command.Tile!.Menu);
        Assert.Null(_hotkey.Registered);
        Assert.Null(_settings.Value.Shortcut);
        Assert.True(_module.Descriptor.PausesInFullscreen);
    }

    [Fact]
    public async Task From_a_tile_or_Search_it_goes_back_to_the_window_before_the_island_and_closes_the_island()
    {
        _islandTarget = 3;
        await Command.Run();
        Assert.Equal(1, _islandClosed);
        Assert.Equal(3, _module.Switcher!.Session!.Original);
        _module.Switcher.Cancel();
        Assert.Equal(3, _pc.Front);
    }

    [Fact]
    public async Task From_a_Radial_slice_or_bound_shortcut_it_goes_back_to_the_window_in_front()
    {
        _actions.InFront = 2;
        _islandTarget = 3;
        await Command.Run();
        Assert.Equal(2, _module.Switcher!.Session!.Original);
    }

    [Fact]
    public async Task Refused_the_keyboard_the_command_fails_with_Switch_failed()
    {
        _pc.RefuseOverlay = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(Command.Run);
        Assert.Equal("Switch failed", error.Message);
        Assert.False(_events.WatchingClosed);
    }

    [Fact]
    public void Its_shortcut_saves_and_opens_with_the_foreground_as_the_original_then_removes()
    {
        Assert.True(_module.ChangeShortcut(Shortcut));
        Assert.Equal(Shortcut, _hotkey.Registered);
        Assert.Equal(Shortcut, _settings.Value.Shortcut);
        _pc.Front = 2;
        _hotkey.Pressed!();
        Assert.Equal(2, _module.Switcher!.Session!.Original);
        _module.RemoveShortcut();
        Assert.Null(_hotkey.Registered);
        Assert.Null(_settings.Value.Shortcut);
    }

    [Fact]
    public void While_Settings_records_a_new_shortcut_the_current_one_doesnt_open_it()
    {
        _module.ChangeShortcut(Shortcut);
        _module.CaptureShortcut(true);
        _hotkey.Pressed!();
        Assert.False(_module.Switcher!.IsOpen);
        _module.CaptureShortcut(false);
        _hotkey.Pressed!();
        Assert.True(_module.Switcher.IsOpen);
    }

    [Fact]
    public void A_shortcut_in_use_is_refused_and_not_saved()
    {
        _hotkey.Available = false;
        Assert.False(_module.ChangeShortcut(Shortcut));
        Assert.Equal(ShortcutService.InUse, _module.ShortcutError);
        Assert.Null(_settings.Value.Shortcut);
    }

    [Fact]
    public void The_shared_pause_lets_go_of_the_shortcut_and_closes_the_overlay_then_takes_it_back()
    {
        _module.ChangeShortcut(Shortcut);
        var press = _hotkey.Pressed!;
        press();
        Assert.True(_module.Switcher!.IsOpen);
        _hub.Pause(true);
        Assert.False(_module.Switcher.IsOpen);
        Assert.Null(_hotkey.Registered);
        press(); // a press already on its way
        Assert.False(_module.Switcher.IsOpen);
        _hub.Pause(false);
        Assert.Equal(Shortcut, _hotkey.Registered);
    }

    [Fact]
    public void Changing_the_shortcut_while_paused_saves_it_without_taking_the_keys()
    {
        _hub.Pause(true);
        Assert.True(_module.ChangeShortcut(Shortcut));
        Assert.Null(_hotkey.Registered);
        Assert.Equal(Shortcut, _settings.Value.Shortcut);
        _hub.Pause(false);
        Assert.Equal(Shortcut, _hotkey.Registered);
    }

    [Fact]
    public async Task Windows_are_listed_most_recently_in_front_first()
    {
        _events.RaiseForeground(3);
        _events.RaiseForeground(2);
        await Command.Run();
        Assert.Equal([2, 3, 1], _module.Switcher!.Session!.Shown.Select(w => w.Handle));
    }

    [Fact]
    public async Task A_window_closing_while_open_leaves_the_list_and_the_closed_hook_comes_off_after()
    {
        Assert.False(_events.WatchingClosed);
        await Command.Run();
        Assert.True(_events.WatchingClosed);
        _events.RaiseDestroyed(2);
        Assert.DoesNotContain(_module.Switcher!.Session!.Shown, w => w.Handle == 2);
        _module.Switcher.Cancel();
        Assert.False(_events.WatchingClosed);
    }

    [Fact]
    public async Task Locking_the_PC_closes_it()
    {
        await Command.Run();
        _module.Locked();
        Assert.False(_module.Switcher!.IsOpen);
    }

    [Fact]
    public async Task The_display_filter_applies_now_and_is_kept()
    {
        await Command.Run();
        _module.ShowCurrentDisplayOnly(true);
        Assert.True(_module.Switcher!.Session!.CurrentDisplayOnly);
        Assert.True(_settings.Value.CurrentDisplayOnly);
    }

    [Fact]
    public async Task Stopping_closes_it_and_lets_everything_go()
    {
        _module.ChangeShortcut(Shortcut);
        await Command.Run();
        await _module.StopAsync(default);
        Assert.True(_hotkey.Disposed);
        Assert.True(_events.Disposed);
        Assert.False(_events.WatchingClosed);
        Assert.Null(_view.Shown);
    }
}
