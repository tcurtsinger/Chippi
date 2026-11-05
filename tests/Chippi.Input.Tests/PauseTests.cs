// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;

namespace Chippi.Input.Tests;

/// <summary>
/// The pause (docs/phase-6/input-layer.md, architecture §5): with Always on off, filters that pause stand down while the
/// window in front counts as fullscreen, and their hook comes off when nothing else needs it.
/// </summary>
public class PauseTests
{
    private const uint A = 0x41;
    private const string Game = @"D:\Games\Hades II\Ship\Hades2.exe";
    private const string Browser = @"C:\Program Files\Browser\browser.exe";

    private static (TestHub Hub, TestFilter Debounce, TestFilter Lock, List<string> Changes) Started(PauseRules? rules = null)
    {
        var hub = new TestHub();
        var changes = new List<string>();
        hub.Core.PauseChanged = () => changes.Add($"{(hub.Core.Paused ? "paused" : "running")}, {(hub.Core.FullscreenInFront ? "fullscreen" : "windowed")}");
        hub.Core.Rules = rules ?? PauseRules.Default;
        var debounce = new TestFilter("debounce", InputOrder.Debounce, InputDevices.Mouse) { KeepMouse = _ => true };
        var lockFilter = new TestFilter("lock", InputOrder.CleaningLock, InputDevices.Keyboard, pauses: false);
        hub.Core.Add(debounce);
        hub.Core.Add(lockFilter);
        return (hub, debounce, lockFilter, changes);
    }

    [Fact]
    public void A_fullscreen_app_in_front_pauses_what_pauses_and_its_hook_comes_off_and_back()
    {
        var (hub, debounce, lockFilter, changes) = Started();

        hub.Core.Foreground(fullscreen: true, Game);

        Assert.True(hub.Core.Paused);
        Assert.Equal(InputDevices.Keyboard, hub.Hooks.In); // the game's mouse doesn't pass through Chippi
        Assert.False(hub.Key(A, down: true)); // the lock never pauses
        Assert.Equal(["lock key 41 down"], lockFilter.Heard);

        hub.Core.Foreground(fullscreen: false, Browser); // the browser on the other monitor, clicked
        Assert.False(hub.Core.Paused);
        Assert.Equal(InputDevices.Both, hub.Hooks.In);
        Assert.True(hub.Mouse(TestHub.WmMButtonDown));
        Assert.Equal(["debounce Down Middle"], debounce.Heard);
        Assert.Equal(["paused, fullscreen", "running, windowed"], changes);
    }

    [Fact]
    public void Always_on_keeps_everything_acting_over_a_fullscreen_app_but_still_says_one_is_in_front()
    {
        var (hub, _, _, changes) = Started(new PauseRules(AlwaysOn: true, []));

        hub.Core.Foreground(fullscreen: true, Game);

        Assert.False(hub.Core.Paused);
        Assert.True(hub.Core.FullscreenInFront); // the Radial menu's own switch reads this
        Assert.True(hub.Mouse(TestHub.WmMButtonDown));
        Assert.Equal(["running, fullscreen"], changes);
    }

    [Fact]
    public void A_windowed_game_on_Pause_in_these_apps_pauses_matched_by_its_exe_wherever_it_lives()
    {
        var (hub, _, _, _) = Started(new PauseRules(false, [@"C:\Old Library\Hades2.exe"]));

        hub.Core.Foreground(fullscreen: false, Game);
        Assert.True(hub.Core.Paused);

        hub.Core.Foreground(fullscreen: false, Browser);
        Assert.False(hub.Core.Paused);
    }

    [Fact]
    public void Turning_Always_on_on_while_paused_resumes_at_once()
    {
        var (hub, _, _, changes) = Started();
        hub.Core.Foreground(fullscreen: true, Game);

        hub.Core.Rules = new PauseRules(AlwaysOn: true, []);

        Assert.False(hub.Core.Paused);
        Assert.Equal(["paused, fullscreen", "running, fullscreen"], changes);
    }

    [Fact]
    public void A_button_held_across_the_pause_still_has_its_up_kept_so_nothing_sticks()
    {
        var (hub, debounce, _, _) = Started();
        Assert.True(hub.Mouse(TestHub.WmMButtonDown));

        hub.Core.Foreground(fullscreen: true, Game);
        Assert.Equal(InputDevices.Both, hub.Hooks.In); // the kept button's up is still to come

        Assert.True(hub.Mouse(TestHub.WmMButtonUp));
        Assert.Equal(["debounce Down Middle", "debounce Up Middle"], debounce.Heard);
        Assert.Equal(InputDevices.Keyboard, hub.Hooks.In);
    }

    [Fact]
    public void Nothing_is_raised_when_nothing_changed()
    {
        var (hub, _, _, changes) = Started();

        hub.Core.Foreground(fullscreen: false, Browser);
        hub.Core.Foreground(fullscreen: true, Game);
        hub.Core.Foreground(fullscreen: true, Game);
        hub.Core.Rules = PauseRules.Default;

        Assert.Equal(["paused, fullscreen"], changes);
    }

    [Fact]
    public void An_app_Chippi_cant_read_never_matches_the_list()
    {
        var rules = new PauseRules(false, [Game]);

        Assert.False(rules.Pauses(fullscreen: false, appPath: null));
        Assert.True(rules.Pauses(fullscreen: true, appPath: null));
        Assert.True(rules.IsListed(@"E:\HADES2.EXE"));
    }
}
